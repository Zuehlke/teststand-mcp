# ============================================================================
#  hook-common.ps1 - helpers the plugin hooks share (dot-sourced, never run alone).
#  Windows PowerShell 5.1 and PowerShell 7. Every hook is silent and non-blocking:
#  these helpers never print a diagnostic, the callers swallow every failure.
# ============================================================================

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:DayInSeconds = 24 * 60 * 60
$script:MaxHookInputChars = 1048576   # hook stdin is untrusted: never buffer more than 1 MB
$script:StdinChunkChars = 4096
$script:AbsoluteLocalPathPattern = '\A[A-Za-z]:[\\/]'   # a drive path; a UNC path, \\?\ or a relative path never matches

# Runs the hook body and ends the process with exit code 0 whatever happens inside: a hook must
# never block the session or print a stack trace.
function Invoke-SilentHook {
    param([Parameter(Mandatory)][scriptblock]$Body)
    try { & $Body } catch { }
    exit 0
}

# The text on stdin as UTF-8, read in chunks until end of input; $null when it is longer than the
# cap (treated as no input). Claude Code closes stdin after writing the input; if it ever did not,
# the hook timeout in hooks.json bounds the wait.
function Read-HookStdin {
    $reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), $script:Utf8NoBom)
    try {
        $builder = New-Object System.Text.StringBuilder
        $chunk = New-Object char[] $script:StdinChunkChars
        while (($count = $reader.Read($chunk, 0, $chunk.Length)) -gt 0) {
            [void]$builder.Append($chunk, 0, $count)
            if ($builder.Length -gt $script:MaxHookInputChars) { return $null }
        }
        return $builder.ToString()
    } finally {
        $reader.Dispose()
    }
}

# Compact JSON in which every non-ASCII character is a \uXXXX escape, so the hook output is
# plain ASCII whatever the console code page is.
function ConvertTo-AsciiJson {
    param([Parameter(Mandatory)]$Object)
    $json = $Object | ConvertTo-Json -Compress -Depth 6
    return [regex]::Replace($json, '[^\x00-\x7F]', { param($match) '\u{0:x4}' -f [int][char]$match.Value })
}

function Write-HookOutput {
    param([Parameter(Mandatory)]$Object)
    [Console]::Out.Write((ConvertTo-AsciiJson $Object) + "`n")
}

