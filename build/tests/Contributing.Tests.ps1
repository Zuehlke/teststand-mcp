# ============================================================================
#  Contributing.Tests.ps1 - plain PowerShell asserts that CONTRIBUTING.md still tells the truth about
#  the scripts it quotes. Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\Contributing.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  Three rules (the fixtures below prove each of them can fail):
#    1. a build\...ps1 / .psm1 file named in the text exists;
#    2. a -Parameter on a line that names a script is a parameter of that script's param() block
#       (read with the PowerShell parser), or a powershell.exe host flag;
#    3. a quoted failure message occurs in the scripts. A message is a BLOCKQUOTE line (> ...);
#       <...> marks a part that changes from run to run, and every literal piece between the
#       markers must occur in build\ (tests excluded) or in .github\workflows\.
#    Also: a `gh workflow run <file>.yml -f name=value` line names a workflow file that exists and
#    inputs that file declares.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $RepoRoot 'build'
$WorkflowsDir = Join-Path $RepoRoot '.github\workflows'
$ContributingPath = Join-Path $RepoRoot 'CONTRIBUTING.md'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
Import-Module (Join-Path $BuildDir 'BuildCommon.psm1') -Force

$ScriptRefPattern = '(?<![A-Za-z0-9_.-])build[\\/]((?:[A-Za-z0-9_.-]+[\\/])*[A-Za-z0-9_.-]+\.psm?1)'
# A -Name that is not the tail of a word (v1.9.0-rc1, pre-push) and not a bare dash.
$ParameterPattern = '(?<![\w-])-([A-Za-z][A-Za-z0-9]*)'
$HostFlags = @('NoProfile', 'NonInteractive', 'ExecutionPolicy', 'File')   # powershell.exe's own, not the script's
$WorkflowRunPattern = 'gh workflow run\s+(\S+\.yml)'
$WorkflowFieldPattern = '(?<!\S)-f\s+([A-Za-z_][A-Za-z0-9_]*)='
$Placeholder = '<...>'
$MessageNumberPattern = '\A\d+\.\s+'

# The parameter names of a script's param() block, read with the PowerShell parser (no execution).
function Get-ScriptParameterNames {
    param([string]$Path)
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$null, [ref]$null)
    if ($null -eq $ast.ParamBlock) { return @() }
    return @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
}

# The keys under the `inputs:` block of a workflow file.
function Get-WorkflowInputNames {
    param([string]$Path)
    $names = @()
    $baseIndent = -1
    foreach ($line in ((Read-Utf8Text $Path) -split "\r?\n")) {
        if ($baseIndent -lt 0) {
            $start = [regex]::Match($line, '\A(\s*)inputs:\s*\z')
            if ($start.Success) { $baseIndent = $start.Groups[1].Length }
            continue
        }
        if ($line.Trim().Length -eq 0 -or $line.TrimStart().StartsWith('#')) { continue }
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -le $baseIndent) { break }
        $key = [regex]::Match($line, '\A\s*([A-Za-z_][A-Za-z0-9_]*):\s*\z')
        if ($key.Success -and $indent -eq ($baseIndent + 2)) { $names += $key.Groups[1].Value }
    }
    return $names
}

# Everything a quoted message may come from: the build scripts and modules (not the tests) and the workflows.
function Get-MessageCorpus {
    $testsDir = Join-Path $BuildDir 'tests'
    $files = @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File | Where-Object {
            $_.Extension -in @('.ps1', '.psm1') -and -not $_.FullName.StartsWith($testsDir, [StringComparison]::OrdinalIgnoreCase) })
    $files += @(Get-ChildItem -LiteralPath $WorkflowsDir -File -Filter '*.yml')
    return (($files | ForEach-Object { Read-Utf8Text $_.FullName }) -join "`n")
}

# The problems found in $Text, and what was looked at (so a test can prove the check saw something).
function Get-ContributingFindings {
    param([string]$Text, [string]$Corpus)
    $problems = @()
    $seen = @{ Scripts = 0; Parameters = 0; Messages = 0; Inputs = 0 }

    foreach ($line in ($Text -split "\r?\n")) {
        # Rule 1 and 2: scripts, and the parameters on the same line.
        $known = @()
        $refs = @([regex]::Matches($line, $ScriptRefPattern))
        $missing = $false
        foreach ($ref in $refs) {
            $seen.Scripts++
            $relative = $ref.Groups[1].Value -replace '/', '\'
            $path = Join-Path $BuildDir $relative
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $problems += "script build\$relative does not exist"; $missing = $true; continue }
            if ($relative.EndsWith('.ps1')) { $known += @(Get-ScriptParameterNames $path) }
        }
        if ($refs.Count -gt 0 -and -not $missing) {
            foreach ($match in [regex]::Matches($line, $ParameterPattern)) {
                $seen.Parameters++
                $name = $match.Groups[1].Value
                if (($HostFlags -notcontains $name) -and ($known -notcontains $name)) {
                    $problems += "-$name is not a parameter of the script named on the line: $($line.Trim())"
                }
            }
        }

        # A gh workflow run command: the file and the -f inputs.
        $run = [regex]::Match($line, $WorkflowRunPattern)
        if ($run.Success) {
            $workflowPath = Join-Path $WorkflowsDir $run.Groups[1].Value
            if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
                $problems += "workflow $($run.Groups[1].Value) does not exist"
            } else {
                $inputs = @(Get-WorkflowInputNames $workflowPath)
                foreach ($field in [regex]::Matches($line, $WorkflowFieldPattern)) {
                    $seen.Inputs++
                    if ($inputs -notcontains $field.Groups[1].Value) { $problems += "$($run.Groups[1].Value) has no input '$($field.Groups[1].Value)'" }
                }
            }
        }

        # Rule 3: a quoted message.
        if ($line -match '\A>') {
            $message = ($line -replace '\A>\s?', '').Trim() -replace $MessageNumberPattern, ''
            if ($message.Length -eq 0) { continue }
            $seen.Messages++
            $pieces = @($message.Split(@($Placeholder), [StringSplitOptions]::None) | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
            if ($pieces.Count -eq 0) { $problems += "the quoted message has no literal text: $message" }
            foreach ($piece in $pieces) {
                if ($Corpus.IndexOf($piece, [StringComparison]::Ordinal) -lt 0) { $problems += "quoted message text is in no script or workflow: [$piece]" }
            }
        }
    }
    return [pscustomobject]@{ Problems = $problems; Seen = $seen }
}

