# ============================================================================
#  platform-guard.ps1 - SessionStart (startup) hook: ONE combined ASCII warning when the
#  station lacks TestStand or one of the doc-agent dependencies, plus a one-line context
#  that points the model at the teststand-rules skill.
#  Windows PowerShell 5.1 and PowerShell 7. Never blocks: it prints one JSON object
#  and exits 0, and every failure is silent.
#
#    - TestStand missing: warned EVERY time (the server cannot work without it). Only the TestStand
#      of the plugin's own bitness counts (the x86 plugin drives 32-bit, the x64 plugin 64-bit);
#      when only the other one is installed, the warning names the plugin that drives it.
#    - Python, python-docx, Pillow, Edge/Chrome missing: OPTIONAL (only the doc and
#      presentation agents need them). They are probed at most once per 24 hours; the
#      outcome (the missing set, possibly empty) is cached with its time in
#      platform-guard.json, and inside the window nothing is probed and nothing is
#      warned. TESTSTAND_MCP_NO_PLATFORM_WARNING=1 switches this part off.
#  The rules pointer and a missing-TestStand warning do not depend on any optional probe:
#  a probe that fails is "not probed", never a reason to stay silent.
#  The server cannot start off Windows, so there is no non-Windows variant of this hook.
# ============================================================================
Set-StrictMode -Version 2.0

try { . (Join-Path $PSScriptRoot 'hook-common.ps1') } catch { exit 0 }

$script:OptOutVariable = 'TESTSTAND_MCP_NO_PLATFORM_WARNING'
$script:GuardStateFile = 'platform-guard.json'
$script:TestStandFolderFilter = 'TestStand*'
$script:TestStandVendorFolder = 'National Instruments'
$script:BrowserRelativePaths = @('Microsoft\Edge\Application\msedge.exe', 'Google\Chrome\Application\chrome.exe')
$script:BrowserCommandNames = @('msedge', 'chrome')
$script:ProgramFilesVariables = @('ProgramW6432', 'ProgramFiles', 'ProgramFiles(x86)')
# The TestStand bitness each plugin drives, with that bitness's Program Files root and the environment
# variable its TestStand sets. A plugin's name is the base name plus the row's suffix (the NameSuffix of
# build/PluginIdentity.psm1; PlatformGuard tests pin this copy), so neither archive spells the other's name.
# The suffixed row comes first: every name ends in the empty suffix.
$script:PluginBitness = @(
    @{ Suffix = '-x64'; Bits = 64; RootVariable = 'ProgramW6432';      EnvVariable = 'TestStand64' }
    @{ Suffix = '';     Bits = 32; RootVariable = 'ProgramFiles(x86)'; EnvVariable = 'TestStand' }
)

# Probe budget. The hook timeout in hooks.json is 10 s. Worst case: a PowerShell start of about
# 0.6 s, then two interpreters that both hang, each cut off after 3 s plus up to 1 s to kill its
# process tree: 0.6 + 2 * (3 + 1) = 8.6 s, which stays under 10 s. The stream reads after a normal
# exit are bounded too, so no step can wait unboundedly.
$script:ProbeTimeoutMilliseconds = 3000
$script:KillTimeoutMilliseconds = 1000
$script:StreamReadMilliseconds = 1000
$script:TaskKillRelativePath = 'System32\taskkill.exe'
# One probe answers both questions: "<docx present> <Pillow present>", each 1 or 0. -E ignores PYTHON*
# environment variables (a PYTHONPATH could shadow the check); -I is not used, it would also drop the
# user site-packages where pip install --user puts the two packages.
$script:PythonProbeCode = "import importlib.util as u; print(int(u.find_spec('docx') is not None), int(u.find_spec('PIL') is not None))"
$script:PythonProbePattern = '\A([01])\s+([01])\z'
$script:PythonCandidates = @(
    @{ Command = 'py';     Arguments = '-3 -E -c' }
    @{ Command = 'python'; Arguments = '-E -c' }
)

$script:RulesContext = 'TestStand MCP: before any TestStand work in this session, load the teststand-rules skill (the usage rules for the TestStand MCP tools).'
$script:MessageHeader = 'TestStand MCP: this station needs attention.'
$script:TestStandFix = 'install NI TestStand 2019 or later, or start Claude Code where TestStand is installed'
$script:TestStandText = "NI TestStand was not detected. Fix: $($script:TestStandFix)."
$script:OtherBitnessText = 'NI TestStand {0}-bit was not detected, but {1}-bit TestStand is installed, and this plugin ({2}) drives {0}-bit TestStand only. Fix: for the {1}-bit TestStand install the {3} plugin (claude plugin install {3}@{4}), or install {0}-bit NI TestStand 2019 or later.'
$script:PythonText = 'Python was not found (neither "py -3" nor "python" answered). Fix: install Python 3 from python.org, then run: py -m pip install python-docx pillow'
$script:DocxText = 'python-docx is not installed. Fix: {0}'
$script:PillowText = 'Pillow is not installed. Fix: {0}'
$script:BrowserText = 'Edge or Chrome was not found. Fix: install Microsoft Edge or Google Chrome.'
$script:PipInstallFormat = '{0} -m pip install python-docx pillow'
$script:OptionalNote = 'The doc and presentation agents are optional: they need Python, python-docx, Pillow and Edge or Chrome. The MCP tools work without them.'
$script:OptOutNote = "To hide these optional notes, set $($script:OptOutVariable)=1."

