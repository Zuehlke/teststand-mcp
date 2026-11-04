# ============================================================================
#  AllowHook.Tests.ps1 - plain PowerShell asserts for plugin/hooks/allow-readonly.ps1: the
#  read-only tools run without a prompt, a path-taking tool only for .seq files inside the
#  session's project folder, and every other or malformed input prints nothing.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\AllowHook.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  This file is ASCII only on purpose (5.1 reads a BOM-less script as ANSI).
# ============================================================================
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'AllowHookTests_'
. (Join-Path $PSScriptRoot 'HookTestSupport.ps1')

$AllowHookPath = Join-Path $HooksDir 'allow-readonly.ps1'
$AllowReason = 'no TestStand file is modified'
$ExpectedToolPrefix = 'mcp__plugin_teststand-mcp_teststand__'
$ProjectDir = 'C:\Work\Proj'


# The hook reads its plugin name from the installed plugin.json: by default the repository's plugin/ (the
# folder above the script), with -PluginRoot a fixture's.
function Invoke-AllowHookProcess {
    param([AllowEmptyString()][string]$Stdin, [string]$PluginRoot = $null, [string]$ScriptPath = $AllowHookPath)
    return Invoke-HookScript -ScriptPath $ScriptPath -StdinText $Stdin -EnvOverrides @{ TESTSTAND_MCP_STATE_DIR = (New-TempDir); CLAUDE_PLUGIN_ROOT = $PluginRoot }
}

function Assert-AllowDecision {
    param($Result)
    Assert-Equal 0 $Result.ExitCode "exit code (stderr: $($Result.Stderr))"
    Assert-AsciiOnly $Result.Stdout 'stdout'
    $decision = $Result.Stdout | ConvertFrom-Json
    Assert-Equal 'PreToolUse' $decision.hookSpecificOutput.hookEventName 'hookEventName'
    Assert-Equal 'allow' $decision.hookSpecificOutput.permissionDecision 'permissionDecision'
    Assert-Equal $AllowReason $decision.hookSpecificOutput.permissionDecisionReason 'permissionDecisionReason'
}

function Assert-NoDecision {
    param($Result)
    Assert-Equal 0 $Result.ExitCode "exit code (stderr: $($Result.Stderr))"
    Assert-Equal '' $Result.Stdout 'stdout'
    Assert-Equal '' $Result.Stderr 'stderr'
}

# The decision of the functions (dot-sourced into the calling case) for one call, as a parsed hook input.
function Test-CallAllowed {
    param([string]$Tool, $ToolInput = @{}, [AllowNull()][string]$Cwd = $ProjectDir)
    $json = ConvertTo-HookInput ($ExpectedToolPrefix + $Tool) $ToolInput $Cwd
    $policy = Read-AllowPolicy $ToolClassesPath
    return (Test-ToolCallAllowed (Get-HookRequest $json) $policy)
}

