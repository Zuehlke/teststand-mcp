# ============================================================================
#  Test-SkillSources.ps1 - source check for the teststand-rules skill.
#  Four groups of checks, each printed as PASS/FAIL; exit code 1 on any FAIL:
#    A  every required rule is present in a references file, and SKILL.md's
#       first section holds exactly the never-do rules plus the pointers
#    B  the developer-only deny-list matches nothing in the skill files
#    C  no moved heading remains in CLAUDE.md or the other tracked markdown
#       (sentence-level duplication is NOT detected here: that is a review
#       responsibility)
#    D  CLAUDE.md wraps its skill imports in begin/end markers, every @path
#       import resolves, and every skill file is imported
#  Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Test-SkillSources.ps1
#  -RepoRoot / -SkillRoot let the tests point it at fixtures. -DefinitionsOnly
#  returns right after the tables below, so a test can dot-source the script and
#  derive its expectations from them.
#  Windows PowerShell 5.1 compatible. ASCII only on purpose: 5.1 reads a BOM-less
#  script as ANSI, so non-ASCII characters (the em dash in headings, the BOM) are
#  built from code points.
# ============================================================================
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$SkillRoot,
    [switch]$DefinitionsOnly
)

$EmDash = [string][char]0x2014
$Bom = [string][char]0xFEFF

# --- Group A: ONE rule table. A rule hits when ONE references file contains all of its
# patterns. NeverDoOrder (when set) marks the rules SKILL.md's first section must list,
# in that order; each listed item must match the same patterns. ---
$RuleTable = @(
    @{ Name = 'builder runs in the main thread'; NeverDoOrder = 1; Patterns = @('teststand-sequence-builder', 'AskUserQuestion', 'main thread') }
    @{ Name = 'NI_Flow_* mandate'; NeverDoOrder = 2; Patterns = @('NI_Flow_\*', 'Goto') }
    @{ Name = '.lvlibp clone-not-load'; NeverDoOrder = 3; Patterns = @('\.lvlibp', 'copy_step_module', 'never load') }
    @{ Name = 'diff plus audit_type_consistency'; NeverDoOrder = 4; Patterns = @('diff_sequence_files', 'audit_type_consistency') }
    @{ Name = 'export/import first'; Patterns = @('export_sequence_file', 'import_sequence_file') }
    @{ Name = 'Select/Case: one End per Case'; Patterns = @('NI_Flow_Select', 'own\s+`NI_Flow_End`') }
    @{ Name = 'SequenceCall as default step type'; Patterns = @('default to\s+`SequenceCall`') }
    @{ Name = 'headless limitations'; Patterns = @('Headless limitations', 'Paused', 'step_results') }
    @{ Name = 'doc generator asks the language first'; Patterns = @('teststand-doc-generator', 'ask the document language first') }
    @{ Name = 'presentation generator asks the language first'; Patterns = @('teststand-presentation-generator', 'ask the presentation language first') }
)
$NeverDoRules = @($RuleTable | Where-Object { $_.ContainsKey('NeverDoOrder') } | Sort-Object { $_.NeverDoOrder })
$ReferenceFiles = @('rebuild.md', 'sequence-design.md', 'agents.md', 'headless-and-tools.md', 'tool-contracts.md')
$SkillFileNames = @('SKILL.md') + @($ReferenceFiles | ForEach-Object { "references\$_" })

# --- Group B: developer-only deny-list (AC-16) ---
$DenyList = @(
    @{ Name = 'date'; Pattern = '\b20\d\d-\d\d-\d\d\b'; CaseSensitive = $true }
    @{ Name = 'test id'; Pattern = '\bT\d{2}\b'; CaseSensitive = $true }
    @{ Name = 'memory'; Pattern = 'memory'; CaseSensitive = $false }
    @{ Name = 'wiki link'; Pattern = '\[\['; CaseSensitive = $true }
    @{ Name = '.Demo_jcm'; Pattern = '\.Demo_jcm'; CaseSensitive = $false }
    @{ Name = 'this file'; Pattern = 'this file'; CaseSensitive = $false }
    @{ Name = 'Marshal'; Pattern = 'Marshal'; CaseSensitive = $false }
    @{ Name = 'dynamic'; Pattern = 'dynamic'; CaseSensitive = $false }
    @{ Name = 'taskkill'; Pattern = 'taskkill'; CaseSensitive = $false }
    @{ Name = 'dotnet build'; Pattern = 'dotnet build'; CaseSensitive = $false }
    @{ Name = 'CLAUDE.md'; Pattern = 'CLAUDE\.md'; CaseSensitive = $false }
    @{ Name = 'build/'; Pattern = '\bbuild/'; CaseSensitive = $false }
    @{ Name = 'src/'; Pattern = '\bsrc/'; CaseSensitive = $false }
)

