# ============================================================================
#  BuildPluginArchive.Tests.ps1 - plain PowerShell asserts for build/Build-PluginArchive.ps1 and its
#  modules: PluginArchive, PluginStaging, PluginSmoke, SkillSources and the child-process and file
#  helpers of BuildCommon.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\BuildPluginArchive.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  Every row of the Gherkin "The script stops with a fix" outline has its own case: the fault is
#  planted, and the failure text must name the fault (What), say why it matters (Why) and give the
#  concrete change that fixes it (Fix). Faults that need no publish run on a tiny fake publish
#  output and the real repository sources, so they are fast.
#  The ONE real end-to-end run per layout is opt-in (TESTSTAND_MCP_PACKAGE_E2E=1): it publishes the
#  real exe (about 180 MB for the folder layout).
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $RepoRoot 'build'
$ScriptPath = Join-Path $BuildDir 'Build-PluginArchive.ps1'
$HostPath = (Get-Process -Id $PID).Path
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$SampleCommit = ('0123456789abcdef' * 2) + '01234567'   # 40 hex characters
$SampleVersion = '1.9.0'
$ForeignExe = Join-Path $env:SystemRoot 'System32\cmd.exe'   # an exe whose ProductVersion is not ours
$ProcessSettleSeconds = 10                                    # BuildCommon waits at most this long for a stopped process to go

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'BuildPluginArchiveTests_'

# --- Asserts ---------------------------------------------------------------------------------------
# The message a body throws, or $null when it does not throw.
function Get-ThrownMessage {
    param([scriptblock]$Body)
    try { & $Body } catch { return $_.Exception.Message }
    return $null
}

# Passes only when the body fails with the three labelled parts, every needle appears in the text
# and every $Fix needle appears in the Fix part (the last part of the text).
function Assert-FailsWith {
    param([scriptblock]$Body, [string[]]$Needles = @(), [string[]]$Fix = @())
    $text = Get-ThrownMessage $Body
    if ($null -eq $text) { throw 'expected a failure but none was thrown' }
    Assert-ThreePartFailure $text
    foreach ($needle in $Needles) { Assert-Contains $text $needle 'failure text' }
    $fixPart = $text.Substring($text.IndexOf("`nFix:", [StringComparison]::Ordinal))
    foreach ($needle in $Fix) { Assert-Contains $fixPart $needle 'the Fix part' }
}


# Fails when a process with this command-line fragment is still running; stops such leftovers first.
function Assert-NoProcessWithCommandLine {
    param([string]$ProgramName, [string]$Fragment)
    $left = @(Get-CimInstance Win32_Process -Filter "Name='$ProgramName'" | Where-Object { $_.CommandLine -like "*$Fragment*" })
    foreach ($process in $left) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
    Assert-Equal 0 $left.Count "leftover $ProgramName processes with '$Fragment'"
}

# A .cmd standing in for an exe; $Lines are its batch lines.
function New-FakeExe {
    param([string[]]$Lines)
    $path = Join-Path (New-TempDir) 'TestStandMCP.cmd'
    [IO.File]::WriteAllText($path, ((@('@echo off') + $Lines) -join "`r`n") + "`r`n", $Utf8NoBom)
    return $path
}

function Invoke-TestGit {
    param([string]$Directory, [string[]]$Arguments)
    $result = Invoke-ChildProcess -FilePath 'git' -ArgumentList (@('-C', $Directory) + $Arguments) -TimeoutSeconds 60
    Assert-Equal 0 $result.ExitCode "git $($Arguments -join ' ') said: $($result.Stdout) $($result.Stderr)"
}

# A small checkout: the folders and files the packaging reads, copied from this repository, as a git
# repository with everything added. -Mutate runs on the copy before git sees it; -WithBuild adds build/.
function New-RepoFixture {
    param([scriptblock]$Mutate = $null, [switch]$WithBuild)
    $root = New-TempDir
    foreach ($relative in @('plugin', '.claude\agents', 'scripts')) {
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent (Join-Path $root $relative)) -Force)
        Copy-Item -LiteralPath (Join-Path $RepoRoot $relative) -Destination (Join-Path $root $relative) -Recurse
    }
    foreach ($relative in @('CLAUDE.md', 'Program.cs', 'src\Tools\TestStandToolRegistry.cs')) {
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent (Join-Path $root $relative)) -Force)
        Copy-Item -LiteralPath (Join-Path $RepoRoot $relative) -Destination (Join-Path $root $relative)
    }
    if ($WithBuild) {
        [void](New-Item -ItemType Directory -Path (Join-Path $root 'build'))
        Get-ChildItem -LiteralPath $BuildDir -File | Where-Object { $_.Extension -in '.ps1', '.psm1' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $root 'build') }
    }
    if ($null -ne $Mutate) { & $Mutate $root }
    Invoke-TestGit $root @('init', '-q')
    Invoke-TestGit $root @('add', '-A')
    return $root
}

