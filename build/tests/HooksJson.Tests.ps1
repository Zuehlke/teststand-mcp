# ============================================================================
#  HooksJson.Tests.ps1 - plain PowerShell asserts for the committed plugin/hooks/hooks.json and
#  build/New-HooksJson.ps1 that generates it: the PreToolUse matcher lists exactly the allow-listed
#  tools, every hook is an exec-form powershell.exe command, and each command starts and behaves
#  from a plugin folder whose path has a space and a non-ASCII letter.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\HooksJson.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  This file is ASCII only on purpose (5.1 reads a BOM-less script as ANSI): the non-ASCII letter of the
#  awkward path is built from its code point.
# ============================================================================
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'HooksJsonTests_'
. (Join-Path $PSScriptRoot 'HookTestSupport.ps1')

$HooksJsonPath = Join-Path $HooksDir 'hooks.json'
$NewHooksJsonPath = Join-Path $RepoRoot 'build\New-HooksJson.ps1'
$PluginRootToken = '${CLAUDE_PLUGIN_ROOT}'
$ExactMatcherPattern = '\A[A-Za-z0-9_|-]+\z'   # the "list of exact strings" character set of the hooks docs
$ExpectedArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File')
$HookTimeoutSeconds = 10
$AllowReason = 'no TestStand file is modified'
$ProjectDir = 'C:\Work\Proj'
$FixtureRegistryText = 'Register("get_a", d, s => s.AddRequired("sequence_file_path", "string", "x")); Register("get_b", d, s => { }); Register("set_c", d, s => { });'

function Read-HooksJson { return ([IO.File]::ReadAllText($HooksJsonPath, $Utf8NoBom) | ConvertFrom-Json) }

# Runs New-HooksJson.ps1 in a child host; returns exit code and text.
function Invoke-NewHooksJson {
    param([string[]]$ScriptArguments = @())
    return Invoke-HookProcess -FilePath $HostPath -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $NewHooksJsonPath) + $ScriptArguments)
}

# The hook entry (type command) of a script, from the parsed hooks.json.
function Get-HookEntry {
    param($Config, [string]$ScriptName)
    $entries = @($Config.hooks.PreToolUse.hooks) + @($Config.hooks.SessionStart.hooks)
    return ($entries | Where-Object { $_.args[-1] -like "*/hooks/$ScriptName" } | Select-Object -First 1)
}

# Spawns a hook entry the way Claude Code does for an exec-form hook: the executable, then each args
# element as ONE argument (quoted here only because Windows needs one command-line string), with the
# placeholder substituted into every element.
function Invoke-HookEntry {
    param($Entry, [string]$PluginRoot, [string]$StdinText, [hashtable]$EnvOverrides = @{})
    $arguments = @(@($Entry.args) | ForEach-Object { ([string]$_).Replace($PluginRootToken, $PluginRoot) })
    $envMap = @{ CLAUDE_PLUGIN_ROOT = $PluginRoot }
    foreach ($key in $EnvOverrides.Keys) { $envMap[$key] = $EnvOverrides[$key] }
    return Invoke-HookProcess -FilePath ([string]$Entry.command).Replace($PluginRootToken, $PluginRoot) -ArgumentList $arguments -StdinText $StdinText -EnvOverrides $envMap
}

# A copy of plugin/ under <guid> T<e-acute>st\plugin root, with a plugin.json that names the stub release server.
$AwkwardSuffix = ' T' + [char]0x00E9 + 'st'
function New-StagedPlugin {
    param([string]$Version = '1.9.0', [string]$Repository = 'https://github.com/Zuehlke/teststand-mcp')
    $staging = New-TempDir $AwkwardSuffix
    $pluginRoot = Join-Path $staging 'plugin root'
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'plugin') -Destination $pluginRoot -Recurse
    $json = [ordered]@{ name = $PluginName; version = $Version; repository = $Repository } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $pluginRoot '.claude-plugin\plugin.json'), $json, $Utf8NoBom)
    return $pluginRoot
}