# --- Group C: the CLAUDE.md headings that moved into the skill (exact heading text) ---
$MovedHeadings = @(
    'Rebuilding a .seq 1:1 <EM> `export_sequence_file` + `import_sequence_file` FIRST'
    'The on-disk FORMAT is reproduced too <EM> and the diff is blind to it (2026-07-31)'
    'A `.lvlibp` pane is CLONED, never loaded <EM> and the server now ENFORCES that (2026-07-30)'
    'The two prototype-load kinds are MUTUALLY EXCLUSIVE per process (measured 2026-07-29)'
    'Residuals after export/import: NONE (2026-07-29)'
    'NEVER replace a TYPED PropertyObject with a clone <EM> write its scalars BY VALUE (2026-07-29)'
    'Cloning ONE sequence <EM> `duplicate_sequence`'
    'CRITICAL: How to build sequences from a flowchart/description'
    'Documenting sequence files (teststand-doc-generator)'
    'Presenting sequence files (teststand-presentation-generator)'
    'Sequence Design Rules'
    'Flow Control: If/Else always with NI_Flow_* Step Types'
    'Default Step Type When Unclear'
    'Correct Structure for an If/Else Block:'
    'Correct Structure for a Select/Case Block:'
    'All Available Step Types (from get_step_types):'
    'Expressions'
    'Properties & variables'
    'Headless limitations = EXPECTED outcomes (not bugs)'
    'Live thread-context inspection (runtime debugging)'
    'Step-type / enum specifics'
    'User management'
    'Pre-build validation'
    'Sequence Analyzer: duration, a REAL timeout, and never trust a zero (2026-07-29)'
    'Sequence Analyzer is ASYNC-capable (cold `.lvlibp` timeout fix; 2026-07-09)'
    'Post-build validation (reference audit)'
    'TestStand ENVIRONMENTS (`.tsenv`) <EM> chosen once per PROCESS (2026-08-14, issue #35)'
    'Enumeration data types (create/modify/delete)'
    'Authoring-complete step editing (1:1 file rebuilds; `T31`/`T32`)'
) | ForEach-Object { $_.Replace('<EM>', $EmDash) }

# The .tsenv section is split: its user half moved, its developer half stays in CLAUDE.md.
$TsenvUserHalfMarkers = @(
    '**Sources, in precedence order:**'
    '**`get_engine_paths` now reports**'
    '**`open_sequence_file` warns on a MISMATCH**'
)
$TsenvDeveloperHeadingPrefix = '## TestStand ENVIRONMENTS'
$TsenvDeveloperMarker = 'SetEnvironmentPath'

# CLAUDE.md wraps its skill imports in markers (and an import is a whole line "@path"): the rules for
# both live in SkillSources.psm1, which exports $ImportsBeginMarker, $ImportsEndMarker and $ImportLinePattern.
Import-Module (Join-Path $PSScriptRoot 'SkillSources.psm1')

# Markdown outside these roots (relative, forward slashes) is searched for moved headings.
$UniquenessExcludedRoots = @('plugin/skills/', 'docs/specs/', 'plans/')
$WalkExcludedDirs = @('.git', 'bin', 'obj', 'node_modules', 'worktrees')

# --- Named patterns and limits ---
$FenceLinePattern = '^\s*```'
$HeadingLinePattern = '^#{1,6}\s+(.*?)\s*$'
$SectionHeadingPattern = '^##\s'
$ListItemPattern = '^[-*]\s+(.*)$'
$ContinuationPattern = '^\s+\S'
$FrontmatterNamePattern = '(?m)^name:\s*teststand-rules\s*$'
$FrontmatterDescriptionPattern = '(?m)^description:\s*\S'
$MaxDetailLines = 8
$GitTimeoutSeconds = 60

if ($DefinitionsOnly) { return }

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')   # Read-Utf8Text: strict UTF-8, BOM kept

