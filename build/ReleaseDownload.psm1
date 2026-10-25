# ============================================================================
#  ReleaseDownload.psm1 - the FETCH layer of the release verifier: it obtains a release and its files and
#  judges nothing (the judge is ReleaseVerdict.psm1, the orchestration is Assert-PublishedRelease.ps1).
#    - the HTTP boundary (Invoke-ReleaseHttp) behind ONE connection object (token, boundary, timeout, base
#      URL): the boundary is a scriptblock, so a test replaces it and never touches the network;
#    - the releases list (paged), one release found by tag, and the plain asset listing the judge works on;
#    - the downloads: one request per asset, a timeout that follows the asset's size, the files saved so
#      far kept, and ONE retry of only the assets whose download failed.
#  A problem with the release itself (no such tag, a failed listing) is a hard failure, never a download
#  error and never retried. Every function takes what it needs as parameters: nothing is read from the
#  script that calls it.
#  Failures go through Write-BuildFailure (BuildCommon.psm1).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')

$script:ListPageSize = 100
$script:MaxListPages = 10
$script:MinTransferBytesPerSecond = 1MB    # the slowest link a download is still allowed to be on
$script:UserAgent = 'teststand-mcp-release-verifier'
$script:JsonAccept = 'application/vnd.github+json'
$script:UnknownUploader = '(unknown)'
Set-Variable -Name ReleaseBinaryAccept -Value 'application/octet-stream' -Option ReadOnly -Scope Script
# ----------------------------------------------------------------------------
#  JSON helpers
# ----------------------------------------------------------------------------
# ConvertFrom-Json emits a JSON array as ONE object in Windows PowerShell 5.1 and unrolled in 7; this is always the items.
function ConvertFrom-JsonList {
    param([string]$Text)
    # The comma keeps a one-release list an array: returned bare, it unrolls to a single object whose .Count throws under strict mode on 5.1.
    return ,@($Text | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object { $null -ne $_ })
}

function Select-ReleaseByTag {
    param($Releases, [string]$Tag)
    return @($Releases | Where-Object { $null -ne $_ -and (Get-JsonProperty $_ 'tag_name') -ceq $Tag })
}

function Get-UploaderLogin {
    param($ReleaseAsset)
    $login = Get-JsonProperty (Get-JsonProperty $ReleaseAsset 'uploader') 'login'
    if ([string]::IsNullOrEmpty($login)) { return $script:UnknownUploader }
    return [string]$login
}

# The plain listing the judge works on: { Name; Uploader; Size; Path; Id }.
function ConvertTo-ListedAssets {
    param($Release)
    $listed = @()
    foreach ($asset in @(Get-JsonProperty $Release 'assets')) {
        if ($null -eq $asset) { continue }
        $size = Get-JsonProperty $asset 'size'
        $listed += [pscustomobject]@{
            Name = [string](Get-JsonProperty $asset 'name'); Uploader = (Get-UploaderLogin $asset)
            Size = $(if ($null -ne $size) { [int64]$size } else { $null }); Path = $null; Id = [string](Get-JsonProperty $asset 'id')
        }
    }
    return $listed
}

# ----------------------------------------------------------------------------
#  HTTP: ONE connection object (token, boundary, timeout, base URL); the boundary is a seam.
# ----------------------------------------------------------------------------
# The real HTTP boundary: TLS 1.2, a timeout on every call, no progress bar. -TimeoutSec semantics differ:
# Windows PowerShell 5.1 bounds the wait for the response (HttpWebRequest.Timeout), not the body transfer;
# PowerShell 7 maps it onto HttpClient.Timeout, which bounds the whole request unless the body is streamed.
# So under 5.1 a body that stalls after the headers is NOT cut by -TimeoutSec; the job's step timeout is the
# real bound there. The size-based timeout only bounds PowerShell 7 (not measured: no pwsh on this station).
function Invoke-ReleaseHttp {
    param($Uri, $Headers, $OutFile, $TimeoutSeconds)
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'   # Windows PowerShell 5.1 downloads crawl with the progress bar on
    $parameters = @{ Uri = $Uri; Headers = $Headers; UseBasicParsing = $true; TimeoutSec = $TimeoutSeconds; ErrorAction = 'Stop' }
    if ($OutFile) {
        Invoke-WebRequest @parameters -OutFile $OutFile
        return
    }
    return (Invoke-WebRequest @parameters).Content
}

