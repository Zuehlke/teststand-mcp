# ============================================================================
#  PluginArchive.psm1 - what build/Build-PluginArchive.ps1 does, one function per stage so each
#  stage can be tested on its own:
#    1. the inputs (version, tag, commit, layout) and the source checks that need no build;
#    2. dotnet publish in the chosen exe layout, and the proof that the exe carries the version;
#    3. the zip (deterministic bytes, entry-name guard), the release assets and the local run of the
#       release verifier.
#  The archive's directory tree (staging and its assertions) is in PluginStaging.psm1, the smoke
#  tests of the staged exe in PluginSmoke.psm1. The ONE layout table below holds everything that
#  differs per exe layout.
#  Every failure goes through Write-BuildFailure (What / Why / Fix).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7 (no PowerShell 7 is installed on the
#  maintainer's station, so nothing here may need it).
# ============================================================================
Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')
Import-Module (Join-Path $PSScriptRoot 'ToolClassification.psm1')
Import-Module (Join-Path $PSScriptRoot 'PluginIdentity.psm1')
Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1')
Import-Module (Join-Path $PSScriptRoot 'PluginStaging.psm1')

$script:SkillCheckScript = Join-Path $PSScriptRoot 'Test-SkillSources.ps1'
$script:VerifierScript = Join-Path $PSScriptRoot 'Assert-PublishedRelease.ps1'

$script:ProjectFile = 'TestStandMCP.csproj'
$script:BuildConfiguration = 'Release'
$script:PublishTimeoutSeconds = 900
$script:SkillCheckTimeoutSeconds = 180
$script:VerifierTimeoutSeconds = 600
$script:GitTimeoutSeconds = 60
$script:SkillCheckExcerptLines = 80
$script:PublishExcerptLines = 25
$script:PublishCommandHintFormat = 'dotnet publish TestStandMCP.csproj -c Release -p:Platform={0} -r {1}'

# ----------------------------------------------------------------------------
#  The exe layouts. 'folder' is the default (docs/validation/self-contained-x86.md): the
#  TestStand engine resolves the .NET runtime from the host's runtimeconfig.json, so a layout that
#  has none on disk is not self-contained against the engine. Everything that differs per layout:
#    PublishArguments : the extra dotnet publish arguments;
#    BinRequired      : files bin/ must hold besides the exe and appsettings.json;
#    BinForbidden     : files bin/ must NOT hold (they would show a different layout was built).
# ----------------------------------------------------------------------------
$script:DefaultLayout = 'folder'
$script:ArchiveLayouts = [ordered]@{
    'folder'              = @{ PublishArguments = @('--self-contained', 'true'); BinRequired = @('TestStandMCP.runtimeconfig.json', 'coreclr.dll'); BinForbidden = @() }
    'single-file'         = @{ PublishArguments = @('--self-contained', 'true', '-p:PublishSingleFile=true'); BinRequired = @(); BinForbidden = @('TestStandMCP.runtimeconfig.json', 'coreclr.dll') }
    'framework-dependent' = @{ PublishArguments = @('--self-contained', 'false'); BinRequired = @('TestStandMCP.runtimeconfig.json'); BinForbidden = @('coreclr.dll') }
}

function Get-ArchiveLayoutName {
    [CmdletBinding()]
    param()
    return @($script:ArchiveLayouts.Keys)
}

function Get-DefaultArchiveLayout {
    [CmdletBinding()]
    param()
    return $script:DefaultLayout
}

function Get-ArchiveLayout {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    if (-not $script:ArchiveLayouts.Contains($Name)) {
        Write-BuildFailure -What "Unknown exe layout '$Name'." `
            -Why 'The layout decides how the exe is published and which files the archive must hold, so there is no safe guess.' `
            -Fix "Pass -Layout with one of: $((Get-ArchiveLayoutName) -join ', ') (default: $script:DefaultLayout)."
    }
    return $script:ArchiveLayouts[$Name]
}

# ----------------------------------------------------------------------------
#  Inputs
# ----------------------------------------------------------------------------
$script:VersionPattern = '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z][0-9A-Za-z.-]*)?\z'

# X.Y.Z with an optional pre-release suffix (0.0.0-ci for a local or pull-request build).
function Assert-BuildVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
    if ($Version -cnotmatch $script:VersionPattern) {
        Write-BuildFailure -What "The version '$Version' is not X.Y.Z (or X.Y.Z-suffix)." `
            -Why 'The version is stamped into the exe, the plugin manifest and the archive names; a malformed one would publish a release nobody can update to.' `
            -Fix 'Pass -Version 1.9.0 (no leading v), or -Version 0.0.0-ci for a local build.'
    }
}

