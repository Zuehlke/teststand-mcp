# ============================================================================
#  PrePushGate.Tests.ps1 - plain PowerShell asserts for .githooks/run-tests.ps1, the pre-push gate:
#  which bitness runs it would make for which installed TestStand, and its script checks.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\PrePushGate.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  The bitness cases use -DryRun against fake Program Files roots. The script-check cases run the
#  real gate in a fake repository whose archive test always fails, so no test suite runs and no
#  process is stopped in either.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$GatePath = Join-Path $RepoRoot '.githooks\run-tests.ps1'
$HostPath = (Get-Process -Id $PID).Path

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'PrePushGateTests_'

# A fake Program Files root, with an engine (teapi.dll) when asked.
function New-FakeRoot {
    param([switch]$WithEngine)
    $root = New-TempDir
    if ($WithEngine) { Write-TextFile (Join-Path $root 'National Instruments\TestStand 2026\Bin\teapi.dll') 'fake' }
    return $root
}

function Invoke-Gate {
    param([string]$X86Root, [string]$X64Root, [string]$Gate = $GatePath, [switch]$NoDryRun)
    $dry = @(); if (-not $NoDryRun) { $dry = @('-DryRun') }
    $output = @(& $HostPath -NoProfile -ExecutionPolicy Bypass -File $Gate -ProgramFilesX86 $X86Root -ProgramFiles64 $X64Root @dry 2>&1 | ForEach-Object { "$_" })
    return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

# A fake repository around a copy of the real gate, for running its script checks for real.
# The stub generator writes generated.json (or fails when a 'fail' marker exists); the stub
# archive test always FAILS, so no case ever reaches the step that stops real processes.
function New-FakeRepo {
    param([string]$Generated, [string]$Committed, [switch]$GeneratorFails)
    $repo = New-TempDir
    [void](New-Item -ItemType Directory -Path (Join-Path $repo '.githooks'))
    Copy-Item -LiteralPath $GatePath -Destination (Join-Path $repo '.githooks\run-tests.ps1')
    Write-TextFile (Join-Path $repo 'Test\TestExecution\TestStandMCP.IntegrationTests.csproj') '<Project />'
    Write-TextFile (Join-Path $repo 'plugin\hooks\hooks.json') $Committed
    Write-TextFile (Join-Path $repo 'build\generated.json') $Generated
    if ($GeneratorFails) { Write-TextFile (Join-Path $repo 'build\fail') 'x' }
    Write-TextFile (Join-Path $repo 'build\New-HooksJson.ps1') @'
param([string]$OutputPath)
if (Test-Path (Join-Path $PSScriptRoot 'fail')) { Write-Host 'GENERATOR BROKE'; exit 1 }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'generated.json') -Destination $OutputPath
exit 0
'@
    Write-TextFile (Join-Path $repo 'build\tests\BuildPluginArchive.Tests.ps1') @'
Write-Host 'PASS  a case the gate hides'
Write-Host "ARCHIVE RAN with GIT_DIR=[$env:GIT_DIR]"
exit 1
'@
    return $repo
}

function Invoke-FakeGate {
    param([string]$Repo)
    return Invoke-Gate (New-FakeRoot -WithEngine) (New-FakeRoot) -Gate (Join-Path $Repo '.githooks\run-tests.ps1') -NoDryRun
}