try {
    Import-Module (Join-Path $RepoRoot 'build\ToolClassification.psm1') -Force   # Read-ToolClasses

    # --- the prefix is the installed plugin's own: the format is pinned to the identity table -----
    Invoke-Case 'prefix: the identity module still derives the literal prefix the hook uses' {
        Assert-Equal $ExpectedToolPrefix $IdentityPrefix 'PluginIdentity prefix'
    }

    Invoke-Case 'prefix: the hook builds every plugin''s identity prefix from its name (drift test)' {
        . $AllowHookPath
        Assert-Equal $IdentityPrefix $script:PluginToolPrefix 'allow-readonly.ps1 prefix of the repository plugin'
        foreach ($architecture in (Get-PluginArchitectures)) {
            $identity = Get-PluginIdentity -Architecture $architecture
            Assert-Equal $identity.pluginToolPrefix ($script:PluginToolPrefixFormat -f $identity.name) "$architecture prefix"
        }
    }

    Invoke-Case 'allow hook: under the x64 plugin its own read-only tool is allowed, the x86 one and a mutating one are not' {
        $x64 = Get-PluginIdentity -Architecture 'x64'
        $root = New-PluginRootFixture $x64.name
        Assert-AllowDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($x64.pluginToolPrefix + 'get_station_info')) -PluginRoot $root)
        Assert-AllowDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($x64.pluginToolPrefix + 'get_steps') @{ sequence_file_path = 'C:\Work\Proj\a.seq' } $ProjectDir) -PluginRoot $root)
        Assert-NoDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')) -PluginRoot $root)
        Assert-NoDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($x64.pluginToolPrefix + 'insert_step')) -PluginRoot $root)
    }

    Invoke-Case 'allow hook: a missing, unreadable or odd plugin name allows nothing' {
        $roots = @((New-TempDir), (New-PluginRootFixture -RawJson '{ "name": '), (New-PluginRootFixture -Name '..\evil'), (New-PluginRootFixture -Name 'Teststand-MCP'))
        foreach ($root in $roots) {
            Assert-NoDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')) -PluginRoot $root)
        }
    }

    # --- a decision for a tool name -----------------------------------------------------------------
    Invoke-Case 'allow hook: a read-only tool with no path argument runs without a prompt, with the reason (needs no cwd)' {
        Assert-AllowDecision (Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')))
    }

    $PromptingTools = @('insert_step', 'evaluate_expression', 'evaluate_in_thread_context', 'diff_sequence_files',
                        'compare_sequence_files', 'find_file', 'analyze_sequence_file', 'read_csv_lines')
    foreach ($tool in $PromptingTools) {
        Invoke-Case "allow hook: $tool still prompts (prints nothing, exits 0)" {
            $hookInput = ConvertTo-HookInput ($IdentityPrefix + $tool) @{ file_path = 'C:\Work\Proj\a.seq' } $ProjectDir
            Assert-NoDecision (Invoke-AllowHookProcess $hookInput)
        }
    }

    $MalformedInputs = [ordered]@{
        'empty input'                  = ''
        'whitespace only'              = "  `r`n "
        'non-JSON text'                = 'this is not json'
        'truncated JSON'               = '{"tool_name": "mcp__plugin_'
        'a JSON array'                 = '["get_steps"]'
        'JSON null'                    = 'null'
        'a JSON string'                = '"get_station_info"'
        'an object without tool_name'  = '{"hook_event_name":"PreToolUse"}'
        'a numeric tool_name'          = '{"tool_name": 42}'
        'a null tool_name'             = '{"tool_name": null}'
        'an empty tool_name'           = '{"tool_name": ""}'
        'a bare short name'            = '{"tool_name": "get_station_info"}'
        'a direct (non-plugin) prefix' = '{"tool_name": "mcp__teststand__get_station_info"}'
        'another plugin'               = '{"tool_name": "mcp__plugin_other-plugin_teststand__get_station_info"}'
        'another server of this plugin' = '{"tool_name": "mcp__plugin_teststand-mcp_other__get_station_info"}'
        'an upper-case short name'     = ('{"tool_name": "' + $ExpectedToolPrefix + 'GET_STATION_INFO"}')
        'an unregistered short name'   = ('{"tool_name": "' + $ExpectedToolPrefix + 'get_station_info_and_more"}')
        'a tool_input that is a string' = ('{"tool_name": "' + $ExpectedToolPrefix + 'get_steps", "tool_input": "x", "cwd": "C:\\Work\\Proj"}')
    }
    foreach ($label in $MalformedInputs.Keys) {
        Invoke-Case "allow hook: $label prints nothing and exits 0" {
            Assert-NoDecision (Invoke-AllowHookProcess $MalformedInputs[$label])
        }
    }

    Invoke-Case 'allow hook: a missing tool-classes.json next to the script prints nothing and exits 0' {
        $dir = New-HooksCopy -Without 'tool-classes.json'
        $result = Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')) -PluginRoot (Join-Path $RepoRoot 'plugin') -ScriptPath (Join-Path $dir 'allow-readonly.ps1')
        Assert-NoDecision $result
    }

    Invoke-Case 'allow hook: a corrupt tool-classes.json prints nothing and exits 0' {
        $dir = New-HooksCopy -Without 'tool-classes.json'
        [IO.File]::WriteAllText((Join-Path $dir 'tool-classes.json'), '{ "allow": [ "get_station_info" ', $Utf8NoBom)
        $result = Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')) -PluginRoot (Join-Path $RepoRoot 'plugin') -ScriptPath (Join-Path $dir 'allow-readonly.ps1')
        Assert-NoDecision $result
    }

    Invoke-Case 'allow hook: a missing hook-common.ps1 prints nothing and exits 0' {
        $dir = New-HooksCopy -Without 'hook-common.ps1'
        $result = Invoke-AllowHookProcess (ConvertTo-HookInput ($IdentityPrefix + 'get_station_info')) -PluginRoot (Join-Path $RepoRoot 'plugin') -ScriptPath (Join-Path $dir 'allow-readonly.ps1')
        Assert-NoDecision $result
    }

    Invoke-Case 'allow hook: an input over the 1 MB cap prints nothing and exits 0' {
        $huge = '{"tool_name": "' + $ExpectedToolPrefix + 'get_station_info", "tool_input": {"pad": "' + ('x' * 1100000) + '"}}'
        Assert-NoDecision (Invoke-AllowHookProcess $huge)
    }

    Invoke-Case 'allow hook: an input just under the cap is still read' {
        $filler = 'x' * 900000
        Assert-AllowDecision (Invoke-AllowHookProcess ('{"tool_name": "' + $ExpectedToolPrefix + 'get_station_info", "tool_input": {"pad": "' + $filler + '"}}'))
    }

    Invoke-Case 'allow hook: the decision matches the classification for every registered tool (in-process, whole list)' {
        . $AllowHookPath    # dot-sourcing loads the functions only; the case body is its own scope
        $classes = Read-ToolClasses -Path $ToolClassesPath
        $policy = Read-AllowPolicy $ToolClassesPath
        Assert-Equal @($classes.allow).Count @($policy.Allow).Count 'allow list the hook reads'
        $wrongAllow = @($classes.allow | Where-Object { -not (Test-ToolAllowed ($IdentityPrefix + $_) $policy.Allow) })
        $wrongPrompt = @($classes.prompt | Where-Object { Test-ToolAllowed ($IdentityPrefix + $_) $policy.Allow })
        Assert-Equal '' ($wrongAllow -join ',') 'allow-listed tools that were not allowed'
        Assert-Equal '' ($wrongPrompt -join ',') 'prompt-listed tools that were allowed'
    }

    Invoke-Case 'allow hook: a call to every allow-listed tool with NO arguments is allowed (nothing is opened), one with a path outside is not' {
        . $AllowHookPath
        $classes = Read-ToolClasses -Path $ToolClassesPath
        $refused = @($classes.allow | Where-Object { -not (Test-CallAllowed $_ @{}) })
        Assert-Equal '' ($refused -join ',') 'allow-listed tools refused without arguments'
        $leaked = @(foreach ($tool in $classes.pathArguments.PSObject.Properties.Name) {
            $arguments = @{}
            foreach ($name in $classes.pathArguments.$tool) { $arguments[$name] = 'C:\Elsewhere\a.seq' }
            if (Test-CallAllowed $tool $arguments) { $tool }
        })
        Assert-Equal '' ($leaked -join ',') 'path-taking tools allowed for a path outside the project'
    }

    Invoke-Case 'allow hook: the short name is what follows the plugin prefix, and only for this plugin and server' {
        . $AllowHookPath
        Assert-Equal 'get_steps' (Get-ShortToolName ($ExpectedToolPrefix + 'get_steps')) 'plugin tool'
        Assert-Equal $null (Get-ShortToolName 'mcp__teststand__get_steps') 'direct tool'
        Assert-Equal $null (Get-ShortToolName 'mcp__plugin_teststand-mcp_other__get_steps') 'other server'
        Assert-Equal $null (Get-ShortToolName 42) 'non-string'
        Assert-Equal $null (Get-ShortToolName $null) 'null'
    }

    # --- the path rule ---------------------------------------------------------------------------------
    Invoke-Case 'path rule: a .seq inside the project folder is allowed (process level, from the hook input text)' {
        $hookInput = ConvertTo-HookInput ($IdentityPrefix + 'get_steps') @{ sequence_file_path = 'C:\Work\Proj\tests\a.seq' } $ProjectDir
        Assert-AllowDecision (Invoke-AllowHookProcess $hookInput)
    }

    Invoke-Case 'path rule: the same call with a path outside the project prints nothing (process level)' {
        $hookInput = ConvertTo-HookInput ($IdentityPrefix + 'get_steps') @{ sequence_file_path = 'C:\Elsewhere\a.seq' } $ProjectDir
        Assert-NoDecision (Invoke-AllowHookProcess $hookInput)
    }

    $PathRows = @(
        @{ Name = 'an inside-project .seq';                       Path = 'C:\Work\Proj\a.seq';                 Allowed = $true }
        @{ Name = 'a nested folder';                              Path = 'C:\Work\Proj\sub\deeper\a.seq';      Allowed = $true }
        @{ Name = 'an upper-case extension';                      Path = 'C:\Work\Proj\A.SEQ';                 Allowed = $true }
        @{ Name = 'forward slashes';                              Path = 'C:/Work/Proj/sub/a.seq';             Allowed = $true }
        @{ Name = 'a different case of the folder and drive';     Path = 'c:\work\PROJ\a.seq';                 Allowed = $true }
        @{ Name = 'a .. that stays inside';                       Path = 'C:\Work\Proj\sub\..\a.seq';          Allowed = $true }
        @{ Name = 'a UNC path';                                   Path = '\\server\share\Work\Proj\a.seq';     Allowed = $false }
        @{ Name = 'a UNC path that names the project drive';      Path = '\\localhost\c$\Work\Proj\a.seq';     Allowed = $false }
        @{ Name = 'a \\?\ path';                                  Path = '\\?\C:\Work\Proj\a.seq';             Allowed = $false }
        @{ Name = 'a \\.\ path';                                  Path = '\\.\C:\Work\Proj\a.seq';             Allowed = $false }
        @{ Name = 'a relative path';                              Path = 'a.seq';                              Allowed = $false }
        @{ Name = 'a relative path with ..';                      Path = '..\Proj\a.seq';                      Allowed = $false }
        @{ Name = 'a drive-relative path';                        Path = 'C:a.seq';                            Allowed = $false }
        @{ Name = 'a rooted path without a drive';                Path = '\Work\Proj\a.seq';                   Allowed = $false }
        @{ Name = 'a path outside the project';                   Path = 'C:\Elsewhere\a.seq';                 Allowed = $false }
        @{ Name = 'another drive with the same folder names';     Path = 'D:\Work\Proj\a.seq';                 Allowed = $false }
        @{ Name = 'a sibling folder that shares the prefix';      Path = 'C:\Work\Proj2\a.seq';                Allowed = $false }
        @{ Name = 'a .. that escapes the project';                Path = 'C:\Work\Proj\..\Other\a.seq';        Allowed = $false }
        @{ Name = 'a .. that escapes by several levels';          Path = 'C:\Work\Proj\sub\..\..\..\a.seq';    Allowed = $false }
        @{ Name = 'a .txt file';                                  Path = 'C:\Work\Proj\a.txt';                 Allowed = $false }
        @{ Name = 'a file without an extension';                  Path = 'C:\Work\Proj\a';                     Allowed = $false }
        @{ Name = 'a double extension that ends in .txt';         Path = 'C:\Work\Proj\a.seq.txt';             Allowed = $false }
        @{ Name = 'an NTFS stream on a .seq';                     Path = 'C:\Work\Proj\a.seq:hidden';          Allowed = $false }
        @{ Name = 'a wildcard';                                   Path = 'C:\Work\Proj\*.seq';                 Allowed = $false }
        @{ Name = 'a question-mark wildcard';                     Path = 'C:\Work\Proj\a?.seq';                Allowed = $false }
        @{ Name = 'an illegal character';                         Path = 'C:\Work\Proj\a<b>.seq';              Allowed = $false }
        @{ Name = 'an empty string';                              Path = '';                                   Allowed = $false }
        @{ Name = 'whitespace only';                              Path = '   ';                                Allowed = $false }
        @{ Name = 'the project folder itself';                    Path = 'C:\Work\Proj';                       Allowed = $false }
    )
    foreach ($row in $PathRows) {
        Invoke-Case "path rule: $($row.Name) -> $(if ($row.Allowed) { 'allow' } else { 'prompt' })" {
            . $AllowHookPath
            Assert-Equal $row.Allowed (Test-CallAllowed 'get_steps' @{ sequence_file_path = $row.Path }) "decision for [$($row.Path)]"
        }
    }

    $CwdRows = @(
        @{ Name = 'a cwd with a trailing separator';  Cwd = 'C:\Work\Proj\';           Allowed = $true }
        @{ Name = 'a cwd with forward slashes';       Cwd = 'C:/Work/Proj';            Allowed = $true }
        @{ Name = 'a cwd with a different case';      Cwd = 'c:\WORK\proj';            Allowed = $true }
        @{ Name = 'a cwd with a .. that resolves';    Cwd = 'C:\Work\Other\..\Proj';   Allowed = $true }
        @{ Name = 'a missing cwd';                    Cwd = $null;                     Allowed = $false }
        @{ Name = 'an empty cwd';                     Cwd = '';                        Allowed = $false }
        @{ Name = 'a relative cwd';                   Cwd = 'Work\Proj';               Allowed = $false }
        @{ Name = 'a UNC cwd';                        Cwd = '\\server\share\Proj';     Allowed = $false }
        @{ Name = 'a drive-root cwd (no project)';    Cwd = 'C:\';                     Allowed = $false }
        @{ Name = 'a cwd that is a sibling';          Cwd = 'C:\Work\Proj2';           Allowed = $false }
        @{ Name = 'a cwd below the file';             Cwd = 'C:\Work\Proj\sub\deeper'; Allowed = $false }
    )
    foreach ($row in $CwdRows) {
        Invoke-Case "path rule: $($row.Name) -> $(if ($row.Allowed) { 'allow' } else { 'prompt' })" {
            . $AllowHookPath
            Assert-Equal $row.Allowed (Test-CallAllowed 'get_steps' @{ sequence_file_path = 'C:\Work\Proj\a.seq' } -Cwd $row.Cwd) "decision for cwd [$($row.Cwd)]"
        }
    }

    Invoke-Case 'path rule: a numeric cwd prompts' {
        . $AllowHookPath
        $policy = Read-AllowPolicy $ToolClassesPath
        $request = '{"tool_name":"' + $ExpectedToolPrefix + 'get_steps","tool_input":{"sequence_file_path":"C:\\Work\\Proj\\a.seq"},"cwd":42}'
        Assert-Equal $false (Test-ToolCallAllowed (Get-HookRequest $request) $policy) 'numeric cwd'
    }

    Invoke-Case 'path rule: a present value that is not a string prompts (number, boolean, null, array, object)' {
        . $AllowHookPath
        foreach ($json in '42', 'true', 'null', '["C:\\Work\\Proj\\a.seq"]', '{"a":1}') {
            $policy = Read-AllowPolicy $ToolClassesPath
            $request = '{"tool_name":"' + $ExpectedToolPrefix + 'get_steps","tool_input":{"sequence_file_path":' + $json + '},"cwd":"C:\\Work\\Proj"}'
            Assert-Equal $false (Test-ToolCallAllowed (Get-HookRequest $request) $policy) "value $json"
        }
    }

    Invoke-Case 'path rule: an absent optional path argument does not block (get_property_tree without file_path)' {
        . $AllowHookPath
        Assert-Equal $true (Test-CallAllowed 'get_property_tree' @{ root = 'StationGlobals' }) 'no file_path'
    }

    Invoke-Case 'path rule: a missing tool_input does not block a path-taking tool (nothing would be opened)' {
        . $AllowHookPath
        $policy = Read-AllowPolicy $ToolClassesPath
        $request = '{"tool_name":"' + $ExpectedToolPrefix + 'get_steps"}'
        Assert-Equal $true (Test-ToolCallAllowed (Get-HookRequest $request) $policy) 'no tool_input'
    }

    Invoke-Case 'path rule: EVERY path argument must pass - reference_file_path outside the project prompts' {
        . $AllowHookPath
        Assert-Equal $true (Test-CallAllowed 'audit_type_consistency' @{ file_path = 'C:\Work\Proj\a.seq'; reference_file_path = 'C:\Work\Proj\ref\b.seq' }) 'both inside'
        Assert-Equal $false (Test-CallAllowed 'audit_type_consistency' @{ file_path = 'C:\Work\Proj\a.seq'; reference_file_path = 'C:\Elsewhere\b.seq' }) 'reference outside'
        Assert-Equal $false (Test-CallAllowed 'audit_type_consistency' @{ file_path = 'C:\Elsewhere\a.seq'; reference_file_path = 'C:\Work\Proj\b.seq' }) 'file outside'
        Assert-Equal $true (Test-CallAllowed 'audit_type_consistency' @{ file_path = 'C:\Work\Proj\a.seq' }) 'optional reference absent'
    }

    Invoke-Case 'path rule: an argument that is not declared as a path (a macro string, a sequence name) is not checked' {
        . $AllowHookPath
        Assert-Equal $true (Test-CallAllowed 'expand_path_macros' @{ path = '<TestStand>\Components' }) 'expand_path_macros'
        Assert-Equal $true (Test-CallAllowed 'get_steps' @{ sequence_file_path = 'C:\Work\Proj\a.seq'; sequence_name = 'C:\Elsewhere\x' }) 'sequence_name'
    }

    Invoke-Case 'path rule: a tool the policy does not list is refused before any path is looked at' {
        . $AllowHookPath
        Assert-Equal $false (Test-CallAllowed 'insert_step' @{ file_path = 'C:\Work\Proj\a.seq' }) 'insert_step'
    }
}
finally {
    Remove-Module ToolClassification -ErrorAction SilentlyContinue
    Remove-TestTempDirs
}

Complete-TestRun