# The numeric X.Y.Z of a version: the release tag format, the versioned archive name and the release
# verifier know only that part, so a pre-release build (0.0.0-ci) names its assets after it.
function Get-VersionCore {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Version)
    Assert-BuildVersion $Version
    return ($Version -split '-', 2)[0]
}

function Assert-BuildCommit {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Commit)
    if ($Commit -cnotmatch $ReleaseCommitPattern) {
        Write-BuildFailure -What "The commit '$Commit' is not a full 40-character SHA." `
            -Why 'The commit is stamped into the exe (SourceRevisionId) and into VERSION.txt, and --version prints it, so an abbreviation could not be told apart later.' `
            -Fix 'Pass -Commit with the output of: git rev-parse HEAD'
    }
}

# The tag stamped into VERSION.txt: exactly v<version>, or its rehearsal v<version>-rehearsal.
function Assert-BuildTag {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Tag, [Parameter(Mandatory)][string]$Version)
    $accepted = @("v$Version", "v$Version$ReleaseRehearsalSuffix")
    if ($accepted -cnotcontains $Tag) {
        Write-BuildFailure -What "The tag '$Tag' does not belong to version $Version (expected $($accepted -join ' or '))." `
            -Why 'VERSION.txt and --version print the tag next to the version; a tag that disagrees with it would mislabel the release.' `
            -Fix "Pass -Tag v$Version, or omit -Tag."
    }
}

# ----------------------------------------------------------------------------
#  Source checks: everything that can be refused before a minute-long publish
# ----------------------------------------------------------------------------
# The skill source check (Test-SkillSources.ps1): the rules skill, its imports and the moved headings.
function Assert-SkillSources {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    $result = Invoke-BuildScript -ScriptPath $script:SkillCheckScript -ArgumentList @('-RepoRoot', $RepoRoot) -TimeoutSeconds $script:SkillCheckTimeoutSeconds
    Assert-ChildOutcome -Result $result -Name 'The skill source check (build\Test-SkillSources.ps1)' -TimeoutSeconds $script:SkillCheckTimeoutSeconds `
        -Why 'The archive ships the teststand-rules skill and a bin/CLAUDE.md generated from it; a skill that breaks its own source rules would publish rules that disagree with the repository.' `
        -Fix 'Run powershell -NoProfile -File build\Test-SkillSources.ps1, fix what it reports, and package again.' -Count $script:SkillCheckExcerptLines
}

# Every tool the server registers is on purpose allowed or prompted (plugin/hooks/tool-classes.json).
function Assert-AllToolsClassified {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ClassesPath, [Parameter(Mandatory)][string]$RegistryPath)
    Assert-ToolClassification -Classes (Read-ToolClasses -Path $ClassesPath) `
        -RegisteredTools (Get-RegisteredToolNames -RegistryPath $RegistryPath) `
        -ClassesPath $ClassesPath -ToolParameters (Get-RegisteredToolParameters -RegistryPath $RegistryPath)
}

function Assert-ManifestPlaceholder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath)
    [void](Find-VersionPlaceholder -Text (Read-Utf8Text $ManifestPath) -Path $ManifestPath)
}

# All source checks, in order, before anything is built.
function Invoke-SourceChecks {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    $tools = Get-ToolSourcePath -RepoRoot $RepoRoot
    Assert-SkillSources -RepoRoot $RepoRoot
    Assert-AllToolsClassified -ClassesPath $tools.Classes -RegistryPath $tools.Registry
    Assert-ManifestPlaceholder -ManifestPath (Get-PluginSourcePath -RepoRoot $RepoRoot).PluginJson
}

