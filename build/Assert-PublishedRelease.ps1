# ============================================================================
#  Assert-PublishedRelease.ps1 - verifies a release the way a user would download it.
#  Exit 0 only when ALL of these hold; otherwise one three-part failure (what / why / fix) and
#  exit 1. The judging itself is ReleaseVerdict.psm1 (no network) and the fetching (HTTP, the
#  downloads, the one retry) is ReleaseDownload.psm1; this script owns the parameters, the order
#  of the checks, the report and the exit code.
#    - the four assets exist: the plugin archive teststand-mcp.zip, the versioned archive
#      teststand-mcp-v<version>.zip, the archive digest and the archive manifest;
#    - EVERY asset of the release was uploaded by github-actions[bot];
#    - the plugin archive's SHA-256 equals the archive digest;
#    - every zip entry equals the archive manifest (none extra, none missing, none changed, none
#      duplicated, none with an unsafe name);
#    - the versioned archive is byte-identical to the plugin archive.
#  Faults visible in the release LISTING (missing or hand-uploaded assets, a versioned archive of
#  another size) are reported before anything is downloaded.
#  ONE retry after RetryDelaySeconds, ONLY for assets whose download failed (HTTP error or timeout);
#  it fetches just those, reusing the files already received. Hash, manifest, missing-asset and
#  uploader faults are never retried.
#
#  Three sources (exactly one):
#    -Repo owner/name -Tag vX.Y.Z [-Token t]   a release through the GitHub API (-Token defaults to
#                                              GITHUB_TOKEN). A DRAFT is visible only with a token;
#                                              /releases/tags/{tag} does not return drafts, so the
#                                              releases list is searched. Exactly one release must
#                                              carry the tag; its id is written as step output
#                                              release_id (asset_ids too) so the workflow publishes
#                                              exactly the release that was verified.
#    -LocalDir <dir> [-Tag vX.Y.Z | -Version X.Y.Z] [-ReleaseJson <file>]
#                                              the assets in a directory (the staged output; the
#                                              packaging step has a version, no tag).
#                                              -ReleaseJson is a releases-list JSON giving the
#                                              uploaders (needs -Tag); without it the uploader check
#                                              is skipped.
#    -Latest [-Repo owner/name]                what users get: the unauthenticated releases/latest
#                                              download of the plugin archive, hashed and compared
#                                              with the published archive digest. Recorded as a
#                                              published download. -Repo names the repository whose
#                                              latest release is fetched (default: the contract
#                                              repository), so a fork checks itself.
#                                              -ExpectedArchiveSha256 <hex> is the hash of the archive
#                                              that was just verified: the check then fails while
#                                              releases/latest still serves ANOTHER release (the
#                                              previous one: its archive and digest agree with each
#                                              other, so without this it would pass). That state is
#                                              expected for a short while after publishing, so the
#                                              check is tried up to -PropagationAttempts times,
#                                              -PropagationDelaySeconds apart; every try is a
#                                              recorded download.
#  -AssetPrefixes names the plugins whose assets the release (or -LocalDir, or releases/latest) carries, each
#  judged on its own (default: every plugin in build\PluginIdentity.psm1, so a manual check of a
#  release covers both). A
#  comma-separated value is split, since powershell -File passes an array as one string.
#  -ExpectedArchiveHashes <json> is -ExpectedArchiveSha256 for several plugins: {"<plugin archive>": "<hex>"}
#  with one entry per plugin, as the draft verification writes it to its step output archive_hashes.
#  -Rehearsal accepts the dry-run tag v<X.Y.Z>-rehearsal.
#  -OwnDownloadsPath records every download in own-downloads.json (see ReleaseAssets.psm1)
#  immediately BEFORE its request. The phase comes from the release's own draft flag; -Draft
#  forces draft. -Date and -RunId override the recorded date and run id (tests).
#  Draft mode also writes the step output archive_sha256: the hash of the archive that was verified.
#  A failure is worded by release.draft: a draft is deleted first, a published release is rolled back
#  or fixed forward (a published release never gets "delete the draft first").
#  Test seams: -HttpGet (the HTTP boundary), -Delay (the wait before the retry).
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$Repo,
    [string]$Tag,
    [string]$Version,
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$LocalDir,
    [string]$ReleaseJson,
    [string[]]$AssetPrefixes,
    [switch]$Latest,
    [string]$ExpectedArchiveSha256,
    [string]$ExpectedArchiveHashes,
    [int]$PropagationAttempts = 6,
    [int]$PropagationDelaySeconds = 15,
    [switch]$Rehearsal,
    [string]$OwnDownloadsPath,
    [switch]$Draft,
    [string]$RunId = $env:GITHUB_RUN_ID,
    [string]$Date,
    [scriptblock]$HttpGet,
    [scriptblock]$Delay = { param([int]$Seconds) if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds } },
    [int]$RetryDelaySeconds = 30,
    [int]$TimeoutSeconds = 120,
    [string]$ApiBaseUrl = 'https://api.github.com'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
