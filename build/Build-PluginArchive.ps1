# ============================================================================
#  Build-PluginArchive.ps1 - builds one plugin archive (teststand-mcp.zip, or teststand-mcp-x64.zip
#  with -Architecture x64) from this checkout.
#  First it refuses a malformed version, tag, commit or layout; then it runs the steps listed in
#  $Steps below, in order (the numbering is computed from that list):
#    - the source checks that need no build (skill sources, tool classification, the plugin.json
#      version placeholder);
#    - dotnet publish in the chosen exe layout, and the proof that the exe carries the version;
#    - staging: the archive tree (converted agents, stamped plugin.json, bin/CLAUDE.md with the user
#      rules of the skill, VERSION.txt) and every assertion on it;
#    - the smoke tests of the STAGED exe: --version, --list-tools --json, a stdio initialize round trip;
#    - the zip (forward-slash entry names, the commit time on every entry) and its entry-name check;
#    - the versioned copy, the digest and the per-file manifest (names from ReleaseAssets.psm1);
#    - build\Assert-PublishedRelease.ps1 in local mode on the result.
#  The assets (<prefix>.zip, <prefix>-v<X.Y.Z>.zip, .sha256, .manifest.sha256; the prefix is the
#  plugin name) end up in -OutDir next to the stage\ tree they were built from, so build the two
#  architectures into two directories.
#  Every failure is three parts - what is wrong, why it matters, the command that fixes it -
#  printed to the host, with exit code 1.
#
#  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File build\Build-PluginArchive.ps1 -Version 0.0.0-ci
#  -Version  X.Y.Z, or X.Y.Z-suffix for a local / pull-request build (0.0.0-ci).
#  -Tag      defaults to v<Version> (v<Version>-rehearsal is accepted too).
#  -Commit   the full 40-character SHA; defaults to git rev-parse HEAD.
#  -OutDir   where everything is written (default: <repo>\artifacts, which git ignores).
#  -Layout   folder (default, see docs/validation/self-contained-x86.md), single-file, framework-dependent.
#  -Architecture  x86 (default: the teststand-mcp plugin, for 32-bit TestStand) or x64 (teststand-mcp-x64).
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Tag,
    [string]$Commit,
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'),
    [string]$Layout,
    [string]$Architecture = 'x86'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'PluginIdentity.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'PluginStaging.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'PluginArchive.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'PluginSmoke.psm1') -Force

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDirName = 'publish'
$StageDirName = 'stage'
$StepClock = [Diagnostics.Stopwatch]::StartNew()
$GitTimeoutSeconds = 60   # the same budget as PluginArchive.psm1's git calls

# The commit of the checkout, when none was given.
function Get-HeadCommit {
    $result = Invoke-Git -RepoRoot $RepoRoot -Arguments @('rev-parse', 'HEAD') -TimeoutSeconds $GitTimeoutSeconds `
        -Why 'No -Commit was given, and the commit is stamped into the exe and VERSION.txt.' -Fix 'Run from a git checkout, or pass -Commit with the 40-character SHA.'
    return $result.Stdout.Trim()
}

try {
    if (-not $Layout) { $Layout = Get-DefaultArchiveLayout }
    if (-not $Tag) { $Tag = "v$Version" }
    if (-not $Commit) { $Commit = Get-HeadCommit }
    Assert-BuildVersion $Version
    Assert-BuildTag -Tag $Tag -Version $Version
    Assert-BuildCommit $Commit
    $identity = Get-PluginIdentity -Architecture $Architecture
    $assetNames = Get-PluginAssetNames -Prefix $identity.assetPrefix -Version (Get-VersionCore $Version)
    $layoutSettings = Get-ArchiveLayout $Layout
    $OutDir = Resolve-BuildPath $OutDir
    $publishDir = Join-Path $OutDir $PublishDirName
    $stageDir = Join-Path $OutDir $StageDirName
    $zipPath = Join-Path $OutDir $assetNames.Archive
    $release = New-ReleaseRecord -Tag $Tag -Version $Version -Commit $Commit -Architecture $Architecture
    $stageSnapshot = $null
    $publishedExe = $null
    Write-Host "Packaging $($identity.name) ($Architecture) version $Version ($Tag, commit $Commit), layout $Layout, into $OutDir"

    $Steps = @(
        @{ Name = 'Checking the sources'; Run = {
            Invoke-SourceChecks -RepoRoot $RepoRoot
        } }
        @{ Name = "Publishing the exe ($Layout)"; Run = {
            [void](New-Item -ItemType Directory -Path $OutDir -Force)
            $script:publishedExe = Publish-PluginExe -RepoRoot $RepoRoot -Layout $Layout -Version $Version -Commit $Commit -PublishDir $publishDir -Architecture $Architecture
        } }
        @{ Name = 'Checking the published exe'; Run = {
            Assert-PublishedVersion -ExePath $publishedExe -Version $Version -Commit $Commit
        } }
        @{ Name = 'Staging the plugin tree'; Run = {
            New-StagedPlugin -RepoRoot $RepoRoot -PublishDir $publishDir -StageDir $stageDir -Layout $layoutSettings -Release $release
            Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stageDir -PublishDir $publishDir -Layout $layoutSettings -Release $release
            $script:stageSnapshot = Get-StageSnapshot -StageDir $stageDir
        } }
        @{ Name = 'Smoke-testing the staged exe'; Run = {
            Invoke-SmokeTests -StageDir $stageDir -Release $release -RepoRoot $RepoRoot
        } }
        @{ Name = "Zipping $($assetNames.Archive)"; Run = {
            Assert-StageUnchanged -StageDir $stageDir -Snapshot $stageSnapshot
            New-PluginZip -StageDir $stageDir -ZipPath $zipPath -Timestamp (Get-CommitTimestamp -RepoRoot $RepoRoot -Commit $Commit)
            Assert-PluginZip -ZipPath $zipPath -StageDir $stageDir
        } }
        @{ Name = 'Writing the versioned copy, the digest and the manifest'; Run = {
            Write-ReleaseAssets -ZipPath $zipPath -ManifestText $stageSnapshot -Version $Version -Prefix $identity.assetPrefix
        } }
        @{ Name = 'Verifying the output the way the release is verified'; Run = {
            Assert-LocalRelease -OutDir $OutDir -Version $Version -Prefix $identity.assetPrefix
        } }
    )

    try {
        for ($index = 0; $index -lt $Steps.Count; $index++) {
            Write-Host ("[{0}/{1}] {2} (+{3:N1}s)" -f ($index + 1), $Steps.Count, $Steps[$index].Name, $StepClock.Elapsed.TotalSeconds)
            & $Steps[$index].Run
        }
    } finally {
        Remove-BuildTempTree -Path $publishDir
    }

    Write-Host ("Done in {0:N1}s: {1} ({2:N1} MB) and its digest and manifest are in {3}" -f $StepClock.Elapsed.TotalSeconds, $assetNames.Archive, ((Get-Item -LiteralPath $zipPath).Length / 1MB), $OutDir)
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
