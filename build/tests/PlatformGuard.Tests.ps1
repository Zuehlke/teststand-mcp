# ============================================================================
#  PlatformGuard.Tests.ps1 - plain PowerShell asserts for plugin/hooks/platform-guard.ps1: one combined
#  ASCII warning for a missing TestStand (every time) or a missing optional doc-agent dependency (probed
#  and warned at most once per 24 hours), and the rules pointer that is always added.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\PlatformGuard.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  Set TESTSTAND_MCP_TIMING_CHECK=1 to also assert that a hanging probe stays inside the hook timeout.
#  This file is ASCII only on purpose (5.1 reads a BOM-less script as ANSI).
# ============================================================================
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'PlatformGuardTests_'
. (Join-Path $PSScriptRoot 'HookTestSupport.ps1')

$GuardPath = Join-Path $HooksDir 'platform-guard.ps1'
$PipInstallCommand = 'py -m pip install python-docx pillow'
$OptionalWording = 'The doc and presentation agents are optional'
$ProbeBudgetMilliseconds = 10000   # the hook timeout in hooks.json

function Assert-GuardClean {
    param($Result, [string]$What = 'guard')
    Assert-Equal 0 $Result.ExitCode "$What exit code (stderr: $($Result.Stderr))"
    Assert-Equal '' $Result.Stderr "$What stderr"
    Assert-AsciiOnly $Result.Stdout "$What stdout"
}

# The "- item" lines of a warning.
function Get-ItemLines {
    param([string]$Message)
    return @($Message -split "`n" | Where-Object { $_.StartsWith('- ', [StringComparison]::Ordinal) })
}