try {
    Invoke-Case 'gate: both TestStands installed -> x86 run, then x64 run' {
        $run = Invoke-Gate (New-FakeRoot -WithEngine) (New-FakeRoot -WithEngine)
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'would test as x86, then as x64 (-p:Platform=x64)' 'both runs, x86 first, x64 built for x64'
        Assert-NotContains $run.Output 'x86 (' 'the x86 run keeps the default (x86) build'
        Assert-Contains $run.Output 'would run the script checks (hooks.json, BuildPluginArchive.Tests.ps1)' 'the script checks that otherwise fail only in CI'
    }

    Invoke-Case 'gate: only 32-bit TestStand -> the x86 run alone, the x64 run is skipped by name' {
        $run = Invoke-Gate (New-FakeRoot -WithEngine) (New-FakeRoot)
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'would test as x86' 'the x86 run'
        Assert-Contains $run.Output 'skipping the x64 run' 'the skip note'
        Assert-NotContains $run.Output 'then as x64' 'no x64 run'
    }

    Invoke-Case 'gate: only 64-bit TestStand -> the x64 run alone' {
        $run = Invoke-Gate (New-FakeRoot) (New-FakeRoot -WithEngine)
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'would test as x64' 'the x64 run'
        Assert-Contains $run.Output 'skipping the x86 run' 'the skip note'
    }

    Invoke-Case 'gate: a TestStand folder without the engine does not count' {
        $root = New-FakeRoot
        [void](New-Item -ItemType Directory -Path (Join-Path $root 'National Instruments\TestStand 2026\Bin') -Force)
        $run = Invoke-Gate $root (New-FakeRoot -WithEngine)
        Assert-Contains $run.Output 'skipping the x86 run' 'an engine-less folder cannot run the suite'
    }

    Invoke-Case 'gate: no TestStand at all -> the push is blocked' {
        $run = Invoke-Gate (New-FakeRoot) (New-FakeRoot)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'no TestStand is installed' 'the reason'
        Assert-NotContains $run.Output 'would run the script checks' 'blocked before the checks are announced'
    }

    Invoke-Case 'script checks: a stale hooks.json blocks the push before the archive test runs' {
        $run = Invoke-FakeGate (New-FakeRepo -Generated "{`n  `"a`": 1`n}`n" -Committed "{`n  `"a`": 2`n}`n")
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'plugin/hooks/hooks.json is stale' 'the reason'
        Assert-NotContains $run.Output 'ARCHIVE RAN' 'the archive test is not started'
    }

    Invoke-Case 'script checks: a missing hooks.json blocks the push' {
        $repo = New-FakeRepo -Generated '{}' -Committed '{}'
        Remove-Item -LiteralPath (Join-Path $repo 'plugin\hooks\hooks.json')
        $run = Invoke-FakeGate $repo
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'plugin/hooks/hooks.json is stale or missing' 'the reason'
        Assert-NotContains $run.Output 'ARCHIVE RAN' 'the archive test is not started'
    }

    Invoke-Case 'script checks: a failing generator blocks the push and shows its own output' {
        $run = Invoke-FakeGate (New-FakeRepo -Generated '{}' -Committed '{}' -GeneratorFails)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'GENERATOR BROKE' 'the generator output is shown'
        Assert-Contains $run.Output 'New-HooksJson.ps1 failed or wrote no file (exit 1)' 'the reason'
        Assert-NotContains $run.Output 'is stale' 'a generator failure is not reported as a stale file'
    }

    Invoke-Case 'script checks: CRLF-only difference passes, GIT_DIR is cleared, a failing archive test blocks' {
        $repo = New-FakeRepo -Generated "{`n  `"a`": 1`n}`n" -Committed "{`r`n  `"a`": 1`r`n}`r`n"
        $run = Use-Environment @{ GIT_DIR = 'C:\not-this-repo\.git\worktrees\x' } { Invoke-FakeGate $repo }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'ARCHIVE RAN with GIT_DIR=[]' 'the CRLF copy was accepted and the archive test saw no GIT_DIR'
        Assert-Contains $run.Output 'BuildPluginArchive.Tests.ps1 did not pass (exit 1)' 'the reason'
        Assert-NotContains $run.Output 'a case the gate hides' 'PASS lines are filtered out'
        # 'dotnet test as' is printed right after the process stop, so its absence proves neither ran.
        Assert-NotContains $run.Output 'dotnet test as' 'the gate stops before the integration tests'
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