try {
    Import-Module (Join-Path $RepoRoot 'build\ToolClassification.psm1') -Force   # Read-ToolClasses

    Invoke-Case 'hooks.json: it has a description and exactly the PreToolUse and SessionStart events' {
        $config = Read-HooksJson
        Assert-True (-not [string]::IsNullOrWhiteSpace($config.description)) 'a top-level description'
        Assert-AsciiOnly $config.description 'description'
        Assert-Equal 'PreToolUse,SessionStart' (@($config.hooks.PSObject.Properties | ForEach-Object { $_.Name }) -join ',') 'events'
    }

    Invoke-Case 'hooks.json: the PreToolUse matcher is exactly the allow-listed names, each with the plugin tool prefix' {
        $groups = @((Read-HooksJson).hooks.PreToolUse)
        Assert-Equal 1 $groups.Count 'PreToolUse groups'
        $matcher = [string]$groups[0].matcher
        Assert-True ($matcher -cmatch $ExactMatcherPattern) 'the matcher uses only exact-string characters, so it is no regular expression'
        $allow = @((Read-ToolClasses -Path $ToolClassesPath).allow)
        Assert-True ($allow.Count -gt 0) 'a non-empty allow list'
        Assert-Equal (($allow | ForEach-Object { $IdentityPrefix + $_ }) -join '|') $matcher 'matcher'
        Assert-Equal $allow.Count $matcher.Split('|').Count 'one name per allow-listed tool'
    }

    Invoke-Case 'hooks.json: no prompting tool is in the PreToolUse matcher, and every name carries the prefix' {
        $names = ([string](Read-HooksJson).hooks.PreToolUse[0].matcher).Split('|')
        $classes = Read-ToolClasses -Path $ToolClassesPath
        Assert-Equal '' (@($classes.prompt | Where-Object { $names -ccontains ($IdentityPrefix + $_) }) -join ',') 'prompting tools that would start the hook'
        Assert-Equal 0 @($names | Where-Object { -not $_.StartsWith($IdentityPrefix, [StringComparison]::Ordinal) }).Count 'names without the prefix'
    }

    Invoke-Case 'hooks.json: every hook is an exec-form powershell.exe command with the script path as ONE argument, no shell and a 10 second timeout' {
        $config = Read-HooksJson
        $pluginRoot = Join-Path $RepoRoot 'plugin'
        $entries = @($config.hooks.PreToolUse.hooks) + @($config.hooks.SessionStart.hooks)
        Assert-Equal 3 $entries.Count 'hook entries (allow, guard, notify)'
        foreach ($entry in $entries) {
            Assert-Equal 'command' $entry.type 'type'
            Assert-Equal 'powershell.exe' $entry.command 'command'
            Assert-Equal $null $entry.PSObject.Properties['shell'] 'no shell field (it is ignored with args)'
            Assert-Equal $null $entry.PSObject.Properties['onFailure'] 'no onFailure (needs Claude Code 2.1.295)'
            $arguments = @($entry.args)
            Assert-Equal ($ExpectedArguments -join ' ') (($arguments | Select-Object -First $ExpectedArguments.Count) -join ' ') 'arguments before the script'
            Assert-Equal ($ExpectedArguments.Count + 1) $arguments.Count 'one script argument'
            Assert-True ($arguments[-1] -cmatch '\A\$\{CLAUDE_PLUGIN_ROOT\}/hooks/[A-Za-z0-9._-]+\.ps1\z') "script argument [$($arguments[-1])]"
            Assert-True (-not ($arguments[-1] -match '"')) 'the path is not quoted'
            Assert-Equal $HookTimeoutSeconds $entry.timeout 'timeout (seconds)'
            $scriptPath = $arguments[-1].Replace($PluginRootToken, $pluginRoot).Replace('/', '\')
            Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) "script missing: $scriptPath"
        }
        Assert-Equal 'allow-readonly.ps1' (Split-Path -Leaf $config.hooks.PreToolUse[0].hooks[0].args[-1]) 'PreToolUse script'
    }

    Invoke-Case 'hooks.json: the SessionStart group matches startup only and runs the guard, then the update notice' {
        $groups = @((Read-HooksJson).hooks.SessionStart)
        Assert-Equal 1 $groups.Count 'SessionStart groups'
        Assert-Equal 'startup' $groups[0].matcher 'matcher'
        Assert-Equal 'platform-guard.ps1,update-notify.ps1' ((@($groups[0].hooks) | ForEach-Object { Split-Path -Leaf $_.args[-1] }) -join ',') 'scripts'
    }

    Invoke-Case 'hooks.json: the POSIX guard is gone and nothing in hooks.json refers to sh or bash' {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $HooksDir 'platform-guard.sh'))) 'platform-guard.sh must not exist'
        $text = [IO.File]::ReadAllText($HooksJsonPath, $Utf8NoBom)
        Assert-NotContains $text '.sh' 'hooks.json'
        Assert-NotContains $text 'bash' 'hooks.json'
        Assert-NotContains $text '"shell"' 'hooks.json'
    }

    # --- the generator ----------------------------------------------------------------------------------
    Invoke-Case 'generator: New-HooksJson.ps1 reproduces the committed hooks.json byte for byte (line endings aside)' {
        $out = Join-Path (New-TempDir) 'hooks.json'
        $result = Invoke-NewHooksJson @('-OutputPath', $out)
        Assert-Equal 0 $result.ExitCode "exit code (output: $($result.Stdout) $($result.Stderr))"
        $generated = [IO.File]::ReadAllBytes($out)
        Assert-True (-not ($generated.Length -ge 3 -and $generated[0] -eq 0xEF -and $generated[1] -eq 0xBB -and $generated[2] -eq 0xBF)) 'no BOM'
        Assert-True ($generated -notcontains 13) 'LF line endings only'
        Assert-Equal 10 $generated[$generated.Length - 1] 'a final newline'
        Assert-Equal 125 $generated[$generated.Length - 2] 'the file ends with a closing brace and one newline'
        $committed = [IO.File]::ReadAllText($HooksJsonPath, $Utf8NoBom).Replace("`r`n", "`n")
        Assert-Equal $committed ($Utf8NoBom.GetString($generated)) 'generated text against committed text'
    }

    Invoke-Case 'generator: the matcher follows the allow-list of whatever classification it is given' {
        $dir = New-TempDir
        $registry = Join-Path $dir 'Registry.cs'
        [IO.File]::WriteAllText($registry, $FixtureRegistryText, $Utf8NoBom)
        $classes = Join-Path $dir 'classes.json'
        [IO.File]::WriteAllText($classes, '{"allow":["get_a","get_b"],"prompt":["set_c"],"pathArguments":{"get_a":["sequence_file_path"]}}', $Utf8NoBom)
        $out = Join-Path $dir 'hooks.json'
        $result = Invoke-NewHooksJson @('-OutputPath', $out, '-ClassesPath', $classes, '-RegistryPath', $registry)
        Assert-Equal 0 $result.ExitCode "exit code (output: $($result.Stdout))"
        $config = [IO.File]::ReadAllText($out, $Utf8NoBom) | ConvertFrom-Json
        Assert-Equal ($IdentityPrefix + 'get_a|' + $IdentityPrefix + 'get_b') $config.hooks.PreToolUse[0].matcher 'matcher'
    }

    $GeneratorFaults = @(
        @{ Name = 'a wildcard in the allow-list';              Classes = '{"allow":["get_*"],"prompt":["get_a","get_b","set_c"]}'; Names = @('get_*', 'E_ALLOW_WILDCARD') }
        @{ Name = 'a registered tool in neither list';         Classes = '{"allow":["get_a"],"prompt":["set_c"],"pathArguments":{"get_a":["sequence_file_path"]}}'; Names = @('get_b', 'E_UNCLASSIFIED') }
        @{ Name = 'an allow-list name that is not registered'; Classes = '{"allow":["get_a","get_b","get_ghost"],"prompt":["set_c"],"pathArguments":{"get_a":["sequence_file_path"]}}'; Names = @('get_ghost', 'E_ALLOW_NOT_REGISTERED') }
        @{ Name = 'a tool in both lists';                      Classes = '{"allow":["get_a","get_b"],"prompt":["get_b","set_c"],"pathArguments":{"get_a":["sequence_file_path"]}}'; Names = @('get_b', 'E_IN_BOTH_LISTS') }
        @{ Name = 'an undeclared path argument';               Classes = '{"allow":["get_a","get_b"],"prompt":["set_c"]}'; Names = @('get_a', 'sequence_file_path', 'E_PATH_ARGUMENT_UNDECLARED') }
        @{ Name = 'an empty allow-list';                       Classes = '{"allow":[],"prompt":["get_a","get_b","set_c"]}'; Names = @('allow list', 'empty') }
        @{ Name = 'a classification without an allow-list';    Classes = '{"prompt":["get_a","get_b","set_c"]}'; Names = @('allow list', 'empty') }
    )
    foreach ($fault in $GeneratorFaults) {
        Invoke-Case "generator: New-HooksJson.ps1 refuses $($fault.Name), names it, and writes nothing" {
            $dir = New-TempDir
            $registry = Join-Path $dir 'Registry.cs'
            [IO.File]::WriteAllText($registry, $FixtureRegistryText, $Utf8NoBom)
            $classes = Join-Path $dir 'classes.json'
            [IO.File]::WriteAllText($classes, $fault.Classes, $Utf8NoBom)
            $out = Join-Path $dir 'hooks.json'
            $result = Invoke-NewHooksJson @('-OutputPath', $out, '-ClassesPath', $classes, '-RegistryPath', $registry)
            Assert-Equal 1 $result.ExitCode 'exit code'
            foreach ($name in $fault.Names) { Assert-Contains $result.Stdout $name 'failure text' }
            foreach ($label in 'What:', 'Why:', 'Fix:') { Assert-Contains $result.Stdout $label 'three-part failure' }
            Assert-True (-not (Test-Path -LiteralPath $out)) 'no hooks.json is written for an invalid classification'
        }
    }

    Invoke-Case 'generator: New-HooksJson.ps1 fails with a three-part message for a missing classification file' {
        $out = Join-Path (New-TempDir) 'hooks.json'
        $result = Invoke-NewHooksJson @('-OutputPath', $out, '-ClassesPath', (Join-Path (New-TempDir) 'absent.json'))
        Assert-Equal 1 $result.ExitCode 'exit code'
        Assert-Contains $result.Stdout 'absent.json' 'names the file'
        Assert-Contains $result.Stdout 'Fix:' 'three-part failure'
    }

    Invoke-Case 'generator (pure): ConvertTo-StableJson writes a fixed layout - ordered keys, one member per line, escapes, empty containers' {
        . $NewHooksJsonPath
        $tree = [ordered]@{ b = 'a "quoted" \ back'; a = [object[]]@('x', 2, $true, $null); empty = [object[]]@(); none = [ordered]@{}; n = [ordered]@{ k = 1L } }
        $expected = "{`n  `"b`": `"a \`"quoted\`" \\ back`",`n  `"a`": [`n    `"x`",`n    2,`n    true,`n    null`n  ],`n  `"empty`": [],`n  `"none`": {},`n  `"n`": {`n    `"k`": 1`n  }`n}"
        Assert-Equal $expected (ConvertTo-StableJson $tree) 'layout'
        $thrown = $null
        try { ConvertTo-StableJson ([ordered]@{ d = 1.5 }) } catch { $thrown = $_.Exception.Message }
        Assert-True ($null -ne $thrown -and $thrown -like '*System.Double*') 'a type it cannot write is refused by name'
        Assert-Equal '"\u00e9"' (ConvertTo-JsonStringLiteral ([string][char]0x00E9)) 'a non-ASCII letter becomes an escape'
    }

    # --- the committed commands, run from a staging copy under a path with a space and a non-ASCII letter ---
    Invoke-ServerCase 'awkward path: the PreToolUse command allows a read-only call from a plugin folder with a space and a non-ASCII letter' {
        $pluginRoot = New-StagedPlugin
        Assert-True ($pluginRoot -match ' ') 'the staged path has a space'
        Assert-True ($pluginRoot -match [string][char]0x00E9) 'the staged path has a non-ASCII letter'
        $entry = Get-HookEntry (Read-HooksJson) 'allow-readonly.ps1'
        $inside = Invoke-HookEntry $entry $pluginRoot (ConvertTo-HookInput ($IdentityPrefix + 'get_steps') @{ sequence_file_path = "$ProjectDir\a.seq" } $ProjectDir)
        Assert-Equal 0 $inside.ExitCode "exit code (stderr: $($inside.Stderr))"
        $decision = $inside.Stdout | ConvertFrom-Json
        Assert-Equal 'allow' $decision.hookSpecificOutput.permissionDecision 'permissionDecision'
        Assert-Equal $AllowReason $decision.hookSpecificOutput.permissionDecisionReason 'reason'
        $outside = Invoke-HookEntry $entry $pluginRoot (ConvertTo-HookInput ($IdentityPrefix + 'get_steps') @{ sequence_file_path = 'C:\Elsewhere\a.seq' } $ProjectDir)
        Assert-Equal '' $outside.Stdout 'a path outside the project prompts'
        $prompting = Invoke-HookEntry $entry $pluginRoot (ConvertTo-HookInput ($IdentityPrefix + 'insert_step'))
        Assert-Equal '' $prompting.Stdout 'a prompting tool'
        Assert-Equal 0 $prompting.ExitCode 'exit code'
    }

    Invoke-ServerCase 'awkward path: the platform guard command starts and adds the rules pointer' {
        $pluginRoot = New-StagedPlugin
        $fixture = New-GuardFixture
        $guardEnv = $fixture.Env.Clone()
        $guardEnv['PATH'] = $fixture.Env['PATH'] + ';' + (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0')   # the command is a bare powershell.exe
        $result = Invoke-HookEntry (Get-HookEntry (Read-HooksJson) 'platform-guard.ps1') $pluginRoot '{"source":"startup"}' $guardEnv
        Assert-Equal 0 $result.ExitCode "exit code (stderr: $($result.Stderr))"
        Assert-Equal '' $result.Stderr 'stderr'
        Assert-Contains $result.Stdout 'teststand-rules' 'stdout'
    }

    Invoke-ServerCase 'awkward path: the update-notify command announces a newer release' {
        $server = Start-StubReleaseServer -Mode redirect -Location 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v1.10.0'
        $pluginRoot = New-StagedPlugin -Repository $server.BaseUrl
        $state = Join-Path (Split-Path -Parent $pluginRoot) 'state'
        $envMap = @{ TESTSTAND_MCP_STATE_DIR = $state; TESTSTAND_MCP_NOW = $FixedNow.ToString('o'); TESTSTAND_MCP_NO_UPDATE_CHECK = $null; TESTSTAND_MCP_RELEASES_BASE_URL = $null }
        $result = Invoke-HookEntry (Get-HookEntry (Read-HooksJson) 'update-notify.ps1') $pluginRoot '{"source":"startup"}' $envMap
        Assert-Equal 0 $result.ExitCode "exit code (stderr: $($result.Stderr))"
        Assert-Equal '' $result.Stderr 'stderr'
        Assert-Contains $result.Stdout '1.10.0' 'stdout'
        Assert-AsciiOnly $result.Stdout 'stdout'
    }
}
finally {
    Stop-AllStubServers
    Remove-Module ToolClassification -ErrorAction SilentlyContinue
    Remove-Module PluginIdentity -ErrorAction SilentlyContinue
    Remove-TestTempDirs
}

Complete-TestRun
