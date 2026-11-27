# ============================================================================
#  TestStandMCP — pre-push test gate (invoked by .githooks/pre-push)
#  ---------------------------------------------------------------------------
#  1. Finds the installed TestStand bitnesses (none at all blocks the push).
#  2. Runs two script checks that otherwise fail only in CI (about a minute, and
#     before anything is stopped): plugin/hooks/hooks.json must match what
#     build/New-HooksJson.ps1 generates, and BuildPluginArchive.Tests.ps1 (which
#     holds the csproj-vs-staging-table drift test) must pass.
#  3. Terminates running TestStand processes (they hold COM engine handles and
#     make the integration tests flaky via file-sharing violations).
#  4. Runs the integration test suite once per installed TestStand bitness:
#     as x86 when 32-bit TestStand is installed, as x64 when 64-bit TestStand is
#     (each plugin drives only its own bitness, so each needs its own run).
#  Exits 0 only when EVERY run passes — any other exit code blocks the push, and
#  so does a station with no TestStand at all.
#  -ProgramFilesX86 / -ProgramFiles64 point the detection at other roots, and
#  -DryRun only prints the runs it would make (both for testing the gate itself).
# ============================================================================
param(
    [string]$ProgramFilesX86 = ${env:ProgramFiles(x86)},
    [string]$ProgramFiles64 = $env:ProgramW6432,
    [switch]$DryRun
)

# Let $LASTEXITCODE — not an exception — carry dotnet's result, even under
# PowerShell 7's native-command error handling.
$ErrorActionPreference = 'Continue'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Repo root = parent of the .githooks directory this script lives in.
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProj = Join-Path $repoRoot 'Test\TestExecution\TestStandMCP.IntegrationTests.csproj'

Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  pre-push gate: TestStandMCP integration tests' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan

if (-not (Test-Path $testProj)) {
    Write-Host "  ERROR: test project not found at $testProj" -ForegroundColor Red
    exit 1
}

# ── 1) Which bitnesses to test: one run per installed TestStand ─────────────
# The x86 run keeps the default build (no Platform), which is x86; x64 needs -p:Platform=x64.
$bitnesses = @(
    @{ Arch = 'x86'; Bits = 32; Root = $ProgramFilesX86; Platform = @() }
    @{ Arch = 'x64'; Bits = 64; Root = $ProgramFiles64;  Platform = @('-p:Platform=x64') }
)
$runs = @()
foreach ($bitness in $bitnesses) {
    $installed = $bitness.Root -and (Test-Path -Path (Join-Path $bitness.Root 'National Instruments\TestStand*\Bin\teapi.dll'))
    if ($installed) {
        $runs += $bitness
    } else {
        Write-Host ("  -> skipping the {0} run: {1}-bit TestStand is not installed (no TestStand*\Bin\teapi.dll under {2})" -f $bitness.Arch, $bitness.Bits, $bitness.Root) -ForegroundColor DarkGray
    }
}
if ($runs.Count -eq 0) {
    Write-Host '  FAIL: no TestStand is installed, so the integration tests cannot run - push aborted.' -ForegroundColor Red
    Write-Host '        Install 32-bit or 64-bit NI TestStand, or bypass in an emergency: git push --no-verify' -ForegroundColor DarkGray
    exit 1
}
if ($DryRun) {
    Write-Host '  dry run: would run the script checks (hooks.json, BuildPluginArchive.Tests.ps1)'
    Write-Host ('  dry run: would test as ' + (($runs | ForEach-Object { $_.Arch + $(if ($_.Platform) { ' (' + ($_.Platform -join ' ') + ')' }) }) -join ', then as '))
    exit 0
}

# ── 2) Script checks: a minute here instead of a red CI run after the push ──
# Under this same host only; CI covers the second host. They read the working tree.
# Measured 2026-11-27 with a probe hook: a push from a linked worktree exports an absolute
# GIT_DIR to pre-push (the main checkout exports none). The archive tests run
# 'git -C <temp fixture> init/add', which would then act on the real repository.
Remove-Item Env:GIT_DIR, Env:GIT_WORK_TREE, Env:GIT_INDEX_FILE, Env:GIT_COMMON_DIR, Env:GIT_OBJECT_DIRECTORY -ErrorAction SilentlyContinue
$powerShellExe = (Get-Process -Id $PID).Path
$scriptArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File')
$passLinePattern = '^PASS  '   # the case lines build/tests/TestHarness.ps1 prints

