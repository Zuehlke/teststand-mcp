# ============================================================================
#  SkillSources.Tests.ps1 - plain PowerShell asserts for build/Test-SkillSources.ps1
#  Every case builds a temp fixture repository (a copy of the real skill plus a
#  small CLAUDE.md), plants ONE defect and runs the checker against it with
#  -RepoRoot. The tables the checker owns (rules, deny-list, moved headings,
#  skill file names) are read from it with -DefinitionsOnly, so a new entry there
#  needs a sample here or the suite fails. Runs under Windows PowerShell 5.1 and
#  PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\SkillSources.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails. Needs git on
#  the PATH for the git-branch cases.
#  ASCII only on purpose (5.1 reads a BOM-less script as ANSI).
# ============================================================================
$CheckerPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Test-SkillSources.ps1'
. $CheckerPath -DefinitionsOnly   # $RuleTable $DenyList $MovedHeadings $SkillFileNames $NeverDoRules ...

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RealSkillRoot = Join-Path $RepoRoot 'plugin\skills\teststand-rules'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# One planted line per deny-list entry; the suite fails when an entry has no sample.
$DenySamples = @{
    'date'         = 'Measured 2026-07-29 on a real file.'
    'test id'      = 'Pinned by T35.'
    'memory'       = 'See the memory notes.'
    'wiki link'    = 'See [[another-note]].'
    '.Demo_jcm'    = 'Copy it from .Demo_jcm first.'
    'this file'    = 'Everything in this file is fixed.'
    'Marshal'      = 'Use Marshal for the call.'
    'dynamic'      = 'The dynamic binder fails.'
    'taskkill'     = 'Run taskkill first.'
    'dotnet build' = 'Then dotnet build the server.'
    'CLAUDE.md'    = 'See CLAUDE.md for details.'
    'build/'       = 'Run build/Pack.ps1 first.'
    'src/'         = 'Edit src/Program.cs.'
}

# A lower-case form of a case-sensitive entry that must NOT match.
$CaseNegatives = @{ 'test id' = 'Pinned by t35.' }

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'SkillSourcesTests_'


# Fixtures are LF-only, so a checkout with CRLF working-tree files still gives the same text.
function Read-FixtureText {
    param([string]$Path)
    return [IO.File]::ReadAllText($Path, $Utf8NoBom).Replace("`r`n", "`n")
}