# A bound but empty -ExpectedArchiveSha256 (an unset workflow output) is refused, never read as "not given".
$ExpectedHashGiven = $PSBoundParameters.ContainsKey('ExpectedArchiveSha256')
$ExpectedHashesGiven = $PSBoundParameters.ContainsKey('ExpectedArchiveHashes')

$MaxAttempts = 2                           # the first try and one retry
$AssetIdPattern = '\A[0-9]+\z'
$Sha256Pattern = '\A[0-9a-f]{64}\z'
$DraftPhase = 'draft'
$PublishedPhase = 'published'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleaseDownload.psm1') -Force

# ----------------------------------------------------------------------------
#  Orchestration
# ----------------------------------------------------------------------------
function Assert-ReleaseFailure {
    param([string]$Subject, [object[]]$Problems, [string]$Mode)
    if ($Problems.Count -eq 0) { return }
    $failure = Format-ReleaseFailure -Subject $Subject -Problems $Problems -Mode $Mode
    Write-BuildFailure @failure
}

function Assert-DownloadsSucceeded {
    param($Outcome, [string]$Subject)
    if ($Outcome.Failed.Count -eq 0) { return }
    Write-BuildFailure -What "$Subject could not be downloaded for verification after $($Outcome.Attempts) attempts: could not download $($Outcome.Failed[0].Name): $($Outcome.Failed[0].Message)" `
        -Why 'Running the verifier again may help: this is a download error (HTTP error or timeout), not a fault found in the release; GitHub may still be settling the upload, or the network failed.' `
        -Fix 'Run the verification again. If it keeps failing, check that the token can read the release and that the asset is listed on it.'
}

function Resolve-ReleaseVersion {
    param([string]$Tag, [string]$Version, [bool]$IsLocal, [bool]$AllowRehearsal)
    $tagVersion = $null
    if ($Tag) {
        $result = ConvertFrom-ReleaseTag -Tag $Tag -AllowRehearsal:$AllowRehearsal
        if (-not $result.IsValid) {
            $failure = Get-TagRejection -Result $result -Commit $env:GITHUB_SHA
            Write-BuildFailure @failure
        }
        $tagVersion = $result.Version
    }
    if ($Version) {
        if ($Version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
            Write-BuildFailure -What "The version '$Version' is not X.Y.Z." -Why 'The versioned archive is named after a version of three numbers without leading zeros.' -Fix 'Pass -Version 1.9.0 (no leading v).'
        }
        if ($tagVersion -and $tagVersion -cne $Version) {
            Write-BuildFailure -What "The tag $Tag and the version $Version disagree." -Why 'They name different releases.' -Fix 'Pass only one of -Tag and -Version, or matching ones.'
        }
        $tagVersion = $Version
    }
    if (-not $tagVersion) {
        $hint = if ($IsLocal) { 'Pass -Tag vX.Y.Z or -Version X.Y.Z.' } else { 'Pass -Tag vX.Y.Z.' }
        Write-BuildFailure -What 'No release tag or version was given.' -Why 'It names the release and the versioned archive to check.' -Fix $hint
    }
    return $tagVersion
}