if (-not $SkillRoot) { $SkillRoot = Join-Path $RepoRoot 'plugin\skills\teststand-rules' }
$ClaudeMdPath = Join-Path $RepoRoot 'CLAUDE.md'

# ----------------------------------------------------------------------------
$script:Failures = 0
$script:TextCache = @{}

function Write-Result {
    param([string]$Name, [bool]$Ok, [string[]]$Detail = @())
    if ($Ok) {
        Write-Host "PASS  $Name"
    } else {
        $script:Failures++
        Write-Host "FAIL  $Name"
        foreach ($line in ($Detail | Select-Object -First $MaxDetailLines)) { Write-Host "      $line" }
    }
}

# Each file is decoded once; a leading BOM is dropped so heading and import patterns anchor at column 0.
function Get-FileText {
    param([string]$Path)
    if (-not $script:TextCache.ContainsKey($Path)) {
        $text = Read-Utf8Text $Path
        # Ordinal on purpose: a culture-sensitive StartsWith treats U+FEFF as ignorable and is true for every string.
        if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
        $script:TextCache[$Path] = $text
    }
    return $script:TextCache[$Path]
}

# The lines of a text outside code fences, with their 1-based line numbers.
function Get-ProseLine {
    param([string]$Text)
    $lines = $Text -split "\r?\n"
    $inFence = $false
    for ($n = 0; $n -lt $lines.Count; $n++) {
        if ($lines[$n] -match $FenceLinePattern) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        [pscustomobject]@{ Number = $n + 1; Text = $lines[$n] }
    }
}

function Test-AllPatterns {
    param([string]$Text, [string[]]$Patterns)
    foreach ($pattern in $Patterns) {
        if (-not [regex]::IsMatch($Text, $pattern, 'IgnoreCase')) { return $false }
    }
    return $true
}

