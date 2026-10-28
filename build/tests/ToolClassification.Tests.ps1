# ============================================================================
#  ToolClassification.Tests.ps1 - plain PowerShell asserts for build/ToolClassification.psm1 and the
#  committed plugin/hooks/tool-classes.json: every registered tool is on purpose either auto-allowed or
#  prompted, and every path argument of an allow-listed tool is declared.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ToolClassification.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  This file is ASCII only on purpose (5.1 reads a BOM-less script as ANSI).
# ============================================================================
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ToolClassificationTests_'
. (Join-Path $PSScriptRoot 'HookTestSupport.ps1')

$ClassificationModulePath = Join-Path $RepoRoot 'build\ToolClassification.psm1'
$RegistryPath = Join-Path $RepoRoot 'src\Tools\TestStandToolRegistry.cs'
$NamedExclusions = @('evaluate_expression', 'evaluate_in_thread_context', 'diff_sequence_files', 'compare_sequence_files', 'find_file', 'analyze_sequence_file', 'run_sequence_analyzer')

# Fixture classification over a tiny registry: tools get_a, get_b, set_c; every fault is a copy with ONE defect.
$FixtureTools = @('get_a', 'get_b', 'set_c')
function New-FixtureClasses {
    param([string[]]$Allow = @('get_a', 'get_b'), [string[]]$Prompt = @('set_c'), $NeverAllow = $null, $Audit = $null,
          $PathArguments = $null, $NotFileArguments = $null)
    $classes = [ordered]@{ allow = $Allow; prompt = $Prompt }
    if ($null -ne $NeverAllow) { $classes['neverAllow'] = $NeverAllow }
    if ($null -ne $Audit) { $classes['audit'] = $Audit }
    if ($null -ne $PathArguments) { $classes['pathArguments'] = $PathArguments }
    if ($null -ne $NotFileArguments) { $classes['notFileArguments'] = $NotFileArguments }
    return ([pscustomobject]$classes)
}

function Assert-OneViolation {
    param($Classes, [string]$Rule, [string]$Tool, [string[]]$Registered = $FixtureTools, $ToolParameters = $null)
    $violations = @(Get-ToolClassificationViolation -Classes $Classes -RegisteredTools $Registered -ToolParameters $ToolParameters)
    Assert-Equal 1 $violations.Count ("expected exactly one violation but got: " + (($violations | ForEach-Object { $_.Message }) -join ' | '))
    Assert-Equal $Rule $violations[0].Rule 'rule'
    Assert-Equal $Tool $violations[0].Tool 'tool'
    Assert-Contains $violations[0].Message $Tool 'message names the tool'
    Assert-Contains $violations[0].Message $Rule 'message names the rule'
}