function Assert-SourceChoice {
    # -Repo is a source of its own (the API) unless -Latest is given: then it only names the repository of the latest download.
    $sources = @([bool]$Latest, (-not $Latest -and -not [string]::IsNullOrEmpty($Repo)), -not [string]::IsNullOrEmpty($LocalDir)) | Where-Object { $_ }
    if (@($sources).Count -ne 1) {
        Write-BuildFailure -What 'Give exactly one source: -Repo, -LocalDir or -Latest.' `
            -Why 'The verifier reads a release through the GitHub API, a directory of staged assets, or the public latest download; mixing them has no meaning.' `
            -Fix 'Use -Repo owner/name -Tag vX.Y.Z [-Token t], or -LocalDir <dir> -Version X.Y.Z, or -Latest [-Repo owner/name].'
    }
    if ($ExpectedHashGiven) {
        if (-not $Latest) {
            Write-BuildFailure -What '-ExpectedArchiveSha256 was given without -Latest.' `
                -Why 'It is the hash the public latest download must serve; the API and local modes verify the digest the release carries instead.' `
                -Fix 'Use it only with -Latest, or drop it.'
        }
        if ($ExpectedArchiveSha256 -cnotmatch $Sha256Pattern) {
            Write-BuildFailure -What "-ExpectedArchiveSha256 '$ExpectedArchiveSha256' is not 64 lower-case hex digits." `
                -Why 'It is compared with the SHA-256 of the archive users download. An empty value would silently turn the check into the weaker one that accepts the previous release too, so it is refused.' `
                -Fix 'Pass the archive_sha256 output of the draft verification step, or omit the parameter.'
        }
        if (@($AssetPrefixes).Count -gt 1) {
            Write-BuildFailure -What "-ExpectedArchiveSha256 was given for $(@($AssetPrefixes).Count) plugins ($($AssetPrefixes -join ', '))." `
                -Why 'It holds ONE hash, so every other plugin''s latest download would be accepted on its own digest alone, which a stale previous release also passes.' `
                -Fix 'Pass -ExpectedArchiveHashes with one hash per plugin (the archive_hashes output of the draft verification step).'
        }
    }
    if ($ExpectedHashesGiven) {
        if (-not $Latest -or $ExpectedHashGiven) {
            Write-BuildFailure -What '-ExpectedArchiveHashes was given without -Latest, or together with -ExpectedArchiveSha256.' `
                -Why 'It holds the hashes the public latest downloads must serve, one per plugin; the single hash is the same check for one plugin.' `
                -Fix 'Use it only with -Latest, and without -ExpectedArchiveSha256.'
        }
    }
    if ($Repo) { Assert-RepoSlug $Repo }
    if ($LocalDir -and -not (Test-Path -LiteralPath (Resolve-BuildPath $LocalDir) -PathType Container)) {
        Write-BuildFailure -What "The directory $LocalDir does not exist." -Why 'It should hold the release assets.' -Fix 'Pass the directory the build wrote the assets to.'
    }
}

# Archive name -> the hash its latest download must have ($null entries: no expectation). An expectation that
# is given but unusable is refused: an empty value would silently accept the previous release.
function Get-ExpectedArchiveHashes {
    $expected = @{}
    $archives = @(foreach ($prefix in $AssetPrefixes) { (Get-PluginAssetNames -Prefix $prefix -Version '0.0.0').Archive })
    if ($ExpectedHashGiven) { $expected[$archives[0]] = $ExpectedArchiveSha256 }
    if (-not $ExpectedHashesGiven) { return $expected }
    $fail = {
        param($Reason)
        Write-BuildFailure -What "-ExpectedArchiveHashes is unusable: $Reason." `
            -Why 'It holds the hash of every archive that was just verified; without one, releases/latest cannot be compared with it, and a gap would silently accept the previous release.' `
            -Fix 'Pass the archive_hashes output of the draft verification step, with the same -AssetPrefixes.'
    }
    $json = $null
    try { $json = $ExpectedArchiveHashes | ConvertFrom-Json } catch { & $fail 'it is not valid JSON' }
    if ($null -eq $json) { & $fail 'the value is empty' }
    foreach ($archive in $archives) {
        $hash = [string](Get-JsonProperty $json $archive)
        if ($hash -cnotmatch $Sha256Pattern) { & $fail "the hash of $archive is '$hash', not 64 lower-case hex digits" }
        $expected[$archive] = $hash
    }
    return $expected
}