# ----------------------------------------------------------------------------
#  Publish
# ----------------------------------------------------------------------------
# dotnet publish into a fresh directory; returns the path of the published exe.
function Publish-PluginExe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Layout,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][string]$PublishDir,
        [string]$Architecture = 'x86'
    )
    $layoutSettings = Get-ArchiveLayout $Layout
    $identity = Get-PluginIdentity -Architecture $Architecture
    $publishCommandHint = $script:PublishCommandHintFormat -f $identity.buildPlatform, $identity.runtimeIdentifier
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        Write-BuildFailure -What 'dotnet was not found on the PATH.' `
            -Why 'The exe is built with dotnet publish.' `
            -Fix 'Install the .NET SDK (https://dotnet.microsoft.com/download), open a new shell and package again.'
    }
    Remove-BuildTempTree -Path $PublishDir
    if (Test-Path -LiteralPath $PublishDir) {
        Write-BuildFailure -What "The old publish directory $PublishDir could not be removed." `
            -Why 'Publishing over leftovers could ship files of an earlier build.' `
            -Fix 'Close whatever holds files in it (a running TestStandMCP.exe from that folder) and delete it by hand.'
    }
    $arguments = @('publish', (Join-Path $RepoRoot $script:ProjectFile), '-c', $script:BuildConfiguration,
        "-p:Platform=$($identity.buildPlatform)", '-r', $identity.runtimeIdentifier) + @($layoutSettings.PublishArguments) +
        @("-p:Version=$Version", "-p:SourceRevisionId=$Commit", '-o', $PublishDir, '--nologo', '-v:minimal', '--disable-build-servers')
    $result = Invoke-ChildProcess -FilePath $dotnet.Source -ArgumentList $arguments -WorkingDirectory $RepoRoot -TimeoutSeconds $script:PublishTimeoutSeconds
    Assert-ChildOutcome -Result $result -Name 'dotnet publish' -TimeoutSeconds $script:PublishTimeoutSeconds `
        -Why 'Without the exe nothing can be packaged. A timeout may be transient (a busy machine, a slow NuGet feed); a build error is not.' `
        -Fix "Fix the build error above. To reproduce: $publishCommandHint" -Mode Last -Count $script:PublishExcerptLines
    $exePath = Get-PublishedExePath -PublishDir $PublishDir
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        Write-BuildFailure -What "dotnet publish succeeded but $exePath is not there." `
            -Why 'The apphost is what the plugin starts (plugin/.mcp.json names bin/TestStandMCP.exe).' `
            -Fix 'Check that TestStandMCP.csproj still has OutputType Exe and AssemblyName TestStandMCP.'
    }
    return $exePath
}

# The exe's ProductVersion is the version stamped at build time: "<version>+<commit>".
function Assert-PublishedVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExePath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit
    )
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-BuildPath $ExePath)).ProductVersion
    $expected = "$Version+$Commit"
    if (-not [string]::Equals($actual, $expected, [StringComparison]::Ordinal)) {
        Write-BuildFailure -What "The published exe reports ProductVersion '$actual' but version $Version at commit $Commit was requested (expected '$expected')." `
            -Why 'The exe is what users run and what --version, serverInfo and the startup log report; shipping one that names another version would make every later update check wrong.' `
            -Fix 'Check that nothing overrides the Version or SourceRevisionId MSBuild properties (Directory.Build.props, environment variables), then package again.'
    }
}

# ----------------------------------------------------------------------------
#  The release record: what VERSION.txt carries
# ----------------------------------------------------------------------------
# The URL of the CI run that builds the archive, or 'local' outside GitHub Actions.
function Get-RunUrl {
    [CmdletBinding()]
    param()
    if ($env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) {
        return "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)"
    }
    return 'local'
}

function New-ReleaseRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Commit, [string]$Architecture = 'x86')
    return @{
        tag     = $Tag
        version = $Version
        commit  = $Commit
        arch    = (Get-PluginIdentity -Architecture $Architecture).buildPlatform
        built   = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
        run     = (Get-RunUrl)
    }
}

# The commit's own timestamp: every zip entry carries it, so two packagings of one commit are one archive.
function Get-CommitTimestamp {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Commit)
    $result = Invoke-Git -RepoRoot $RepoRoot -Arguments @('show', '-s', '--format=%cI', "$Commit^{commit}") -TimeoutSeconds $script:GitTimeoutSeconds `
        -Why 'Every entry of the zip is stamped with the commit time, so the archive of one commit has one set of bytes; the commit must exist in this checkout.' `
        -Fix 'Pass -Commit with a commit of this checkout (git rev-parse HEAD), after fetching it if needed.'
    return [DateTimeOffset]::Parse($result.Stdout.Trim(), [Globalization.CultureInfo]::InvariantCulture)
}