try {
    Import-Module (Join-Path $BuildDir 'BuildCommon.psm1') -Force
    Import-Module (Join-Path $BuildDir 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $BuildDir 'ToolClassification.psm1') -Force
    Import-Module (Join-Path $BuildDir 'SkillSources.psm1') -Force
    Import-Module (Join-Path $BuildDir 'PluginIdentity.psm1') -Force
    Import-Module (Join-Path $BuildDir 'PluginStaging.psm1') -Force
    Import-Module (Join-Path $BuildDir 'PluginArchive.psm1') -Force
    Import-Module (Join-Path $BuildDir 'PluginSmoke.psm1') -Force

    # ======================================================================
    #  Child-process and file helpers (BuildCommon)
    # ======================================================================
    Invoke-Case 'child process: arguments with spaces and quotes arrive as single elements' {
        $echo = Join-Path (New-TempDir) 'echo-args.ps1'
        Write-TextFile $echo 'param($a, $b); [Console]::Out.Write("[$a][$b]")'
        $result = Invoke-ChildProcess -FilePath $HostPath -TimeoutSeconds 60 -ArgumentList @('-NoProfile', '-File', $echo, 'one two', 'say "hi"')
        Assert-Equal 0 $result.ExitCode 'exit code'
        Assert-Equal '[one two][say "hi"]' $result.Stdout 'both arguments arrive whole'
        Assert-Equal '"C:\Program Files\x y\\"' (ConvertTo-ArgumentElement 'C:\Program Files\x y\') 'a trailing backslash is doubled inside the quotes'
        Assert-Equal 'plain' (ConvertTo-ArgumentElement 'plain') 'plain text stays unquoted'
        Assert-Equal '""' (ConvertTo-ArgumentElement '') 'the empty argument is an empty pair of quotes'
    }

    Invoke-Case 'child process: a program that outlives its timeout is stopped and reported' {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-ChildProcess -FilePath $HostPath -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 120') -TimeoutSeconds 2
        Assert-True $result.TimedOut 'the program was not reported as timed out'
        Assert-True ($null -eq $result.ExitCode) 'a timed-out program has no exit code'
        Assert-True ($clock.Elapsed.TotalSeconds -lt 60) "the timeout took $($clock.Elapsed.TotalSeconds) s"
    }

    Invoke-Case 'child process: stopping a timed-out wrapper stops the program it started too' {
        $marker = Get-Random -Minimum 700000 -Maximum 799999
        $exe = New-FakeExe @("ping -n $marker 127.0.0.1 >nul")
        try {
            $result = Invoke-ChildProcess -FilePath $exe -TimeoutSeconds 2
            Assert-True $result.TimedOut 'the wrapper was not reported as timed out'
            Assert-NoProcessWithCommandLine 'PING.EXE' "-n $marker "
        } finally { Assert-NoProcessWithCommandLine 'PING.EXE' "-n $marker " }
    }

    Invoke-Case 'child process: a non-zero exit is reported, not thrown, and stdout and stderr are captured separately' {
        $result = Invoke-ChildProcess -FilePath $HostPath -TimeoutSeconds 60 `
            -ArgumentList @('-NoProfile', '-Command', '[Console]::Out.Write("to-out"); [Console]::Error.Write("to-err"); exit 3')
        Assert-Equal 3 $result.ExitCode 'exit code'
        Assert-Equal 'to-out' $result.Stdout 'stdout'
        Assert-Equal 'to-err' $result.Stderr 'stderr'
    }

    Invoke-Case 'child process: Invoke-BuildScript runs a script under the same host with its arguments' {
        $script = Join-Path (New-TempDir) 'args.ps1'
        Write-TextFile $script 'param($Name); [Console]::Out.Write("hello $Name")'
        $result = Invoke-BuildScript -ScriptPath $script -ArgumentList @('-Name', 'a b') -TimeoutSeconds 60
        Assert-Equal 'hello a b' $result.Stdout 'stdout'
    }

    Invoke-Case 'child process: Assert-ChildOutcome names a timeout, a non-zero exit and carries the output; exit 0 passes' {
        $ok = [pscustomobject]@{ ExitCode = 0; TimedOut = $false; Stdout = 'fine'; Stderr = '' }
        Assert-ChildOutcome -Result $ok -Name 'tool' -TimeoutSeconds 5 -Why 'w' -Fix 'f'
        $failed = [pscustomobject]@{ ExitCode = 4; TimedOut = $false; Stdout = "line1`nline2"; Stderr = 'oops' }
        Assert-FailsWith { Assert-ChildOutcome -Result $failed -Name 'tool' -TimeoutSeconds 5 -Why 'because' -Fix 'do this' } @('tool failed (exit code 4)', 'line1', 'oops', 'because') -Fix @('do this')
        $stuck = [pscustomobject]@{ ExitCode = $null; TimedOut = $true; Stdout = ''; Stderr = '' }
        Assert-FailsWith { Assert-ChildOutcome -Result $stuck -Name 'tool' -TimeoutSeconds 5 -Why 'because' -Fix 'do this' } @('tool did not finish within 5 s and was stopped')
    }

    Invoke-Case 'child process: Get-OutputExcerpt takes the first or the last non-blank lines' {
        $text = "a`n`nb`nc`nd"
        Assert-Equal "a`nb" (Get-OutputExcerpt $text -Mode First -Count 2) 'first two'
        Assert-Equal "c`nd" (Get-OutputExcerpt $text -Mode Last -Count 2) 'last two'
        Assert-Equal '(no output)' (Get-OutputExcerpt " `n" ) 'blank text'
    }

    Invoke-Case 'files: Get-RelativeFileList lists forward-slash names in ordinal order and is empty for a missing folder' {
        $dir = New-TempDir
        Write-TextFile (Join-Path $dir 'b.txt') '1'
        Write-TextFile (Join-Path $dir 'B2.txt') '1'
        Write-TextFile (Join-Path $dir '.hidden\a.txt') '1'
        Assert-Equal '.hidden/a.txt,B2.txt,b.txt' ((Get-RelativeFileList $dir) -join ',') 'names'
        Assert-Equal 0 (Get-RelativeFileList (Join-Path $dir 'missing')).Count 'missing folder'
    }

    Invoke-Case 'files: a junction below the folder is refused' {
        $dir = New-TempDir
        $target = New-TempDir
        Write-TextFile (Join-Path $target 'outside.txt') 'x'
        $link = Join-Path $dir 'link'
        $made = Invoke-ChildProcess -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList @('/c', 'mklink', '/J', $link, $target) -TimeoutSeconds 30
        Assert-Equal 0 $made.ExitCode "mklink said: $($made.Stdout) $($made.Stderr)"
        try {
            Assert-FailsWith { Get-RelativeFileList $dir } @($link, 'junction or symbolic link') -Fix @('rmdir')
        } finally { [IO.Directory]::Delete($link) }
    }

    # ======================================================================
    #  Inputs and the layout table
    # ======================================================================
    Invoke-Case 'layout: folder is the default and every layout names its publish arguments and bin files' {
        Assert-Equal 'folder' (Get-DefaultArchiveLayout) 'default layout (docs/validation/self-contained-x86.md)'
        foreach ($name in (Get-ArchiveLayoutName)) {
            $layout = Get-ArchiveLayout $name
            foreach ($key in @('PublishArguments', 'BinRequired', 'BinForbidden')) { Assert-True ($layout.ContainsKey($key)) "layout $name has no $key" }
            Assert-True ($layout.PublishArguments -contains '--self-contained') "layout $name must decide --self-contained"
        }
        Assert-True ((Get-ArchiveLayoutName) -contains 'single-file') 'single-file is an explicit option'
        Assert-True ((Get-ArchiveLayoutName) -contains 'framework-dependent') 'framework-dependent is an explicit option'
    }

    Invoke-Case 'layout: an unknown layout fails and lists the valid ones' {
        Assert-FailsWith { Get-ArchiveLayout 'zip-of-doom' } @("Unknown exe layout 'zip-of-doom'", 'folder', 'single-file', 'framework-dependent') -Fix @('-Layout')
    }

    Invoke-Case 'version: X.Y.Z and X.Y.Z-suffix are accepted, anything else is refused with the fix' {
        foreach ($good in @('1.9.0', '0.0.0-ci', '10.20.30', '1.9.0-rc.1')) { Assert-BuildVersion $good }
        foreach ($bad in @('', 'v1.9.0', '1.9', '1.9.0.1', '01.9.0', '1.9.0-', '1.9.0 ', '1.9.0-ci/x')) {
            Assert-FailsWith { Assert-BuildVersion $bad } @() -Fix @('-Version 1.9.0')
        }
    }

    Invoke-Case 'version: the numeric core drops the pre-release suffix' {
        Assert-Equal '0.0.0' (Get-VersionCore '0.0.0-ci') 'core of 0.0.0-ci'
        Assert-Equal '1.9.0' (Get-VersionCore '1.9.0') 'core of 1.9.0'
    }

    Invoke-Case 'commit: only a full lower-case 40-character SHA is accepted' {
        Assert-BuildCommit $SampleCommit
        foreach ($bad in @('', $SampleCommit.Substring(1), ($SampleCommit + '0'), $SampleCommit.ToUpperInvariant(), ('g' * 40))) {
            Assert-FailsWith { Assert-BuildCommit $bad } @('40-character') -Fix @('git rev-parse HEAD')
        }
    }

    Invoke-Case 'tag: exactly v<version> and v<version>-rehearsal are accepted' {
        Assert-BuildTag -Tag 'v1.9.0' -Version '1.9.0'
        Assert-BuildTag -Tag 'v1.9.0-rehearsal' -Version '1.9.0'
        Assert-BuildTag -Tag 'v0.0.0-ci' -Version '0.0.0-ci'
    }

    Invoke-Case 'tag: a tag that merely starts with v<version>, or differs in case or length, is refused' {
        Assert-FailsWith { Assert-BuildTag -Tag 'v1.9.10' -Version '1.9.1' } @("'v1.9.10'", '1.9.1') -Fix @('-Tag v1.9.1')
        Assert-FailsWith { Assert-BuildTag -Tag 'v1.9.0.1' -Version '1.9.0' } @("'v1.9.0.1'")
        Assert-FailsWith { Assert-BuildTag -Tag 'V1.9.0' -Version '1.9.0' } @("'V1.9.0'")
        Assert-FailsWith { Assert-BuildTag -Tag 'v1.9.0-rehearsal2' -Version '1.9.0' } @("'v1.9.0-rehearsal2'")
        Assert-FailsWith { Assert-BuildTag -Tag 'v1.8.0' -Version '1.9.0' } @("'v1.8.0'", '-Tag v1.9.0')
        Assert-FailsWith { Assert-BuildTag -Tag '' -Version '1.9.0' } @('does not belong to version 1.9.0')
    }

    Invoke-Case 'release record: the run is the CI run URL under GitHub Actions and local otherwise' {
        Use-Environment @{ GITHUB_SERVER_URL = 'https://github.com'; GITHUB_REPOSITORY = 'o/r'; GITHUB_RUN_ID = '42' } {
            Assert-Equal 'https://github.com/o/r/actions/runs/42' (Get-RunUrl) 'run URL'
        }
        Use-Environment @{ GITHUB_SERVER_URL = $null; GITHUB_REPOSITORY = $null; GITHUB_RUN_ID = $null } { Assert-Equal 'local' (Get-RunUrl) 'run outside CI' }
        Use-Environment @{ GITHUB_SERVER_URL = 'https://github.com'; GITHUB_REPOSITORY = 'o/r'; GITHUB_RUN_ID = $null } { Assert-Equal 'local' (Get-RunUrl) 'a partial CI environment is not a run URL' }
    }

    Invoke-Case 'release record: New-ReleaseRecord carries the five VERSION.txt values, the build time in UTC ISO-8601' {
        $record = Use-Environment @{ GITHUB_SERVER_URL = $null; GITHUB_REPOSITORY = $null; GITHUB_RUN_ID = $null } { New-ReleaseRecord -Tag 'v1.9.0' -Version '1.9.0' -Commit $SampleCommit }
        Assert-Equal 'v1.9.0,1.9.0,local' ("$($record.tag),$($record.version),$($record.run)") 'values'
        Assert-Equal $SampleCommit $record.commit 'commit'
        Assert-True ($record.built -cmatch '\A\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ\z') "built was [$($record.built)]"
        Assert-Equal 'x86' $record.arch 'the default architecture'
        Assert-Equal 'x64' (New-ReleaseRecord -Tag 'v1.9.0' -Version '1.9.0' -Commit $SampleCommit -Architecture 'x64').arch 'the x64 architecture'
    }

    Invoke-Case 'commit time: the timestamp of a commit of this checkout, and a clear fix for one that is not' {
        $head = (Invoke-ChildProcess -FilePath 'git' -ArgumentList @('-C', $RepoRoot, 'rev-parse', 'HEAD') -TimeoutSeconds 30).Stdout.Trim()
        $seconds = (Invoke-Git -RepoRoot $RepoRoot -Arguments @('show', '-s', '--format=%ct', $head) -Why 'the expected commit time' -Fix 'run it in a checkout').Stdout.Trim()
        Assert-Equal $seconds "$((Get-CommitTimestamp -RepoRoot $RepoRoot -Commit $head).ToUnixTimeSeconds())" 'the commit time of HEAD, as git itself prints it'
        Assert-FailsWith { Get-CommitTimestamp -RepoRoot $RepoRoot -Commit $SampleCommit } @("git show", $SampleCommit) -Fix @('git rev-parse HEAD')
    }

    # ======================================================================
    #  SkillSources: the skill-import rules in one place
    # ======================================================================
    function New-SkillFixture {
        param([string]$ClaudeMd, [string[]]$SkillFiles = @('SKILL.md', 'references/a.md'))
        $root = New-TempDir
        Write-TextFile (Join-Path $root 'CLAUDE.md') $ClaudeMd
        foreach ($name in $SkillFiles) { Write-TextFile (Join-Path $root "plugin\skills\teststand-rules\$name") "---`nname: x`n---`n`n# Text of $name`n" }
        return $root
    }
    $Begin = $ImportsBeginMarker
    $End = $ImportsEndMarker

    Invoke-Case 'skill imports: the real CLAUDE.md names SKILL.md first and every reference, each below plugin/skills' {
        $plan = @(Get-SkillImportPlan -RepoRoot $RepoRoot)
        Assert-Equal 6 $plan.Count 'imports'
        Assert-Equal 'SKILL.md' (Split-Path -Leaf $plan[0].FullPath) 'first import'
        foreach ($import in $plan) { Assert-True (Test-Path -LiteralPath $import.FullPath -PathType Leaf) "$($import.FullPath) does not exist" }
    }

    Invoke-Case 'skill imports: a begin marker on the very first line works, and prose lines in the block are fine' {
        $root = New-SkillFixture "$Begin`nSome intro without a path.`n`n@plugin/skills/teststand-rules/SKILL.md`n@plugin/skills/teststand-rules/references/a.md`n$End`n"
        Assert-Equal 'SKILL.md,a.md' ((@(Get-SkillImportPlan -RepoRoot $root) | ForEach-Object { Split-Path -Leaf $_.FullPath }) -join ',') 'plan'
    }

    Invoke-Case 'fault: an import block line that mentions an @ path but is not a whole-line import' {
        $root = New-SkillFixture "$Begin`n@plugin/skills/teststand-rules/SKILL.md extra words`n$End`n"
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot $root } @('not a whole-line import', 'extra words') -Fix @('line of its own')
        $indented = New-SkillFixture "$Begin`n  @plugin/skills/teststand-rules/SKILL.md`n$End`n"
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot $indented } @('not a whole-line import')
    }

    Invoke-Case 'fault: an import that leaves plugin/skills through ".." is refused' {
        $root = New-SkillFixture "$Begin`n@plugin/skills/../../CLAUDE.md`n$End`n"
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot $root } @('outside', 'plugin') -Fix @('plugin/skills')
    }

    Invoke-Case 'fault: an import of a file that does not exist, markers that are missing, and an empty block' {
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot (New-SkillFixture "$Begin`n@plugin/skills/teststand-rules/gone.md`n$End`n") } @('gone.md', 'does not exist') -Fix @('git checkout')
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot (New-SkillFixture "# rules`n@plugin/skills/teststand-rules/SKILL.md`n") } @('skill-imports:begin') -Fix @('Test-SkillSources.ps1')
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot (New-SkillFixture "$Begin`n$End`n") } @('with lines between them')
        Assert-FailsWith { Get-SkillImportPlan -RepoRoot (New-SkillFixture "$Begin`njust prose`n$End`n") } @('no @ import')
    }

    Invoke-Case 'skill imports: frontmatter is matched with or without a BOM and inlined text drops it' {
        $bom = [string][char]0xFEFF
        Assert-True (Get-FrontmatterMatch "---`nname: x`n---`nbody").Success 'plain frontmatter'
        Assert-True (Get-FrontmatterMatch "${bom}---`r`nname: x`r`n---`r`nbody").Success 'BOM and CRLF'
        Assert-True (-not (Get-FrontmatterMatch "no frontmatter`n---`nx`n---`n").Success) 'a fence that is not at the start'
        $file = Join-Path (New-TempDir) 'a.md'
        Write-TextFile $file "---`nname: x`n---`n`n# Title`n`nbody`n`n"
        Assert-Equal "# Title`n`nbody" (Read-SkillFileText $file) 'inlined text'
    }

    # ======================================================================
    #  Outline row: plugin.json lacks the "version": "0.0.0" placeholder
    # ======================================================================
    Invoke-Case 'fault: plugin.json without the version placeholder' {
        $dir = New-TempDir
        $path = Join-Path $dir 'plugin.json'
        Write-TextFile $path "{ `"name`": `"x`", `"version`": `"1.2.3`" }"
        Assert-FailsWith { Assert-ManifestPlaceholder -ManifestPath $path } @($path, '0 occurrences', '0.0.0') -Fix @('git checkout -- plugin/.claude-plugin/plugin.json')
    }

    Invoke-Case 'fault: plugin.json with the placeholder twice is just as wrong' {
        $dir = New-TempDir
        $path = Join-Path $dir 'plugin.json'
        Write-TextFile $path "{ `"version`": `"0.0.0`", `"nested`": { `"version`": `"0.0.0`" } }"
        Assert-FailsWith { Assert-ManifestPlaceholder -ManifestPath $path } @('2 occurrences')
    }

    Invoke-Case 'placeholder: the committed manifest carries exactly one, wherever whitespace puts it' {
        Assert-ManifestPlaceholder -ManifestPath (Join-Path $RepoRoot 'plugin\.claude-plugin\plugin.json')
        Assert-Equal '"version":"0.0.0"' (Find-VersionPlaceholder -Text '{"version":"0.0.0"}' -Path 'inline').Value 'compact JSON is found too'
    }

    # ======================================================================
    #  Outline row: the skill source check fails
    # ======================================================================
    Invoke-Case 'fault: the skill source check fails because a moved heading is back in CLAUDE.md' {
        $root = New-RepoFixture -Mutate { param($r) [IO.File]::AppendAllText((Join-Path $r 'CLAUDE.md'), "`n## Sequence Design Rules`n") }
        Assert-FailsWith { Assert-SkillSources -RepoRoot $root } @('Test-SkillSources.ps1', 'FAIL', 'moved heading still present', 'Sequence Design Rules') -Fix @('Test-SkillSources.ps1', 'package again')
    }

    Invoke-Case 'skill sources: the real repository passes the check' {
        Assert-SkillSources -RepoRoot $RepoRoot
    }

    # ======================================================================
    #  Outline row: a registered tool is not classified
    # ======================================================================
    Invoke-Case 'fault: a registered tool is not classified' {
        $dir = New-TempDir
        $registry = Join-Path $dir 'Registry.cs'
        $classes = Join-Path $dir 'tool-classes.json'
        Write-TextFile $registry "Register(`"get_a`", `"a`")`nRegister(`"set_new_tool`", `"b`")`n"
        Write-TextFile $classes '{ "allow": ["get_a"], "prompt": [] }'
        Assert-FailsWith { Assert-AllToolsClassified -ClassesPath $classes -RegistryPath $registry } @('E_UNCLASSIFIED', 'set_new_tool', $classes) -Fix @('allow and prompt lists')
    }

    Invoke-Case 'classification: the real registry and the committed classification agree' {
        $tools = Get-ToolSourcePath -RepoRoot $RepoRoot
        Assert-AllToolsClassified -ClassesPath $tools.Classes -RegistryPath $tools.Registry
    }

    # ======================================================================
    #  Outline row: the published exe does not report the requested version
    # ======================================================================
    Invoke-Case 'fault: the published exe does not report the requested version' {
        Assert-FailsWith { Assert-PublishedVersion -ExePath $ForeignExe -Version $SampleVersion -Commit $SampleCommit } `
            @('ProductVersion', $SampleVersion, $SampleCommit) -Fix @('Directory.Build.props')
    }

    Invoke-Case 'publish: a failing dotnet publish is reported with its output and the command to reproduce it' {
        $emptyRepo = New-TempDir
        $publishDir = Join-Path (New-TempDir) 'publish'
        Assert-FailsWith { Publish-PluginExe -RepoRoot $emptyRepo -Layout 'folder' -Version $SampleVersion -Commit $SampleCommit -PublishDir $publishDir } `
            @('dotnet publish failed', 'exit code') -Fix @('dotnet publish TestStandMCP.csproj -c Release -p:Platform=x86 -r win-x86')
        Assert-FailsWith { Publish-PluginExe -RepoRoot $emptyRepo -Layout 'folder' -Version $SampleVersion -Commit $SampleCommit -PublishDir $publishDir -Architecture 'x64' } `
            @('dotnet publish failed') -Fix @('-p:Platform=x64 -r win-x64')
    }

    # ======================================================================
    #  Staging: a tiny fake publish output, the real repository sources
    # ======================================================================
    $Release = @{ tag = "v$SampleVersion"; version = $SampleVersion; commit = $SampleCommit; arch = 'x86'; built = '2026-10-24T12:00:00Z'; run = 'local' }
    $FolderLayout = Get-ArchiveLayout 'folder'

    # What dotnet publish leaves: the exe and runtime files, plus the files the csproj copies into every
    # output (CLAUDE.md, README.md, scripts, .claude) that the archive must take from the repository instead.
    function New-FakePublishDir {
        param([string[]]$Without = @(), [string[]]$Extra = @())
        $dir = New-TempDir
        $files = @('TestStandMCP.exe', 'appsettings.json', 'TestStandMCP.runtimeconfig.json', 'coreclr.dll', 'Other.dll', 'TestStandMCP.pdb',
            'CLAUDE.md', 'README.md', 'scripts/Test-LoadPrototype.ps1', '.claude/agents/stale-publish-agent.md', 'cs/Resource.dll') + $Extra
        foreach ($name in $files) {
            if ($Without -contains $name) { continue }
            Write-TextFile (Join-Path $dir $name) "fake $name"
        }
        return $dir
    }

    function New-StageFrom {
        param([string]$PublishDir, $Layout = $FolderLayout, [string]$Root = $RepoRoot, [hashtable]$StageRelease = $Release)
        $stage = Join-Path (New-TempDir) 'stage'
        New-StagedPlugin -RepoRoot $Root -PublishDir $PublishDir -StageDir $stage -Layout $Layout -Release $StageRelease
        return $stage
    }

    # The x64 plugin: the same release, its own identity.
    $ReleaseX64 = $Release.Clone(); $ReleaseX64.arch = 'x64'
    $IdentityX86 = Get-PluginIdentity -Architecture 'x86'
    $IdentityX64 = Get-PluginIdentity -Architecture 'x64'

    # Every staged text file (not the fake bin/ binaries) that contains the term.
    function Get-StagedFilesContaining {
        param([string]$StageDir, [string]$Term)
        return @(foreach ($name in (Get-RelativeFileList $StageDir)) {
            if ($name -notlike 'bin/*.dll' -and $name -notlike 'bin/*.exe' -and (Read-Utf8Text (Join-Path $StageDir $name)).IndexOf($Term, [StringComparison]::Ordinal) -ge 0) { $name }
        })
    }

    # One staged tree is built once; every fault case works on its own copy.
    $script:TemplateStage = $null
    $script:TemplatePublish = $null
    function New-StagedCopy {
        if ($null -eq $script:TemplateStage) {
            $script:TemplatePublish = New-FakePublishDir
            $script:TemplateStage = New-StageFrom -PublishDir $script:TemplatePublish
        }
        $copy = Join-Path (New-TempDir) 'stage'
        Copy-Item -LiteralPath $script:TemplateStage -Destination $copy -Recurse
        return $copy
    }

    function Assert-StageFault {
        param([scriptblock]$Plant, [string[]]$Needles, [string[]]$Fix = @(), $Layout = $FolderLayout)
        $stage = New-StagedCopy
        & $Plant $stage
        Assert-FailsWith { Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $script:TemplatePublish -Layout $Layout -Release $Release } $Needles -Fix $Fix
    }

    Invoke-Case 'staging: the staged tree matches the layout of spec AC-4 and passes every assertion' {
        $stage = New-StagedCopy
        Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $script:TemplatePublish -Layout $FolderLayout -Release $Release
        $files = Get-RelativeFileList $stage
        foreach ($required in @('.claude-plugin/plugin.json', '.mcp.json', 'VERSION.txt', 'skills/teststand-rules/SKILL.md', 'hooks/hooks.json',
                'bin/TestStandMCP.exe', 'bin/appsettings.json', 'bin/TestStandMCP.runtimeconfig.json', 'bin/CLAUDE.md', 'bin/cs/Resource.dll')) {
            Assert-True ($files -ccontains $required) "the staged tree has no $required"
        }
        Assert-Equal 0 @($files | Where-Object { $_ -like 'build/*' -or $_ -like '*/build/*' }).Count 'files from build/'
        Assert-True ($files -cnotcontains 'bin/README.md') 'the publish output''s README.md must not be staged'
        Assert-True ($files -cnotcontains 'bin/TestStandMCP.pdb') 'debug symbols are not staged'
        Assert-True ($files -cnotcontains 'bin/.claude/agents/stale-publish-agent.md') 'bin/.claude/agents comes from the repository, not the publish output'
    }

    Invoke-Case 'staging: only git-tracked plugin files are staged' {
        $tracked = @((Invoke-ChildProcess -FilePath 'git' -ArgumentList @('-C', $RepoRoot, 'ls-files', '-z', '--', 'plugin') -TimeoutSeconds 30).Stdout -split "`0" | Where-Object { $_ })
        $files = Get-RelativeFileList (New-StagedCopy)
        foreach ($name in $tracked) {
            $relative = $name.Substring('plugin/'.Length)
            if ($relative -ceq '.claude-plugin/plugin.json') { continue }
            Assert-True ($files -ccontains $relative) "tracked plugin file $relative was not staged"
        }
    }

    Invoke-Case 'staging: bin/scripts holds exactly the two generators and the template, hooks exactly the runtime hook files' {
        $files = Get-RelativeFileList (New-StagedCopy)
        Assert-Equal 'generate_teststand_doc.py,generate_teststand_presentation.py,presentation_template.html' ((@($files | Where-Object { $_ -like 'bin/scripts/*' } | ForEach-Object { $_.Substring('bin/scripts/'.Length) })) -join ',') 'bin/scripts'
        Assert-Equal 'allow-readonly.ps1,hook-common.ps1,hooks.json,platform-guard.ps1,tool-classes.json,update-notify.ps1' ((@($files | Where-Object { $_ -like 'hooks/*' } | ForEach-Object { $_.Substring('hooks/'.Length) })) -join ',') 'hooks'
    }

    Invoke-Case 'staging: the plugin agents are the converted source agents, the direct agents are unchanged and ship link-agents.bat' {
        $stage = New-StagedCopy
        $sources = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot '.claude\agents') -File -Filter '*.md')
        Assert-True ($sources.Count -gt 0) 'the repository has no agent, so the loop below would check nothing'
        Assert-Equal $sources.Count (Get-RelativeFileList (Join-Path $stage 'agents')).Count 'plugin agent count'
        foreach ($source in $sources) {
            $plugin = [IO.File]::ReadAllText((Join-Path $stage "agents\$($source.Name)"))
            Assert-NotContains $plugin 'mcp__teststand__' "plugin agent $($source.Name)"
            Assert-True ($plugin.IndexOf('CLAUDE.md', [StringComparison]::OrdinalIgnoreCase) -lt 0) "plugin agent $($source.Name) mentions CLAUDE.md"
            Assert-Equal (Get-FileSha256 $source.FullName) (Get-FileSha256 (Join-Path $stage "bin\.claude\agents\$($source.Name)")) "direct agent $($source.Name) byte for byte"
        }
        Assert-Contains ([IO.File]::ReadAllText((Join-Path $stage 'agents\teststand-doc-generator.md'))) 'mcp__plugin_teststand-mcp_teststand__' 'the plugin tool prefix'
        Assert-True (Test-Path -LiteralPath (Join-Path $stage 'bin\.claude\agents\link-agents.bat')) 'link-agents.bat'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $stage 'agents\link-agents.bat'))) 'link-agents.bat is not a plugin agent'
    }

    Invoke-Case 'staging: plugin.json is the source with only the version replaced, byte for byte' {
        $stage = New-StagedCopy
        $source = [IO.File]::ReadAllBytes((Join-Path $RepoRoot 'plugin\.claude-plugin\plugin.json'))
        $staged = [IO.File]::ReadAllBytes((Join-Path $stage '.claude-plugin\plugin.json'))
        $latin1 = [Text.Encoding]::GetEncoding(28591)
        $expected = $latin1.GetString($source) -replace '"version"(\s*):(\s*)"0\.0\.0"', ('"version"$1:$2"' + $SampleVersion + '"')
        Assert-Equal $expected $latin1.GetString($staged) 'staged manifest text'
        Assert-Equal $SampleVersion (([Text.Encoding]::UTF8.GetString($staged) | ConvertFrom-Json).version) 'staged version'
    }

    Invoke-Case 'staging: a CRLF manifest keeps its line ends and its non-ASCII bytes' {
        $dir = New-TempDir
        $sourcePath = Join-Path $dir 'plugin.json'
        $text = "{`r`n  `"name`": `"Z$([char]0x00FC)hlke`",`r`n  `"version`":`"0.0.0`"`r`n}`r`n"
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        [IO.File]::WriteAllBytes($sourcePath, $bytes)
        $stagedPath = Join-Path $dir 'staged.json'
        Write-StampedManifest -SourcePath $sourcePath -DestPath $stagedPath -Version '1.9.0-rc.1'
        $staged = [IO.File]::ReadAllBytes($stagedPath)
        Assert-Equal ($bytes.Length + 5) $staged.Length 'only "0.0.0" becomes "1.9.0-rc.1": five characters longer'
        $umlautAt = [Array]::IndexOf($bytes, [byte]0xC3)
        Assert-True ($umlautAt -gt 0 -and $staged[$umlautAt] -eq 0xC3 -and $staged[$umlautAt + 1] -eq 0xBC) 'the two UTF-8 bytes of the u-umlaut are untouched'
        Assert-Equal 4 ([regex]::Matches([Text.Encoding]::UTF8.GetString($staged), "`r`n")).Count 'every CRLF is kept'
        Assert-StampedManifest -SourcePath $sourcePath -StagedPath $stagedPath -Version '1.9.0-rc.1'
    }

    Invoke-Case 'staging x64: plugin.json, hooks.json and the agents carry the x64 identity, and the tree passes every assertion' {
        $publish = New-FakePublishDir
        $stage = New-StageFrom -PublishDir $publish -StageRelease $ReleaseX64
        Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $publish -Layout $FolderLayout -Release $ReleaseX64
        $manifest = (Read-Utf8Text (Join-Path $stage '.claude-plugin\plugin.json')) | ConvertFrom-Json
        foreach ($field in @('name', 'displayName', 'description')) { Assert-Equal $IdentityX64.$field $manifest.$field "plugin.json $field" }
        Assert-Equal $SampleVersion $manifest.version 'plugin.json version'
        $matcher = ((Read-Utf8Text (Join-Path $stage 'hooks\hooks.json')) | ConvertFrom-Json).hooks.PreToolUse[0].matcher
        $entries = @($matcher -split '\|')
        Assert-True ($entries.Count -gt 10) 'the matcher lists the allow-listed tools'
        Assert-Equal 0 @($entries | Where-Object { -not $_.StartsWith($IdentityX64.pluginToolPrefix, [StringComparison]::Ordinal) }).Count 'matcher entries without the x64 prefix'
        $agents = @(Get-ChildItem -LiteralPath (Join-Path $stage 'agents') -File)
        Assert-True (@($agents | Where-Object { (Read-Utf8Text $_.FullName).Contains($IdentityX64.pluginToolPrefix) }).Count -gt 0) 'an agent references the x64 tool prefix'
        Assert-Equal '' ((Get-StagedFilesContaining $stage $IdentityX86.pluginToolPrefix) -join ', ') 'staged files naming the x86 tool prefix'
        Assert-Contains (Read-Utf8Text (Join-Path $stage 'VERSION.txt')) 'arch: x64' 'VERSION.txt'
    }

    Invoke-Case 'staging x86: the tree is the committed plugin and names no x64 plugin' {
        $stage = New-StagedCopy
        Assert-Equal (Read-Utf8Text (Join-Path $RepoRoot 'plugin\hooks\hooks.json')) (Read-Utf8Text (Join-Path $stage 'hooks\hooks.json')) 'hooks.json equals the committed one'
        Assert-Equal '' ((Get-StagedFilesContaining $stage $IdentityX64.name) -join ', ') 'staged files naming the x64 plugin'
    }

    Invoke-Case 'fault: a staged x64 file names the x86 tool prefix' {
        $publish = New-FakePublishDir
        $stage = New-StageFrom -PublishDir $publish -StageRelease $ReleaseX64
        [IO.File]::AppendAllText((Join-Path $stage 'skills\teststand-rules\SKILL.md'), "`nCall $($IdentityX86.pluginToolPrefix)get_steps.`n")
        Assert-FailsWith { Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $publish -Layout $FolderLayout -Release $ReleaseX64 } `
            @('skills/teststand-rules/SKILL.md', $IdentityX86.pluginToolPrefix, 'another plugin') -Fix @('plugin.json')
    }

    Invoke-Case 'fault: the staged hooks.json is not the committed one with the plugin''s prefix' {
        Assert-StageFault { param($stage) [IO.File]::AppendAllText((Join-Path $stage 'hooks\hooks.json'), ' ') } @('hooks/hooks.json is not plugin/hooks/hooks.json') -Fix @('New-HooksJson.ps1')
    }

    Invoke-Case 'staging: VERSION.txt carries tag, version, commit, arch, built and run, one per line, in the format the exe reads' {
        $text = [IO.File]::ReadAllText((Join-Path (New-StagedCopy) 'VERSION.txt'))
        Assert-Equal "tag: v1.9.0`nversion: 1.9.0`ncommit: $SampleCommit`narch: x86`nbuilt: 2026-10-24T12:00:00Z`nrun: local`n" $text 'VERSION.txt'
    }

    Invoke-Case 'staging: bin/CLAUDE.md is the user rules only: a header, then the skill files in the import order, no developer section' {
        $text = [IO.File]::ReadAllText((Join-Path (New-StagedCopy) 'bin\CLAUDE.md'))
        Assert-Equal $text (New-ClaudeMdText -RepoRoot $RepoRoot) 'bin/CLAUDE.md equals a fresh generation'
        Assert-True ($text.StartsWith('# TestStand MCP rules', [StringComparison]::Ordinal)) 'the header line comes first'
        $positions = @('# TestStand rules', '# Rebuilding, migrating and cloning sequence files', '# Sequence design rules', '# The TestStand agents', '# Headless behaviour and tool rules', '# Tool contracts') | ForEach-Object { $text.IndexOf($_, [StringComparison]::Ordinal) }
        Assert-True (@($positions | Where-Object { $_ -lt 0 }).Count -eq 0) "a skill file is missing: $($positions -join ',')"
        Assert-Equal ((@($positions | Sort-Object)) -join ',') ($positions -join ',') 'the skill files follow the CLAUDE.md import order'
        Assert-NotContains $text 'skill-imports' 'the import markers'
        Assert-NotContains $text 'name: teststand-rules' 'the skill frontmatter'
        # No heading that CLAUDE.md keeps outside its import block (the developer sections) may be in it.
        $claudeLines = [IO.File]::ReadAllText((Join-Path $RepoRoot 'CLAUDE.md')) -split '\r?\n'
        $endLine = [Array]::IndexOf($claudeLines, $ImportsEndMarker)
        $developerHeadings = @($claudeLines[($endLine + 1)..($claudeLines.Count - 1)] | Where-Object { $_ -cmatch '^#{1,3} \S' })
        Assert-True ($developerHeadings.Count -gt 5) 'CLAUDE.md should still have its developer sections'
        $textLines = $text -split '\r?\n'
        foreach ($heading in $developerHeadings) { Assert-True ($textLines -cnotcontains $heading) "developer heading in bin/CLAUDE.md: $heading" }
    }

    Invoke-Case 'staging: another exe layout is built and asserted with its own table row' {
        $framework = Get-ArchiveLayout 'framework-dependent'
        $publish = New-FakePublishDir -Without @('coreclr.dll')
        $stage = New-StageFrom -PublishDir $publish -Layout $framework
        Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $publish -Layout $framework -Release $Release
        $single = Get-ArchiveLayout 'single-file'
        $singlePublish = New-FakePublishDir -Without @('coreclr.dll', 'TestStandMCP.runtimeconfig.json')
        $singleStage = New-StageFrom -PublishDir $singlePublish -Layout $single
        Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $singleStage -PublishDir $singlePublish -Layout $single -Release $Release
    }

    Invoke-Case 'staging: Select-StagedFile honours top-level excludes, wildcards and the only-list' {
        $names = @('a.dll', 'x.pdb', 'sub/y.pdb', 'scripts/s.ps1', 'keep/k.txt')
        Assert-Equal 'a.dll,keep/k.txt' ((Select-StagedFile -Names $names -Exclude @('scripts') -ExcludePattern @('*.pdb')) -join ',') 'excluded'
        Assert-Equal 'a.dll' ((Select-StagedFile -Names $names -Only @('a.dll', 'missing.txt')) -join ',') 'only'
    }

    Invoke-Case 'drift: every top-level item the csproj copies to the output is excluded from bin/ or required in it' {
        $bin = @(Get-StagingTable -RepoRoot $RepoRoot -Layout $FolderLayout -PublishDir $script:TemplatePublish | Where-Object { $_.Path -ceq 'bin' })[0]
        $handled = @($bin.Exclude) + @($bin.Required)
        function Get-UnhandledOutputItem {
            param([string]$ProjectText)
            $items = [regex]::Matches($ProjectText, '(?s)<(?:None|Content)\s+(?:Include|Update)="([^"]+)"[^>]*?CopyToOutputDirectory')
            return @($items | ForEach-Object { ($_.Groups[1].Value -split '[\\/]')[0] } | Sort-Object -Unique | Where-Object { $handled -cnotcontains $_ })
        }
        $project = [IO.File]::ReadAllText((Join-Path $RepoRoot 'TestStandMCP.csproj'))
        Assert-Equal 0 (Get-UnhandledOutputItem $project).Count "csproj output items the staging table does not handle: $((Get-UnhandledOutputItem $project) -join ', ')"
        # What the check can see is proved on a small fixture, not by counting things in the real csproj.
        Assert-Equal 'extras' ((Get-UnhandledOutputItem '<None Include="extras\**\*" CopyToOutputDirectory="PreserveNewest" />') -join ',') 'the drift check sees a new item'
        Assert-Equal 'extras,more' ((Get-UnhandledOutputItem '<Content Include="more\a.txt" CopyToOutputDirectory="Always" /><None Update="extras\b" CopyToOutputDirectory="Always" /><None Include="scripts\x" CopyToOutputDirectory="Always" />') -join ',') 'both Include and Update, Content and None, and an item that is handled is not reported'
        Assert-Equal 0 (Get-UnhandledOutputItem '<None Include="scripts\x.ps1" />').Count 'an item that is not copied to the output is not looked at'
    }

    # ---- the Outline rows of slice 5.2, one fixture each ------------------
    Invoke-Case 'fault: a required staged file is missing' {
        Assert-StageFault { param($stage) Remove-Item -LiteralPath (Join-Path $stage 'bin\appsettings.json') } @('missing appsettings.json', 'bin/') -Fix @('layout table', 'build\PluginArchive.psm1')
        Assert-StageFault { param($stage) Remove-Item -LiteralPath (Join-Path $stage '.mcp.json') } @('top-level', '.mcp.json') -Fix @()
    }

    Invoke-Case 'fault: the generated plugin.json or VERSION.txt file is missing' {
        Assert-StageFault { param($stage) Remove-Item -LiteralPath (Join-Path $stage 'VERSION.txt') } @('missing VERSION.txt') -Fix @('Write-VersionTxt')
        Assert-StageFault { param($stage) Remove-Item -LiteralPath (Join-Path $stage '.claude-plugin\plugin.json') } @('missing .claude-plugin/plugin.json') -Fix @('Write-StampedManifest')
    }

    Invoke-Case 'fault: the exe layout needs a runtime file the publish did not produce' {
        $publish = New-FakePublishDir -Without @('coreclr.dll')
        $stage = New-StageFrom -PublishDir $publish
        Assert-FailsWith { Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $publish -Layout $FolderLayout -Release $Release } @('missing coreclr.dll', 'bin/') -Fix @('layout table')
    }

    Invoke-Case 'fault: a file of another exe layout is staged' {
        $framework = Get-ArchiveLayout 'framework-dependent'
        $publish = New-FakePublishDir
        $stage = New-StageFrom -PublishDir $publish -Layout $framework
        Assert-FailsWith { Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $publish -Layout $framework -Release $Release } @('coreclr.dll', 'must not ship')
    }

    Invoke-Case 'fault: a build script, a README, debug symbols or an unlisted file sits in bin/' {
        foreach ($case in @(
            @{ Plant = { param($s) Write-TextFile (Join-Path $s 'bin\build\x.ps1') 'x' }; Needle = 'build/x.ps1' }
            @{ Plant = { param($s) Write-TextFile (Join-Path $s 'bin\README.md') 'x' }; Needle = 'README.md' }
            @{ Plant = { param($s) Write-TextFile (Join-Path $s 'bin\TestStandMCP.pdb') 'x' }; Needle = 'TestStandMCP.pdb' }
            @{ Plant = { param($s) Write-TextFile (Join-Path $s 'bin\zzz-unlisted.dll') 'x' }; Needle = 'zzz-unlisted.dll' }
        )) {
            Assert-StageFault $case.Plant @($case.Needle) -Fix @('csproj')
        }
    }

    Invoke-Case 'fault: an unexpected file at the archive root' {
        Assert-StageFault { param($stage) Write-TextFile (Join-Path $stage 'stray.txt') 'x' } @('top-level entries that nothing stages', 'stray.txt') -Fix @('Get-StagingTable')
    }

    Invoke-Case 'fault: a converted agent still contains mcp__teststand__' {
        Assert-StageFault {
            param($stage)
            [IO.File]::AppendAllText((Join-Path $stage 'agents\teststand-doc-generator.md'), "`ncall mcp__teststand__connect_engine`n")
        } @('agents/teststand-doc-generator.md', "'mcp__teststand__'") -Fix @('Get-AgentMappingTable')
    }

    Invoke-Case 'fault: a converted agent names CLAUDE.md in any case' {
        Assert-StageFault {
            param($stage)
            [IO.File]::AppendAllText((Join-Path $stage 'agents\teststand-doc-generator.md'), "`nSee claude.MD for the rules`n")
        } @("'CLAUDE.md'", 'agents/teststand-doc-generator.md')
    }

    Invoke-Case 'fault: the number of plugin agents differs from .claude/agents' {
        Assert-StageFault {
            param($stage)
            Remove-Item -LiteralPath (Join-Path $stage 'agents\teststand-doc-generator.md')
        } @('agents/ holds 2 file(s) but exactly 3 are expected', 'missing: teststand-doc-generator.md', 'one converted agent per source agent') -Fix @('.claude/agents', 'Write-PluginAgents')
    }

    Invoke-Case 'fault: bin/scripts holds a file outside its expected set' {
        Assert-StageFault {
            param($stage)
            Copy-Item -LiteralPath (Join-Path $RepoRoot 'scripts\Test-LoadPrototype.ps1') -Destination (Join-Path $stage 'bin\scripts')
        } @('bin/scripts/ holds 4 file(s) but exactly 3 are expected', 'unexpected: Test-LoadPrototype.ps1', 'maintainer script') -Fix @('DocScriptFiles')
    }

    Invoke-Case 'fault: hooks/ holds a file that is not a runtime hook' {
        Assert-StageFault {
            param($stage)
            Write-TextFile (Join-Path $stage 'hooks\AllowHook.Tests.ps1') 'test'
        } @('hooks/ holds 7 file(s)', 'unexpected: AllowHook.Tests.ps1') -Fix @('HookFiles', 'plugin/hooks')
    }

    Invoke-Case 'fault: bin/CLAUDE.md still contains an unresolved import' {
        Assert-StageFault {
            param($stage)
            [IO.File]::AppendAllText((Join-Path $stage 'bin\CLAUDE.md'), "`n@plugin/skills/teststand-rules/references/gone.md`n")
        } @('unresolved import', '@plugin/skills/teststand-rules/references/gone.md') -Fix @('New-ClaudeMdText')
    }

    Invoke-Case 'fault: bin/CLAUDE.md differs from a fresh generation by one extra line, one edit or one dropped file' {
        Assert-StageFault { param($stage) [IO.File]::AppendAllText((Join-Path $stage 'bin\CLAUDE.md'), "`n## Developer notes`n") } @('not what the skill files generate', 'line') -Fix @('Never edit bin/CLAUDE.md')
        Assert-StageFault {
            param($stage)
            $path = Join-Path $stage 'bin\CLAUDE.md'
            [IO.File]::WriteAllText($path, ([IO.File]::ReadAllText($path).Replace('## Never do', '## Never do (edited)')))
        } @('expected', 'edited')
        Assert-StageFault { param($stage) [IO.File]::WriteAllText((Join-Path $stage 'bin\CLAUDE.md'), "# TestStand MCP rules`n") } @('not what the skill files generate')
    }

    Invoke-Case 'fault: VERSION.txt lacks one of tag, version, commit, arch, built, run' {
        foreach ($key in @('tag', 'version', 'commit', 'arch', 'built', 'run')) {
            Assert-StageFault {
                param($stage)
                $path = Join-Path $stage 'VERSION.txt'
                $kept = @([IO.File]::ReadAllLines($path) | Where-Object { -not $_.StartsWith("${key}:") })
                [IO.File]::WriteAllText($path, (($kept -join "`n") + "`n"))
            } @("VERSION.txt lacks $key") -Fix @('Write-VersionTxt', 'Never edit VERSION.txt')
        }
    }

    Invoke-Case 'fault: VERSION.txt names another version than the one built' {
        Assert-StageFault { param($stage) Write-TextFile (Join-Path $stage 'VERSION.txt') "tag: v1.9.0`nversion: 1.8.0`ncommit: $SampleCommit`narch: x86`nbuilt: x`nrun: local`n" } @("version '1.8.0'", "'1.9.0' was requested")
    }

    Invoke-Case 'fault: VERSION.txt names another architecture than the one built' {
        Assert-StageFault { param($stage) Write-TextFile (Join-Path $stage 'VERSION.txt') "tag: v1.9.0`nversion: 1.9.0`ncommit: $SampleCommit`narch: x64`nbuilt: x`nrun: local`n" } @("arch 'x64'", "'x86' was requested")
    }

    Invoke-Case 'fault: the staged plugin.json differs from the source in more than the version' {
        Assert-StageFault {
            param($stage)
            $path = Join-Path $stage '.claude-plugin\plugin.json'
            [IO.File]::WriteAllText($path, ([IO.File]::ReadAllText($path).Replace('"license": "MIT"', '"license": "GPL"')))
        } @('not the source with only', 'unreviewed change') -Fix @('plugin/.claude-plugin/plugin.json')
    }

    Invoke-Case 'fault: a direct agent was changed after copying' {
        Assert-StageFault {
            param($stage)
            [IO.File]::AppendAllText((Join-Path $stage 'bin\.claude\agents\teststand-doc-generator.md'), 'x')
        } @('bin/.claude/agents/teststand-doc-generator.md', 'differs from') -Fix @('git checkout -- .claude/agents')
    }

    Invoke-Case 'fault: link-agents.bat is missing next to the direct agents' {
        Assert-StageFault { param($stage) Remove-Item -LiteralPath (Join-Path $stage 'bin\.claude\agents\link-agents.bat') } @('missing link-agents.bat', 'bin/.claude/agents/')
    }

    Invoke-Case 'fault: a junction inside the staged tree is refused' {
        $stage = New-StagedCopy
        $target = New-TempDir
        Write-TextFile (Join-Path $target 'outside.txt') 'x'
        $link = Join-Path $stage 'hooks\link'
        $made = Invoke-ChildProcess -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList @('/c', 'mklink', '/J', $link, $target) -TimeoutSeconds 30
        Assert-Equal 0 $made.ExitCode 'mklink'
        try {
            Assert-FailsWith { Assert-StagedPlugin -RepoRoot $RepoRoot -StageDir $stage -PublishDir $script:TemplatePublish -Layout $FolderLayout -Release $Release } @('junction or symbolic link')
        } finally { [IO.Directory]::Delete($link) }
    }

    Invoke-Case 'fault: an untracked or an ignored file below plugin/ stops the staging' {
        $untracked = New-RepoFixture
        Write-TextFile (Join-Path $untracked 'plugin\hooks\scratch.ps1') 'x'
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $untracked } @('plugin holds 1 file(s) git does not track', 'scratch.ps1') -Fix @('git add plugin')
        $ignored = New-RepoFixture -Mutate { param($r) Write-TextFile (Join-Path $r '.gitignore') "*.log`n" }
        Write-TextFile (Join-Path $ignored 'plugin\debug.log') 'x'
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $ignored } @('debug.log')
    }

    Invoke-Case 'fault: an untracked or an ignored agent below .claude/agents stops the staging, and is not an agent of the plugin' {
        $untracked = New-RepoFixture
        Write-TextFile (Join-Path $untracked '.claude\agents\scratch-agent.md') "---`nname: scratch-agent`ntools: Read`n---`nx`n"
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $untracked } @('.claude/agents holds 1 file(s) git does not track', 'scratch-agent.md') -Fix @('git add .claude/agents')
        $ignored = New-RepoFixture -Mutate { param($r) Write-TextFile (Join-Path $r '.gitignore') "ignored-agent.md`n"; Write-TextFile (Join-Path $r '.claude\agents\ignored-agent.md') "---`nname: ignored-agent`ntools: Read`n---`nx`n" }
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $ignored } @('ignored-agent.md')
        $table = @(Get-StagingTable -RepoRoot $untracked -Layout $FolderLayout -PublishDir (New-FakePublishDir))
        $agents = @($table | Where-Object { $_.Path -ceq 'agents' })[0]
        Assert-True (@($agents.Exact) -cnotcontains 'scratch-agent.md') 'the expected plugin agents are derived from the tracked agents, never from the working folder'
        $copied = @($table | Where-Object { $_.Path -ceq 'bin/.claude/agents' })[0]
        Assert-True (@($copied.Exact) -cnotcontains 'scratch-agent.md') 'the copied source agents are the tracked ones too'
        Assert-True ($copied.TrackedOnly -and @($table | Where-Object { $_.Path -ceq 'bin/scripts' })[0].TrackedOnly) 'agents and doc scripts are copied from git, not from the folder'
    }

    Invoke-Case 'fault: an untracked or an ignored documentation script (one that ships) stops the staging; an unrelated file in scripts/ does not' {
        $noScript = New-RepoFixture -Mutate { param($r) Write-TextFile (Join-Path $r '.gitignore') "generate_teststand_doc.py`n" }
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $noScript } @('scripts holds 1 file(s) git does not track', 'generate_teststand_doc.py') -Fix @('git add scripts')
        $fresh = New-RepoFixture
        Write-TextFile (Join-Path $fresh 'scripts\private-notes.py') 'not shipped'
        $stage = New-StageFrom -PublishDir (New-FakePublishDir) -Root $fresh
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $stage 'bin\scripts\private-notes.py'))) 'an unrelated file in scripts/ is not staged'
    }

    Invoke-Case 'staging: a tracked fixture repository stages cleanly and ignores nothing tracked' {
        $root = New-RepoFixture
        $publish = New-FakePublishDir
        $stage = New-StageFrom -PublishDir $publish -Root $root
        Assert-StagedPlugin -RepoRoot $root -StageDir $stage -PublishDir $publish -Layout $FolderLayout -Release $Release
    }

    Invoke-Case 'fault: CLAUDE.md without its import markers cannot generate bin/CLAUDE.md' {
        $root = New-SkillFixture "# rules`n@plugin/skills/teststand-rules/SKILL.md`n"
        Assert-FailsWith { New-ClaudeMdText -RepoRoot $root } @('skill-imports:begin') -Fix @('Test-SkillSources.ps1')
    }

    Invoke-Case 'staging: an agent that does not convert is reported with its file name' {
        $root = New-RepoFixture -Mutate { param($r) Write-TextFile (Join-Path $r '.claude\agents\broken.md') "---`nname: broken`n---`nno tools line`n" }
        Assert-FailsWith { New-StageFrom -PublishDir (New-FakePublishDir) -Root $root } @('broken.md', "no 'tools:' line") -Fix @('build\PluginIdentity.psm1')
    }

    # ---- the staged tree is frozen between its assertion and the zip --------
    Invoke-Case 'snapshot: an unchanged stage passes, an added, a changed or a removed file fails naming it' {
        $stage = New-StagedCopy
        $snapshot = Get-StageSnapshot -StageDir $stage
        Assert-StageUnchanged -StageDir $stage -Snapshot $snapshot
        Write-TextFile (Join-Path $stage 'bin\written-by-the-smoke-run.log') 'x'
        Assert-FailsWith { Assert-StageUnchanged -StageDir $stage -Snapshot $snapshot } @('changed after it was asserted', 'written-by-the-smoke-run.log') -Fix @('stage folder')
        Remove-Item -LiteralPath (Join-Path $stage 'bin\written-by-the-smoke-run.log')
        [IO.File]::AppendAllText((Join-Path $stage 'hooks\hooks.json'), ' ')
        Assert-FailsWith { Assert-StageUnchanged -StageDir $stage -Snapshot $snapshot } @('hooks/hooks.json')
    }

    Invoke-Case 'snapshot: ONE changed file is named by its path alone, never glued to its hash (a changed file is one old and one new line)' {
        $stage = New-StagedCopy
        $snapshot = Get-StageSnapshot -StageDir $stage
        $oldHash = (@($snapshot -split "`n" | Where-Object { $_.EndsWith('  hooks/hooks.json') })[0] -split '  ')[0]
        [IO.File]::AppendAllText((Join-Path $stage 'hooks\hooks.json'), ' ')
        $newHash = (@((Get-StageSnapshot -StageDir $stage) -split "`n" | Where-Object { $_.EndsWith('  hooks/hooks.json') })[0] -split '  ')[0]
        Assert-True ($oldHash -cne $newHash -and $oldHash.Length -eq 64 -and $newHash.Length -eq 64) 'the fixture must change the hash'
        $message = Get-ThrownMessage { Assert-StageUnchanged -StageDir $stage -Snapshot $snapshot }
        Assert-Contains $message 'changed after it was asserted: hooks/hooks.json' 'the changed path'
        Assert-NotContains $message $oldHash 'the message (the old hash)'
        Assert-NotContains $message $newHash 'the message (the new hash)'
        Assert-Equal 1 ([regex]::Matches($message, [regex]::Escape('hooks/hooks.json'))).Count 'the path is named once, not once per line of the diff'
    }

    # ======================================================================
    #  Smoke tests of the staged exe (PluginSmoke): fake exes are .cmd files
    # ======================================================================
    $VersionLines = @('echo TestStand MCP Server 1.9.0', 'echo   tag:     v1.9.0', "echo   commit:  $SampleCommit", 'echo   arch:    x86', 'echo   built:   2026-10-24T12:00:00Z', 'echo   run:     local')

    Invoke-Case 'smoke --version: the version, the tag and the commit on stdout with exit 0 pass' {
        Assert-VersionOutput -ExePath (New-FakeExe $VersionLines) -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x86'
    }

    Invoke-Case 'fault: --version names another version' {
        $exe = New-FakeExe (@('echo TestStand MCP Server 1.8.0') + $VersionLines[1..5])
        Assert-FailsWith { Assert-VersionOutput -ExePath $exe -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x86' } @("its first line ends with '1.8.0', not '1.9.0'", '--version output is wrong') -Fix @('VERSION.txt')
    }

    Invoke-Case 'fault: --version names another architecture than the archive' {
        $exe = New-FakeExe $VersionLines
        Assert-FailsWith { Assert-VersionOutput -ExePath $exe -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x64' } @("reports arch 'x86' but the archive is for 'x64'") -Fix @('-Architecture')
    }

    Invoke-Case 'fault: --version lacks the tag (an untagged or unreadable VERSION.txt)' {
        $exe = New-FakeExe @('echo TestStand MCP Server 1.9.0', "echo   commit:  $SampleCommit", 'echo   note:    release metadata incomplete: no tag')
        Assert-FailsWith { Assert-VersionOutput -ExePath $exe -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x86' } @("does not name 'v1.9.0'", 'release-metadata note')
    }

    Invoke-Case 'fault: --version reports an untagged -dev build' {
        $exe = New-FakeExe @('echo TestStand MCP Server 1.9.0-dev', 'echo   tag:     v1.9.0', "echo   commit:  $SampleCommit")
        Assert-FailsWith { Assert-VersionOutput -ExePath $exe -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x86' } @("untagged build ('-dev')")
    }

    Invoke-Case 'fault: --version exits non-zero' {
        $exe = New-FakeExe @('echo broken', 'exit /b 3')
        Assert-FailsWith { Assert-VersionOutput -ExePath $exe -Version $SampleVersion -Tag "v$SampleVersion" -Commit $SampleCommit -Arch 'x86' } @('failed (exit code 3)', 'broken') -Fix @('Run it by hand')
    }

    # tool list: a classification of get_a, get_b (allow) and set_c (prompt) over a three-tool registry
    function New-ToolListFixture {
        $dir = New-TempDir
        Write-TextFile (Join-Path $dir 'Registry.cs') "Register(`"get_a`", `"a`")`nRegister(`"get_b`", `"b`")`nRegister(`"set_c`", `"c`")`n"
        Write-TextFile (Join-Path $dir 'tool-classes.json') '{ "allow": ["get_a", "get_b"], "prompt": ["set_c"] }'
        return @{ Classes = (Join-Path $dir 'tool-classes.json'); Registry = (Join-Path $dir 'Registry.cs') }
    }

    Invoke-Case 'smoke --list-tools: exactly the classified tools, in any order, pass' {
        $fixture = New-ToolListFixture
        Assert-ToolList -ExePath (New-FakeExe @('echo ["set_c","get_a","get_b"]')) -ClassesPath $fixture.Classes -RegistryPath $fixture.Registry
    }

    Invoke-Case 'fault: the exe lists a tool that is not classified, and a classified one is missing' {
        $fixture = New-ToolListFixture
        Assert-FailsWith { Assert-ToolList -ExePath (New-FakeExe @('echo ["get_a","get_b","rogue_tool"]')) -ClassesPath $fixture.Classes -RegistryPath $fixture.Registry } `
            @('listed by the exe but not classified: rogue_tool', 'classified but not listed by the exe: set_c', 'tool-classes.json') -Fix @('allow and prompt lists')
    }

    Invoke-Case 'fault: the exe lists a tool twice, so its count differs from the registry' {
        $fixture = New-ToolListFixture
        Assert-FailsWith { Assert-ToolList -ExePath (New-FakeExe @('echo ["get_a","get_a","get_b","set_c"]')) -ClassesPath $fixture.Classes -RegistryPath $fixture.Registry } `
            @('the exe lists 4 tools, the registry has 3')
    }

    Invoke-Case 'fault: --list-tools --json prints text that is not JSON' {
        $fixture = New-ToolListFixture
        Assert-FailsWith { Assert-ToolList -ExePath (New-FakeExe @('echo Available Tools:')) -ClassesPath $fixture.Classes -RegistryPath $fixture.Registry } @('did not print a JSON array', 'Available Tools:') -Fix @('Program.cs')
    }

    # initialize: the fake reads one line from stdin, then answers like the server would
    function New-InitializeFake {
        param([string]$Version)
        return New-FakeExe @('set /p line=', "echo {`"jsonrpc`":`"2.0`",`"id`":1,`"result`":{`"serverInfo`":{`"name`":`"x`",`"version`":`"$Version`"}}}")
    }
    $RealProgramPath = Join-Path $RepoRoot 'Program.cs'

    Invoke-Case 'smoke initialize: serverInfo.version is the version built' {
        Assert-InitializeRoundTrip -ExePath (New-InitializeFake $SampleVersion) -Version $SampleVersion -ProgramSourcePath $RealProgramPath
    }

    Invoke-Case 'fault: initialize answers another version' {
        Assert-FailsWith { Assert-InitializeRoundTrip -ExePath (New-InitializeFake '1.8.0') -Version $SampleVersion -ProgramSourcePath $RealProgramPath } `
            @("serverInfo.version '1.8.0'", 'version 1.9.0 was built') -Fix @('VersionInfo.Current.ToServerInfo()')
    }

    Invoke-Case 'fault: initialize is never answered: the exe and what it started are stopped at once, without waiting for it to exit' {
        $marker = Get-Random -Minimum 800000 -Maximum 899999
        $exe = New-FakeExe @('set /p line=', "ping -n $marker 127.0.0.1 >nul")
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            Assert-FailsWith { Assert-InitializeRoundTrip -ExePath $exe -Version $SampleVersion -ProgramSourcePath $RealProgramPath -TimeoutSeconds 2 } @('did not answer an MCP initialize request within 2 s') -Fix @('bin\TestStandMCP.exe')
            Assert-True ($clock.Elapsed.TotalSeconds -lt (2 + $ProcessSettleSeconds)) "the check took $($clock.Elapsed.TotalSeconds) s; it must not wait for the post-EOF exit"
            Assert-NoProcessWithCommandLine 'PING.EXE' "-n $marker "
        } finally { Assert-NoProcessWithCommandLine 'PING.EXE' "-n $marker " }
    }

    Invoke-Case 'startup log: a version on a startup line of stderr passes; another version fails' {
        Assert-StartupLogVersion -StderrText 'info: TestStand MCP Server v1.9.0 starting...' -Version $SampleVersion -ProgramSourcePath $RealProgramPath
        Assert-FailsWith { Assert-StartupLogVersion -StderrText 'info: TestStand MCP Server v1.8.0 starting...' -Version $SampleVersion -ProgramSourcePath $RealProgramPath } `
            @('does not name version 1.9.0', 'v1.8.0')
    }

    Invoke-Case 'startup log: the version is matched as a whole number, so 1.9.0 is not found inside 11.9.0 or 1.9.0.1' {
        Assert-True (Test-ContainsVersion 'v1.9.0 starting' '1.9.0') 'plain'
        Assert-True (Test-ContainsVersion 'v0.0.0-ci starting' '0.0.0-ci') 'pre-release'
        foreach ($other in @('v11.9.0 starting', 'v1.9.01 starting', 'v1.9.0.1 starting', 'v1.9.0-rc1 starting', 'v21.9.0 starting')) {
            Assert-True (-not (Test-ContainsVersion $other '1.9.0')) "[$other] must not count as 1.9.0"
        }
        Assert-FailsWith { Assert-StartupLogVersion -StderrText 'info: TestStand MCP Server v11.9.0 starting...' -Version $SampleVersion -ProgramSourcePath $RealProgramPath } @('v11.9.0')
    }

    Invoke-Case 'startup log: stderr text without a startup line falls back to the source check' {
        Assert-StartupLogVersion -StderrText "warn: something else happened`nanother line" -Version $SampleVersion -ProgramSourcePath $RealProgramPath
        Assert-StartupLogVersion -StderrText '' -Version $SampleVersion -ProgramSourcePath $RealProgramPath
    }

    Invoke-Case 'startup log: the source must log VersionInfo.Current.Version in a LogInformation call' {
        $dir = New-TempDir
        $wrongVersion = Join-Path $dir 'Program.cs'
        Write-TextFile $wrongVersion 'logger.LogInformation("{Product} v{Version} starting...", VersionInfo.ProductName, "1.0.0");'
        Assert-FailsWith { Assert-StartupLogVersion -StderrText '' -Version $SampleVersion -ProgramSourcePath $wrongVersion } @('No startup line reached stderr', 'VersionInfo.Current.Version') -Fix @('logger.LogInformation')
        $onlyAComment = Join-Path $dir 'Program2.cs'
        Write-TextFile $onlyAComment '// starting: VersionInfo.Current.Version is what the log should print'
        Assert-FailsWith { Assert-StartupLogVersion -StderrText '' -Version $SampleVersion -ProgramSourcePath $onlyAComment } @('LogInformation call')
    }

    # ======================================================================
    #  Zip, release assets, local verification
    # ======================================================================
    $FixedTimestamp = [DateTimeOffset]::new(2026, 10, 24, 12, 0, 0, [TimeSpan]::FromHours(2))

    function New-SmallStage {
        $dir = Join-Path (New-TempDir) 'stage'
        Write-TextFile (Join-Path $dir 'VERSION.txt') 'tag: v1.9.0'
        Write-TextFile (Join-Path $dir '.claude-plugin\plugin.json') '{}'
        Write-TextFile (Join-Path $dir 'bin\scripts\a.py') 'print(1)'
        return $dir
    }

    Invoke-Case 'zip: every file is an entry with forward slashes, hidden folders included, no directory entries' {
        $stage = New-SmallStage
        $zipPath = Join-Path (New-TempDir) 'a.zip'
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Assert-PluginZip -ZipPath $zipPath -StageDir $stage
        $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try { $names = @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
        Assert-Equal '.claude-plugin/plugin.json,VERSION.txt,bin/scripts/a.py' ($names -join ',') 'entry names'
    }

    Invoke-Case 'zip: two packagings of the same stage are the same bytes, whatever the files'' own times are, and every entry carries the commit time' {
        $stage = New-SmallStage
        $first = Join-Path (New-TempDir) 'a.zip'
        $second = Join-Path (New-TempDir) 'b.zip'
        New-PluginZip -StageDir $stage -ZipPath $first -Timestamp $FixedTimestamp
        (Get-Item -LiteralPath (Join-Path $stage 'VERSION.txt')).LastWriteTime = [DateTime]::new(2020, 1, 2, 3, 4, 6)
        New-PluginZip -StageDir $stage -ZipPath $second -Timestamp $FixedTimestamp
        Assert-Equal (Get-FileSha256 $first) (Get-FileSha256 $second) 'the two archives'
        $zip = [IO.Compression.ZipFile]::OpenRead($first)
        try { $times = @($zip.Entries | ForEach-Object { $_.LastWriteTime.DateTime.ToString('yyyy-MM-dd HH:mm:ss') } | Sort-Object -Unique) } finally { $zip.Dispose() }
        Assert-Equal '2026-10-24 12:00:00' ($times -join ',') 'entry times'
    }

    Invoke-Case 'fault: a zip entry contains a backslash' {
        $stage = New-SmallStage
        $zipPath = Join-Path (New-TempDir) 'bad.zip'
        $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        foreach ($name in @('VERSION.txt', '.claude-plugin\plugin.json', 'bin/scripts/a.py')) { $zip.CreateEntry($name).Open().Dispose() }
        $zip.Dispose(); $stream.Dispose()
        Assert-FailsWith { Assert-PluginZip -ZipPath $zipPath -StageDir $stage } @("backslash, for example '.claude-plugin\plugin.json'") -Fix @('New-PluginZip', 'ZipFile.CreateFromDirectory')
    }

    Invoke-Case 'fault: the zip holds a file the staged tree does not, or lacks one it has' {
        $stage = New-SmallStage
        $zipPath = Join-Path (New-TempDir) 'a.zip'
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-TextFile (Join-Path $stage 'bin\scripts\late.py') 'x'
        Assert-FailsWith { Assert-PluginZip -ZipPath $zipPath -StageDir $stage } @('3 entries but the staged tree has 4 files', 'missing: bin/scripts/late.py') -Fix @('New-PluginZip')
    }

    # sha256sum -c on the digest; without sha256sum an independent Get-FileHash comparison instead
    function Assert-DigestAccepted {
        param([string]$OutDir)
        $sha256sum = Get-Command sha256sum -ErrorAction SilentlyContinue
        if ($null -ne $sha256sum) {
            $check = Invoke-ChildProcess -FilePath $sha256sum.Source -ArgumentList @('-c', $ReleaseDigestName) -WorkingDirectory $OutDir -TimeoutSeconds 120
            Assert-Equal 0 $check.ExitCode "sha256sum -c said: $($check.Stdout) $($check.Stderr)"
            Assert-Contains $check.Stdout 'OK' 'sha256sum output'
            return
        }
        Write-Host '      SKIP  sha256sum was not found; comparing with Get-FileHash instead'
        $line = ([IO.File]::ReadAllText((Join-Path $OutDir $ReleaseDigestName))).TrimEnd("`n")
        $expected = (Get-FileHash -LiteralPath (Join-Path $OutDir $ReleaseArchiveName) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $ReleaseArchiveName
        Assert-Equal $expected $line 'the digest line'
    }

    Invoke-Case 'assets: the versioned copy, digest and manifest are written under the ReleaseAssets names and the digest is accepted' {
        $stage = New-SmallStage
        $outDir = New-TempDir
        $zipPath = Join-Path $outDir $ReleaseArchiveName
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-ReleaseAssets -ZipPath $zipPath -ManifestText (Get-StageSnapshot -StageDir $stage) -Version '0.0.0-ci'
        foreach ($name in @($ReleaseArchiveName, $ReleaseDigestName, $ReleaseManifestName, (Get-VersionedArchiveName -Version '0.0.0'))) {
            Assert-True (Test-Path -LiteralPath (Join-Path $outDir $name)) "$name was not written"
        }
        Assert-Equal (Get-FileSha256 $zipPath) (Get-FileSha256 (Join-Path $outDir (Get-VersionedArchiveName -Version '0.0.0'))) 'versioned copy'
        $manifest = [IO.File]::ReadAllText((Join-Path $outDir $ReleaseManifestName))
        Assert-Equal 3 @($manifest.TrimEnd("`n") -split "`n").Count 'manifest lines'
        Assert-Contains $manifest '  bin/scripts/a.py' 'manifest'
        Assert-DigestAccepted $outDir
    }

    Invoke-Case 'verify: the release verifier accepts the assets in local mode' {
        $stage = New-SmallStage
        $outDir = New-TempDir
        $zipPath = Join-Path $outDir $ReleaseArchiveName
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-ReleaseAssets -ZipPath $zipPath -ManifestText (Get-StageSnapshot -StageDir $stage) -Version '1.9.0'
        Assert-LocalRelease -OutDir $outDir -Version '1.9.0'
    }

    Invoke-Case 'fault: the verifier rejects a digest that does not match the archive' {
        $stage = New-SmallStage
        $outDir = New-TempDir
        $zipPath = Join-Path $outDir $ReleaseArchiveName
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-ReleaseAssets -ZipPath $zipPath -ManifestText (Get-StageSnapshot -StageDir $stage) -Version '1.9.0'
        Write-TextFile (Join-Path $outDir $ReleaseDigestName) (('0' * 64) + "  $ReleaseArchiveName`n")
        Assert-FailsWith { Assert-LocalRelease -OutDir $outDir -Version '1.9.0' } @('release verifier', $ReleaseDigestName) -Fix @('Fix what the verifier reports')
    }

    Invoke-Case 'assets x64: the four assets carry the x64 prefix, its verifier accepts them and the x86 one finds its own missing' {
        $prefix = (Get-PluginIdentity -Architecture 'x64').assetPrefix
        $names = Get-PluginAssetNames -Prefix $prefix -Version '1.9.0'
        $stage = New-SmallStage
        $outDir = New-TempDir
        $zipPath = Join-Path $outDir $names.Archive
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-ReleaseAssets -ZipPath $zipPath -ManifestText (Get-StageSnapshot -StageDir $stage) -Version '1.9.0' -Prefix $prefix
        Assert-Equal ((@($names.Archive, $names.Digest, $names.Manifest, $names.Versioned) | Sort-Object) -join ',') ((@(Get-ChildItem -LiteralPath $outDir -File | ForEach-Object { $_.Name }) | Sort-Object) -join ',') 'the written assets'
        Assert-Equal 'teststand-mcp-x64-v1.9.0.zip' $names.Versioned 'the versioned name'
        Assert-Contains (Read-Utf8Text (Join-Path $outDir $names.Digest)) "  $($names.Archive)" 'the digest names the x64 archive'
        Assert-LocalRelease -OutDir $outDir -Version '1.9.0' -Prefix $prefix
        Assert-FailsWith { Assert-LocalRelease -OutDir $outDir -Version '1.9.0' } @('release verifier', "the plugin archive $ReleaseArchiveName is missing") -Fix @('Fix what the verifier reports')
    }

    Invoke-Case 'fault x64: a digest that does not match the x64 archive is named with the x64 names' {
        $prefix = (Get-PluginIdentity -Architecture 'x64').assetPrefix
        $names = Get-PluginAssetNames -Prefix $prefix -Version '1.9.0'
        $stage = New-SmallStage
        $outDir = New-TempDir
        $zipPath = Join-Path $outDir $names.Archive
        New-PluginZip -StageDir $stage -ZipPath $zipPath -Timestamp $FixedTimestamp
        Write-ReleaseAssets -ZipPath $zipPath -ManifestText (Get-StageSnapshot -StageDir $stage) -Version '1.9.0' -Prefix $prefix
        Write-TextFile (Join-Path $outDir $names.Digest) (('0' * 64) + "  $($names.Archive)`n")
        Assert-FailsWith { Assert-LocalRelease -OutDir $outDir -Version '1.9.0' -Prefix $prefix } @("the plugin archive $($names.Archive) does not match the archive digest $($names.Digest)") -Fix @('Fix what the verifier reports')
    }

    # ======================================================================
    #  The script itself
    # ======================================================================
    Invoke-Case 'script: a malformed version stops it with exit code 1 and the three parts, before anything is built' {
        $outDir = Join-Path (New-TempDir) 'out'
        $result = Invoke-BuildScript -ScriptPath $ScriptPath -TimeoutSeconds 120 -ArgumentList @('-Version', 'one.nine', '-OutDir', $outDir)
        Assert-Equal 1 $result.ExitCode 'exit code'
        Assert-ThreePartFailure $result.Stdout
        Assert-Contains $result.Stdout "'one.nine'" 'output'
        Assert-True (-not (Test-Path -LiteralPath $outDir)) 'nothing may be created for a refused input'
    }

    Invoke-Case 'script: an unknown architecture stops it with exit code 1, naming x86 and x64, before anything is built' {
        $outDir = Join-Path (New-TempDir) 'out'
        $result = Invoke-BuildScript -ScriptPath $ScriptPath -TimeoutSeconds 120 -ArgumentList @('-Version', $SampleVersion, '-Commit', $SampleCommit, '-OutDir', $outDir, '-Architecture', 'arm64')
        Assert-Equal 1 $result.ExitCode 'exit code'
        Assert-ThreePartFailure $result.Stdout
        Assert-Contains $result.Stdout "Unknown architecture 'arm64'" 'output'
        Assert-Contains $result.Stdout 'x86 and x64' 'the allowed values'
        Assert-True (-not (Test-Path -LiteralPath $outDir)) 'nothing may be created for a refused input'
    }

    Invoke-Case 'script: an unknown layout is refused before the source checks run' {
        $result = Invoke-BuildScript -ScriptPath $ScriptPath -TimeoutSeconds 120 `
            -ArgumentList @('-Version', '0.0.0-ci', '-Commit', $SampleCommit, '-Layout', 'nope', '-OutDir', (Join-Path (New-TempDir) 'out'))
        Assert-Equal 1 $result.ExitCode 'exit code'
        Assert-Contains $result.Stdout "Unknown exe layout 'nope'" 'output'
        Assert-NotContains $result.Stdout '[1/' 'no step may have started'
    }

    Invoke-Case 'script: a fault in the sources stops it at step 1 with the three parts and writes no archive' {
        $root = New-RepoFixture -WithBuild -Mutate {
            param($r)
            $manifest = Join-Path $r 'plugin\.claude-plugin\plugin.json'
            [IO.File]::WriteAllText($manifest, ([IO.File]::ReadAllText($manifest).Replace('"version": "0.0.0"', '"version": "1.2.3"')))
        }
        $outDir = Join-Path (New-TempDir) 'out'
        $result = Invoke-BuildScript -ScriptPath (Join-Path $root 'build\Build-PluginArchive.ps1') -TimeoutSeconds 300 -ArgumentList @('-Version', '0.0.0-ci', '-Commit', $SampleCommit, '-OutDir', $outDir)
        Assert-Equal 1 $result.ExitCode "exit code; output was:`n$($result.Stdout)"
        Assert-ThreePartFailure $result.Stdout
        Assert-Contains $result.Stdout '[1/8]' 'step 1 started'
        Assert-Contains $result.Stdout 'plugin.json' 'the fault names the manifest'
        Assert-NotContains $result.Stdout '[2/8]' 'step 2 must not start'
        Assert-Equal 0 @(Get-ChildItem -LiteralPath $outDir -Filter '*.zip' -Recurse -ErrorAction SilentlyContinue).Count 'zip files written'
    }

    # ======================================================================
    #  The real end-to-end run, once per layout. Opt-in: it publishes the real exe.
    # ======================================================================
    foreach ($layoutName in (Get-ArchiveLayoutName)) {
        if ($env:TESTSTAND_MCP_PACKAGE_E2E -ne '1') {
            Write-Host "SKIP  e2e ($layoutName): set TESTSTAND_MCP_PACKAGE_E2E=1 to publish and package the real exe"
            continue
        }
        Invoke-Case "e2e ($layoutName): the script packages the real exe and the verifier accepts the output" {
            $outDir = Join-Path (New-TempDir) 'out'
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $result = Invoke-BuildScript -ScriptPath $ScriptPath -TimeoutSeconds 1200 -ArgumentList @('-Version', '0.0.0-ci', '-Layout', $layoutName, '-OutDir', $outDir)
            Assert-Equal 0 $result.ExitCode "the script failed:`n$($result.Stdout)"
            Assert-True ($result.Stdout -cmatch '\[(\d+)/\1\]') 'the last step ran'
            $zipPath = Join-Path $outDir $ReleaseArchiveName
            $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
            try { $names = @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
            Assert-Equal 0 @($names | Where-Object { $_.Contains('\') }).Count 'entry names with a backslash'
            Assert-Equal (Get-RelativeFileList (Join-Path $outDir 'stage')).Count $names.Count 'entries against staged files'
            $version = Invoke-ChildProcess -FilePath (Get-StagedExePath -StageDir (Join-Path $outDir 'stage')) -ArgumentList @('--version') -TimeoutSeconds 60
            Assert-Contains $version.Stdout '0.0.0-ci' 'staged --version'
            Assert-DigestAccepted $outDir
            Write-Host ("      e2e {0}: {1:N1} MB, {2} entries, {3:N1} s" -f $layoutName, ((Get-Item -LiteralPath $zipPath).Length / 1MB), $names.Count, $clock.Elapsed.TotalSeconds)
            Write-Host ($result.Stdout -split "`n" | Where-Object { $_ -match '^\[\d+/\d+\]' } | ForEach-Object { "      $_" }) -Separator "`n"
        }
    }
} finally {
    Remove-TestTempDirs
}

Complete-TestRun