Write-Host '  -> checking plugin/hooks/hooks.json against build/New-HooksJson.ps1 ...' -ForegroundColor Yellow
$generatedHooksPath = Join-Path ([IO.Path]::GetTempPath()) ("hooks-{0}.json" -f [guid]::NewGuid())
$generatorOutput = @(& $powerShellExe @scriptArgs (Join-Path $repoRoot 'build\New-HooksJson.ps1') -OutputPath $generatedHooksPath 2>&1 | ForEach-Object { "$_" })
$generatorExitCode = $LASTEXITCODE
if ($generatorExitCode -ne 0 -or -not (Test-Path $generatedHooksPath)) {
    $generatorOutput | ForEach-Object { Write-Host "     $_" }
    Write-Host ("  FAIL: build/New-HooksJson.ps1 failed or wrote no file (exit {0}) - push aborted." -f $generatorExitCode) -ForegroundColor Red
    Remove-Item $generatedHooksPath -ErrorAction SilentlyContinue
    exit 1
}
# Compare without CRs: the generator writes LF, a checkout under core.autocrlf=true gives CRLF.
function Get-TextWithoutCR([string]$Path) { ([IO.File]::ReadAllText($Path)) -replace "`r", '' }
$committedHooksPath = Join-Path $repoRoot 'plugin\hooks\hooks.json'
$hooksJsonIsFresh = (Test-Path $committedHooksPath) -and
    ((Get-TextWithoutCR $generatedHooksPath) -ceq (Get-TextWithoutCR $committedHooksPath))
Remove-Item $generatedHooksPath -ErrorAction SilentlyContinue
if (-not $hooksJsonIsFresh) {
    Write-Host '  FAIL: plugin/hooks/hooks.json is stale or missing - push aborted.' -ForegroundColor Red
    Write-Host '        Run: powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\New-HooksJson.ps1, then commit hooks.json' -ForegroundColor DarkGray
    exit 1
}

Write-Host '  -> running build/tests/BuildPluginArchive.Tests.ps1 (about a minute) ...' -ForegroundColor Yellow
$archiveTestOutput = @(& $powerShellExe @scriptArgs (Join-Path $repoRoot 'build\tests\BuildPluginArchive.Tests.ps1') 2>&1 | ForEach-Object { "$_" })
$archiveExitCode = $LASTEXITCODE
if ($archiveExitCode -ne 0) {
    $archiveTestOutput | Where-Object { $_ -notmatch $passLinePattern } | ForEach-Object { Write-Host "     $_" }
    Write-Host ("  FAIL: BuildPluginArchive.Tests.ps1 did not pass (exit {0}) - push aborted." -f $archiveExitCode) -ForegroundColor Red
    Write-Host '        A new csproj output item needs an entry in Get-StagingTable (build/PluginStaging.psm1).' -ForegroundColor DarkGray
    exit 1
}
Write-Host '  PASS: script checks are green.' -ForegroundColor Green

# ── 3) Free the engine: stop processes that hold COM handles ────────────────
function Stop-EngineProcesses {
    $killed = $false
    foreach ($name in @('TestStandMCP', 'SeqEdit')) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Host ("  -> stopping {0}x {1}.exe (holds engine handles)" -f $procs.Count, $name) -ForegroundColor Yellow
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
            $killed = $true
        }
    }
    # The engine releases OS file handles asynchronously after the process dies;
    # give it a brief moment before the test host opens the engine.
    if ($killed) { Start-Sleep -Milliseconds 700 }
}

# ── 4) Run the tests, once per bitness; the first failure stops the gate ────
foreach ($run in $runs) {
    Stop-EngineProcesses
    Write-Host ("  -> dotnet test as {0} ({1}-bit TestStand; Debug / net8.0-windows) ..." -f $run.Arch, $run.Bits) -ForegroundColor Yellow
    Write-Host ''

    & dotnet test $testProj --configuration Debug --framework net8.0-windows --nologo @($run.Platform)
    $code = $LASTEXITCODE

    Write-Host ''
    if ($code -ne 0) {
        Write-Host ("  FAIL: the {0} tests did not pass (exit {1}) - push aborted." -f $run.Arch, $code) -ForegroundColor Red
        Write-Host '        Fix the tests, or bypass in an emergency: git push --no-verify' -ForegroundColor DarkGray
        exit $code
    }
    Write-Host ("  PASS: the {0} tests are green." -f $run.Arch) -ForegroundColor Green
}

Write-Host ('  PASS: all tests green (' + (($runs | ForEach-Object { $_.Arch }) -join ', ') + ') - push continues.') -ForegroundColor Green
exit 0