# ----------------------------------------------------------------------------
#  Probing the station
# ----------------------------------------------------------------------------
function Get-ProgramFilesRoots {
    $roots = @()
    foreach ($variable in $script:ProgramFilesVariables) {
        $path = Get-HookEnvironmentText $variable
        if ($null -ne $path -and (Test-Path -LiteralPath $path -PathType Container) -and ($roots -notcontains $path)) { $roots += $path }
    }
    return $roots
}

# A TestStand of one bitness: its environment variable, or a TestStand folder under its own Program Files root.
function Test-TestStandDetected {
    param([Parameter(Mandatory)]$Bitness)
    if ($null -ne (Get-HookEnvironmentText $Bitness.EnvVariable)) { return $true }
    $root = Get-HookEnvironmentText $Bitness.RootVariable
    if ($null -eq $root) { return $false }
    $vendorDir = Join-Path $root $script:TestStandVendorFolder
    if (-not (Test-Path -LiteralPath $vendorDir -PathType Container)) { return $false }
    return (@(Get-ChildItem -LiteralPath $vendorDir -Directory -Filter $script:TestStandFolderFilter -ErrorAction SilentlyContinue).Count -gt 0)
}

# The TestStand warning for this plugin, or $null when its own TestStand is there. An unreadable plugin
# name counts as the default plugin.
function Get-TestStandWarning {
    $ownName = Get-HookPluginName
    if ($null -eq $ownName) { $ownName = $script:DefaultPluginName }
    $own = @($script:PluginBitness | Where-Object { $ownName.EndsWith($_.Suffix, [StringComparison]::Ordinal) })[0]
    if (Test-TestStandDetected $own) { return $null }
    $baseName = $ownName.Substring(0, $ownName.Length - $own.Suffix.Length)
    foreach ($other in $script:PluginBitness) {
        if ($other.Bits -ne $own.Bits -and (Test-TestStandDetected $other)) {
            return ($script:OtherBitnessText -f $own.Bits, $other.Bits, $ownName, ($baseName + $other.Suffix), $script:MarketplaceName)
        }
    }
    return $script:TestStandText
}

function Test-BrowserInstalled {
    foreach ($root in (Get-ProgramFilesRoots)) {
        foreach ($relativePath in $script:BrowserRelativePaths) {
            if (Test-Path -LiteralPath (Join-Path $root $relativePath) -PathType Leaf) { return $true }
        }
    }
    foreach ($name in $script:BrowserCommandNames) {
        if ($null -ne (Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)) { return $true }
    }
    return $false
}

# Ends a process and everything it started (a .cmd stub or a launcher leaves children behind).
function Stop-ProcessTree {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    try {
        $killer = New-Object System.Diagnostics.ProcessStartInfo
        $killer.FileName = Join-Path $env:SystemRoot $script:TaskKillRelativePath
        $killer.Arguments = "/T /F /PID $($Process.Id)"
        $killer.UseShellExecute = $false
        $killer.CreateNoWindow = $true
        $killer.RedirectStandardOutput = $true
        $killer.RedirectStandardError = $true
        $started = [System.Diagnostics.Process]::Start($killer)
        try {
            if (-not $started.WaitForExit($script:KillTimeoutMilliseconds)) { $started.Kill() }
        } finally {
            $started.Dispose()
        }
    } catch {
        try { $Process.Kill() } catch { }
    }
}

# Runs one program with a hard time limit; its stdout when it exits 0 in time, else $null.
# It runs in the hooks folder, so a project's own python.exe or py.ini is not picked up through the
# current directory.
function Invoke-Probe {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string]$Arguments, [int]$TimeoutMilliseconds)
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $PSScriptRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            Stop-ProcessTree $process
            return $null
        }
        if ($process.ExitCode -ne 0) { return $null }
        if (-not $stdout.Wait($script:StreamReadMilliseconds)) { return $null }
        return $stdout.Result
    } finally {
        $process.Dispose()
    }
}

# The first interpreter that answers the probe: @{ Command; HasDocx; HasPillow }, or $null.
function Find-Python {
    Invoke-HookSimulation 'python-probe'
    foreach ($candidate in $script:PythonCandidates) {
        $application = Get-Command -Name $candidate.Command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $application) { continue }
        try {
            $output = Invoke-Probe -FilePath $application.Source -Arguments ($candidate.Arguments + ' "' + $script:PythonProbeCode + '"') -TimeoutMilliseconds $script:ProbeTimeoutMilliseconds
        } catch {
            continue
        }
        if ($null -eq $output) { continue }
        $match = [regex]::Match($output.Trim(), $script:PythonProbePattern)
        if (-not $match.Success) { continue }
        return @{ Command = $candidate.Command; HasDocx = ($match.Groups[1].Value -eq '1'); HasPillow = ($match.Groups[2].Value -eq '1') }
    }
    return $null
}

