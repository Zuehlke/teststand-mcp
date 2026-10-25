# ============================================================================
#  ReleaseTestSupport.ps1 - what the release test files share (dot-source it AFTER TestHarness.ps1):
#  the constants of the release contract, the zip and release fixtures, a fake GitHub at the HTTP boundary
#  of the verifier, and the guard that keeps a real CI value of GITHUB_* out of the scripts under test.
#  Used by ReleaseAssets.Tests.ps1, ReleaseVerifier.Tests.ps1 and ReleaseVerdict.Tests.ps1.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $RepoRoot 'build'
$CommonModulePath = Join-Path $BuildDir 'BuildCommon.psm1'
$ModulePath = Join-Path $BuildDir 'ReleaseAssets.psm1'
$VerdictModulePath = Join-Path $BuildDir 'ReleaseVerdict.psm1'
$DownloadModulePath = Join-Path $BuildDir 'ReleaseDownload.psm1'
$TagGatePath = Join-Path $BuildDir 'Assert-ReleaseTag.ps1'
$VerifierPath = Join-Path $BuildDir 'Assert-PublishedRelease.ps1'
$MarketplacePath = Join-Path $RepoRoot '.claude-plugin\marketplace.json'
$OwnDownloadsFixturePath = Join-Path $PSScriptRoot 'fixtures\own-downloads.json'

$ExpectedRepoSlug = 'Zuehlke/teststand-mcp'
$ExpectedLatestUrl = 'https://github.com/Zuehlke/teststand-mcp/releases/latest/download/teststand-mcp.zip'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$ShaCrypto = [System.Security.Cryptography.SHA256]::Create()
$SampleCommit = ('0123456789abcdef' * 2) + '01234567'   # 40 hex characters
$ArabicIndicOne = [string][char]0x0661                  # a digit that \d matches in .NET but is not 0-9
$Utf8Bom = [byte[]](0xEF, 0xBB, 0xBF)

$BotLogin = 'github-actions[bot]'
$ApiAssetUrlFormat = 'https://api.github.com/repos/Zuehlke/teststand-mcp/releases/assets/{0}'
$FirstAssetId = 1000
$ReleaseId = 777
$DefaultRetryDelay = 30
$FakeToken = 'ghs_FAKE_TOKEN_FOR_TESTS'
$ListPageSize = 100
$MaxListPages = 10
$NeverHelps = 'Running the verifier again will not help'
$MayHelp = 'Running the verifier again may help'
$ZeroHash = '0' * 64
$FixtureRunId = '1234567890'
$FixtureVerifyDate = '2026-08-20'
$FixtureLatestDate = '2026-08-21'

function Assert-NoBom {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq $Utf8Bom[0] -and $bytes[1] -eq $Utf8Bom[1] -and $bytes[2] -eq $Utf8Bom[2])) "$Path starts with a UTF-8 BOM"
}

# The record's flat rows ("date|asset|draft|published"), for comparison with the shared fixture.
function Get-OwnDownloadRows {
    param($Record)
    $rows = @()
    foreach ($date in $Record.Counts.Keys) {
        foreach ($asset in $Record.Counts[$date].Keys) {
            $cell = $Record.Counts[$date][$asset]
            $rows += "$date|$asset|$($cell['draft'])|$($cell['published'])"
        }
    }
    return (($rows | Sort-Object) -join "`n")
}

function Assert-RecordMatchesFixture {
    param($Record)
    $fixture = Read-OwnDownloads -Path $OwnDownloadsFixturePath
    Assert-Equal $fixture.RunId $Record.RunId 'run_id'
    Assert-Equal (Get-OwnDownloadRows $fixture) (Get-OwnDownloadRows $Record) 'counts per date, asset and phase'
}