function New-DownloadRecorder {
    param([string]$Phase)
    if (-not $OwnDownloadsPath) { return $null }
    $recordPath = $OwnDownloadsPath
    $optional = @{}
    if ($RunId) { $optional.RunId = $RunId }
    if ($Date) { $optional.Date = $Date }
    return { param([string]$AssetName) Add-OwnDownload -Path $recordPath -Asset $AssetName -Phase $Phase @optional }.GetNewClosure()
}

function Write-StepOutputs {
    param($Release, [object[]]$Assets, [string[]]$RequiredNames, [string]$ArchiveSha256, $ArchiveHashes)
    $releaseId = [string](Get-JsonProperty $Release 'id')
    if ($releaseId -cmatch $AssetIdPattern) { Add-GitHubOutput -Name 'release_id' -Value $releaseId }
    $ids = [ordered]@{}
    foreach ($name in $RequiredNames) {
        $asset = @($Assets | Where-Object { $_.Name -ceq $name })[0]
        if ($null -ne $asset -and $asset.Id -cmatch $AssetIdPattern) { $ids[$name] = [int64]$asset.Id }
    }
    Add-GitHubOutput -Name 'asset_ids' -Value (ConvertTo-Json -InputObject $ids -Compress)
    Add-GitHubOutput -Name 'archive_sha256' -Value $ArchiveSha256
    Add-GitHubOutput -Name 'archive_hashes' -Value (ConvertTo-Json -InputObject $ArchiveHashes -Compress)
}

# ONE fetch of the public latest downloads (each plugin's archive and digest) and their judgement. Throws a
# build failure; $Totals counts the requests. $ExpectedByArchive: Get-ExpectedArchiveHashes.
function Test-LatestOnce {
    param($Connection, [string]$DestDir, $ExpectedByArchive, [int]$DownloadAttempts, $Totals)
    $transfer = [pscustomobject]@{ Requested = 0; Saved = @{} }
    $latestRepo = if ($Repo) { $Repo } else { $ReleaseRepoSlug }
    $plugins = @(foreach ($prefix in $AssetPrefixes) { Get-PluginAssetNames -Prefix $prefix -Version '0.0.0' })
    $requests = @(foreach ($names in $plugins) {
        foreach ($name in @($names.Archive, $names.Digest)) {
            New-DownloadRequest -Name $name -Uri (Get-LatestDownloadUrl -AssetName $name -Repo $latestRepo) -Accept $ReleaseBinaryAccept -UseToken $false -Size $null
        }
    })
    $recorder = New-DownloadRecorder -Phase $PublishedPhase
    [void](New-Item -ItemType Directory -Path $DestDir -Force)
    try {
        $outcome = Invoke-FetchWithRetry -Fetch { param($attempt) Save-Downloads $Connection $requests $DestDir $transfer $recorder } `
            -Delay $Delay -DelaySeconds $RetryDelaySeconds -MaxAttempts $DownloadAttempts
    } finally {
        $Totals.Requested += $transfer.Requested
    }
    Assert-DownloadsSucceeded $outcome 'The latest release'

    foreach ($names in $plugins) {
        $subject = "The latest release ($($names.Archive))"   # one release carries several plugins: name the failing one
        try {
            $digest = ConvertFrom-DigestText -Text (Read-Utf8Text $transfer.Saved[$names.Digest])
        } catch {
            Assert-ReleaseFailure $subject @(New-Problem 'digest-malformed' @($_.Exception.Message, $names.Digest)) 'ApiPublished'
        }
        $archiveHash = Get-FileSha256 $transfer.Saved[$names.Archive]
        if ($digest.Name -cne $names.Archive) { Assert-ReleaseFailure $subject @(New-Problem 'digest-name' @($digest.Name, $names.Archive)) 'ApiPublished' }
        if ($digest.Hash -cne $archiveHash) { Assert-ReleaseFailure $subject @(New-Problem 'latest-mismatch' @($archiveHash, $digest.Hash)) 'ApiPublished' }
        $expectedHash = $ExpectedByArchive[$names.Archive]
        if ($expectedHash -and $archiveHash -cne $expectedHash) {
            Assert-ReleaseFailure $subject @(New-Problem 'latest-not-verified' @($archiveHash, $expectedHash)) 'ApiPublished'
        }
        $claim = if ($expectedHash) { 'matches the published archive digest and is the archive of the verified release' } else { 'matches the published archive digest' }
        Write-Host "The latest download of $($names.Archive) $claim ($archiveHash)."
    }
}

# Without an expected hash: one try (a download error is retried once). With one, releases/latest may still serve the
# previous release for a while (or lack the asset altogether, for the first workflow release), so every kind of failure
# is tried again, -PropagationDelaySeconds apart, up to -PropagationAttempts times; the last failure is the report.
function Test-LatestDownload {
    param($Connection, [string]$TempRoot)
    $expectedByArchive = Get-ExpectedArchiveHashes
    $expecting = ($ExpectedHashGiven -or $ExpectedHashesGiven)
    $tries = if ($expecting) { [Math]::Max(1, $PropagationAttempts) } else { 1 }
    $downloadAttempts = if ($expecting) { 1 } else { $MaxAttempts }
    $totals = [pscustomobject]@{ Requested = 0 }
    for ($try = 1; $try -le $tries; $try++) {
        try {
            Test-LatestOnce -Connection $Connection -DestDir (Join-Path $TempRoot "latest-$try") -ExpectedByArchive $expectedByArchive -DownloadAttempts $downloadAttempts -Totals $totals
            break
        } catch {
            if ($try -ge $tries) { throw }
            $firstLine = @($_.Exception.Message -split '\r?\n')[0]
            Write-Host "Try $try of ${tries}: $firstLine`nWaiting $PropagationDelaySeconds s for releases/latest to settle."
            & $Delay $PropagationDelaySeconds
        }
    }
    Write-Host "Requested $($totals.Requested) asset download(s) in $try try(ies)."
}

