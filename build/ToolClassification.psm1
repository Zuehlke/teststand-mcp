# ============================================================================
#  ToolClassification.psm1 - the check behind plugin/hooks/tool-classes.json:
#  every tool the server registers is on purpose either auto-allowed or prompted.
#    Get-RegisteredToolNames : the tool names, read from the Register("...") calls
#                              of src/Tools/TestStandToolRegistry.cs;
#    Get-RegisteredToolParameters : tool name -> parameter names, from the same calls;
#    Read-ToolClasses        : tool-classes.json as an object;
#    Get-ClassList           : one list of it (allow, prompt) as an array;
#    Get-ClassifiedToolNames : both lists together; Get-ToolSourcePath: where the two source files live;
#    Get-ToolClassificationViolation : the violations of one classification (empty = valid);
#    Assert-ToolClassification : the same check as a three-part build failure.
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')

$script:RegisterCallPattern = 'Register\("([^"]+)"'
$script:LiteralToolNamePattern = '\A[a-z0-9_]+\z'
$script:ParameterCallPattern = '\.Add(?:Required|Optional)\("([^"]+)"'
$script:RegistryMethodEndPattern = '\r?\n    \}'
$script:PathLikeParameterPattern = '(?i)path|file'

function Read-ToolClasses {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    return Read-JsonFile $Path 'The auto-allow hook and the classification check both read this file, so no tool can be classified without it.'
}