function New-Connection {
    param([string]$Token, [scriptblock]$HttpGet, [int]$TimeoutSeconds, [string]$ApiBaseUrl)
    if ($null -eq $HttpGet) { $HttpGet = ${function:Invoke-ReleaseHttp} }
    return [pscustomobject]@{ Token = $Token; HttpGet = $HttpGet; TimeoutSeconds = $TimeoutSeconds; ApiBaseUrl = $ApiBaseUrl }
}

function Invoke-ConnectionGet {
    param($Connection, [string]$Uri, [string]$Accept, [string]$OutFile, [int]$TimeoutSeconds, [bool]$UseToken = $true)
    $headers = @{ 'User-Agent' = $script:UserAgent; 'Accept' = $Accept }
    if ($UseToken -and -not [string]::IsNullOrEmpty($Connection.Token)) { $headers['Authorization'] = "Bearer $($Connection.Token)" }
    if ($TimeoutSeconds -le 0) { $TimeoutSeconds = $Connection.TimeoutSeconds }
    return (& $Connection.HttpGet -Uri $Uri -Headers $headers -OutFile $OutFile -TimeoutSeconds $TimeoutSeconds)
}

# ----------------------------------------------------------------------------
#  FETCH: obtains the release and its files; judges nothing. A problem with the release itself (no
#  such tag, listing failed) is a hard failure, never a download error and never retried.
# ----------------------------------------------------------------------------
function Get-ReleaseFromApi {
    param($Connection, [string]$Repo, [string]$Tag)
    $found = @()
    $exhausted = $false
    for ($page = 1; $page -le $script:MaxListPages; $page++) {
        $uri = "$($Connection.ApiBaseUrl)/repos/$Repo/releases?per_page=$script:ListPageSize&page=$page"
        try {
            $releases = ConvertFrom-JsonList (Invoke-ConnectionGet $Connection $uri $script:JsonAccept $null 0)
        } catch {
            Write-BuildFailure -What "Could not read the releases of $Repo from the GitHub API: $($_.Exception.Message)" `
                -Why 'The release list is needed to find the draft and its assets. Running the verifier again may help if the cause is transient (network, rate limit); it will not help for a bad token.' `
                -Fix 'Check the network and that the token is valid and allowed to read the repository, then run it again.'
        }
        $found += @(Select-ReleaseByTag $releases $Tag)
        if ($releases.Count -lt $script:ListPageSize) { break }
        if ($page -eq $script:MaxListPages) { $exhausted = $true }
    }
    if ($found.Count -gt 1) {
        $ids = (@($found | ForEach-Object { Get-JsonProperty $_ 'id' }) -join ', ')
        Write-BuildFailure -What "$($found.Count) releases of $Repo carry the tag $Tag (ids: $ids)." `
            -Why 'The verifier cannot tell which one the workflow would publish, and publishing the wrong one would ship assets nobody verified. Running it again will not help.' `
            -Fix "Delete the stale draft (gh release delete $Tag --repo $Repo, or in the web UI) so exactly one release carries the tag."
    }
    if ($found.Count -eq 0) {
        $searched = if ($exhausted) { " The search stopped after $script:MaxListPages pages of $script:ListPageSize releases; an older release is not found." } else { '' }
        Write-BuildFailure -What "No release tagged $Tag was found in $Repo.$searched" `
            -Why 'A draft release is visible through the API only with a token that may write the repository contents, and the tag must match exactly. Running the verifier again will not help until the release exists.' `
            -Fix "Pass -Token (the workflow's GITHUB_TOKEN with contents: write) for a draft, and check the tag spelling with: gh release list --repo $Repo"
    }
    return $found[0]
}