try {
    Assert-True (Test-Path -LiteralPath $ContributingPath) "$ContributingPath does not exist"
    $realText = Read-Utf8Text $ContributingPath
    $corpus = Get-MessageCorpus
    $real = Get-ContributingFindings -Text $realText -Corpus $corpus

    Invoke-Case 'CONTRIBUTING: every quoted script, parameter, workflow input and failure message exists' {
        Assert-Equal 0 $real.Problems.Count "problems:`n$($real.Problems -join "`n")"
    }

    # What the check counts is proved on a small fixture with a known number of each, so a check that silently
    # stopped looking cannot pass; on the real text it only has to have looked at all four kinds.
    Invoke-Case 'CONTRIBUTING: the check counts exactly what a small fixture holds (scripts, parameters, workflow inputs, messages)' {
        $fixture = @(
            'powershell.exe -NoProfile -File build\Build-PluginArchive.ps1 -Version 0.0.0-ci -OutDir x'
            'gh workflow run release.yml -f tag=v1.9.2-rehearsal -f dry_run=true'
            '> The version ''<...>'' is not X.Y.Z (or X.Y.Z-suffix).'
            '> The commit ''<...>'' is not a full 40-character SHA.'
        ) -join "`n"
        $found = Get-ContributingFindings -Text $fixture -Corpus $corpus
        Assert-Equal 0 $found.Problems.Count "problems:`n$($found.Problems -join "`n")"
        Assert-Equal 1 $found.Seen.Scripts 'script references'
        Assert-Equal 4 $found.Seen.Parameters 'parameters of the script line: -NoProfile and -File (host flags, counted), -Version and -OutDir (the 0.0.0-ci suffix is no parameter)'
        Assert-Equal 2 $found.Seen.Inputs 'workflow inputs'
        Assert-Equal 2 $found.Seen.Messages 'quoted messages'
    }

    Invoke-Case 'CONTRIBUTING: the real text has scripts, parameters, workflow inputs and quoted messages, and the check saw each kind' {
        foreach ($kind in @('Scripts', 'Parameters', 'Inputs', 'Messages')) { Assert-True ($real.Seen[$kind] -gt 0) "the check saw no $kind in CONTRIBUTING.md" }
    }

    # A doctored copy: the real text plus one bad line. Each case adds exactly one fault, so a check that
    # silently looks at nothing cannot pass.
    function Get-FindingsWith {
        param([string]$ExtraLine)
        return (Get-ContributingFindings -Text ($realText + "`n" + $ExtraLine + "`n") -Corpus $corpus)
    }

    Invoke-Case 'doctored: a script that does not exist is reported' {
        $found = Get-FindingsWith 'Run `build\Invent-Something.ps1` first.'
        Assert-Contains ($found.Problems -join "`n") 'build\Invent-Something.ps1 does not exist' 'the problems'
    }

    Invoke-Case 'doctored: a parameter the script does not have is reported' {
        $found = Get-FindingsWith 'powershell.exe -NoProfile -File build\Build-PluginArchive.ps1 -NoSuchParameter 1'
        Assert-Contains ($found.Problems -join "`n") '-NoSuchParameter is not a parameter' 'the problems'
    }

    Invoke-Case 'doctored: a parameter that belongs to ANOTHER script is reported' {
        $found = Get-FindingsWith 'powershell.exe -NoProfile -File build\Build-PluginArchive.ps1 -Only x'
        Assert-Contains ($found.Problems -join "`n") '-Only is not a parameter' 'the problems'
    }

    Invoke-Case 'doctored: a quoted message that no script prints is reported' {
        $found = Get-FindingsWith '> The archive <...> was eaten by a zebra at <...> o clock.'
        Assert-Contains ($found.Problems -join "`n") 'was eaten by a zebra at' 'the problems'
    }

    Invoke-Case 'doctored: a message that is only placeholders is reported' {
        $found = Get-FindingsWith '> <...> <...>'
        Assert-Contains ($found.Problems -join "`n") 'has no literal text' 'the problems'
    }

    Invoke-Case 'doctored: a workflow input that the workflow does not declare is reported' {
        $found = Get-FindingsWith 'gh workflow run release.yml -f no_such_input=1'
        Assert-Contains ($found.Problems -join "`n") "release.yml has no input 'no_such_input'" 'the problems'
    }

    Invoke-Case 'doctored: a workflow file that does not exist is reported' {
        $found = Get-FindingsWith 'gh workflow run nonexistent.yml -f tag=v1.9.2-rehearsal'
        Assert-Contains ($found.Problems -join "`n") 'workflow nonexistent.yml does not exist' 'the problems'
    }

    Invoke-Case 'doctored: a real line with a version suffix next to a script is not a false alarm' {
        $found = Get-FindingsWith 'powershell.exe -NoProfile -File build\Assert-ReleaseTag.ps1 -Tag v1.9.0-rc1 and a pre-push hook'
        Assert-Equal 0 $found.Problems.Count "problems:`n$($found.Problems -join "`n")"
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
