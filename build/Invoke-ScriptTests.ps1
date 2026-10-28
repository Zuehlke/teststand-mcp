# ============================================================================
#  Invoke-ScriptTests.ps1 - runs every PowerShell script test of this repository, once under Windows
#  PowerShell 5.1 (powershell.exe) and once under PowerShell 7 (pwsh): every build\tests\*.Tests.ps1
#  and build\Test-SkillSources.ps1. The pull-request check (ci.yml) runs exactly this, so a maintainer
#  sees locally what the check will say. (The Python tests of the stats are run by ci.yml itself:
#  python -m unittest discover -s .github/stats.)
#  Each script runs in a child process of its own with a timeout, because every test script ends with
#  exit. A passing run prints one line with its case count; a failing run prints everything it wrote.
#  A script that exits 0 but printed no "PASS  <case>" line ran no case at all (an early exit, a broken
#  dot-source): that is a FAILURE, not a pass. Every script of this repository prints PASS lines, so none is exempt.
#  A script that declares "#Requires -Version 6" or higher runs under pwsh only.
#  pwsh missing: a visible SKIP on a maintainer's machine, but a FAILURE when TESTSTAND_MCP_REQUIRE_PWSH=1
#  (the pull-request check sets it), so the 5.1-only result can never pass as "both hosts".
#
#  Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Invoke-ScriptTests.ps1
#  -Only         run only these scripts (default: all of them, found as described above).
#  -List         print the scripts that would run, one path per line, and exit 0.
#  -PwshPath     the PowerShell 7 executable (default: pwsh from the PATH).
#  -TimeoutSeconds  how long ONE script may run under ONE host (default 1200).
#  -TotalSeconds    the budget of the whole run (default 0 = none). A script is not started when less than
#                   -TimeoutSeconds of the budget is left: it is reported as FAIL (never silently dropped),
#                   so a run in a job with its own time limit ends with its summary instead of a bare cancellation.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string[]]$Only = @(),
    [switch]$List,
    [string]$PwshPath,
    [int]$TimeoutSeconds = 1200,
    [int]$TotalSeconds = 0
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force

$RequirePwshVariable = 'TESTSTAND_MCP_REQUIRE_PWSH'
$TestsDir = Join-Path $PSScriptRoot 'tests'
$SkillSourcesScript = Join-Path $PSScriptRoot 'Test-SkillSources.ps1'
$ScriptTestPattern = '*.Tests.ps1'
$PassedCaseLabel = 'PASS  <case>'
$PassedCasePattern = '(?m)^PASS  '
$RequiresPattern = '(?im)^#Requires\s+-Version\s+([0-9]+)'
$PwshOnlyFromMajor = 6
$PowerShell5Name = 'powershell.exe'
$PowerShell7Name = 'pwsh'

# The two hosts, as { Name; Path } (Path is empty when the host is not installed).
function Get-TestHosts {
    param([string]$Pwsh)
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not $Pwsh) {
        $found = Get-Command $PowerShell7Name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $found) { $Pwsh = $found.Source }
    }
    if ($Pwsh -and -not (Test-Path -LiteralPath $Pwsh -PathType Leaf)) { $Pwsh = '' }
    return @(
        [pscustomobject]@{ Name = $PowerShell5Name; Path = $windowsPowerShell }
        [pscustomobject]@{ Name = $PowerShell7Name; Path = $Pwsh }
    )
}

function Get-DefaultScripts {
    $scripts = @(Get-ChildItem -LiteralPath $TestsDir -File -Filter $ScriptTestPattern | Sort-Object Name | ForEach-Object { $_.FullName })
    return @($scripts) + @($SkillSourcesScript)
}

# True when the script says it needs PowerShell 7 (#Requires -Version 7).
function Test-RequiresPwsh {
    param([string]$ScriptPath)
    $match = [regex]::Match((Read-Utf8Text $ScriptPath), $RequiresPattern)
    return ($match.Success -and [int]$match.Groups[1].Value -ge $PwshOnlyFromMajor)
}