function Get-LocalListing {
    param([string]$LocalDir, [string]$Tag, [string]$ReleaseJson)
    $uploaders = $null
    if ($ReleaseJson) {
        $jsonPath = Resolve-BuildPath $ReleaseJson
        if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
            Write-BuildFailure -What "The release metadata file $ReleaseJson does not exist." `
                -Why 'It supplies the uploader of each asset; without it the uploader check cannot run.' -Fix 'Pass an existing releases-list JSON, or omit -ReleaseJson to skip the uploader check.'
        }
        $matching = @(Select-ReleaseByTag (ConvertFrom-JsonList (Read-Utf8Text $jsonPath)) $Tag)
        if ($matching.Count -ne 1) {
            Write-BuildFailure -What "The release metadata file $ReleaseJson has $($matching.Count) releases tagged $Tag; exactly one is needed." `
                -Why 'The uploaders cannot be checked for a release that is not in the file, or is in it twice.' -Fix 'Pass the releases-list JSON that contains this tag once.'
        }
        $uploaders = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
        foreach ($listed in (ConvertTo-ListedAssets $matching[0])) { $uploaders[$listed.Name] = $listed.Uploader }
    }
    $assets = @()
    foreach ($file in (Get-ChildItem -LiteralPath (Resolve-BuildPath $LocalDir) -File | Sort-Object Name)) {
        $uploader = $null
        if ($null -ne $uploaders) {
            $uploader = $script:UnknownUploader
            if ($uploaders.ContainsKey($file.Name)) { $uploader = $uploaders[$file.Name] }
        }
        $assets += [pscustomobject]@{ Name = $file.Name; Uploader = $uploader; Size = [int64]$file.Length; Path = $file.FullName; Id = $null }
    }
    return [pscustomobject]@{ Assets = $assets; CheckUploader = ($null -ne $uploaders) }
}

# One download to make: where from, with what, and how long it may take.
function New-DownloadRequest {
    param([string]$Name, [string]$Uri, [string]$Accept, [bool]$UseToken, $Size)
    return [pscustomobject]@{ Name = $Name; Uri = $Uri; Accept = $Accept; UseToken = $UseToken; Size = $Size }
}

# At most this long per asset: the base timeout, or the time the asset needs at the slowest allowed link.
function Get-DownloadTimeoutSeconds {
    param($Request, [int]$BaseSeconds)
    if ($null -eq $Request.Size) { return $BaseSeconds }
    return [int][Math]::Max($BaseSeconds, [Math]::Ceiling($Request.Size / $script:MinTransferBytesPerSecond))
}

# Downloads each request not yet in $Transfer.Saved, stopping at the first failure; returns the failures
# (zero or one). $Recorder runs immediately BEFORE each request, so an interrupted run still counts it.
function Save-Downloads {
    param($Connection, [object[]]$Requests, [string]$DestDir, $Transfer, [scriptblock]$Recorder)
    foreach ($request in $Requests) {
        if ($Transfer.Saved.ContainsKey($request.Name)) { continue }
        $path = Join-Path $DestDir $request.Name
        if ($null -ne $Recorder) { & $Recorder $request.Name }
        $Transfer.Requested++
        try {
            [void](Invoke-ConnectionGet $Connection $request.Uri $request.Accept $path (Get-DownloadTimeoutSeconds $request $Connection.TimeoutSeconds) $request.UseToken)
            $Transfer.Saved[$request.Name] = $path
        } catch {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue   # never keep a partial file
            return @([pscustomobject]@{ Name = $request.Name; Message = $_.Exception.Message })
        }
    }
    return @()
}

# Runs $Fetch (attempt number -> failures) up to $MaxAttempts times, waiting between tries.
function Invoke-FetchWithRetry {
    param([scriptblock]$Fetch, [scriptblock]$Delay, [int]$DelaySeconds, [int]$MaxAttempts)
    $failed = @()
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $failed = @(& $Fetch $attempt)
        if ($failed.Count -eq 0) { return [pscustomobject]@{ Attempts = $attempt; Failed = @() } }
        if ($attempt -lt $MaxAttempts) {
            Write-Host "Attempt $attempt could not download $($failed[0].Name) ($($failed[0].Message)); waiting $DelaySeconds s and trying once more."
            & $Delay $DelaySeconds
        }
    }
    return [pscustomobject]@{ Attempts = $MaxAttempts; Failed = $failed }
}

Export-ModuleMember -Variable ReleaseBinaryAccept
Export-ModuleMember -Function ConvertFrom-JsonList, Select-ReleaseByTag, ConvertTo-ListedAssets, Invoke-ReleaseHttp, New-Connection,
    Invoke-ConnectionGet, Get-ReleaseFromApi, Get-LocalListing, New-DownloadRequest, Get-DownloadTimeoutSeconds, Save-Downloads,
    Invoke-FetchWithRetry