function Test-ApiRelease {
    param($Connection, [string]$Tag, [string]$ReleaseVersion, [string]$TempRoot)
    $release = Get-ReleaseFromApi $Connection $Repo $Tag
    $isDraft = [bool]$Draft -or ((Get-JsonProperty $release 'draft') -eq $true)
    $failureMode = if ($isDraft) { 'Api' } else { 'ApiPublished' }   # the fix differs: a draft is deleted, a published release is rolled back
    $listedAssets = @(ConvertTo-ListedAssets $release)
    Assert-ReleaseFailure "Release $Tag" @(Get-ReleaseProblems -Assets $listedAssets -Version $ReleaseVersion -Scope Listing -Prefixes $AssetPrefixes) $failureMode

    $requiredNames = @(Get-RequiredAssetNames -Version $ReleaseVersion -Prefixes $AssetPrefixes)
    $requests = @(foreach ($name in $requiredNames) {
        $asset = @($listedAssets | Where-Object { $_.Name -ceq $name })[0]
        if ($asset.Id -cnotmatch $AssetIdPattern) {
            Write-BuildFailure -What "The release lists $name with an unusable asset id '$($asset.Id)'." `
                -Why 'The asset cannot be downloaded through the API without its numeric id. Running the verifier again will not help.' -Fix 'Check the release in the GitHub UI; start a new release run if the asset is broken.'
        }
        New-DownloadRequest -Name $name -Uri "$($Connection.ApiBaseUrl)/repos/$Repo/releases/assets/$($asset.Id)" -Accept $ReleaseBinaryAccept -UseToken $true -Size $asset.Size
    })

    $recorder = New-DownloadRecorder -Phase $(if ($isDraft) { $DraftPhase } else { $PublishedPhase })
    $transfer = [pscustomobject]@{ Requested = 0; Saved = @{} }
    $destDir = Join-Path $TempRoot 'release'
    [void](New-Item -ItemType Directory -Path $destDir -Force)
    $outcome = Invoke-FetchWithRetry -Fetch { param($attempt) Save-Downloads $Connection $requests $destDir $transfer $recorder } `
        -Delay $Delay -DelaySeconds $RetryDelaySeconds -MaxAttempts $MaxAttempts
    Assert-DownloadsSucceeded $outcome "Release $Tag"

    foreach ($asset in $listedAssets) { if ($transfer.Saved.ContainsKey($asset.Name)) { $asset.Path = $transfer.Saved[$asset.Name] } }
    Assert-ReleaseFailure "Release $Tag" @(Get-ReleaseProblems -Assets $listedAssets -Version $ReleaseVersion -Scope Content -Prefixes $AssetPrefixes) $failureMode

    $archiveHashes = [ordered]@{}
    foreach ($prefix in $AssetPrefixes) {
        $archive = (Get-PluginAssetNames -Prefix $prefix -Version $ReleaseVersion).Archive
        $archiveHashes[$archive] = Get-FileSha256 $transfer.Saved[$archive]
    }
    Write-StepOutputs -Release $release -Assets $listedAssets -RequiredNames $requiredNames -ArchiveSha256 $archiveHashes[0] -ArchiveHashes $archiveHashes
    Write-Host "Release $Tag verified: the $($requiredNames.Count) required assets match (digest, manifest, versioned archive), every asset uploaded by $ReleaseUploaderLogin."
    Write-Host "Requested $($transfer.Requested) asset download(s) in $($outcome.Attempts) attempt(s)."
}

function Test-LocalRelease {
    param([string]$Tag, [string]$ReleaseVersion)
    $subject = if ($Tag) { "Release $Tag" } else { "The staged assets of version $ReleaseVersion" }
    if ($ReleaseJson -and -not $Tag) {
        Write-BuildFailure -What '-ReleaseJson needs -Tag.' -Why 'The release is looked up in the JSON by its tag.' -Fix 'Pass -Tag vX.Y.Z together with -ReleaseJson.'
    }
    $listing = Get-LocalListing -LocalDir $LocalDir -Tag $Tag -ReleaseJson $ReleaseJson
    Assert-ReleaseFailure $subject @(Get-ReleaseProblems -Assets $listing.Assets -Version $ReleaseVersion -CheckUploader $listing.CheckUploader -Prefixes $AssetPrefixes) 'Local'
    $uploaderNote = if ($listing.CheckUploader) { "every asset uploaded by $ReleaseUploaderLogin" } else { 'uploader check skipped (no release metadata given)' }
    Write-Host "$subject verified: the $(@(Get-RequiredAssetNames -Version $ReleaseVersion -Prefixes $AssetPrefixes).Count) required assets match (digest, manifest, versioned archive), $uploaderNote."
    Write-Host 'Requested 0 asset download(s) in 1 attempt(s) (local assets).'
}

try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'ReleaseVerdict.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'PluginIdentity.psm1') -Force
    if (-not $AssetPrefixes) { $AssetPrefixes = @(Get-PluginAssetPrefixes) }
    $AssetPrefixes = ConvertTo-AssetPrefixList $AssetPrefixes

    Assert-SourceChoice
    $connection = New-Connection -Token $Token -HttpGet $HttpGet -TimeoutSeconds $TimeoutSeconds -ApiBaseUrl $ApiBaseUrl
    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('release-verify-' + [Guid]::NewGuid().ToString('N'))
    try {
        if ($Latest) {
            Test-LatestDownload -Connection $connection -TempRoot $tempRoot
        } elseif ($LocalDir) {
            $releaseVersion = Resolve-ReleaseVersion -Tag $Tag -Version $Version -IsLocal $true -AllowRehearsal ([bool]$Rehearsal)
            Test-LocalRelease -Tag $Tag -ReleaseVersion $releaseVersion
        } else {
            if (-not $Tag) { Write-BuildFailure -What 'No release tag was given.' -Why 'It names the release to read from the API.' -Fix 'Pass -Tag vX.Y.Z.' }
            $releaseVersion = Resolve-ReleaseVersion -Tag $Tag -Version $Version -IsLocal $false -AllowRehearsal ([bool]$Rehearsal)
            Test-ApiRelease -Connection $connection -Tag $Tag -ReleaseVersion $releaseVersion -TempRoot $tempRoot
        }
    } finally {
        Remove-BuildTempTree -Path $tempRoot
    }
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