try {
    Import-Module $ClassificationModulePath -Force

    Invoke-Case 'real classification: every registered tool is in exactly one list and every path argument is declared' {
        $registered = Get-RegisteredToolNames -RegistryPath $RegistryPath
        Assert-True ($registered.Count -gt 0) 'no registered tools were read from the registry'
        $classes = Read-ToolClasses -Path $ToolClassesPath
        $violations = @(Get-ToolClassificationViolation -Classes $classes -RegisteredTools $registered -ToolParameters (Get-RegisteredToolParameters -RegistryPath $RegistryPath))
        Assert-Equal 0 $violations.Count ("violations: " + (($violations | ForEach-Object { $_.Message }) -join ' | '))
    }

    Invoke-Case 'real classification: the audited decisions and the named exclusions are kept' {
        $classes = Read-ToolClasses -Path $ToolClassesPath
        Assert-True ($classes.allow -ccontains 'get_step_unique_id') 'get_step_unique_id is audited as read-only'
        Assert-True ($classes.prompt -ccontains 'read_csv_lines') 'read_csv_lines is an arbitrary file read, so it prompts'
        foreach ($name in $NamedExclusions) {
            Assert-True ($classes.prompt -ccontains $name) "$name must prompt"
            Assert-True ($null -ne $classes.neverAllow.PSObject.Properties[$name]) "$name must be a named never-allow exclusion"
        }
    }

    Invoke-Case 'real classification: both arrays are sorted (ordinal) without duplicates' {
        $classes = Read-ToolClasses -Path $ToolClassesPath
        foreach ($listName in 'allow', 'prompt') {
            $list = @($classes.$listName)
            $ordinal = [string[]]$list.Clone()
            [Array]::Sort($ordinal, [StringComparer]::Ordinal)
            Assert-Equal ($ordinal -join ',') ($list -join ',') "$listName order"
            Assert-Equal @($list | Sort-Object { $_ } -Unique).Count $list.Count "$listName duplicates"
        }
    }

    Invoke-Case 'real classification: a path-taking read tool names its path arguments (derived from the C# schemas)' {
        $classes = Read-ToolClasses -Path $ToolClassesPath
        Assert-Equal 'sequence_file_path' (@($classes.pathArguments.get_steps) -join ',') 'get_steps'
        Assert-Equal 'file_path,reference_file_path' (@($classes.pathArguments.audit_type_consistency) -join ',') 'audit_type_consistency'
        Assert-Equal $null $classes.pathArguments.PSObject.Properties['get_station_info'] 'a tool without a path parameter has no entry'
    }

    # --- the classification rules, one defect per fixture ------------------------------------------
    # --- one-entry and empty lists: a single item must stay a list (a bare one is a string, and '+' glues two strings) ---
    function Get-ClassifiedNames {
        param([string]$Json)
        $path = Join-Path (New-TempDir) 'tool-classes.json'
        Write-TextFile $path $Json
        return @(Get-ClassifiedToolNames -ClassesPath $path)
    }

    Invoke-Case 'classified names: ONE allowed and ONE prompted tool are two names, not one glued string' {
        $names = Get-ClassifiedNames '{"allow":["get_a"],"prompt":["set_c"]}'
        Assert-Equal 2 $names.Count 'the number of names'
        Assert-Equal 'get_a,set_c' ($names -join ',') 'names'
    }

    Invoke-Case 'classified names: a one-entry allow list with an EMPTY prompt list, and the other way round' {
        Assert-Equal 'get_a' ((Get-ClassifiedNames '{"allow":["get_a"],"prompt":[]}') -join ',') 'one allowed, none prompted'
        Assert-Equal 'set_c' ((Get-ClassifiedNames '{"allow":[],"prompt":["set_c"]}') -join ',') 'none allowed, one prompted'
        Assert-Equal 0 (Get-ClassifiedNames '{"allow":[],"prompt":[]}').Count 'both empty'
        Assert-Equal 'get_a' ((Get-ClassifiedNames '{"allow":["get_a"]}') -join ',') 'a missing prompt list'
    }

    Invoke-Case 'Get-ClassList: a one-entry list, an empty list and a missing list each give an array of the right length' {
        $classes = [pscustomobject]@{ allow = @('get_a'); prompt = @() }
        Assert-Equal 1 @(Get-ClassList $classes 'allow').Count 'one entry'
        Assert-Equal 0 @(Get-ClassList $classes 'prompt').Count 'empty list'
        Assert-Equal 0 @(Get-ClassList $classes 'neverThere').Count 'missing list'
    }

    Invoke-Case 'classification: a fixture with ONE allowed and ONE prompted tool is checked completely' {
        Assert-Equal 0 @(Get-ToolClassificationViolation -Classes (New-FixtureClasses -Allow @('get_a') -Prompt @('set_c')) -RegisteredTools @('get_a', 'set_c')).Count 'violations'
        Assert-OneViolation (New-FixtureClasses -Allow @('get_a') -Prompt @('get_a')) 'E_IN_BOTH_LISTS' 'get_a' @('get_a')
        Assert-OneViolation (New-FixtureClasses -Allow @('get_a') -Prompt @()) 'E_UNCLASSIFIED' 'set_c' @('get_a', 'set_c')
    }

    Invoke-Case 'classification: a well-formed fixture has no violations' {
        Assert-Equal 0 @(Get-ToolClassificationViolation -Classes (New-FixtureClasses) -RegisteredTools $FixtureTools).Count 'violations'
    }

    Invoke-Case 'classification fault: a registered tool in neither list is named' {
        Assert-OneViolation (New-FixtureClasses -Prompt @()) 'E_UNCLASSIFIED' 'set_c'
    }

    Invoke-Case 'classification fault: a tool in both lists is named' {
        Assert-OneViolation (New-FixtureClasses -Prompt @('set_c', 'get_b')) 'E_IN_BOTH_LISTS' 'get_b'
    }

    Invoke-Case 'classification fault: an allow-list name that is not a registered tool is named' {
        Assert-OneViolation (New-FixtureClasses -Allow @('get_a', 'get_b', 'get_ghost')) 'E_ALLOW_NOT_REGISTERED' 'get_ghost'
    }

    foreach ($wildcard in @('get_*', 'get_?', 'get_[ab]', 'get_(a|b)', 'get_a ')) {
        Invoke-Case "classification fault: allow entry [$wildcard] is refused as a wildcard" {
            $classes = New-FixtureClasses -Allow @('get_a', 'get_b', $wildcard)
            $violations = @(Get-ToolClassificationViolation -Classes $classes -RegisteredTools $FixtureTools | Where-Object { $_.Rule -eq 'E_ALLOW_WILDCARD' })
            Assert-Equal 1 $violations.Count 'wildcard violations'
            Assert-Equal $wildcard $violations[0].Tool 'tool'
        }
    }

    foreach ($excluded in $NamedExclusions) {
        Invoke-Case "classification fault: $excluded can never be allow-listed" {
            $registered = $FixtureTools + $excluded
            $classes = New-FixtureClasses -Allow @('get_a', 'get_b', $excluded) -NeverAllow ([pscustomobject]@{ $excluded = 'reason text' })
            $violations = @(Get-ToolClassificationViolation -Classes $classes -RegisteredTools $registered)
            Assert-Equal 1 $violations.Count ("violations: " + (($violations | ForEach-Object { $_.Message }) -join ' | '))
            Assert-Equal 'E_ALLOW_NEVER' $violations[0].Rule 'rule'
            Assert-Equal $excluded $violations[0].Tool 'tool'
            Assert-Contains $violations[0].Message 'reason text' 'message states the rule it breaks'
        }
    }

    Invoke-Case 'classification fault: the same name twice in one list is named' {
        Assert-OneViolation (New-FixtureClasses -Allow @('get_a', 'get_b', 'get_a')) 'E_DUPLICATE' 'get_a'
    }

    Invoke-Case 'classification fault: an audit decision that disagrees with the list is named' {
        $audit = [pscustomobject]@{ get_b = [pscustomobject]@{ decision = 'prompt'; reason = 'r' } }
        Assert-OneViolation (New-FixtureClasses -Audit $audit) 'E_AUDIT_MISMATCH' 'get_b'
    }

    Invoke-Case 'classification: the audit field is called decision; an entry that still says verdict is refused, and the real file has no verdict key' {
        $old = [pscustomobject]@{ get_b = [pscustomobject]@{ verdict = 'allow'; reason = 'r' } }
        Assert-OneViolation (New-FixtureClasses -Audit $old) 'E_AUDIT_MISMATCH' 'get_b'
        $real = Read-ToolClasses -Path $ToolClassesPath
        foreach ($tool in (Get-PropertyNames $real.audit)) {
            $entry = $real.audit.$tool
            Assert-True ($null -ne $entry.PSObject.Properties['decision']) "audit entry $tool has no decision"
            Assert-True ($null -eq $entry.PSObject.Properties['verdict']) "audit entry $tool still has the old verdict key"
        }
    }

    Invoke-Case 'classification: an audit entry without a reason is refused' {
        $audit = [pscustomobject]@{ get_b = [pscustomobject]@{ decision = 'allow'; reason = ' ' } }
        Assert-OneViolation (New-FixtureClasses -Audit $audit) 'E_AUDIT_NO_REASON' 'get_b'
    }

    Invoke-Case 'classification: an object with no lists classifies nothing (every tool is reported)' {
        Assert-Equal 3 @(Get-ToolClassificationViolation -Classes ([pscustomobject]@{}) -RegisteredTools $FixtureTools).Count 'violations'
    }

    Invoke-Case 'classification: a registry without any Register call is refused, not read as complete' {
        $empty = Join-Path (New-TempDir) 'Empty.cs'
        [IO.File]::WriteAllText($empty, 'class X { }', $Utf8NoBom)
        $thrown = $null
        try { Get-RegisteredToolNames -RegistryPath $empty } catch { $thrown = $_.Exception.Message }
        Assert-True ($null -ne $thrown) 'expected a refusal'
        Assert-Contains $thrown 'Empty.cs' 'names the file'
    }

    # --- the path-argument rules ------------------------------------------------------------------
    $FixtureParameters = @{ get_a = @('sequence_file_path', 'sequence_name'); get_b = @('execution_id'); set_c = @('file_path') }

    Invoke-Case 'path rules: a well-formed declaration has no violations (an undeclared path on a PROMPT tool is not checked)' {
        $classes = New-FixtureClasses -PathArguments ([pscustomobject]@{ get_a = @('sequence_file_path') })
        Assert-Equal 0 @(Get-ToolClassificationViolation -Classes $classes -RegisteredTools $FixtureTools -ToolParameters $FixtureParameters).Count 'violations'
    }

    Invoke-Case 'path rules fault: an allow-listed tool with an undeclared path-like parameter is named' {
        Assert-OneViolation (New-FixtureClasses) 'E_PATH_ARGUMENT_UNDECLARED' 'get_a' -ToolParameters $FixtureParameters
    }

    Invoke-Case 'path rules: a parameter named in notFileArguments counts as declared' {
        $classes = New-FixtureClasses -NotFileArguments ([pscustomobject]@{ get_a = [pscustomobject]@{ sequence_file_path = 'a macro string' } })
        Assert-Equal 0 @(Get-ToolClassificationViolation -Classes $classes -RegisteredTools $FixtureTools -ToolParameters $FixtureParameters).Count 'violations'
    }

    foreach ($odd in @('FilePath', 'target_PATH', 'my_file', 'sequencefile')) {
        Invoke-Case "path rules fault: the path-like name [$odd] is matched case-insensitively" {
            $parameters = @{ get_a = @($odd); get_b = @(); set_c = @() }
            Assert-OneViolation (New-FixtureClasses) 'E_PATH_ARGUMENT_UNDECLARED' 'get_a' -ToolParameters $parameters
        }
    }

    Invoke-Case 'path rules fault: a declared name that is no parameter of the tool is named (a typo would leave the real one unchecked)' {
        $classes = New-FixtureClasses -PathArguments ([pscustomobject]@{ get_a = @('sequence_file_path', 'sequence_file_pth') })
        Assert-OneViolation $classes 'E_PATH_ARGUMENT_UNKNOWN' 'get_a' -ToolParameters $FixtureParameters
    }

    Invoke-Case 'path rules: without the registered parameters the rules are skipped (the older call shape)' {
        Assert-Equal 0 @(Get-ToolClassificationViolation -Classes (New-FixtureClasses) -RegisteredTools $FixtureTools).Count 'violations'
    }

    Invoke-Case 'registry parameters: every AddRequired / AddOptional of a registration is listed, nested ones included, per tool' {
        $registry = Join-Path (New-TempDir) 'Registry.cs'
        $text = @'
    private void RegisterAll()
    {
        Register("get_a", "d", s => s.AddRequired("sequence_file_path", "string", "x").AddOptional("max", "integer", "y"), H);
        Register("plan", "d", s => s.AddRequired("name", "string", "x").AddArray("steps", "d", i => i.AddOptional("target_file", "string", "z")), H);
        Register("last", "d", s => { }, H);
    }

    private void Register(string name)
    {
        s.AddRequired("not_a_parameter_of_any_tool", "string", "x");
    }
'@
        [IO.File]::WriteAllText($registry, $text, $Utf8NoBom)
        $parameters = Get-RegisteredToolParameters -RegistryPath $registry
        Assert-Equal 'sequence_file_path,max' ($parameters['get_a'] -join ',') 'get_a'
        Assert-Equal 'name,target_file' ($parameters['plan'] -join ',') 'plan (the nested item property is included)'
        Assert-Equal 0 @($parameters['last']).Count 'last (the text after the method end is not read into it)'
    }

    Invoke-Case 'registry parameters: the real registry gives get_steps its path parameter' {
        $parameters = Get-RegisteredToolParameters -RegistryPath $RegistryPath
        Assert-True ($parameters['get_steps'] -ccontains 'sequence_file_path') 'get_steps.sequence_file_path'
        Assert-Equal (Get-RegisteredToolNames -RegistryPath $RegistryPath).Count $parameters.Count 'one entry per registered tool'
    }
}
finally {
    Remove-Module ToolClassification -ErrorAction SilentlyContinue
    Remove-Module PluginIdentity -ErrorAction SilentlyContinue
    Remove-TestTempDirs
}

Complete-TestRun