# Runs one script under one host; returns { Passed; Cases; Seconds; Output }.
function Invoke-ScriptUnderHost {
    param([string]$ScriptPath, $TestHost)
    $result = Invoke-ChildProcess -FilePath $TestHost.Path -TimeoutSeconds $TimeoutSeconds `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath)
    $output = $result.Stdout + $result.Stderr
    if ($result.TimedOut) { $output += "`n(stopped after $TimeoutSeconds s: it did not finish)" }
    $cases = [regex]::Matches($output, $PassedCasePattern).Count
    if ((-not $result.TimedOut) -and $result.ExitCode -eq 0 -and $cases -eq 0) { $output += "`n(it exited 0 but printed no '$PassedCaseLabel' line, so it ran no case)" }
    return [pscustomobject]@{
        Passed  = ((-not $result.TimedOut) -and $result.ExitCode -eq 0 -and $cases -gt 0)
        Cases   = $cases
        Seconds = [Math]::Round($result.ElapsedMs / 1000, 1)
        Output  = $output
    }
}

try {
    if ($TimeoutSeconds -le 0) {
        Write-BuildFailure -What "-TimeoutSeconds $TimeoutSeconds is not a positive number of seconds." -Why 'A script that never ends would hold the whole check.' -Fix 'Pass a positive number, for example 1200.'
    }
    if ($TotalSeconds -lt 0 -or ($TotalSeconds -gt 0 -and $TotalSeconds -lt $TimeoutSeconds)) {
        Write-BuildFailure -What "-TotalSeconds $TotalSeconds is not 0 (no budget) or at least -TimeoutSeconds ($TimeoutSeconds)." -Why 'A budget smaller than the time one script may take would start nothing.' -Fix 'Pass 0, or a number of seconds that is at least -TimeoutSeconds.'
    }
    $scripts = if ($Only.Count -gt 0) { @($Only | ForEach-Object { Resolve-BuildPath $_ }) } else { @(Get-DefaultScripts) }
    foreach ($path in $scripts) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Write-BuildFailure -What "The script $path does not exist." -Why 'It is a script test the check is meant to run; skipping it silently would hide a gap.' -Fix 'Pass existing scripts, or run without -Only to run all of them.'
        }
    }
    if ($List) {
        $scripts | ForEach-Object { Write-Host $_ }
        exit 0
    }
    $testHosts = @(Get-TestHosts -Pwsh $PwshPath)
    $requirePwsh = [Environment]::GetEnvironmentVariable($RequirePwshVariable) -eq '1'

    $failed = @()
    $skipped = 0
    $runs = 0
    $clock = [Diagnostics.Stopwatch]::StartNew()
    foreach ($path in $scripts) {
        $name = Split-Path -Leaf $path
        $pwshOnly = Test-RequiresPwsh $path
        foreach ($testHost in $testHosts) {
            if ($pwshOnly -and $testHost.Name -ne $PowerShell7Name) { continue }
            if ([string]::IsNullOrEmpty($testHost.Path)) {
                if ($requirePwsh) {
                    Write-Host "FAIL  $name under $($testHost.Name): $($testHost.Name) was not found and $RequirePwshVariable=1"
                    $failed += "$name under $($testHost.Name) (not installed)"
                } else {
                    Write-Host "SKIP  $name under $($testHost.Name): $($testHost.Name) was not found (the pull-request check requires it: $RequirePwshVariable=1)"
                    $skipped++
                }
                continue
            }
            $left = $TotalSeconds - $clock.Elapsed.TotalSeconds
            if ($TotalSeconds -gt 0 -and $left -lt $TimeoutSeconds) {
                Write-Host "FAIL  $name under $($testHost.Name): not started, only $([Math]::Max(0, [Math]::Round($left))) s of the $TotalSeconds s budget are left and one script may take $TimeoutSeconds s"
                $failed += "$name under $($testHost.Name) (not started: out of time)"
                continue
            }
            $runs++
            $run = Invoke-ScriptUnderHost -ScriptPath $path -TestHost $testHost
            if ($run.Passed) {
                Write-Host "PASS  $name under $($testHost.Name) ($($run.Cases) case lines, $($run.Seconds) s)"
            } else {
                Write-Host "FAIL  $name under $($testHost.Name) ($($run.Seconds) s). Its output:"
                Write-Host $run.Output
                $failed += "$name under $($testHost.Name)"
            }
        }
    }

    Write-Host ''
    Write-Host "$runs script run(s), $($failed.Count) failed, $skipped skipped."
    if ($failed.Count -gt 0) {
        Write-BuildFailure -What "$($failed.Count) script test run(s) failed: $($failed -join '; ')." `
            -Why 'The same scripts run on every pull request; a failing one blocks the merge. Running them again helps only if the failure was a timeout on a busy machine.' `
            -Fix 'Run the named script by hand under the named host, for example: powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\<name>'
    }
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