function Get-RelativePath {
    param([string]$Root, [string]$Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($Path)
    return $full.Substring($rootFull.Length).Replace('\', '/')
}

# git's tracked paths (raw, NUL separated); $null when git is missing, fails or the repository is unreadable.
function Get-GitTrackedPath {
    $result = Invoke-Git -RepoRoot $RepoRoot -Arguments @('ls-files', '-z') -TimeoutSeconds $GitTimeoutSeconds -SoftFail
    if ($null -eq $result) { return $null }
    return , @($result.Stdout -split "`0" | Where-Object { $_ })   # the comma keeps a one-element list an array
}

function Get-MarkdownByWalk {
    $found = @()
    $pending = New-Object System.Collections.Generic.Stack[string]
    $pending.Push((Resolve-Path -LiteralPath $RepoRoot).ProviderPath)
    while ($pending.Count -gt 0) {
        $dir = $pending.Pop()
        foreach ($child in Get-ChildItem -LiteralPath $dir -Force) {
            if ($child.PSIsContainer) {
                if ($WalkExcludedDirs -notcontains $child.Name) { $pending.Push($child.FullName) }
            } elseif ($child.Extension -ieq '.md') {
                $found += (Get-RelativePath $RepoRoot $child.FullName)
            }
        }
    }
    return $found
}

# The markdown files of the repository: git-tracked ones when it is a git checkout (with a warning and a
# directory walk when git cannot answer), else a directory walk. CLAUDE.md is always included.
function Get-RepositoryMarkdownPath {
    $paths = $null
    if (Test-Path -LiteralPath (Join-Path $RepoRoot '.git')) {
        $tracked = Get-GitTrackedPath
        if ($null -eq $tracked -or $tracked.Count -eq 0) {
            Write-Host 'WARN  git ls-files failed or listed nothing; scanning the directory tree instead'
        } else {
            $paths = @($tracked | Where-Object { $_ -match '\.md$' } | ForEach-Object { $_.Replace('\', '/') })
        }
    }
    if ($null -eq $paths) { $paths = Get-MarkdownByWalk }
    if (-not ($paths | Where-Object { $_ -ieq 'CLAUDE.md' })) { $paths = @($paths) + 'CLAUDE.md' }
    return $paths
}

# ============================================================================
#  Group A - required rules
# ============================================================================
$skillFiles = @()
if (Test-Path -LiteralPath $SkillRoot -PathType Container) {
    $skillFiles = @(Get-ChildItem -LiteralPath $SkillRoot -Recurse -File -Filter '*.md' | Sort-Object FullName)
}

$missingFiles = @($SkillFileNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $SkillRoot $_)) })
Write-Result "skill: SKILL.md and the $($ReferenceFiles.Count) reference files exist" ($missingFiles.Count -eq 0) @($missingFiles | ForEach-Object { "missing: $_" })

$referenceTexts = @()
foreach ($name in $ReferenceFiles) {
    $path = Join-Path $SkillRoot "references\$name"
    if (Test-Path -LiteralPath $path -PathType Leaf) { $referenceTexts += (Get-FileText $path) }
}
foreach ($rule in $RuleTable) {
    $hit = $false
    foreach ($text in $referenceTexts) { if (Test-AllPatterns $text $rule.Patterns) { $hit = $true; break } }
    Write-Result "required rule present: $($rule.Name)" $hit @("no references/*.md file contains all of: $($rule.Patterns -join ' | ')")
}

$skillMdPath = Join-Path $SkillRoot 'SKILL.md'
if (Test-Path -LiteralPath $skillMdPath) {
    $skillMd = Get-FileText $skillMdPath
    $skillLines = @($skillMd -split "\r?\n")

    $frontmatter = Get-FrontmatterMatch $skillMd
    $frontOk = $frontmatter.Success -and
        ($frontmatter.Groups[1].Value -match $FrontmatterNamePattern) -and
        ($frontmatter.Groups[1].Value -match $FrontmatterDescriptionPattern)
    Write-Result 'SKILL.md: frontmatter has name teststand-rules and a description' $frontOk @('expected a leading --- block with "name: teststand-rules" and a non-empty "description:"')

    # The first section: from the first "## " heading to the next one. Wrapped list items are folded.
    $sectionStart = -1; $sectionEnd = $skillLines.Count
    for ($i = 0; $i -lt $skillLines.Count; $i++) {
        if ($skillLines[$i] -match $SectionHeadingPattern) {
            if ($sectionStart -lt 0) { $sectionStart = $i } else { $sectionEnd = $i; break }
        }
    }
    $items = @()
    if ($sectionStart -ge 0) {
        $current = $null
        for ($i = $sectionStart + 1; $i -lt $sectionEnd; $i++) {
            if ($skillLines[$i] -match $ListItemPattern) {
                if ($null -ne $current) { $items += $current }
                $current = $Matches[1]
            } elseif ($null -ne $current -and $skillLines[$i] -match $ContinuationPattern) {
                $current += ' ' + $skillLines[$i].Trim()
            }
        }
        if ($null -ne $current) { $items += $current }
    }
    $neverDoOk = ($sectionStart -ge 0) -and ($items.Count -eq $NeverDoRules.Count)
    $neverDoDetail = @("the first section of SKILL.md must list exactly $($NeverDoRules.Count) rules, found $($items.Count)")
    if ($neverDoOk) {
        for ($r = 0; $r -lt $NeverDoRules.Count; $r++) {
            if (-not (Test-AllPatterns $items[$r] $NeverDoRules[$r].Patterns)) {
                $neverDoOk = $false
                $neverDoDetail = @("never-do item $($r + 1) is not the rule '$($NeverDoRules[$r].Name)' (needs: $($NeverDoRules[$r].Patterns -join ' | '))")
                break
            }
        }
    }
    Write-Result "SKILL.md: the first section holds exactly the $($NeverDoRules.Count) never-do rules" $neverDoOk $neverDoDetail

    $missingPointers = @($ReferenceFiles | Where-Object { $skillMd -notmatch [regex]::Escape("references/$_") })
    Write-Result 'SKILL.md: points to every reference file' ($missingPointers.Count -eq 0) @($missingPointers | ForEach-Object { "no pointer to: references/$_" })
}

# ============================================================================
#  Group B - deny-list
# ============================================================================
foreach ($entry in $DenyList) {
    $options = if ($entry.CaseSensitive) { 'None' } else { 'IgnoreCase' }
    $hits = @()
    foreach ($file in $skillFiles) {
        $lines = @((Get-FileText $file.FullName) -split "\r?\n")
        for ($n = 0; $n -lt $lines.Count; $n++) {
            if ([regex]::IsMatch($lines[$n], $entry.Pattern, $options)) {
                $hits += "$(Get-RelativePath $SkillRoot $file.FullName):$($n + 1): $($lines[$n].Trim())"
            }
        }
    }
    Write-Result "deny-list: no '$($entry.Name)' ($($entry.Pattern)) in the skill files" ($hits.Count -eq 0) $hits
}

# ============================================================================
#  Group C - no moved heading remains
# ============================================================================
$claudeMdExists = Test-Path -LiteralPath $ClaudeMdPath -PathType Leaf
$claudeProse = @()
$claudeText = ''
if ($claudeMdExists) {
    $claudeText = Get-FileText $ClaudeMdPath
    $claudeProse = @(Get-ProseLine $claudeText)
}

$leftovers = @()
foreach ($relative in (Get-RepositoryMarkdownPath)) {
    $excluded = $false
    foreach ($root in $UniquenessExcludedRoots) { if ($relative.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $excluded = $true } }
    if ($excluded) { continue }
    $full = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    foreach ($line in (Get-ProseLine (Get-FileText $full))) {
        $heading = [regex]::Match($line.Text, $HeadingLinePattern)
        if ($heading.Success -and ($MovedHeadings -ccontains $heading.Groups[1].Value)) {
            $leftovers += "${relative}:$($line.Number): moved heading still present: $($heading.Groups[1].Value)"
        }
    }
}
Write-Result 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' ($leftovers.Count -eq 0) $leftovers

$userHalfLeft = @($TsenvUserHalfMarkers | Where-Object { $claudeText.Contains($_) })
Write-Result 'uniqueness: CLAUDE.md keeps no user half of the .tsenv section' ($userHalfLeft.Count -eq 0) @($userHalfLeft | ForEach-Object { "still in CLAUDE.md: $_" })

$developerHeadings = @($claudeProse | Where-Object { $_.Text.StartsWith($TsenvDeveloperHeadingPrefix) })
$developerHalfPresent = $claudeMdExists -and ($developerHeadings.Count -eq 1) -and $claudeText.Contains($TsenvDeveloperMarker)
Write-Result 'uniqueness: CLAUDE.md keeps the developer half of the .tsenv section' $developerHalfPresent @("expected exactly one '$TsenvDeveloperHeadingPrefix...' heading (found $($developerHeadings.Count)) and the text '$TsenvDeveloperMarker' in CLAUDE.md")

# ============================================================================
#  Group D - imports
# ============================================================================
$beginLines = @($claudeProse | Where-Object { $_.Text.Trim() -eq $ImportsBeginMarker })
$endLines = @($claudeProse | Where-Object { $_.Text.Trim() -eq $ImportsEndMarker })
$markersOk = ($beginLines.Count -eq 1) -and ($endLines.Count -eq 1) -and ($beginLines[0].Number -lt $endLines[0].Number)
Write-Result 'imports: CLAUDE.md has one begin and one end marker around the skill imports' $markersOk @("expected exactly one '$ImportsBeginMarker' before exactly one '$ImportsEndMarker' (found $($beginLines.Count) and $($endLines.Count))")

$dangling = @()
$imported = @()
$outsideMarkers = @()
foreach ($import in (Get-ImportLine @($claudeText -split '\r?\n'))) {
    $resolved = [IO.Path]::GetFullPath((Join-Path $RepoRoot $import.Path))
    $imported += $resolved
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { $dangling += "CLAUDE.md:$($import.Number): import @$($import.Path) does not exist" }
    if ($markersOk -and ($import.Number -lt $beginLines[0].Number -or $import.Number -gt $endLines[0].Number)) {
        $outsideMarkers += "CLAUDE.md:$($import.Number): import @$($import.Path) is outside the markers"
    }
}
Write-Result 'imports: every @path import in CLAUDE.md resolves' ($dangling.Count -eq 0) $dangling
Write-Result 'imports: every import sits between the markers' ($outsideMarkers.Count -eq 0) $outsideMarkers

$notImported = @()
foreach ($file in $skillFiles) {
    if ($imported -notcontains [IO.Path]::GetFullPath($file.FullName)) { $notImported += (Get-RelativePath $RepoRoot $file.FullName) }
}
Write-Result 'imports: CLAUDE.md imports SKILL.md and every reference file' ($notImported.Count -eq 0) @($notImported | ForEach-Object { "not imported: $_" })

# ============================================================================
Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host "$script:Failures check(s) FAILED"
    exit 1
}
Write-Host 'All checks passed'
exit 0
