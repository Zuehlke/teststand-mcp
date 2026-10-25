# ============================================================================
#  ScriptTestRunner.Tests.ps1 - plain PowerShell asserts for build/Invoke-ScriptTests.ps1, the runner
#  the pull-request check uses for every script test.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ScriptTestRunner.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  The runner runs in a child process against tiny fixture scripts; it never runs the real suites here.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $RepoRoot 'build'
$RunnerPath = Join-Path $BuildDir 'Invoke-ScriptTests.ps1'
$CommonModulePath = Join-Path $BuildDir 'BuildCommon.psm1'
$RequirePwshVariable = 'TESTSTAND_MCP_REQUIRE_PWSH'
$MissingPwsh = Join-Path ([IO.Path]::GetTempPath()) 'no-such-pwsh-for-tests\pwsh.exe'
$RunnerTimeoutSeconds = 120
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ScriptTestRunnerTests_'

Import-Module $CommonModulePath -Force

# A fixture script that prints its lines and exits with the given code.
function New-FixtureScript {
    param([string]$Name, [string[]]$Lines, [int]$ExitCode = 0, [string[]]$Header = @())
    $path = Join-Path (New-TempDir) $Name
    $body = @($Header) + @($Lines | ForEach-Object { "Write-Host '$_'" }) + @("exit $ExitCode")
    [IO.File]::WriteAllText($path, (($body -join "`n") + "`n"), $Utf8NoBom)
    return $path
}