# A property of a parsed JSON object, or $null (a missing property throws under strict mode).
function Get-HookProperty {
    param($Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# ----------------------------------------------------------------------------
#  Environment, clock and time windows
# ----------------------------------------------------------------------------
$script:StateDirOverrideVariable = 'TESTSTAND_MCP_STATE_DIR'      # tests and unusual stations
$script:PluginDataVariable = 'CLAUDE_PLUGIN_DATA'                 # survives plugin updates
$script:DefaultPluginName = 'teststand-mcp'                       # when the installed plugin.json is unreadable
$script:MarketplaceName = 'zuehlke-teststand'                     # .claude-plugin/marketplace.json name
$script:PluginRootVariable = 'CLAUDE_PLUGIN_ROOT'
$script:PluginJsonRelativePath = '.claude-plugin\plugin.json'
$script:ClockOverrideVariable = 'TESTSTAND_MCP_NOW'               # an ISO 8601 instant, for the tests
$script:FlagOnValue = '1'

# The value of an environment variable, or $null when it is unset or blank.
function Get-HookEnvironmentText {
    param([Parameter(Mandatory)][string]$Name)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

# The plugin root: CLAUDE_PLUGIN_ROOT, else the folder above hooks/.
function Get-PluginRoot {
    $fromEnvironment = Get-HookEnvironmentText $script:PluginRootVariable
    if ($null -ne $fromEnvironment) { return $fromEnvironment }
    return (Split-Path -Parent $PSScriptRoot)
}

# The installed plugin.json as an object; $null when it is missing or unreadable.
function Read-InstalledPluginJson {
    try {
        return ([IO.File]::ReadAllText((Join-Path (Get-PluginRoot) $script:PluginJsonRelativePath), $script:Utf8NoBom) | ConvertFrom-Json)
    } catch {
        return $null
    }
}

# This plugin's name (there is one plugin per TestStand bitness); $null when unreadable or not a plugin name.
function Get-HookPluginName {
    $name = [string](Get-HookProperty (Read-InstalledPluginJson) 'name')
    # It becomes a folder name and a tool prefix: a plugin name is lower case, digits and '-' only.
    if ($name -cnotmatch '\A[a-z0-9][a-z0-9-]*\z') { return $null }
    return $name
}

# A failure the tests can ask a hook for by name (TESTSTAND_MCP_HOOK_SIMULATE=<name>), to prove that the hook
# survives it. It is honoured ONLY when TESTSTAND_MCP_STATE_DIR is set too: that variable only tests (and a few
# unusual stations) set, so a stray value in a user's environment can never make a hook fail by itself.
$script:SimulateVariable = 'TESTSTAND_MCP_HOOK_SIMULATE'
function Invoke-HookSimulation {
    param([Parameter(Mandatory)][string]$Name)
    if ($null -eq (Get-HookEnvironmentText $script:StateDirOverrideVariable)) { return }
    if ((Get-HookEnvironmentText $script:SimulateVariable) -ceq $Name) { throw "simulated failure: $Name" }
}

# A switch variable: only the exact value 1 turns it on.
function Test-HookFlag {
    param([Parameter(Mandatory)][string]$Name)
    return ((Get-HookEnvironmentText $Name) -ceq $script:FlagOnValue)
}

# The current instant; TESTSTAND_MCP_NOW overrides it when it parses.
function Get-HookNow {
    $override = Get-HookEnvironmentText $script:ClockOverrideVariable
    $parsed = [DateTimeOffset]::MinValue
    if ($null -ne $override -and [DateTimeOffset]::TryParse($override, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) {
        return $parsed
    }
    return [DateTimeOffset]::UtcNow
}

function ConvertTo-EpochOrZero {
    param($Value)
    $parsed = 0L
    if ([long]::TryParse([string]$Value, [ref]$parsed)) { return $parsed }
    return 0L
}

# True when the instant is less than the window in the past. An instant in the future (the clock
# moved back) is not fresh, so a wrong clock can never silence a hook for longer than the window.
function Test-IsFresh {
    param([long]$Epoch, [long]$NowEpoch, [long]$WindowSeconds)
    $ageSeconds = $NowEpoch - $Epoch
    return ($ageSeconds -ge 0 -and $ageSeconds -lt $WindowSeconds)
}

function Test-IsAbsoluteLocalPath {
    param($Path)
    return (($Path -is [string]) -and $Path -match $script:AbsoluteLocalPathPattern)
}

# ----------------------------------------------------------------------------
#  State (the throttle / notify caches)
# ----------------------------------------------------------------------------
$script:StateReplaceAttempts = 3
$script:StateReplaceRetryMilliseconds = 50

# Where the caches live: TESTSTAND_MCP_STATE_DIR (an absolute local path only), else
# CLAUDE_PLUGIN_DATA, else %LOCALAPPDATA%\<plugin name>, so the two plugins never share a cache;
# $null when none of them is usable.
function Get-HookStateDir {
    $override = Get-HookEnvironmentText $script:StateDirOverrideVariable
    if ($null -ne $override -and (Test-IsAbsoluteLocalPath $override)) { return $override }
    $pluginData = Get-HookEnvironmentText $script:PluginDataVariable
    if ($null -ne $pluginData) { return $pluginData }
    $localAppData = Get-HookEnvironmentText 'LOCALAPPDATA'
    if ($null -eq $localAppData) { return $null }
    $name = Get-HookPluginName
    if ($null -eq $name) { $name = $script:DefaultPluginName }
    return (Join-Path $localAppData $name)
}

# The state file as an object; $null when it is missing, unreadable or not JSON. Opened with
# FileShare.ReadWrite and Delete so a concurrent atomic replace by another session never fails the read.
function Read-HookState {
    param([Parameter(Mandatory)][string]$Name)
    try {
        $dir = Get-HookStateDir
        if ($null -eq $dir) { return $null }
        $path = Join-Path $dir $Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
        $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
        $stream = New-Object IO.FileStream($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        try {
            $text = (New-Object IO.StreamReader($stream, $script:Utf8NoBom)).ReadToEnd()
        } finally {
            $stream.Dispose()
        }
        return ($text | ConvertFrom-Json)
    } catch {
        return $null
    }
}

# Moves the finished temp file over the target, retrying a few times: another session may hold the
# target open for an instant.
function Move-HookStateIntoPlace {
    param([string]$Temp, [string]$Target)
    for ($attempt = 1; $attempt -le $script:StateReplaceAttempts; $attempt++) {
        try {
            # A plain $null would reach .NET as an empty string, which Replace rejects as an illegal path.
            if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($Temp, $Target, [NullString]::Value) } else { [IO.File]::Move($Temp, $Target) }
            return
        } catch {
            if ($attempt -eq $script:StateReplaceAttempts) { throw }
            Start-Sleep -Milliseconds $script:StateReplaceRetryMilliseconds
        }
    }
}

# Writes the state file atomically (temp file, then move or replace). True when it was written;
# a failure is swallowed, so a read-only profile costs a repeated message and a repeated lookup.
function Write-HookState {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][hashtable]$State)
    $temp = $null
    try {
        $dir = Get-HookStateDir
        if ($null -eq $dir) { return $false }
        [void][IO.Directory]::CreateDirectory($dir)
        $target = Join-Path $dir $Name
        $temp = Join-Path $dir ($Name + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($temp, ($State | ConvertTo-Json -Compress), $script:Utf8NoBom)
        Move-HookStateIntoPlace $temp $target
        $temp = $null
        return $true
    } catch {
        return $false
    } finally {
        if ($null -ne $temp -and (Test-Path -LiteralPath $temp)) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }
}