function Get-Sha256Of {
    param([byte[]]$Bytes)
    return ([BitConverter]::ToString($ShaCrypto.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
}

# --- Zip and release fixtures -------------------------------------------------------------------------
# Zip entries are named with '/'. ZipFile.CreateFromDirectory is NOT used: under Windows PowerShell 5.1 it
# writes '\' separators (measured). Entries are written verbatim, so a test can ask for a bad name.
function New-ZipFromEntries {
    param([string]$ZipPath, [object[]]$Entries)
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Create($ZipPath)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $Entries) {
                $zipEntry = $archive.CreateEntry($entry.Name)
                $entryStream = $zipEntry.Open()
                try { $entryStream.Write($entry.Bytes, 0, $entry.Bytes.Length) } finally { $entryStream.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-DirectoryEntries {
    param([string]$SourceDir)
    $root = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force | ForEach-Object {
        @{ Name = $_.FullName.Substring($root.Length).Replace('\', '/'); Bytes = [IO.File]::ReadAllBytes($_.FullName) }
    })
}

function New-ZipFromDirectory {
    param([string]$SourceDir, [string]$ZipPath)
    New-ZipFromEntries -ZipPath $ZipPath -Entries (Get-DirectoryEntries $SourceDir)
}

# A small staged tree: nested folders, a name with a space and non-text bytes.
function New-StagedTree {
    $dir = Join-Path (New-TempDir) 'staged'
    Write-TextFile (Join-Path $dir 'VERSION.txt') "tag=v1.9.0`n"
    Write-TextFile (Join-Path $dir '.claude-plugin\plugin.json') '{"name":"teststand-mcp"}'
    Write-TextFile (Join-Path $dir 'bin\appsettings.json') '{}'
    Write-TextFile (Join-Path $dir 'bin\scripts\with space.txt') 'spaced'
    [IO.File]::WriteAllBytes((Join-Path $dir 'bin\TestStandMCP.exe'), [byte[]](0, 1, 2, 253, 254, 255, 0, 77))
    return $dir
}

function Set-ZeroDigest {
    param($Fixture)
    Write-TextFile (Join-Path $Fixture.Dir 'teststand-mcp.sha256') ($ZeroHash + '  teststand-mcp.zip' + "`n")
}

# Rewrites the manifest asset: $Edit receives the manifest's lines and returns the new lines.
function Edit-ManifestLines {
    param($Fixture, [scriptblock]$Edit)
    $path = Join-Path $Fixture.Dir 'teststand-mcp.manifest.sha256'
    $lines = @([IO.File]::ReadAllText($path, $Utf8NoBom) -split "`n" | Where-Object { $_ })
    $edited = @(& $Edit $lines)
    Write-TextFile $path (($edited -join "`n") + "`n")
}

# A release as a user would download it: the four assets of version 1.9.0 in one directory.
# -ExtraEntries puts additional entries (verbatim names) into the zip that the manifest does not know.
function New-ReleaseFixture {
    param([string]$Version = '1.9.0', [object[]]$ExtraEntries = @(), [switch]$CorruptZip)
    $staged = New-StagedTree
    $dir = Join-Path (New-TempDir) 'assets'
    [void](New-Item -ItemType Directory -Path $dir)
    $zip = Join-Path $dir 'teststand-mcp.zip'
    if ($CorruptZip) { [IO.File]::WriteAllBytes($zip, [byte[]](80, 75, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8)) }
    else { New-ZipFromEntries -ZipPath $zip -Entries ((Get-DirectoryEntries $staged) + $ExtraEntries) }
    Write-TextFile (Join-Path $dir 'teststand-mcp.sha256') (New-ArchiveDigest -ArchivePath $zip)
    Write-TextFile (Join-Path $dir 'teststand-mcp.manifest.sha256') (New-ArchiveManifest -StagedDir $staged)
    Copy-Item -LiteralPath $zip -Destination (Join-Path $dir "teststand-mcp-v$Version.zip")
    return @{ Dir = $dir; Version = $Version; Tag = "v$Version"; Staged = $staged; Prefixes = @($ReleaseAssetPrefix) }
}

# A second plugin's four assets next to the fixture's (the x64 plugin by default), so one release carries
# eight: the same archive bytes under the other plugin's names, with a digest that names its own archive.
function Add-PluginAssets {
    param($Fixture, [string]$Prefix = 'teststand-mcp-x64')
    $own = Get-PluginAssetNames -Prefix $ReleaseAssetPrefix -Version $Fixture.Version
    $other = Get-PluginAssetNames -Prefix $Prefix -Version $Fixture.Version
    $zip = Join-Path $Fixture.Dir $other.Archive
    Copy-Item -LiteralPath (Join-Path $Fixture.Dir $own.Archive) -Destination $zip
    Copy-Item -LiteralPath $zip -Destination (Join-Path $Fixture.Dir $other.Versioned)
    Copy-Item -LiteralPath (Join-Path $Fixture.Dir $own.Manifest) -Destination (Join-Path $Fixture.Dir $other.Manifest)
    Write-TextFile (Join-Path $Fixture.Dir $other.Digest) (New-ArchiveDigest -ArchivePath $zip)
    $Fixture.Prefixes = @($Fixture.Prefixes) + $Prefix
    return $Fixture
}

function Get-AssetSizes {
    param($Fixture)
    $sizes = @{}
    foreach ($file in (Get-ChildItem -LiteralPath $Fixture.Dir -File)) { $sizes[$file.Name] = [int64]$file.Length }
    return $sizes
}

# The GitHub releases-list JSON: a decoy plus one release carrying the tag; -NoDecoy lists that one release alone
# (a one-element list is the shape that breaks code which forgets that a single item is still a list).
function New-ReleaseJsonText {
    param(
        [string]$Tag, [string[]]$AssetNames, [hashtable]$UploaderByAsset = @{}, [bool]$IsDraft = $true,
        [hashtable]$SizeByAsset = @{}, [hashtable]$IdByAsset = @{}, [string[]]$NoUploaderFor = @(),
        [switch]$NoDecoy, $ReleaseNumber = $ReleaseId   # not $Id: the asset loop below has a variable of that name (PowerShell names are case-insensitive)
    )
    $assets = @()
    for ($index = 0; $index -lt $AssetNames.Count; $index++) {
        $name = $AssetNames[$index]
        $login = $BotLogin
        if ($UploaderByAsset.ContainsKey($name)) { $login = $UploaderByAsset[$name] }
        $id = $FirstAssetId + $index
        if ($IdByAsset.ContainsKey($name)) { $id = $IdByAsset[$name] }
        $asset = @{ id = $id; name = $name; url = ($ApiAssetUrlFormat -f $id); uploader = @{ login = $login } }
        if ($NoUploaderFor -contains $name) { $asset.uploader = $null }
        if ($SizeByAsset.ContainsKey($name)) { $asset.size = $SizeByAsset[$name] }
        $assets += $asset
    }
    $decoy = @{ id = 1; tag_name = 'v0.0.1'; draft = $false; assets = @() }
    $release = @{ id = $ReleaseNumber; tag_name = $Tag; draft = $IsDraft; assets = $assets }
    $releases = if ($NoDecoy) { @($release) } else { @($decoy, $release) }
    return (ConvertTo-Json -InputObject $releases -Depth 6)
}

function Invoke-LocalVerify {
    param($Fixture, [hashtable]$UploaderByAsset = @{}, [switch]$NoReleaseJson, [hashtable]$Extra = @{}, [string[]]$ListedNames = @(), [switch]$NoDecoy, [switch]$ListNothing)
    $arguments = @{ Tag = $Fixture.Tag; LocalDir = $Fixture.Dir; AssetPrefixes = @($Fixture.Prefixes) -join ',' }
    if (-not $NoReleaseJson) {
        $names = if ($ListNothing) { @() } elseif ($ListedNames.Count -gt 0) { $ListedNames } else { @(Get-RequiredAssetNames -Version $Fixture.Version -Prefixes $Fixture.Prefixes) }
        $jsonPath = Join-Path (New-TempDir) 'release.json'
        Write-TextFile $jsonPath (New-ReleaseJsonText -Tag $Fixture.Tag -AssetNames $names -UploaderByAsset $UploaderByAsset -NoDecoy:$NoDecoy)
        $arguments.ReleaseJson = $jsonPath
    }
    foreach ($key in $Extra.Keys) { $arguments[$key] = $Extra[$key] }
    return Invoke-Script $VerifierPath $arguments
}

# A fake GitHub at the HTTP boundary. $Plan decides, per asset and per download count, whether a download
# fails (returns the failure text) or succeeds ($null). The state records every request. -Pages maps a
# page number to its releases-list JSON (default: page 1 is the release of the fixture).
function New-FakeApi {
    param(
        $Fixture, [scriptblock]$Plan = { param($Name, $Count) $null }, [string]$ReleasesFailure = $null,
        [hashtable]$Pages = $null, [hashtable]$ReleaseOptions = @{}
    )
    $names = @(Get-RequiredAssetNames -Version $Fixture.Version -Prefixes $Fixture.Prefixes)
    if ($null -eq $Pages) {
        $options = @{ Tag = $Fixture.Tag; AssetNames = $names; SizeByAsset = (Get-AssetSizes $Fixture) }
        foreach ($key in $ReleaseOptions.Keys) { $options[$key] = $ReleaseOptions[$key] }
        $Pages = @{ 1 = (New-ReleaseJsonText @options) }
    }
    $nameById = @{}
    foreach ($pageText in $Pages.Values) {
        foreach ($release in @($pageText | ConvertFrom-Json | ForEach-Object { $_ })) {
            foreach ($asset in @($release.assets)) { $nameById[[string]$asset.id] = $asset.name }
        }
    }
    $dir = $Fixture.Dir
    $state = @{ Calls = (New-Object System.Collections.ArrayList); Downloads = @{} }
    $http = {
        param($Uri, $Headers, $OutFile, $TimeoutSeconds)
        [void]$state.Calls.Add(@{ Uri = $Uri; Headers = $Headers; OutFile = $OutFile; TimeoutSeconds = $TimeoutSeconds })
        $name = $null
        if ($Uri -match '/releases\?.*page=(\d+)') {
            if ($ReleasesFailure) { throw $ReleasesFailure }
            if ($Pages.ContainsKey([int]$Matches[1])) { return $Pages[[int]$Matches[1]] }
            return '[]'
        }
        if ($Uri -match '/releases/assets/(\d+)\z') { $name = $nameById[$Matches[1]] }
        elseif ($Uri -match '/releases/latest/download/([^/]+)\z') { $name = $Matches[1] }
        if ($null -eq $name) { throw "unexpected request $Uri" }
        if (-not $state.Downloads.ContainsKey($name)) { $state.Downloads[$name] = 0 }
        $state.Downloads[$name] = $state.Downloads[$name] + 1
        $failure = & $Plan $name $state.Downloads[$name]
        if ($failure) { throw $failure }
        Copy-Item -LiteralPath (Join-Path $dir $name) -Destination $OutFile
        return
    }.GetNewClosure()
    return @{ Http = $http; State = $state }
}

function Invoke-ApiVerify {
    param($Fixture, $Api, [string]$OwnDownloadsPath, [hashtable]$Extra = @{}, [switch]$ForceDraft, [switch]$OmitToken, [string]$Tag)
    $delays = New-Object System.Collections.ArrayList
    $delay = { param([int]$Seconds) [void]$delays.Add($Seconds) }.GetNewClosure()
    if (-not $Tag) { $Tag = $Fixture.Tag }
    $arguments = @{ Repo = $ExpectedRepoSlug; Tag = $Tag; HttpGet = $Api.Http; Delay = $delay; AssetPrefixes = @($Fixture.Prefixes) -join ',' }
    if (-not $OmitToken) { $arguments.Token = $FakeToken }
    if ($ForceDraft) { $arguments.Draft = $true }
    if ($OwnDownloadsPath) { $arguments.OwnDownloadsPath = $OwnDownloadsPath }
    foreach ($key in $Extra.Keys) { $arguments[$key] = $Extra[$key] }
    $run = Invoke-Script $VerifierPath $arguments
    $run.Delays = @($delays)
    return $run
}

function Invoke-LatestVerify {
    param($Api, [hashtable]$Extra = @{})
    $delays = New-Object System.Collections.ArrayList
    $delay = { param([int]$Seconds) [void]$delays.Add($Seconds) }.GetNewClosure()
    $arguments = @{ Latest = $true; HttpGet = $Api.Http; Delay = $delay; AssetPrefixes = 'teststand-mcp' }
    foreach ($key in $Extra.Keys) { $arguments[$key] = $Extra[$key] }
    $run = Invoke-Script $VerifierPath $arguments
    $run.Delays = @($delays)
    return $run
}

function Get-AssetRequests {
    param($Api)
    return @($Api.State.Calls | Where-Object { $_.Uri -match '/releases/assets/' })
}

# 100 decoy releases: a full page of the releases list.
function New-FullPageJson {
    $page = @()
    for ($index = 1; $index -le $ListPageSize; $index++) { $page += @{ id = (5000 + $index); tag_name = "v0.0.$index"; draft = $false; assets = @() } }
    return (ConvertTo-Json -InputObject $page -Depth 4)
}

# --- The environment ------------------------------------------------------------------------------------
# The scripts under test write step outputs and read GITHUB_* variables: never let a real CI value leak in or out.
function Enter-ReleaseTestEnvironment {
    $script:SavedReleaseEnvironment = @{}
    foreach ($name in @('GITHUB_OUTPUT', 'GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_TOKEN')) { $script:SavedReleaseEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
    [Environment]::SetEnvironmentVariable('GITHUB_OUTPUT', (Join-Path (New-TempDir) 'shared_github_output'))
    foreach ($name in @('GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_TOKEN')) { [Environment]::SetEnvironmentVariable($name, $null) }
}

function Exit-ReleaseTestEnvironment {
    foreach ($name in $script:SavedReleaseEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $script:SavedReleaseEnvironment[$name]) }
}
