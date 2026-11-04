# ============================================================================
#  allow-readonly.ps1 - PreToolUse hook: lets the read-only TestStand MCP tools run
#  without a permission prompt. Windows PowerShell 5.1 and PowerShell 7.
#
#  hooks.json starts this script only for the literal names of tool-classes.json's
#  "allow" list, so a prompting tool starts no process. The script checks the same
#  list again (defence in depth) and prints nothing, exit 0, for everything else:
#  an unlisted tool, a malformed input, a missing or unreadable tool-classes.json.
#
#  A read tool that takes a file path also LOADS that file, and loading runs the file's
#  SequenceFileLoad callback (measured, docs/validation/claude-code-plugin-facts.md,
#  fact 18). So a call is auto-allowed only when EVERY path argument the tool declares
#  ("pathArguments" in tool-classes.json) is a .seq file on a local drive inside the
#  session's project folder (the hook input's "cwd"). Anything else keeps the prompt.
#
#  The tool prefix is this plugin's own (mcp__plugin_<name>_teststand__), with the name read
#  from the installed plugin.json: each plugin (one per TestStand bitness) allows only its own
#  tools. An unreadable plugin.json leaves no prefix, so nothing is allowed. AllowHook tests pin
#  the format to build/PluginIdentity.psm1.
# ============================================================================
Set-StrictMode -Version 2.0

try { . (Join-Path $PSScriptRoot 'hook-common.ps1') } catch { exit 0 }

$script:AllowReason = 'no TestStand file is modified'
$script:PluginToolPrefixFormat = 'mcp__plugin_{0}_teststand__'
$script:PluginToolPrefix = $null
$pluginName = Get-HookPluginName
if ($null -ne $pluginName) { $script:PluginToolPrefix = $script:PluginToolPrefixFormat -f $pluginName }
$script:ToolClassesFileName = 'tool-classes.json'
$script:AllowedFileExtension = '.seq'
$script:DirectorySeparator = '\'
$script:AlternateStreamSearchStart = 2          # a ':' after the drive letter names an NTFS stream
$script:ParentSegmentPattern = '(\A|\\)\.\.(\\|\z)'
$script:IllegalPathCharacterPattern = '[<>"|*?\x00-\x1f]'   # wildcards and the characters Windows forbids in a name

# ----------------------------------------------------------------------------
#  The request
# ----------------------------------------------------------------------------
# The tool name after the plugin prefix; $null for anything else (another plugin, a direct
# server, a non-string).
function Get-ShortToolName {
    param($ToolName)
    if ($ToolName -isnot [string] -or $null -eq $script:PluginToolPrefix) { return $null }
    if (-not $ToolName.StartsWith($script:PluginToolPrefix, [StringComparison]::Ordinal)) { return $null }
    return $ToolName.Substring($script:PluginToolPrefix.Length)
}

# The parsed hook input; $null when the text is not a JSON object.
function Get-HookRequest {
    param([string]$InputText)
    if ([string]::IsNullOrWhiteSpace($InputText)) { return $null }
    $request = $InputText | ConvertFrom-Json
    if ($request -isnot [pscustomobject]) { return $null }
    return $request
}

# True when the (possibly malformed) tool name is a plugin tool whose short name is allow-listed.
function Test-ToolAllowed {
    param($ToolName, [string[]]$AllowList)
    $shortName = Get-ShortToolName $ToolName
    if ([string]::IsNullOrEmpty($shortName)) { return $false }
    return ($AllowList -ccontains $shortName)
}

# ----------------------------------------------------------------------------
#  Paths
# ----------------------------------------------------------------------------
# The full, normalised form of an absolute drive path ("..", "." and mixed separators resolved);
# $null for anything else (UNC, \\?\, \\.\, relative, drive-relative, a stream name, an illegal path).
function ConvertTo-FullLocalPath {
    param($Path)
    if (-not (Test-IsAbsoluteLocalPath $Path)) { return $null }
    if ($Path -match $script:IllegalPathCharacterPattern) { return $null }
    try { $full = [IO.Path]::GetFullPath($Path) } catch { return $null }
    if ($full.IndexOf(':', $script:AlternateStreamSearchStart) -ge 0) { return $null }
    if ($full -match $script:ParentSegmentPattern) { return $null }
    return $full
}

