# ============================================================================
#  SkillSources.psm1 - the rules of the teststand-rules skill and its import block, in ONE place:
#    - where the skill and the repository's rules file live;
#    - the markers CLAUDE.md wraps its @path imports in, and the shape of an import line;
#    - the YAML frontmatter of a skill or agent file;
#    - the import PLAN: the ordered, validated list of skill files the block imports.
#  Used by build/Test-SkillSources.ps1 (the source check), build/PluginIdentity.psm1 (agent
#  frontmatter and the skill's name) and build/PluginStaging.psm1 (bin/CLAUDE.md).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7. ASCII only: 5.1 reads a BOM-less script
#  as ANSI, so the BOM is built from its code point.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')

Set-Variable -Name SkillName -Value 'teststand-rules' -Option ReadOnly -Scope Script
Set-Variable -Name ImportsBeginMarker -Value '<!-- skill-imports:begin -->' -Option ReadOnly -Scope Script
Set-Variable -Name ImportsEndMarker -Value '<!-- skill-imports:end -->' -Option ReadOnly -Scope Script
Set-Variable -Name ImportLinePattern -Value '^@(\S+)\s*$' -Option ReadOnly -Scope Script

$script:RulesFileName = 'CLAUDE.md'
$script:SkillsRootRelative = 'plugin/skills'
$script:FencePattern = '^\s*```'
$script:ImportLikePattern = '(^|\s)@\S+'
$script:FrontmatterPattern = '(?s)\A' + [regex]::Escape([string][char]0xFEFF) + '?---\r?\n(.*?)\r?\n---[ \t]*(?:\r?\n|\z)'

# ----------------------------------------------------------------------------
#  Where things live
# ----------------------------------------------------------------------------
function Get-SkillSourcePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    $skillsRoot = Join-Path $RepoRoot $script:SkillsRootRelative
    return [pscustomobject]@{
        RulesFile  = Join-Path $RepoRoot $script:RulesFileName
        SkillsRoot = $skillsRoot
        SkillDir   = Join-Path $skillsRoot $SkillName
        SkillMd    = Join-Path (Join-Path $skillsRoot $SkillName) 'SKILL.md'
    }
}

# ----------------------------------------------------------------------------
#  Frontmatter
# ----------------------------------------------------------------------------
# The regex match of a leading YAML frontmatter block; Groups[1] is its content.
function Get-FrontmatterMatch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    return [regex]::Match($Text, $script:FrontmatterPattern)
}

# A file's text for inlining: BOM and frontmatter dropped, LF line ends, no trailing blank lines.
function Read-SkillFileText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $text = (Read-Utf8Text $Path).TrimStart([char]0xFEFF)
    $frontmatter = Get-FrontmatterMatch $text
    if ($frontmatter.Success) { $text = $text.Substring($frontmatter.Length) }
    return ($text -replace '\r\n', "`n").Trim("`n")
}

# ----------------------------------------------------------------------------
#  Imports
# ----------------------------------------------------------------------------
# The lines of a text that are @path imports, outside code fences: { Number (1-based); Path }.
function Get-ImportLine {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string[]]$Lines)
    $inFence = $false
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match $script:FencePattern) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $import = [regex]::Match($Lines[$index], $ImportLinePattern)
        if ($import.Success) { [pscustomobject]@{ Number = $index + 1; Path = $import.Groups[1].Value } }
    }
}

# The skill files the import block of the rules file names, in order, each checked:
# one begin and one end marker with lines between; every @ line inside is a whole-line import;
# every import resolves to a file below plugin/skills (a ".." cannot lead out of it).
function Get-SkillImportPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    $paths = Get-SkillSourcePath $RepoRoot
    $lines = @((Read-Utf8Text $paths.RulesFile).TrimStart([char]0xFEFF) -split '\r?\n')
    $begin = @(0..($lines.Count - 1) | Where-Object { $lines[$_].Trim() -ceq $ImportsBeginMarker })
    $end = @(0..($lines.Count - 1) | Where-Object { $lines[$_].Trim() -ceq $ImportsEndMarker })
    if ($begin.Count -ne 1 -or $end.Count -ne 1 -or $end[0] - $begin[0] -lt 2) {
        Write-BuildFailure -What "$($paths.RulesFile) does not hold exactly one '$ImportsBeginMarker' before exactly one '$ImportsEndMarker' with lines between them." `
            -Why 'The import block names the skill files bin/CLAUDE.md is generated from; without the markers the order and the set of rules files is a guess.' `
            -Fix "Restore the markers around the @plugin/skills/$SkillName/... lines of $script:RulesFileName (powershell -NoProfile -File build\Test-SkillSources.ps1 names the rule)."
    }
    $block = @($lines[($begin[0] + 1)..($end[0] - 1)])
    $wholeLine = @(Get-ImportLine $block)
    $stray = @($block | Where-Object { $_ -match $script:ImportLikePattern -and $_ -cnotmatch $ImportLinePattern })
    if ($stray.Count -gt 0) {
        Write-BuildFailure -What "$script:RulesFileName has a line inside the import block that mentions an @ path but is not a whole-line import: '$($stray[0].Trim())'" `
            -Why 'Only a line that is exactly "@path" is an import; anything else would be dropped or inlined wrongly, so the generated rules would silently differ from what Claude Code loads.' `
            -Fix 'Put each import on a line of its own, starting at column 0 with the @, and nothing after the path.'
    }
    if ($wholeLine.Count -eq 0) {
        Write-BuildFailure -What "$script:RulesFileName has no @ import between its skill-import markers." `
            -Why 'bin/CLAUDE.md would then carry no rules at all.' -Fix "Restore the @plugin/skills/$SkillName/... import lines."
    }
    $root = [IO.Path]::GetFullPath($paths.SkillsRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return @($wholeLine | ForEach-Object {
        $full = [IO.Path]::GetFullPath((Join-Path $RepoRoot $_.Path))
        if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            Write-BuildFailure -What "$script:RulesFileName imports @$($_.Path), which resolves to $full, outside $root" `
                -Why 'The rules shipped to users may only come from the reviewed skill folder; an import that leaves it could inline any file of the checkout into the archive.' `
                -Fix "Import only files below plugin/skills/$SkillName."
        }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            Write-BuildFailure -What "$script:RulesFileName imports @$($_.Path) but $full does not exist." `
                -Why 'The import cannot be inlined, so the generated rules would lose part of the skill.' `
                -Fix "Fix the import path in $script:RulesFileName or restore the file (git checkout -- $($_.Path))."
        }
        [pscustomobject]@{ ImportPath = $_.Path; FullPath = $full }
    })
}

Export-ModuleMember -Variable SkillName, ImportsBeginMarker, ImportsEndMarker, ImportLinePattern
Export-ModuleMember -Function Get-SkillSourcePath, Get-FrontmatterMatch, Read-SkillFileText, Get-ImportLine, Get-SkillImportPlan