function Get-RegisteredToolNames {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RegistryPath)
    $text = Read-Utf8Text $RegistryPath
    $names = @([regex]::Matches($text, $script:RegisterCallPattern) | ForEach-Object { $_.Groups[1].Value })
    if ($names.Count -eq 0) {
        Write-BuildFailure -What "No Register(`"...`") call found in $RegistryPath" `
            -Why 'An empty tool list would make every classification look complete, so the check refuses it.' `
            -Fix 'Point -RegistryPath at the file that registers the MCP tools.'
    }
    return $names
}

# tool name -> the names of its parameters (every AddRequired / AddOptional of its registration,
# nested array-item properties included, so a nested path property is not overlooked).
function Get-RegisteredToolParameters {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RegistryPath)
    $text = Read-Utf8Text $RegistryPath
    $starts = @([regex]::Matches($text, $script:RegisterCallPattern))
    $parameters = @{}
    for ($index = 0; $index -lt $starts.Count; $index++) {
        $end = if ($index + 1 -lt $starts.Count) { $starts[$index + 1].Index } else { $text.Length }
        $block = $text.Substring($starts[$index].Index, $end - $starts[$index].Index)
        $methodEnd = [regex]::Match($block, $script:RegistryMethodEndPattern)
        if ($methodEnd.Success) { $block = $block.Substring(0, $methodEnd.Index) }
        $parameters[$starts[$index].Groups[1].Value] = @([regex]::Matches($block, $script:ParameterCallPattern) | ForEach-Object { $_.Groups[1].Value })
    }
    return $parameters
}

# ----------------------------------------------------------------------------
#  Where the classification and the tool registry live (relative to the repository root)
# ----------------------------------------------------------------------------
$script:ToolClassesRelative = 'plugin\hooks\tool-classes.json'
$script:ToolRegistryRelative = 'src\Tools\TestStandToolRegistry.cs'

function Get-ToolSourcePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    return [pscustomobject]@{
        Classes  = (Join-Path $RepoRoot $script:ToolClassesRelative)
        Registry = (Join-Path $RepoRoot $script:ToolRegistryRelative)
    }
}

# Every tool name the classification file puts in the allow or the prompt list.
function Get-ClassifiedToolNames {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ClassesPath)
    $classes = Read-ToolClasses -Path $ClassesPath
    return @(@(Get-ClassList $classes 'allow') + @(Get-ClassList $classes 'prompt'))
}

function New-Violation {
    param([string]$Tool, [string]$Rule, [string]$Detail)
    return [pscustomobject]@{ Tool = $Tool; Rule = $Rule; Message = "$Rule : $Tool - $Detail" }
}

function Get-ClassList {
    param($Classes, [string]$ListName)
    $list = Get-JsonProperty $Classes $ListName
    if ($null -eq $list) { return @() }
    return @($list)
}

function Test-CoverageRules {
    param([string[]]$Allow, [string[]]$Prompt, [string[]]$RegisteredTools)
    foreach ($tool in $RegisteredTools) {
        $isAllowed = $Allow -ccontains $tool
        $isPrompted = $Prompt -ccontains $tool
        if (-not $isAllowed -and -not $isPrompted) {
            New-Violation $tool 'E_UNCLASSIFIED' 'is registered but is in neither the allow list nor the prompt list'
        }
        if ($isAllowed -and $isPrompted) {
            New-Violation $tool 'E_IN_BOTH_LISTS' 'is in both the allow list and the prompt list'
        }
    }
}

# Literal names only: lower-case letters, digits and underscore. Anything else (a *, ?, [], (), |,
# a space) is a pattern or a typo, and a pattern in the allow list would approve tools nobody reviewed.
function Test-AllowEntryRules {
    param([string[]]$Allow, [string[]]$RegisteredTools, $NeverAllow)
    $neverNames = Get-PropertyNames $NeverAllow
    foreach ($tool in $Allow) {
        if ($tool -cnotmatch $script:LiteralToolNamePattern) {
            New-Violation $tool 'E_ALLOW_WILDCARD' 'is not a literal tool name (a wildcard or a character outside a-z, 0-9 and _)'
        } elseif ($RegisteredTools -cnotcontains $tool) {
            New-Violation $tool 'E_ALLOW_NOT_REGISTERED' 'is in the allow list but is not a registered tool'
        }
        if ($neverNames -ccontains $tool) {
            $why = Get-JsonProperty $NeverAllow $tool
            New-Violation $tool 'E_ALLOW_NEVER' "is a named exclusion and can never be auto-allowed ($why)"
        }
    }
}

function Test-DuplicateRules {
    param([string[]]$Names, [string]$ListName)
    $seen = @{}
    foreach ($name in $Names) {
        if ($seen.ContainsKey($name)) {
            New-Violation $name 'E_DUPLICATE' "appears more than once in the $ListName list"
        }
        $seen[$name] = $true
    }
}

# An audit entry records WHY a tool sits where it does; it must agree with the lists and say why.
function Test-AuditRules {
    param($Audit, [string[]]$Allow, [string[]]$Prompt)
    foreach ($tool in (Get-PropertyNames $Audit)) {
        $entry = Get-JsonProperty $Audit $tool
        $decision = Get-JsonProperty $entry 'decision'
        $reason = Get-JsonProperty $entry 'reason'
        $actualList = if ($Allow -ccontains $tool) { 'allow' } elseif ($Prompt -ccontains $tool) { 'prompt' } else { 'none' }
        if ($decision -cne $actualList) {
            New-Violation $tool 'E_AUDIT_MISMATCH' "is audited as '$decision' but is in the '$actualList' list"
        }
        if ([string]::IsNullOrWhiteSpace($reason)) {
            New-Violation $tool 'E_AUDIT_NO_REASON' 'has an audit entry without a reason'
        }
    }
}

# A tool that takes a file path loads that file, and loading runs its callbacks, so every path-like
# parameter (a name with "path" or "file") of an allow-listed tool must be declared: in pathArguments
# (the hook checks it is a .seq file inside the project) or in notFileArguments (with the reason it
# opens no file). A declared name that is no parameter of the tool would leave the real one unchecked.
function Test-PathArgumentRules {
    param([string[]]$Allow, $ToolParameters, $PathArguments, $NotFileArguments)
    foreach ($tool in $Allow) {
        if (-not $ToolParameters.ContainsKey($tool)) { continue }
        $parameters = @($ToolParameters[$tool])
        $fileArguments = @(Get-JsonProperty $PathArguments $tool)
        $notFileNames = Get-PropertyNames (Get-JsonProperty $NotFileArguments $tool)
        foreach ($name in $parameters) {
            if ($name -notmatch $script:PathLikeParameterPattern) { continue }
            if (($fileArguments -ccontains $name) -or ($notFileNames -ccontains $name)) { continue }
            New-Violation $tool 'E_PATH_ARGUMENT_UNDECLARED' "has the path-like parameter '$name' that is declared neither in pathArguments nor in notFileArguments"
        }
        foreach ($name in @($fileArguments + $notFileNames | Where-Object { $_ })) {
            if ($parameters -cnotcontains $name) {
                New-Violation $tool 'E_PATH_ARGUMENT_UNKNOWN' "declares the path argument '$name', which is no parameter of the tool"
            }
        }
    }
}

# $ToolParameters (from Get-RegisteredToolParameters) switches the path rules on.
function Get-ToolClassificationViolation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Classes,
        [Parameter(Mandatory)][string[]]$RegisteredTools,
        $ToolParameters = $null
    )
    $allow = @(Get-ClassList $Classes 'allow')
    $prompt = @(Get-ClassList $Classes 'prompt')
    $violations = @()
    $violations += @(Test-CoverageRules $allow $prompt $RegisteredTools)
    $violations += @(Test-AllowEntryRules $allow $RegisteredTools (Get-JsonProperty $Classes 'neverAllow'))
    $violations += @(Test-DuplicateRules $allow 'allow')
    $violations += @(Test-DuplicateRules $prompt 'prompt')
    $violations += @(Test-AuditRules (Get-JsonProperty $Classes 'audit') $allow $prompt)
    if ($null -ne $ToolParameters) {
        $violations += @(Test-PathArgumentRules $allow $ToolParameters (Get-JsonProperty $Classes 'pathArguments') (Get-JsonProperty $Classes 'notFileArguments'))
    }
    return $violations
}

# Fails (What / Why / Fix) naming every violation; returns nothing when the classification is valid.
function Assert-ToolClassification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Classes,
        [Parameter(Mandatory)][string[]]$RegisteredTools,
        [Parameter(Mandatory)][string]$ClassesPath,
        $ToolParameters = $null
    )
    $violations = @(Get-ToolClassificationViolation -Classes $Classes -RegisteredTools $RegisteredTools -ToolParameters $ToolParameters)
    if ($violations.Count -eq 0) { return }
    $lines = ($violations | ForEach-Object { $_.Message }) -join "`n"
    Write-BuildFailure -What "$ClassesPath breaks the tool classification rules:`n$lines" `
        -Why 'An unclassified or malformed entry would auto-allow a tool nobody reviewed, or leave one without a decision, so no hooks.json is written.' `
        -Fix 'Put every registered tool in exactly one of the allow and prompt lists (literal names only), keep the named exclusions out of allow, and declare every path argument of an allow-listed tool.'
}

Export-ModuleMember -Function Read-ToolClasses, Get-ClassList, Get-ClassifiedToolNames, Get-ToolSourcePath, Get-RegisteredToolNames, Get-RegisteredToolParameters, Get-ToolClassificationViolation, Assert-ToolClassification