# The project folder as a prefix that ends in one separator, so a match is on a segment boundary
# (C:\proj does not contain C:\proj2\a.seq). A drive root is no project folder.
function ConvertTo-ProjectPrefix {
    param($ProjectDir)
    $full = ConvertTo-FullLocalPath $ProjectDir
    if ($null -eq $full) { return $null }
    if ($full -ceq [IO.Path]::GetPathRoot($full)) { return $null }
    return ($full.TrimEnd($script:DirectorySeparator) + $script:DirectorySeparator)
}

# True for a .seq file whose full path lies inside the project folder.
function Test-SeqPathInsideProject {
    param($Path, $ProjectDir)
    $prefix = ConvertTo-ProjectPrefix $ProjectDir
    $full = ConvertTo-FullLocalPath $Path
    if ($null -eq $prefix -or $null -eq $full) { return $false }
    if ([IO.Path]::GetExtension($full) -ine $script:AllowedFileExtension) { return $false }
    return $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

# True when every declared path argument that is present is a .seq file inside the project folder.
# An argument that is absent is fine (it is optional, or the server rejects the call); a present
# non-string value prompts.
function Test-PathArgumentsAllowed {
    param($ToolInput, [string[]]$ArgumentNames, $ProjectDir)
    if (@($ArgumentNames).Count -eq 0) { return $true }
    if ($null -eq $ToolInput) { return $true }
    if ($ToolInput -isnot [pscustomobject]) { return $false }
    foreach ($name in $ArgumentNames) {
        if ($null -eq $ToolInput.PSObject.Properties[$name]) { continue }
        if (-not (Test-SeqPathInsideProject $ToolInput.$name $ProjectDir)) { return $false }
    }
    return $true
}

# ----------------------------------------------------------------------------
#  The policy and the decision
# ----------------------------------------------------------------------------
# @{ Allow = [string[]]; PathArguments = <object: tool name -> argument names> } from tool-classes.json.
function Read-AllowPolicy {
    param([string]$Path)
    $json = [IO.File]::ReadAllText($Path, $script:Utf8NoBom) | ConvertFrom-Json
    return @{
        Allow         = @(@(Get-HookProperty $json 'allow') | Where-Object { $_ -is [string] })
        PathArguments = (Get-HookProperty $json 'pathArguments')
    }
}

function Test-ToolCallAllowed {
    param($Request, $Policy)
    if ($null -eq $Request) { return $false }
    $toolName = Get-HookProperty $Request 'tool_name'
    if (-not (Test-ToolAllowed $toolName $Policy.Allow)) { return $false }
    $argumentNames = @(Get-HookProperty $Policy.PathArguments (Get-ShortToolName $toolName) | Where-Object { $_ -is [string] })
    return (Test-PathArgumentsAllowed (Get-HookProperty $Request 'tool_input') $argumentNames (Get-HookProperty $Request 'cwd'))
}

function New-AllowDecision {
    return @{
        hookSpecificOutput = [ordered]@{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'allow'
            permissionDecisionReason = $script:AllowReason
        }
    }
}

function Invoke-AllowHook {
    $request = Get-HookRequest (Read-HookStdin)
    $policy = Read-AllowPolicy (Join-Path $PSScriptRoot $script:ToolClassesFileName)
    if (Test-ToolCallAllowed $request $policy) { Write-HookOutput (New-AllowDecision) }
}

# Dot-sourcing (the tests) loads the functions only.
if ($MyInvocation.InvocationName -ne '.') { Invoke-SilentHook { Invoke-AllowHook } }