try {
    Invoke-Case 'guard: a healthy station prints no warning and adds the rules pointer (one line, no newline)' {
        $result = Invoke-Guard (New-GuardFixture)
        Assert-GuardClean $result
        Assert-Equal $null $result.Message 'systemMessage'
        Assert-Equal 0 (Get-OccurrenceCount $result.Stdout '"systemMessage"') 'systemMessage occurrences in the raw output'
        Assert-Equal 'SessionStart' $result.Json.hookSpecificOutput.hookEventName 'hookEventName'
        Assert-Contains $result.Context 'teststand-rules' 'additionalContext'
        Assert-NotContains $result.Context "`n" 'additionalContext holds no newline'
    }

    $GuardRows = @(
        @{ Name = 'TestStand is not detected';            Fixture = @{ NoTestStand = $true };  Items = 1; Named = @('NI TestStand was not detected', 'install NI TestStand 2019 or later, or start Claude Code where TestStand is installed'); Optional = $false }
        @{ Name = 'neither py nor python is on PATH';     Fixture = @{ PyOut = $null };        Items = 1; Named = @('Python was not found', 'install Python 3 from python.org', $PipInstallCommand); Optional = $true }
        @{ Name = 'python-docx is not installed';         Fixture = @{ PyOut = '0 1' };        Items = 1; Named = @('python-docx is not installed', $PipInstallCommand); Optional = $true }
        @{ Name = 'Pillow is not installed';              Fixture = @{ PyOut = '1 0' };        Items = 1; Named = @('Pillow is not installed', $PipInstallCommand); Optional = $true }
        @{ Name = 'neither Edge nor Chrome is installed'; Fixture = @{ NoBrowser = $true };    Items = 1; Named = @('Edge or Chrome was not found', 'install Microsoft Edge or Google Chrome'); Optional = $true }
        @{ Name = 'TestStand and Pillow are missing';     Fixture = @{ NoTestStand = $true; PyOut = '1 0' }; Items = 2; Named = @('NI TestStand was not detected', 'Pillow is not installed', $PipInstallCommand); Optional = $true }
        @{ Name = 'python-docx and Pillow are missing';   Fixture = @{ PyOut = '0 0' };        Items = 2; Named = @('python-docx is not installed', 'Pillow is not installed'); Optional = $true }
    )
    foreach ($row in $GuardRows) {
        Invoke-Case "guard: $($row.Name) gives exactly one ASCII warning naming the item and its fix" {
            $fixtureArguments = $row.Fixture
            $result = Invoke-Guard (New-GuardFixture @fixtureArguments)
            Assert-GuardClean $result
            Assert-True ($null -ne $result.Message) 'expected a systemMessage'
            Assert-Equal 1 (Get-OccurrenceCount $result.Stdout '"systemMessage"') 'systemMessage occurrences in the raw output'
            foreach ($named in $row.Named) { Assert-Contains $result.Message $named 'warning' }
            $items = Get-ItemLines $result.Message
            Assert-Equal $row.Items $items.Count 'item lines'
            Assert-Equal $items.Count @($items | Sort-Object -Unique).Count 'no item is repeated'
            if ($row.Optional) {
                Assert-Contains $result.Message $OptionalWording 'the optional wording'
                Assert-Contains $result.Message 'TESTSTAND_MCP_NO_PLATFORM_WARNING=1' 'the opt-out hint'
            } else {
                Assert-NotContains $result.Message 'optional' 'a TestStand-only warning'
            }
            Assert-Contains $result.Context 'teststand-rules' 'the rules pointer stays alongside a warning'
            Assert-NotContains $result.Context "`n" 'additionalContext holds no newline'
        }
    }

    Invoke-Case 'guard: the TestStand environment variable counts as detected' {
        $result = Invoke-Guard (New-GuardFixture -NoTestStand) @{ TestStand = 'C:\somewhere\TestStand' }
        Assert-Equal $null $result.Message 'systemMessage'
    }

    Invoke-Case 'guard: a TestStand folder under the Program Files (x86) root counts as detected' {
        Assert-Equal $null (Invoke-Guard (New-GuardFixture)).Message 'systemMessage'   # the default fixture installs it under pf86 only
    }

    # --- one TestStand bitness per plugin ----------------------------------------------------------------
    Invoke-Case 'guard: the bitness table names the two plugins of the identity table, 32-bit and 64-bit' {
        . $GuardPath
        Import-Module (Join-Path $RepoRoot 'build\PluginIdentity.psm1') -Force
        $base = (Get-PluginIdentity -Architecture 'x86').name
        foreach ($architecture in (Get-PluginArchitectures)) {
            $identity = Get-PluginIdentity -Architecture $architecture
            $rows = @($script:PluginBitness | Where-Object { ($base + $_.Suffix) -ceq $identity.name })
            Assert-Equal 1 $rows.Count "the guard rows for $($identity.name)"
            Assert-Equal $identity.bits $rows[0].Bits "$($identity.name) bits"
        }
        Assert-Equal 2 @($script:PluginBitness).Count 'rows'
    }

    Invoke-Case 'guard: the x64 plugin with a 64-bit TestStand reports no TestStand problem' {
        $fixture = New-GuardFixture -NoTestStand -TestStand64
        Assert-Equal $null (Invoke-Guard $fixture @{ CLAUDE_PLUGIN_ROOT = (New-PluginRootFixture 'teststand-mcp-x64') }).Message 'systemMessage'
    }

    Invoke-Case 'guard: the x64 plugin with only a 32-bit TestStand warns and names teststand-mcp as the plugin to install' {
        $result = Invoke-Guard (New-GuardFixture) @{ CLAUDE_PLUGIN_ROOT = (New-PluginRootFixture 'teststand-mcp-x64') }
        Assert-GuardClean $result
        foreach ($named in @('NI TestStand 64-bit was not detected', '32-bit TestStand is installed', 'claude plugin install teststand-mcp@zuehlke-teststand')) {
            Assert-Contains $result.Message $named 'warning'
        }
        Assert-Equal 1 (Get-ItemLines $result.Message).Count 'item lines'
    }

    Invoke-Case 'guard: the x86 plugin with only a 64-bit TestStand warns and names teststand-mcp-x64' {
        $result = Invoke-Guard (New-GuardFixture -NoTestStand -TestStand64)
        Assert-GuardClean $result
        Assert-Contains $result.Message 'NI TestStand 32-bit was not detected' 'warning'
        Assert-Contains $result.Message 'claude plugin install teststand-mcp-x64@zuehlke-teststand' 'warning'
    }

    Invoke-Case 'guard: the TestStand64 variable counts for the x64 plugin only' {
        $x64Root = New-PluginRootFixture 'teststand-mcp-x64'
        Assert-Equal $null (Invoke-Guard (New-GuardFixture -NoTestStand) @{ CLAUDE_PLUGIN_ROOT = $x64Root; TestStand64 = 'C:\somewhere\TestStand' }).Message 'x64 plugin'
        Assert-Contains (Invoke-Guard (New-GuardFixture -NoTestStand) @{ TestStand64 = 'C:\somewhere\TestStand' }).Message 'NI TestStand 32-bit was not detected' 'x86 plugin'
        Assert-Contains (Invoke-Guard (New-GuardFixture -NoTestStand) @{ CLAUDE_PLUGIN_ROOT = $x64Root; TestStand = 'C:\somewhere\TestStand' }).Message 'NI TestStand 64-bit was not detected' 'TestStand does not count for x64'
    }

    Invoke-Case 'guard: no TestStand at all warns as before, for either plugin' {
        foreach ($root in @($null, (New-PluginRootFixture 'teststand-mcp-x64'))) {
            $message = (Invoke-Guard (New-GuardFixture -NoTestStand) @{ CLAUDE_PLUGIN_ROOT = $root }).Message
            Assert-Contains $message 'NI TestStand was not detected. Fix: install NI TestStand 2019 or later' 'warning'
            Assert-NotContains $message 'claude plugin install' 'no other plugin to point at'
        }
    }

    Invoke-Case 'guard: a National Instruments folder without a TestStand folder is not detected' {
        $fixture = New-GuardFixture -NoTestStand
        [void](New-Item -ItemType Directory -Path (Join-Path $fixture.ProgramFiles 'National Instruments\LabVIEW 2024'))
        Assert-Contains (Invoke-Guard $fixture).Message 'NI TestStand was not detected' 'warning'
    }

    # --- finding Python -------------------------------------------------------------------------------
    Invoke-Case 'guard: py is tried first with -3 and -E, and python is not started when py answers' {
        $fixture = New-GuardFixture -PyOut '1 1' -PythonOut '1 1'
        $result = Invoke-Guard $fixture
        Assert-Equal $null $result.Message 'systemMessage'
        $pyLog = Get-StubLog $fixture.PyLog
        Assert-Equal 1 $pyLog.Count 'py starts'
        Assert-Contains $pyLog[0] '-3' 'the py launcher is asked for Python 3'
        Assert-Contains $pyLog[0] '-E' 'PYTHON* environment variables are ignored'
        Assert-Equal 0 (Get-StubLog $fixture.PythonLog).Count 'python starts'
    }

    Invoke-Case 'guard: when py answers 1 0 and python answers 1 1, py wins and Pillow is reported' {
        $fixture = New-GuardFixture -PyOut '1 0' -PythonOut '1 1'
        $result = Invoke-Guard $fixture
        Assert-Contains $result.Message 'Pillow is not installed' 'warning'
        Assert-Equal 0 (Get-StubLog $fixture.PythonLog).Count 'python starts'
    }

    Invoke-Case 'guard: python found through the fallback (py absent) prints no warning' {
        $fixture = New-GuardFixture -PyOut $null -PythonOut '1 1'
        $result = Invoke-Guard $fixture
        Assert-GuardClean $result
        Assert-Equal $null $result.Message 'systemMessage'
        Assert-Contains (Get-StubLog $fixture.PythonLog)[0] '-E' 'python is probed without PYTHON* variables'
    }

    Invoke-Case 'guard: a py launcher with no Python 3 falls back to python' {
        $fixture = New-GuardFixture -PyOut 'No suitable Python runtime found' -PyExit 103 -PythonOut '1 1'
        Assert-Equal $null (Invoke-Guard $fixture).Message 'systemMessage'
        Assert-Equal 1 (Get-StubLog $fixture.PythonLog).Count 'python starts'
    }

    Invoke-Case 'guard: a python that exits non-zero (Store alias stub) counts as no Python' {
        Assert-Contains (Invoke-Guard (New-GuardFixture -PyOut $null -PythonOut 'Python was not found' -PythonExit 9009)).Message 'Python was not found' 'warning'
    }

    Invoke-Case 'guard: unparsable probe output counts as no Python, not as a crash' {
        $result = Invoke-Guard (New-GuardFixture -PyOut 'banana')
        Assert-GuardClean $result
        Assert-Contains $result.Message 'Python was not found' 'warning'
    }

    Invoke-Case 'guard: a python-only station is told the python command, not the py launcher' {
        Assert-Contains (Invoke-Guard (New-GuardFixture -PyOut $null -PythonOut '1 0')).Message 'python -m pip install python-docx pillow' 'fix'
    }

    Invoke-Case 'guard: the interpreter runs in the hooks folder, not in the session folder' {
        $fixture = New-GuardFixture -PyOut '1 1' -StubBody "echo CWD=%CD%>>`"%STUB_PY_LOG%`"`r`n"
        [void](Invoke-Guard $fixture)
        $log = (Get-StubLog $fixture.PyLog) -join "`n"
        Assert-Contains $log ('CWD=' + $HooksDir) 'working directory of the probe'
    }

    Invoke-Case 'guard: two hanging interpreters are reported as no Python (the hook-timeout bound only with TESTSTAND_MCP_TIMING_CHECK=1)' {
        $fixture = New-GuardFixture -PyOut '1 1' -PythonOut '1 1' -StubBody "ping -n 12 127.0.0.1 >nul`r`n"
        $result = Invoke-Guard $fixture
        Assert-GuardClean $result
        Assert-Contains $result.Message 'Python was not found' 'warning'
        Assert-Contains $result.Context 'teststand-rules' 'the rules pointer survives the hang'
        if ($env:TESTSTAND_MCP_TIMING_CHECK -eq '1') { Assert-True ($result.ElapsedMs -lt $ProbeBudgetMilliseconds) "the guard took $($result.ElapsedMs) ms against a $ProbeBudgetMilliseconds ms hook timeout" }
    }

    # --- the 24 hour window (the injected clock; the probe log shows whether Python was started) ------
    Invoke-Case 'guard: the optional warning is shown once, then nothing is probed or warned for 24 hours (23 h 59 m 59 s still quiet, exactly 24 h again)' {
        $fixture = New-GuardFixture -PyOut '1 0'
        Assert-Contains (Invoke-Guard $fixture).Message 'Pillow' 'first run'
        Assert-Equal 1 (Get-StubLog $fixture.PyLog).Count 'py starts after the first run'
        $threeHours = Invoke-Guard $fixture -Now $FixedNow.AddHours(3)
        Assert-Equal $null $threeHours.Message 'after 3 h'
        Assert-Contains $threeHours.Context 'teststand-rules' 'the rules pointer is not throttled'
        $justUnder = Invoke-Guard $fixture -Now $FixedNow.AddHours(24).AddSeconds(-1)
        Assert-Equal $null $justUnder.Message 'after 23 h 59 m 59 s'
        Assert-Equal 1 (Get-StubLog $fixture.PyLog).Count 'py starts inside the window'
        $exactly = Invoke-Guard $fixture -Now $FixedNow.AddHours(24)
        Assert-Contains $exactly.Message 'Pillow' 'after exactly 24 h'
        Assert-Equal 2 (Get-StubLog $fixture.PyLog).Count 'py starts after the window'
    }

    Invoke-Case 'guard: a healthy outcome is cached too, so a healthy station does not start Python at every session' {
        $fixture = New-GuardFixture -PyOut '1 1'
        [void](Invoke-Guard $fixture)
        [void](Invoke-Guard $fixture -Now $FixedNow.AddHours(1))
        Assert-Equal 1 (Get-StubLog $fixture.PyLog).Count 'py starts'
    }

    Invoke-Case 'guard: a repeated warning renews the window (quiet one hour after the 24 h repeat)' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [void](Invoke-Guard $fixture)
        Assert-Contains (Invoke-Guard $fixture -Now $FixedNow.AddHours(24)).Message 'Pillow' 'repeat after 24 h'
        Assert-Equal $null (Invoke-Guard $fixture -Now $FixedNow.AddHours(25)).Message 'one hour after the repeat'
    }

    Invoke-Case 'guard: a changed missing set is noticed when the window ends' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [void](Invoke-Guard $fixture)
        $fixture.Env['STUB_PY_OUT'] = '0 1'
        Assert-Equal $null (Invoke-Guard $fixture -Now $FixedNow.AddHours(1)).Message 'inside the window'
        $changed = Invoke-Guard $fixture -Now $FixedNow.AddHours(25)
        Assert-Contains $changed.Message 'python-docx is not installed' 'the new set'
        Assert-NotContains $changed.Message 'Pillow is not installed' 'the old set'
    }

    Invoke-Case 'guard: a missing TestStand is reported every time while the optional part stays quiet' {
        $fixture = New-GuardFixture -NoTestStand -PyOut '1 0'
        Assert-Contains (Invoke-Guard $fixture).Message 'Pillow' 'first run lists the optional item'
        $second = Invoke-Guard $fixture -Now $FixedNow.AddHours(3)
        Assert-Contains $second.Message 'NI TestStand was not detected' 'second run still reports TestStand'
        Assert-NotContains $second.Message 'Pillow' 'second run no longer nags about Pillow'
        Assert-NotContains $second.Message 'optional' 'second run carries no optional note'
    }

    Invoke-Case 'guard: a clock that moved backwards (state in the future) does not suppress the warning' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [void](Invoke-Guard $fixture)
        Assert-Contains (Invoke-Guard $fixture -Now $FixedNow.AddHours(-1)).Message 'Pillow' 'state from the future'
    }

    Invoke-Case 'guard: a corrupt state file does not suppress the warning or crash' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [void](Invoke-Guard $fixture)
        foreach ($file in Get-ChildItem -LiteralPath $fixture.State -File) { [IO.File]::WriteAllText($file.FullName, '{ not json', $Utf8NoBom) }
        $result = Invoke-Guard $fixture -Now $FixedNow.AddHours(1)
        Assert-GuardClean $result
        Assert-Contains $result.Message 'Pillow' 'corrupt state'
    }

    Invoke-Case 'guard: TESTSTAND_MCP_NO_PLATFORM_WARNING=1 hides the optional warning and starts no interpreter, but not a missing TestStand' {
        $fixture = New-GuardFixture -PyOut '1 0'
        Assert-Equal $null (Invoke-Guard $fixture @{ TESTSTAND_MCP_NO_PLATFORM_WARNING = '1' }).Message 'optional warning with the opt-out'
        Assert-Equal 0 (Get-StubLog $fixture.PyLog).Count 'py starts with the opt-out'
        $withTestStand = Invoke-Guard (New-GuardFixture -NoTestStand -PyOut '1 0') @{ TESTSTAND_MCP_NO_PLATFORM_WARNING = '1' }
        Assert-Contains $withTestStand.Message 'NI TestStand was not detected' 'TestStand is still reported'
        Assert-NotContains $withTestStand.Message 'Pillow' 'the optional part stays off'
    }

    Invoke-Case 'guard: any other value of the opt-out variable does not switch the warning off' {
        Assert-Contains (Invoke-Guard (New-GuardFixture -PyOut '1 0') @{ TESTSTAND_MCP_NO_PLATFORM_WARNING = '0' }).Message 'Pillow' 'opt-out value 0'
    }

    Invoke-Case 'guard: the opt-out does not consume the window (the warning shows once it is lifted)' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [void](Invoke-Guard $fixture @{ TESTSTAND_MCP_NO_PLATFORM_WARNING = '1' })
        Assert-Contains (Invoke-Guard $fixture -Now $FixedNow.AddHours(1)).Message 'Pillow' 'after the opt-out is lifted'
    }

    Invoke-Case 'guard: an unwritable state directory still warns and exits 0' {
        $fixture = New-GuardFixture -PyOut '1 0'
        [IO.File]::WriteAllText($fixture.State, 'a file where the state directory should be', $Utf8NoBom)
        $result = Invoke-Guard $fixture
        Assert-GuardClean $result
        Assert-Contains $result.Message 'Pillow' 'unwritable state'
    }

    Invoke-Case 'guard: the state goes to TESTSTAND_MCP_STATE_DIR, else CLAUDE_PLUGIN_DATA, else LOCALAPPDATA\teststand-mcp' {
        $fixture = New-GuardFixture -PyOut '1 0'
        $pluginData = Join-Path $fixture.Root 'plugin-data'
        $localAppData = Join-Path $fixture.Root 'local-app-data'
        [void](Invoke-Guard $fixture)
        Assert-True (@(Get-ChildItem -LiteralPath $fixture.State -File).Count -gt 0) 'state written to TESTSTAND_MCP_STATE_DIR'
        [void](Invoke-Guard $fixture @{ TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $pluginData; LOCALAPPDATA = $localAppData })
        Assert-True (@(Get-ChildItem -LiteralPath $pluginData -File).Count -gt 0) 'state written to CLAUDE_PLUGIN_DATA'
        Assert-True (-not (Test-Path -LiteralPath $localAppData)) 'LOCALAPPDATA untouched while CLAUDE_PLUGIN_DATA is set'
        [void](Invoke-Guard $fixture @{ TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $null; LOCALAPPDATA = $localAppData })
        Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $localAppData 'teststand-mcp') -File).Count -gt 0) 'state written to LOCALAPPDATA\teststand-mcp'
    }

    Invoke-Case 'guard: a relative TESTSTAND_MCP_STATE_DIR is ignored (only an absolute local path is accepted)' {
        $fixture = New-GuardFixture -PyOut '1 0'
        $pluginData = Join-Path $fixture.Root 'plugin-data'
        $relative = 'relative-state-' + [Guid]::NewGuid().ToString('N')
        [void](Invoke-Guard $fixture @{ TESTSTAND_MCP_STATE_DIR = $relative; CLAUDE_PLUGIN_DATA = $pluginData })
        Assert-True (@(Get-ChildItem -LiteralPath $pluginData -File).Count -gt 0) 'state fell back to CLAUDE_PLUGIN_DATA'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path (Get-Location).Path $relative))) 'no relative folder was created'
    }

    # --- a failing probe must not cost the TestStand warning or the rules pointer -----------------------
    Invoke-Case 'guard: a probe that throws is "not probed": the TestStand warning and the rules pointer are still printed, no state is recorded' {
        $fixture = New-GuardFixture -NoTestStand
        $result = Invoke-Guard $fixture -Extra @{ TESTSTAND_MCP_HOOK_SIMULATE = 'python-probe' }
        Assert-GuardClean $result
        Assert-Contains $result.Message 'NI TestStand was not detected' 'warning'
        Assert-NotContains $result.Message 'Python' 'no optional finding from a failed probe'
        Assert-Contains $result.Context 'teststand-rules' 'the rules pointer'
        Assert-True (-not (Test-Path -LiteralPath $fixture.State)) 'no state is recorded for a failed probe'
    }

    Invoke-Case 'guard: the simulated failure is ignored without TESTSTAND_MCP_STATE_DIR, so production cannot trip it' {
        $fixture = New-GuardFixture -NoTestStand -PyOut '1 0'
        $pluginData = Join-Path $fixture.Root 'plugin-data'
        $result = Invoke-Guard $fixture -Extra @{ TESTSTAND_MCP_HOOK_SIMULATE = 'python-probe'; TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $pluginData }
        Assert-GuardClean $result
        Assert-Contains $result.Message 'Pillow' 'the probe ran and found the missing optional package: the failure was never raised'
    }

    Invoke-Case 'hooks (pure): Invoke-HookSimulation throws for its name only, and only when the state directory is set' {
        . (Join-Path $HooksDir 'hook-common.ps1')
        Use-Environment @{ TESTSTAND_MCP_STATE_DIR = 'C:\Temp\state'; TESTSTAND_MCP_HOOK_SIMULATE = 'a' } {
            Invoke-HookSimulation 'b'
            $thrown = $null
            try { Invoke-HookSimulation 'a' } catch { $thrown = $_.Exception.Message }
            Assert-Equal 'simulated failure: a' $thrown 'the named failure'
        }
        Use-Environment @{ TESTSTAND_MCP_STATE_DIR = $null; TESTSTAND_MCP_HOOK_SIMULATE = 'a' } { Invoke-HookSimulation 'a' }
        Use-Environment @{ TESTSTAND_MCP_STATE_DIR = 'C:\Temp\state'; TESTSTAND_MCP_HOOK_SIMULATE = $null } { Invoke-HookSimulation 'a' }
    }

    # --- pure decisions ----------------------------------------------------------------------------------
    Invoke-Case 'guard (pure): Select-DueFindings keeps required findings always and optional ones only when due' {
        . $GuardPath
        $required = New-Finding 'teststand' $false 'a'
        $optional = New-Finding 'docx' $true 'b'
        Assert-Equal 'teststand' ((Select-DueFindings @($required, $optional) $false) | ForEach-Object { $_.Id }) -What 'not due'
        Assert-Equal 'teststand,docx' ((Select-DueFindings @($required, $optional) $true | ForEach-Object { $_.Id }) -join ',') 'due'
        Assert-Equal 0 @(Select-DueFindings @() $true).Count 'empty'
    }

    Invoke-Case 'guard (pure): Test-OptionalProbeDue is false when opted out or probed under 24 h ago, true otherwise (boundaries included)' {
        . $GuardPath
        $now = 2000000000L
        $day = 24 * 3600
        Assert-Equal $false (Test-OptionalProbeDue $null $now $true) 'opted out'
        Assert-Equal $true (Test-OptionalProbeDue $null $now $false) 'no state'
        Assert-Equal $true (Test-OptionalProbeDue ([pscustomobject]@{ probedAtEpoch = ($now - $day) }) $now $false) 'exactly 24 h'
        Assert-Equal $false (Test-OptionalProbeDue ([pscustomobject]@{ probedAtEpoch = ($now - $day + 1) }) $now $false) 'just under 24 h'
        Assert-Equal $true (Test-OptionalProbeDue ([pscustomobject]@{ probedAtEpoch = ($now + 60) }) $now $false) 'in the future'
        Assert-Equal $true (Test-OptionalProbeDue ([pscustomobject]@{ probedAtEpoch = 'soon' }) $now $false) 'not a number'
    }

    Invoke-Case 'guard (pure): the missing-set key is the sorted ids, empty when nothing is missing' {
        . $GuardPath
        Assert-Equal '' (Get-MissingSetKey @()) 'empty'
        Assert-Equal 'docx,pillow' (Get-MissingSetKey @((New-Finding 'pillow' $true 'x'), (New-Finding 'docx' $true 'y'))) 'sorted'
    }

    # --- the interpreter on this station ---------------------------------------------------------------
    Invoke-Case 'guard: the verdict agrees with the interpreter on this station (real python, no stubs)' {
        $realPython = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $realPython) { Write-Host '      (no python on this PATH: nothing to cross-check)'; return }
        $probe = 'import importlib.util as u; print(int(u.find_spec(''docx'') is not None), int(u.find_spec(''PIL'') is not None))'
        $direct = (& $realPython.Source -c $probe 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $direct -cnotmatch '\A[01] [01]\z') {
            Write-Host "      (the real python does not answer the probe cleanly [$direct]: nothing to cross-check)"
            return
        }
        $fixture = New-GuardFixture -PyOut $null
        $result = Invoke-Guard $fixture @{ PATH = ((Split-Path -Parent $realPython.Source) + ';' + $fixture.Env['PATH']) }
        Assert-GuardClean $result
        $names = @{ '1 1' = @(); '0 1' = @('python-docx'); '1 0' = @('Pillow'); '0 0' = @('python-docx', 'Pillow') }
        if ($names[$direct].Count -eq 0) { Assert-Equal $null $result.Message 'systemMessage' }
        foreach ($name in $names[$direct]) { Assert-Contains $result.Message $name 'warning' }
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