# Runs a child of the current host with the require-pwsh variable set as asked.
function Invoke-RunnerHost {
    param([string[]]$HostArguments, [string]$RequirePwsh = $null)
    $old = [Environment]::GetEnvironmentVariable($RequirePwshVariable)
    try {
        [Environment]::SetEnvironmentVariable($RequirePwshVariable, $RequirePwsh)
        $hostPath = (Get-Process -Id $PID).Path
        $result = Invoke-ChildProcess -FilePath $hostPath -TimeoutSeconds $RunnerTimeoutSeconds -ArgumentList (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass') + $HostArguments)
    } finally {
        [Environment]::SetEnvironmentVariable($RequirePwshVariable, $old)
    }
    if ($result.TimedOut) { throw "the runner itself did not finish within $RunnerTimeoutSeconds s" }
    return @{ ExitCode = $result.ExitCode; Output = ($result.Stdout + $result.Stderr) }
}

function Invoke-Runner {
    param([string[]]$Arguments, [string]$RequirePwsh = $null)
    return Invoke-RunnerHost (@('-File', $RunnerPath) + $Arguments) $RequirePwsh
}

# Several -Only scripts need a command string: a -File command line cannot bind a second value to a string[].
# $Extra holds ready-made tokens, quoted with Format-PowerShellLiteral where they are text.
function Format-PowerShellLiteral {
    param([string]$Text)
    return "'" + $Text.Replace("'", "''") + "'"
}

function Invoke-RunnerOnly {
    param([string[]]$Scripts, [string[]]$Extra = @(), [string]$RequirePwsh = $null)
    $list = ($Scripts | ForEach-Object { Format-PowerShellLiteral $_ }) -join ','
    $command = "& $(Format-PowerShellLiteral $RunnerPath) -Only @($list) $($Extra -join ' '); exit `$LASTEXITCODE"
    return Invoke-RunnerHost @('-Command', $command) $RequirePwsh
}

$PwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue | Select-Object -First 1

try {
    Assert-True (Test-Path -LiteralPath $RunnerPath) "$RunnerPath does not exist"

    Invoke-Case 'runner: a passing script exits 0, counts its PASS lines and says which host ran it' {
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one', 'PASS  two')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh)
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'PASS  ok.Tests.ps1 under powershell.exe (2 case lines' 'result line'
    }

    Invoke-Case 'runner: a failing script fails the run, prints everything the script wrote and names the host and the fix' {
        $script = New-FixtureScript 'bad.Tests.ps1' @('FAIL  the broken case', 'details of the break') -ExitCode 1
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'FAIL  bad.Tests.ps1 under powershell.exe' 'result line'
        Assert-Contains $run.Output 'details of the break' 'the script output is shown'
        Assert-Contains $run.Output 'bad.Tests.ps1 under powershell.exe' 'the failure names the run'
        Assert-ThreePartFailure $run.Output
    }

    Invoke-Case 'runner: pwsh missing is a visible SKIP on a maintainer machine and does not fail the run' {
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh)
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'SKIP  ok.Tests.ps1 under pwsh' 'the skip is visible'
        Assert-Contains $run.Output $RequirePwshVariable 'the skip says what makes it a failure'
    }

    Invoke-Case 'runner: pwsh missing FAILS when TESTSTAND_MCP_REQUIRE_PWSH=1, so CI never passes on 5.1 alone' {
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh) -RequirePwsh '1'
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'FAIL  ok.Tests.ps1 under pwsh' 'result line'
        Assert-Contains $run.Output 'not installed' 'the failure says why'
        Assert-ThreePartFailure $run.Output
    }

    Invoke-Case 'runner: any other value of the variable keeps the skip a skip' {
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh) -RequirePwsh '0'
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
    }

    Invoke-Case 'runner: a script that declares #Requires -Version 7 runs under pwsh only' {
        $script = New-FixtureScript 'seven.Tests.ps1' @('PASS  one') -Header @('#Requires -Version 7')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh)
        Assert-NotContains $run.Output 'under powershell.exe' 'the 5.1 run must not happen'
        Assert-Contains $run.Output 'SKIP  seven.Tests.ps1 under pwsh' 'only the pwsh host is considered'
    }

    Invoke-Case 'runner: a script that declares #Requires -Version 7 FAILS when pwsh is missing and TESTSTAND_MCP_REQUIRE_PWSH=1' {
        $script = New-FixtureScript 'seven.Tests.ps1' @('PASS  one') -Header @('#Requires -Version 7')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh) -RequirePwsh '1'
        Assert-Equal 1 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'FAIL  seven.Tests.ps1 under pwsh' 'the pwsh-only script is not silently dropped'
        Assert-NotContains $run.Output 'under powershell.exe' 'the 5.1 run must not happen'
        Assert-ThreePartFailure $run.Output
    }

    Invoke-Case 'runner: two scripts, the first passes and the second fails: both are reported and the summary counts them' {
        $first = New-FixtureScript 'first.Tests.ps1' @('PASS  one')
        $second = New-FixtureScript 'second.Tests.ps1' @('FAIL  the broken case') -ExitCode 1
        $run = Invoke-RunnerOnly @($first, $second) @('-PwshPath', (Format-PowerShellLiteral $MissingPwsh))
        Assert-Equal 1 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'PASS  first.Tests.ps1 under powershell.exe' 'the passing script is reported'
        Assert-Contains $run.Output 'FAIL  second.Tests.ps1 under powershell.exe' 'the failing script is reported'
        Assert-Contains $run.Output '2 script run(s), 1 failed' 'the summary'
        Assert-ThreePartFailure $run.Output
    }

    Invoke-Case 'runner: a script that exits 0 but printed no PASS line ran no case and FAILS' {
        $script = New-FixtureScript 'empty.Tests.ps1' @('hello, but no case ran')
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh)
        Assert-Equal 1 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'FAIL  empty.Tests.ps1 under powershell.exe' 'result line'
        Assert-Contains $run.Output 'ran no case' 'the output says why'
        Assert-ThreePartFailure $run.Output
    }

    Invoke-Case 'runner: no script starts once less than one per-script timeout is left of -TotalSeconds, and the skipped one FAILS (never dropped)' {
        $slow = New-FixtureScript 'slow.Tests.ps1' @('PASS  one') -Header @('Start-Sleep -Seconds 3')
        $late = New-FixtureScript 'late.Tests.ps1' @('PASS  two')
        $run = Invoke-RunnerOnly @($slow, $late) @('-PwshPath', (Format-PowerShellLiteral $MissingPwsh), '-TimeoutSeconds', '20', '-TotalSeconds', '22')
        Assert-Equal 1 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'PASS  slow.Tests.ps1 under powershell.exe' 'the script that started in time ran'
        Assert-Contains $run.Output 'FAIL  late.Tests.ps1 under powershell.exe: not started' 'the late script is reported as not started'
        Assert-Contains $run.Output '1 script run(s), 1 failed' 'only one script ran'
        Assert-ThreePartFailure $run.Output
        $enough = Invoke-RunnerOnly @($slow, $late) @('-PwshPath', (Format-PowerShellLiteral $MissingPwsh), '-TimeoutSeconds', '20', '-TotalSeconds', '600')
        Assert-Equal 0 $enough.ExitCode "with a budget that is enough, both run. Output was:`n$($enough.Output)"
        Assert-Contains $enough.Output '2 script run(s), 0 failed' 'both ran'
    }

    Invoke-Case 'runner: a budget smaller than one script timeout is a three-part failure, not a run that starts nothing' {
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
        $run = Invoke-Runner @('-Only', $script, '-TimeoutSeconds', '60', '-TotalSeconds', '30')
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-ThreePartFailure $run.Output
        Assert-NotContains $run.Output 'PASS  ok.Tests.ps1' 'nothing ran'
    }

    Invoke-Case 'runner: a script that outlives -TimeoutSeconds is stopped and fails the run' {
        $script = Join-Path (New-TempDir) 'hang.Tests.ps1'
        [IO.File]::WriteAllText($script, "Start-Sleep -Seconds 60`nexit 0`n", $Utf8NoBom)
        $run = Invoke-Runner @('-Only', $script, '-PwshPath', $MissingPwsh, '-TimeoutSeconds', '3')
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'FAIL  hang.Tests.ps1 under powershell.exe' 'result line'
        Assert-Contains $run.Output 'did not finish' 'the output says it was stopped'
    }

    Invoke-Case 'runner: a script that does not exist, or a bad timeout, is a three-part failure' {
        $missing = Invoke-Runner @('-Only', (Join-Path (New-TempDir) 'absent.Tests.ps1'))
        Assert-Equal 1 $missing.ExitCode 'exit code for a missing script'
        Assert-ThreePartFailure $missing.Output
        $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
        $badTimeout = Invoke-Runner @('-Only', $script, '-TimeoutSeconds', '0')
        Assert-Equal 1 $badTimeout.ExitCode 'exit code for a bad timeout'
        Assert-ThreePartFailure $badTimeout.Output
    }

    Invoke-Case 'runner: by default it lists every build test script and the skill source check, this file included' {
        $run = Invoke-Runner @('-List')
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        $listed = @($run.Output -split '\r?\n' | Where-Object { $_ } | ForEach-Object { Split-Path -Leaf $_ })
        $expected = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.Tests.ps1' | ForEach-Object { $_.Name }) + @('Test-SkillSources.ps1')
        foreach ($name in $expected) { Assert-True ($listed -ccontains $name) "$name is not run by default. Listed: $($listed -join ', ')" }
        Assert-Equal $expected.Count $listed.Count 'no script is listed twice or invented'
        Assert-True ($listed -ccontains 'ScriptTestRunner.Tests.ps1') 'the runner tests run too'
    }

    if ($null -ne $PwshCommand) {
        Invoke-Case 'runner: with pwsh installed the same script runs under both hosts' {
            $script = New-FixtureScript 'ok.Tests.ps1' @('PASS  one')
            $run = Invoke-Runner @('-Only', $script) -RequirePwsh '1'
            Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
            Assert-Contains $run.Output 'PASS  ok.Tests.ps1 under powershell.exe' 'the 5.1 run'
            Assert-Contains $run.Output 'PASS  ok.Tests.ps1 under pwsh' 'the 7 run'
        }
    } else {
        Write-Host 'SKIP  runner: with pwsh installed the same script runs under both hosts (pwsh is not installed here; the pull-request check requires it)'
        if ([Environment]::GetEnvironmentVariable($RequirePwshVariable) -eq '1') {
            Invoke-Case 'runner: pwsh is required here and was not found' { throw "$RequirePwshVariable=1 but pwsh is not on the PATH" }
        }
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
