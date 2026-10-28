# ============================================================================
#  New-HooksJson.ps1 - generates plugin/hooks/hooks.json (committed).
#  The PreToolUse matcher is the "|"-joined list of the full tool names of the
#  allow-list in plugin/hooks/tool-classes.json, each with the plugin tool prefix
#  from build/PluginIdentity.psm1 (a matcher of letters, digits, _ - and | is a list
#  of EXACT strings, never a regular expression), so a tool that prompts starts no
#  process. The classification is checked against the tools the server registers
#  first; an invalid or empty one writes nothing and exits 1.
#  Every hook is in exec form: "command": "powershell.exe" with an "args" list, so no shell
#  (and no Git Bash) is involved and a plugin path with a space or a non-ASCII letter needs
#  no quoting.
#  Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\New-HooksJson.ps1
#  -OutputPath / -ClassesPath / -RegistryPath let the tests point it at fixtures.
#  The JSON is written by ConvertTo-StableJson (LF, UTF-8 without BOM, final newline) so the
#  bytes are the same on Windows PowerShell 5.1 and PowerShell 7: ConvertTo-Json indents
#  differently in the two.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'plugin\hooks\hooks.json'),
    [string]$ClassesPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'plugin\hooks\tool-classes.json'),
    [string]$RegistryPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Tools\TestStandToolRegistry.cs')
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$Description = 'TestStand MCP hooks: read-only TestStand tools on .seq files inside the project run without a permission prompt, a startup check warns about a missing TestStand or doc-agent dependency, and a startup notice names a newer release. The hooks print nothing and never block when something fails.'
$PreToolUseEvent = 'PreToolUse'
$SessionStartEvent = 'SessionStart'
$HookExecutable = 'powershell.exe'
$HookArgumentsBeforeScript = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File')
$ScriptPathFormat = '${{CLAUDE_PLUGIN_ROOT}}/hooks/{0}'
$PreToolUseScript = 'allow-readonly.ps1'
$SessionStartScripts = @('platform-guard.ps1', 'update-notify.ps1')
$SessionStartMatcher = 'startup'          # not resume, clear or compact
$HookTimeoutSeconds = 10                  # hook timeouts are in seconds (slice 0, fact 5)
$ToolNameSeparator = '|'
$IndentUnit = '  '

# A JSON string literal: backslash, quote and control characters escaped, everything else must be ASCII.
function ConvertTo-JsonStringLiteral {
    param([Parameter(Mandatory)][string]$Text)
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($char in $Text.ToCharArray()) {
        switch ($char) {
            '"'  { [void]$builder.Append('\"') }
            '\'  { [void]$builder.Append('\\') }
            default {
                if ([int]$char -lt 32 -or [int]$char -gt 126) { [void]$builder.Append(('\u{0:x4}' -f [int]$char)) }
                else { [void]$builder.Append($char) }
            }
        }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

# One stable layout for a tree of ordered dictionaries, arrays, strings, integers and booleans: two-space
# indent, one member or element per line, keys in the given order.
function ConvertTo-StableJson {
    param($Value, [int]$Depth = 0)
    $outer = $IndentUnit * $Depth
    $inner = $IndentUnit * ($Depth + 1)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    if ($Value -is [string]) { return (ConvertTo-JsonStringLiteral $Value) }
    if ($Value -is [int] -or $Value -is [long]) { return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture) }
    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Count -eq 0) { return '{}' }
        $members = @($Value.Keys | ForEach-Object { $inner + (ConvertTo-JsonStringLiteral $_) + ': ' + (ConvertTo-StableJson $Value[$_] ($Depth + 1)) })
        return "{`n" + ($members -join ",`n") + "`n$outer}"
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @($Value | ForEach-Object { $inner + (ConvertTo-StableJson $_ ($Depth + 1)) })
        if ($items.Count -eq 0) { return '[]' }
        return "[`n" + ($items -join ",`n") + "`n$outer]"
    }
    throw "ConvertTo-StableJson cannot write a value of type $($Value.GetType().FullName)"
}

function New-HookEntry {
    param([Parameter(Mandatory)][string]$ScriptName)
    $arguments = @($HookArgumentsBeforeScript) + ($ScriptPathFormat -f $ScriptName)
    return [ordered]@{
        type    = 'command'
        command = $HookExecutable
        args    = [object[]]$arguments
        timeout = $HookTimeoutSeconds
    }
}

function New-HooksTree {
    param([Parameter(Mandatory)][string[]]$ToolNames, [Parameter(Mandatory)][string]$ToolPrefix)
    $matcher = ($ToolNames | ForEach-Object { $ToolPrefix + $_ }) -join $ToolNameSeparator
    $sessionStartEntries = [object[]]@($SessionStartScripts | ForEach-Object { New-HookEntry $_ })
    return [ordered]@{
        description = $Description
        hooks       = [ordered]@{
            $PreToolUseEvent   = [object[]]@([ordered]@{ matcher = $matcher; hooks = [object[]]@(New-HookEntry $PreToolUseScript) })
            $SessionStartEvent = [object[]]@([ordered]@{ matcher = $SessionStartMatcher; hooks = $sessionStartEntries })
        }
    }
}

# Dot-sourcing (the tests) loads the functions only.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'PluginIdentity.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'ToolClassification.psm1') -Force

        $classes = Read-ToolClasses -Path $ClassesPath
        $allow = @(Get-ClassList $classes 'allow')
        if ($allow.Count -eq 0) {
            Write-BuildFailure -What "The allow list in $ClassesPath is empty." `
                -Why 'A PreToolUse entry with an empty matcher would start the hook for EVERY tool of every server, so no hooks.json is written.' `
                -Fix 'List the read-only tools under "allow", or do not generate the hooks.'
        }
        Assert-ToolClassification -Classes $classes -RegisteredTools (Get-RegisteredToolNames -RegistryPath $RegistryPath) `
            -ClassesPath $ClassesPath -ToolParameters (Get-RegisteredToolParameters -RegistryPath $RegistryPath)

        $text = (ConvertTo-StableJson (New-HooksTree -ToolNames $allow -ToolPrefix (Get-PluginIdentity).pluginToolPrefix)) + "`n"
        $target = Resolve-BuildPath $OutputPath
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        [IO.File]::WriteAllText($target, $text, (Get-Utf8NoBomEncoding))
        Write-Host "Wrote $OutputPath ($($allow.Count) allow-listed tools)"
        exit 0
    } catch {
        if (Get-Command Get-ScriptFailureText -ErrorAction SilentlyContinue) { Write-Host (Get-ScriptFailureText -ErrorRecord $_) }
        else { Write-Host "What: $($_.Exception.Message)" }
        exit 1
    }
}
