# ============================================================================
#  PrePushGate.Tests.ps1 - plain PowerShell asserts for .githooks/run-tests.ps1, the pre-push gate:
#  which bitness runs it would make for which installed TestStand.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\PrePushGate.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  Only -DryRun runs against fake Program Files roots: no test suite runs and no process is stopped.
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
    param([string]$X86Root, [string]$X64Root)
    $output = @(& $HostPath -NoProfile -ExecutionPolicy Bypass -File $GatePath -ProgramFilesX86 $X86Root -ProgramFiles64 $X64Root -DryRun 2>&1 | ForEach-Object { "$_" })
    return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

try {
    Invoke-Case 'gate: both TestStands installed -> x86 run, then x64 run' {
        $run = Invoke-Gate (New-FakeRoot -WithEngine) (New-FakeRoot -WithEngine)
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'would test as x86, then as x64' 'both runs, x86 first'
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
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