# ----------------------------------------------------------------------------
#  Findings: pure decisions over the facts
# ----------------------------------------------------------------------------
function New-Finding {
    param([string]$Id, [bool]$IsOptional, [string]$Text)
    return [pscustomobject]@{ Id = $Id; IsOptional = $IsOptional; Text = $Text }
}

# $Facts: TestStandWarning ($null when TestStand is fine), and Optional = $null (not probed) or @{ Python = $null | @{ Command; HasDocx; HasPillow }; HasBrowser }.
function Get-Findings {
    param([Parameter(Mandatory)]$Facts)
    $findings = @()
    if ($null -ne $Facts.TestStandWarning) { $findings += New-Finding 'teststand' $false $Facts.TestStandWarning }
    if ($null -eq $Facts.Optional) { return $findings }
    $python = $Facts.Optional.Python
    if ($null -eq $python) {
        $findings += New-Finding 'python' $true $script:PythonText
    } else {
        $pip = $script:PipInstallFormat -f $python.Command
        if (-not $python.HasDocx)   { $findings += New-Finding 'docx' $true ($script:DocxText -f $pip) }
        if (-not $python.HasPillow) { $findings += New-Finding 'pillow' $true ($script:PillowText -f $pip) }
    }
    if (-not $Facts.Optional.HasBrowser) { $findings += New-Finding 'browser' $true $script:BrowserText }
    return $findings
}

# The identity of a missing set: the sorted ids ('' when nothing is missing).
function Get-MissingSetKey {
    param($Findings)
    $ids = @($Findings | ForEach-Object { $_.Id })
    [Array]::Sort($ids, [StringComparer]::Ordinal)
    return ($ids -join ',')
}

# True when the optional part must be probed now: not switched off, and not probed within the last 24 hours.
function Test-OptionalProbeDue {
    param($State, [long]$NowEpochSeconds, [bool]$IsOptedOut)
    if ($IsOptedOut) { return $false }
    $probedAt = ConvertTo-EpochOrZero (Get-HookProperty $State 'probedAtEpoch')
    return (-not (Test-IsFresh $probedAt $NowEpochSeconds $script:DayInSeconds))
}

# The findings to show: every required one, and the optional ones only when the optional part is due.
function Select-DueFindings {
    param([Parameter(Mandatory)]$Findings, [bool]$IsOptionalDue)
    return @($Findings | Where-Object { -not $_.IsOptional -or $IsOptionalDue })
}

function Format-GuardMessage {
    param([Parameter(Mandatory)]$Findings)
    $lines = @($script:MessageHeader)
    $lines += @($Findings | ForEach-Object { '- ' + $_.Text })
    if (@($Findings | Where-Object { $_.IsOptional }).Count -gt 0) {
        $lines += $script:OptionalNote
        $lines += $script:OptOutNote
    }
    return ($lines -join "`n")
}

# ----------------------------------------------------------------------------
#  The hook
# ----------------------------------------------------------------------------
# $null when the probes fail as a whole ("not probed"): the other findings must survive that.
function Get-OptionalFacts {
    try { return @{ Python = (Find-Python); HasBrowser = (Test-BrowserInstalled) } } catch { return $null }
}

# A detection that throws counts as "detected": a warning must be about something observed.
function Get-SafeTestStandWarning {
    try { return (Get-TestStandWarning) } catch { return $null }
}

function Invoke-PlatformGuard {
    $nowEpoch = (Get-HookNow).ToUnixTimeSeconds()
    $isOptionalDue = Test-OptionalProbeDue (Read-HookState $script:GuardStateFile) $nowEpoch (Test-HookFlag $script:OptOutVariable)
    $facts = @{ TestStandWarning = (Get-SafeTestStandWarning); Optional = $null }
    if ($isOptionalDue) { $facts.Optional = Get-OptionalFacts }
    $allFindings = @(Get-Findings $facts)
    $shown = @(Select-DueFindings $allFindings $isOptionalDue)

    $output = [ordered]@{}
    if ($shown.Count -gt 0) { $output['systemMessage'] = Format-GuardMessage $shown }
    $output['hookSpecificOutput'] = [ordered]@{ hookEventName = 'SessionStart'; additionalContext = $script:RulesContext }
    Write-HookOutput $output

    # The probe outcome is recorded after the output is written, so a failing write can never cost the message.
    if ($null -ne $facts.Optional) {
        $missing = @($allFindings | Where-Object { $_.IsOptional })
        [void](Write-HookState $script:GuardStateFile @{ probedAtEpoch = $nowEpoch; optionalMissing = (Get-MissingSetKey $missing) })
    }
}

# Dot-sourcing (the tests) loads the functions only. Off Windows nothing runs: the server cannot start there.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-SilentHook { if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { Invoke-PlatformGuard } }
}