function Get-SkillPath {
    param([string]$Root, [string]$Name)
    return (Join-Path $Root ('plugin\skills\teststand-rules\' + $Name))
}

function New-ClaudeMdText {
    $imports = $SkillFileNames | ForEach-Object { '@plugin/skills/teststand-rules/' + $_.Replace('\', '/') }
    return (@('# Fixture rules', '', '## User-facing rules', '', $ImportsBeginMarker) + $imports + @($ImportsEndMarker,
            '', "## TestStand ENVIRONMENTS (``.tsenv``) $EmDash engine internals", '',
            'The environment is applied through SetEnvironmentPath on the engine thread.', '')) -join "`n"
}

# A fixture repository that passes every check; each case then plants one defect.
function New-Fixture {
    $root = New-TempDir
    foreach ($name in $SkillFileNames) {
        Write-TextFile (Get-SkillPath $root $name) (Read-FixtureText (Join-Path $RealSkillRoot $name))
    }
    Write-TextFile (Join-Path $root 'CLAUDE.md') (New-ClaudeMdText)
    Write-TextFile (Join-Path $root 'README.md') "# Fixture readme`n`nNothing moved here.`n"
    return $root
}

function Edit-FixtureText {
    param([string]$Path, [scriptblock]$Edit)
    $text = Read-FixtureText $Path
    $edited = & $Edit $text
    Assert-True ($edited -ne $text) "the edit changed nothing in $Path"
    Write-TextFile $Path $edited
}

function Add-Line {
    param([string]$Path, [string]$Line)
    Write-TextFile $Path ((Read-FixtureText $Path).TrimEnd() + "`n" + $Line + "`n")
}

# Runs the checker against a fixture; returns exit code and the combined output.
function Invoke-Checker {
    param([string]$Root)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # stderr lines of the child must not throw under 5.1
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $CheckerPath -RepoRoot $Root 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    return @{ ExitCode = $exitCode; Output = $output }
}

function Get-FailCount {
    param([hashtable]$Run)
    return [regex]::Matches($Run.Output, '(?m)^FAIL  ').Count
}

function Assert-CheckerPasses {
    param([hashtable]$Run)
    Assert-True ($Run.ExitCode -eq 0) "expected exit 0 but got $($Run.ExitCode):`n$($Run.Output)"
    Assert-True ((Get-FailCount $Run) -eq 0) "unexpected FAIL line in:`n$($Run.Output)"
}

# The run must fail with exactly $FailCount FAIL lines, one of them the named check, and the output must carry the reason.
function Assert-CheckerFails {
    param([hashtable]$Run, [string]$FailingCheck, [string[]]$MustMention = @(), [int]$FailCount = 1)
    Assert-True ($Run.ExitCode -ne 0) "expected a non-zero exit code but got $($Run.ExitCode)"
    Assert-True ($Run.Output -match [regex]::Escape("FAIL  $FailingCheck")) "expected a FAIL line for [$FailingCheck] in:`n$($Run.Output)"
    $actual = Get-FailCount $Run
    Assert-True ($actual -eq $FailCount) "expected $FailCount FAIL line(s) but got $actual in:`n$($Run.Output)"
    foreach ($text in $MustMention) {
        Assert-True ($Run.Output.Contains($text)) "expected the output to mention [$text] in:`n$($Run.Output)"
    }
}

function Get-DenyCheckName {
    param($Entry)
    return "deny-list: no '$($Entry.Name)' ($($Entry.Pattern)) in the skill files"
}

function Initialize-GitRepository {
    param([string]$Root, [string[]]$Track)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $Root init -q 2>&1 | Out-Null
        foreach ($path in $Track) { & git -C $Root add -- $path 2>&1 | Out-Null }
    } finally {
        $ErrorActionPreference = $previous
    }
}

try {
    Invoke-Case 'control: the unmodified fixture passes every check' {
        Assert-CheckerPasses (Invoke-Checker (New-Fixture))
    }

    Invoke-Case 'integration: the real tree passes every check (assumes a quiet checkout)' {
        Assert-CheckerPasses (Invoke-Checker $RepoRoot)
    }

    Invoke-Case 'deny-list: every entry has a sample in this suite' {
        $missing = @($DenyList | Where-Object { -not $DenySamples.ContainsKey($_.Name) } | ForEach-Object { $_.Name })
        Assert-True ($missing.Count -eq 0) "no sample for deny-list entries: $($missing -join ', ')"
    }

    foreach ($entry in $DenyList) {
        $sample = $DenySamples[$entry.Name]
        $checkName = Get-DenyCheckName $entry

        Invoke-Case "deny-list: a planted '$($entry.Name)' fails and names file and line" {
            $root = New-Fixture
            Add-Line (Get-SkillPath $root 'references\rebuild.md') $sample
            Assert-CheckerFails (Invoke-Checker $root) $checkName @('references/rebuild.md', $sample)
        }

        if (-not $entry.CaseSensitive) {
            Invoke-Case "deny-list: '$($entry.Name)' also fails in upper case" {
                $root = New-Fixture
                Add-Line (Get-SkillPath $root 'references\agents.md') $sample.ToUpperInvariant()
                Assert-CheckerFails (Invoke-Checker $root) $checkName @('references/agents.md')
            }
        }
        elseif ($CaseNegatives.ContainsKey($entry.Name)) {
            Invoke-Case "deny-list: '$($entry.Name)' is case-sensitive, so the lower-case form passes" {
                $root = New-Fixture
                Add-Line (Get-SkillPath $root 'references\agents.md') $CaseNegatives[$entry.Name]
                Assert-CheckerPasses (Invoke-Checker $root)
            }
        }
    }

    Invoke-Case 'required rule: a removed rule fails and names the rule' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'references\sequence-design.md') { param($t) $t.Replace('default to `SequenceCall`', 'prefer SequenceCall') }
        Assert-CheckerFails (Invoke-Checker $root) 'required rule present: SequenceCall as default step type' @('default to')
    }

    Invoke-Case 'required rule: a rule found only in SKILL.md still fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'references\sequence-design.md') { param($t) $t.Replace('Goto', 'jump') }
        Assert-CheckerFails (Invoke-Checker $root) 'required rule present: NI_Flow_* mandate' @('references/*.md')
    }

    Invoke-Case 'skill files: a missing reference file fails, names only that file and breaks its import' {
        $root = New-Fixture
        Remove-Item -LiteralPath (Get-SkillPath $root 'references\tool-contracts.md')
        $run = Invoke-Checker $root
        Assert-CheckerFails $run "skill: SKILL.md and the $($ReferenceFiles.Count) reference files exist" @('missing: references\tool-contracts.md') -FailCount 2
        Assert-True ($run.Output -notmatch 'missing: references\\rebuild.md') 'the message names a file that exists'
        Assert-True ($run.Output.Contains('FAIL  imports: every @path import in CLAUDE.md resolves')) 'the dangling import was not reported'
    }

    Invoke-Case 'SKILL.md: a fifth item in the first section fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) $t.Replace("`n## Details", "- Never do a fifth thing.`n`n## Details") }
        $expected = $NeverDoRules.Count + 1
        Assert-CheckerFails (Invoke-Checker $root) "SKILL.md: the first section holds exactly the $($NeverDoRules.Count) never-do rules" @("found $expected")
    }

    Invoke-Case 'SKILL.md: never-do rules in the wrong order fail and name the item' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') {
            param($t)
            $lines = $t -split "`n"
            $first = [array]::FindIndex($lines, [Predicate[string]]{ param($l) $l.StartsWith('- Never run the') })
            $second = $first + 1
            $swap = $lines[$first]; $lines[$first] = $lines[$second]; $lines[$second] = $swap
            $lines -join "`n"
        }
        Assert-CheckerFails (Invoke-Checker $root) "SKILL.md: the first section holds exactly the $($NeverDoRules.Count) never-do rules" @('never-do item 1')
    }

    Invoke-Case 'SKILL.md: a missing never-do section fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) [regex]::Replace($t, '(?s)## Never do.*?(?=## Details)', '') }
        Assert-CheckerFails (Invoke-Checker $root) "SKILL.md: the first section holds exactly the $($NeverDoRules.Count) never-do rules"
    }

    Invoke-Case 'SKILL.md: a never-do item wrapped over several lines still counts' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) $t.Replace('as a subagent. Run its workflow in the main thread:', "as a`n  subagent. Run its workflow in the`n  main thread:") }
        Assert-CheckerPasses (Invoke-Checker $root)
    }

    Invoke-Case 'SKILL.md: a missing pointer fails and names the reference' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) $t.Replace('references/agents.md', 'references/other.md') }
        Assert-CheckerFails (Invoke-Checker $root) 'SKILL.md: points to every reference file' @('no pointer to: references/agents.md') -FailCount 1
    }

    Invoke-Case 'SKILL.md: frontmatter without a name fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) $t.Replace('name: teststand-rules', 'title: teststand-rules') }
        Assert-CheckerFails (Invoke-Checker $root) 'SKILL.md: frontmatter has name teststand-rules and a description'
    }

    Invoke-Case 'SKILL.md: frontmatter with an empty description fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) [regex]::Replace($t, '(?m)^description:.*$', 'description:') }
        Assert-CheckerFails (Invoke-Checker $root) 'SKILL.md: frontmatter has name teststand-rules and a description'
    }

    Invoke-Case 'SKILL.md: a file without a frontmatter block fails' {
        $root = New-Fixture
        Edit-FixtureText (Get-SkillPath $root 'SKILL.md') { param($t) [regex]::Replace($t, '(?s)^---.*?---\n', '') }
        Assert-CheckerFails (Invoke-Checker $root) 'SKILL.md: frontmatter has name teststand-rules and a description'
    }

    Invoke-Case 'imports: a dangling import fails and names the import' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace($ImportsEndMarker, "@plugin/skills/teststand-rules/references/missing.md`n$ImportsEndMarker") }
        Assert-CheckerFails (Invoke-Checker $root) 'imports: every @path import in CLAUDE.md resolves' @('@plugin/skills/teststand-rules/references/missing.md')
    }

    Invoke-Case 'imports: a skill file CLAUDE.md does not import fails and names the file' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace("@plugin/skills/teststand-rules/references/agents.md`n", '') }
        Assert-CheckerFails (Invoke-Checker $root) 'imports: CLAUDE.md imports SKILL.md and every reference file' @('not imported: plugin/skills/teststand-rules/references/agents.md')
    }

    Invoke-Case 'imports: an import outside the markers fails and names the line' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace("## User-facing rules`n", "## User-facing rules`n`n@plugin/skills/teststand-rules/SKILL.md`n") }
        Assert-CheckerFails (Invoke-Checker $root) 'imports: every import sits between the markers' @('outside the markers')
    }

    Invoke-Case 'imports: a missing begin marker fails' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace("$ImportsBeginMarker`n", '') }
        Assert-CheckerFails (Invoke-Checker $root) 'imports: CLAUDE.md has one begin and one end marker around the skill imports' @('found 0 and 1')
    }

    Invoke-Case 'imports: a duplicated end marker fails' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') $ImportsEndMarker
        Assert-CheckerFails (Invoke-Checker $root) 'imports: CLAUDE.md has one begin and one end marker around the skill imports' @('found 1 and 2')
    }

    Invoke-Case 'imports: an import inside a code fence is ignored' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') "`n``````text`n@plugin/skills/teststand-rules/references/missing.md`n``````"
        Assert-CheckerPasses (Invoke-Checker $root)
    }

    Invoke-Case 'uniqueness: a moved heading left in CLAUDE.md fails and names heading and line' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') "`n## Sequence Design Rules"
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('CLAUDE.md:', 'Sequence Design Rules')
    }

    Invoke-Case 'uniqueness: every committed moved heading is caught in CLAUDE.md' {
        $root = New-Fixture
        $claude = Join-Path $root 'CLAUDE.md'
        # The old .tsenv heading is covered by the developer-half checks, not by this loop.
        foreach ($heading in ($MovedHeadings | Where-Object { -not $_.StartsWith('TestStand ENVIRONMENTS') })) { Add-Line $claude "`n## $heading" }
        $run = Invoke-Checker $root
        Assert-CheckerFails $run 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @()
        Assert-True ($run.Output.Contains("CLAUDE.md:")) 'no line number reported'
    }

    Invoke-Case 'uniqueness: a moved heading with an em dash left in CLAUDE.md fails' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') "`n## Cloning ONE sequence $EmDash ``duplicate_sequence``"
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('Cloning ONE sequence')
    }

    Invoke-Case 'uniqueness: a moved heading in other markdown fails and names that file' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'README.md') "`n### Default Step Type When Unclear"
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('README.md:', 'Default Step Type When Unclear')
    }

    Invoke-Case 'uniqueness: a moved heading behind a leading BOM still fails' {
        $root = New-Fixture
        [IO.File]::WriteAllText((Join-Path $root 'README.md'), ($Bom + "## Default Step Type When Unclear`n"), $Utf8NoBom)
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('README.md:1')
    }

    Invoke-Case 'uniqueness: a moved heading inside a code fence is ignored' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'README.md') "`n``````md`n## Default Step Type When Unclear`n``````"
        Assert-CheckerPasses (Invoke-Checker $root)
    }

    Invoke-Case 'uniqueness: a moved heading quoted in docs/specs or plans is allowed' {
        $root = New-Fixture
        Write-TextFile (Join-Path $root 'docs\specs\quote.md') "# Spec`n`n## Sequence Design Rules`n"
        Write-TextFile (Join-Path $root 'plans\quote.md') "# Plan`n`n### Default Step Type When Unclear`n"
        Assert-CheckerPasses (Invoke-Checker $root)
    }

    Invoke-Case 'uniqueness: the user half of the .tsenv section left in CLAUDE.md fails' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') '- **Sources, in precedence order:** connect_engine first.'
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: CLAUDE.md keeps no user half of the .tsenv section' @('Sources, in precedence order')
    }

    Invoke-Case 'uniqueness: dropping the developer half of the .tsenv section fails' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace('SetEnvironmentPath', 'something else') }
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: CLAUDE.md keeps the developer half of the .tsenv section' @('SetEnvironmentPath')
    }

    Invoke-Case 'uniqueness: no developer .tsenv heading fails' {
        $root = New-Fixture
        Edit-FixtureText (Join-Path $root 'CLAUDE.md') { param($t) $t.Replace('## TestStand ENVIRONMENTS', '## Environments') }
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: CLAUDE.md keeps the developer half of the .tsenv section' @('found 0')
    }

    Invoke-Case 'uniqueness: two developer .tsenv headings fail' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') "`n## TestStand ENVIRONMENTS again"
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: CLAUDE.md keeps the developer half of the .tsenv section' @('found 2')
    }

    Invoke-Case 'git: a tracked file with a moved heading fails and an untracked one is not scanned' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'README.md') "`n### Default Step Type When Unclear"
        Write-TextFile (Join-Path $root 'notes.md') "# Notes`n`n## Sequence Design Rules`n"
        Initialize-GitRepository $root @('README.md', 'CLAUDE.md')
        $run = Invoke-Checker $root
        Assert-CheckerFails $run 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('README.md:')
        Assert-True (-not $run.Output.Contains('notes.md')) 'the untracked file was scanned'
        Assert-True (-not $run.Output.Contains('WARN')) 'git answered, so there is nothing to warn about'
    }

    Invoke-Case 'git: CLAUDE.md is scanned even when it is not tracked' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'CLAUDE.md') "`n## Sequence Design Rules"
        Initialize-GitRepository $root @('README.md')
        Assert-CheckerFails (Invoke-Checker $root) 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('CLAUDE.md:')
    }

    Invoke-Case 'git: a failing git falls back to a directory walk and warns' {
        $root = New-Fixture
        Add-Line (Join-Path $root 'README.md') "`n### Default Step Type When Unclear"
        [void](New-Item -ItemType Directory -Path (Join-Path $root '.git'))   # an empty .git directory: git cannot read it
        $run = Invoke-Checker $root
        Assert-CheckerFails $run 'uniqueness: no moved heading remains in CLAUDE.md or other tracked markdown' @('README.md:', 'WARN  git ls-files failed')
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