# ----------------------------------------------------------------------------
#  Zip, release assets, local verification
# ----------------------------------------------------------------------------
# The archive, written entry by entry with explicit forward-slash names and the same timestamp on every
# entry. ZipFile.CreateFromDirectory is not used: it writes backslash names on Windows PowerShell 5.1
# (.NET Framework), which the zip specification does not allow and which extract into the wrong folders
# elsewhere.
function New-PluginZip {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$StageDir, [Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][DateTimeOffset]$Timestamp)
    $zipFullPath = Resolve-BuildPath $ZipPath
    $stageFullPath = Resolve-BuildPath $StageDir
    if (Test-Path -LiteralPath $zipFullPath) { Remove-Item -LiteralPath $zipFullPath -Force }
    $stream = [IO.File]::Open($zipFullPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($name in (Get-RelativeFileList $stageFullPath)) {
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $Timestamp
                $entryStream = $entry.Open()
                try {
                    $source = [IO.File]::OpenRead((Join-Path $stageFullPath $name))
                    try { $source.CopyTo($entryStream) } finally { $source.Dispose() }
                } finally { $entryStream.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

# The finished archive: no entry name with a backslash, and exactly the files of the staged tree.
function Assert-PluginZip {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string]$StageDir)
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-BuildPath $ZipPath))
    try { $names = [string[]]@($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
    $backslashed = @($names | Where-Object { $_.Contains('\') })
    if ($backslashed.Count -gt 0) {
        Write-BuildFailure -What "The archive holds $($backslashed.Count) entry name(s) with a backslash, for example '$($backslashed[0])'." `
            -Why 'Zip entry names must use "/". A backslash name extracts as one oddly named file on other platforms and breaks the manifest check, so the release verifier would reject the archive.' `
            -Fix 'Write the zip with New-PluginZip (build\PluginArchive.psm1: explicit forward-slash entry names), never with ZipFile.CreateFromDirectory on Windows PowerShell 5.1.'
    }
    $staged = Get-RelativeFileList $StageDir
    $unexpected = @($names | Where-Object { $staged -cnotcontains $_ })
    $missing = @($staged | Where-Object { $names -cnotcontains $_ })
    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        Write-BuildFailure -What "The archive holds $($names.Count) entries but the staged tree has $($staged.Count) files (unexpected: $(($unexpected | Select-Object -First 3) -join ', '); missing: $(($missing | Select-Object -First 3) -join ', '))." `
            -Why 'The archive must be exactly the staged, asserted tree; anything else was never checked.' `
            -Fix 'Package again with build\Build-PluginArchive.ps1; if it repeats, New-PluginZip (build\PluginArchive.psm1) is not zipping the stage folder.'
    }
}

# The version-named copy, the digest and the per-file manifest next to the archive. Every name comes
# from ReleaseAssets.psm1; $Prefix is the plugin's asset prefix. $ManifestText is the hash list of the staged tree.
function Write-ReleaseAssets {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string]$ManifestText, [Parameter(Mandatory)][string]$Version, [string]$Prefix = $ReleaseAssetPrefix)
    $zipFullPath = Resolve-BuildPath $ZipPath
    $outDir = Split-Path -Parent $zipFullPath
    $names = Get-PluginAssetNames -Prefix $Prefix -Version (Get-VersionCore $Version)
    Copy-Item -LiteralPath $zipFullPath -Destination (Join-Path $outDir $names.Versioned) -Force
    [IO.File]::WriteAllText((Join-Path $outDir $names.Digest), (New-ArchiveDigest -ArchivePath $zipFullPath), (Get-Utf8NoBomEncoding))
    [IO.File]::WriteAllText((Join-Path $outDir $names.Manifest), $ManifestText, (Get-Utf8NoBomEncoding))
}

# The release verifier (spec AC-8) in local mode on the asset directory: it must accept the output.
function Assert-LocalRelease {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OutDir, [Parameter(Mandatory)][string]$Version, [string]$Prefix = $ReleaseAssetPrefix)
    $result = Invoke-BuildScript -ScriptPath $script:VerifierScript -TimeoutSeconds $script:VerifierTimeoutSeconds `
        -ArgumentList @('-LocalDir', $OutDir, '-Version', (Get-VersionCore $Version), '-AssetPrefixes', $Prefix)
    Assert-ChildOutcome -Result $result -Name "The release verifier (build\Assert-PublishedRelease.ps1, local mode, $OutDir)" -TimeoutSeconds $script:VerifierTimeoutSeconds `
        -Why 'The same verifier gates the real release; an archive it rejects here would be rejected there, after the tag is pushed.' `
        -Fix 'Fix what the verifier reports (it names the asset and the command), then package again.' -Count $script:PublishExcerptLines
}

Export-ModuleMember -Function Get-ArchiveLayoutName, Get-DefaultArchiveLayout, Get-ArchiveLayout,
    Assert-BuildVersion, Get-VersionCore, Assert-BuildCommit, Assert-BuildTag,
    Assert-SkillSources, Assert-AllToolsClassified, Assert-ManifestPlaceholder, Invoke-SourceChecks,
    Publish-PluginExe, Assert-PublishedVersion,
    Get-RunUrl, New-ReleaseRecord, Get-CommitTimestamp,
    New-PluginZip, Assert-PluginZip, Write-ReleaseAssets, Assert-LocalRelease
