# ============================================================================
#  WorkflowContract.ps1 - the CONTRACT of the GitHub Actions workflows (.github/workflows/*.yml), as text,
#  because actionlint is not installed here and is never downloaded by the build. It is dot-sourced by
#  Workflows.Tests.ps1 (which runs it against the real workflows) and by WorkflowMutations.Tests.ps1 (which
#  runs it, in the same process, against a copy with ONE deliberate defect and expects it to fail).
#  The workflows only call the build scripts; what they must get right is the wiring, and that is what
#  these cases pin:
#    every workflow  actions pinned to a full commit SHA with a version comment, no ${{ }} inside any
#                    run: block (tags, inputs and refs are user input), permissions stated per job,
#                    a timeout on every job;
#    ci.yml          triggers, read-only permissions, step order, the time budget (every step that can
#                    hang is capped, the job outlives the sum), publishes nothing;
#    release.yml     triggers, the per-ref concurrency group, the tag gate first, publish by release_id
#                    and only after the verify step, the final public fetch with the verified digest, the
#                    own-downloads artifact, the dry run and its cleanup by the id it created.
#  What the scripts do (delete a leftover draft, decide make_latest, delete only the own draft) is tested
#  with a fake gh in ReleaseDraft.Tests.ps1; here only that the workflow calls them the way they expect.
#  The asset names and their common prefix come from build\ReleaseAssets.psm1, never from a literal.
#  The first real run of these workflows is the fork rehearsal (see the plan): this file cannot prove
#  that GitHub accepts them, only that they keep the shape that was designed.
#  Invoke-WorkflowContract reports every case through -Case (default: Invoke-Case of TestHarness.ps1), a
#  scriptblock `param([string]$Name, [scriptblock]$Body)` that runs the body and records a throw as a failure.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'BuildCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ReleaseAssets.psm1') -Force

function Invoke-WorkflowContract {
    param([Parameter(Mandatory)][string]$WorkflowDir, [Parameter(Mandatory)][string]$RepoRoot, [scriptblock]$Case)

    $report = if ($null -ne $Case) { $Case } else { ${function:Invoke-Case} }
    function Invoke-Case {
        param([string]$Name, [scriptblock]$Body)
        & $report $Name $Body
    }

    $AssetGlob = "/$ReleaseAssetPrefix*"   # forward slash: a backslash is a glob escape
    $FullShaPattern = '[0-9a-f]{40}'
    $PinnedUsesPattern = '\Auses:\s+[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@' + $FullShaPattern + '\s+#\s+v[0-9]+(\.[0-9]+)*\s*\z'
    $ExpressionOpen = '${{'

# --- A deliberately small reader for the YAML these workflows use --------------------------------------
# Indentation convention of the workflows: top-level keys at column 0, job names at 2, job keys at 4,
# step items ("- name:") at 6. The reader relies on it, and a case below pins it.
function Read-WorkflowText {
    param([string]$Name)
    $path = Join-Path $WorkflowDir $Name
    Assert-True (Test-Path -LiteralPath $path) "$path does not exist"
    return ([IO.File]::ReadAllText($path)).Replace("`r`n", "`n")
}

function Get-WorkflowFileNames {
    return @(Get-ChildItem -LiteralPath $WorkflowDir -File -Filter '*.yml' | Sort-Object Name | ForEach-Object { $_.Name })
}

# The text without its comment-only lines: what a pattern about the workflow's behaviour must be matched against.
function Remove-CommentLines {
    param([string]$Text)
    return (@($Text -split "`n" | Where-Object { $_ -cnotmatch '\A\s*#' }) -join "`n")
}

# The text of a top-level block: from "<key>:" up to the next top-level key.
function Get-TopLevelBlock {
    param([string]$Text, [string]$Key)
    $lines = $Text -split "`n"
    $collected = @()
    $inside = $false
    foreach ($line in $lines) {
        if ($line -cmatch '\A[A-Za-z_][A-Za-z0-9_-]*:') {
            if ($inside) { break }
            if ($line -cmatch ('\A' + [regex]::Escape($Key) + ':')) { $inside = $true }
        }
        if ($inside) { $collected += $line }
    }
    return ($collected -join "`n")
}

function Get-JobNames {
    param([string]$Text)
    $jobs = Get-TopLevelBlock $Text 'jobs'
    return @([regex]::Matches($jobs, '(?m)^  ([A-Za-z0-9_-]+):\s*$') | ForEach-Object { $_.Groups[1].Value })
}

function Get-JobText {
    param([string]$Text, [string]$Job)
    $jobs = (Get-TopLevelBlock $Text 'jobs') -split "`n"
    $collected = @()
    $inside = $false
    foreach ($line in $jobs) {
        if ($line -cmatch '\A  ([A-Za-z0-9_-]+):\s*\z') {
            if ($inside) { break }
            if ($Matches[1] -ceq $Job) { $inside = $true }
        }
        if ($inside) { $collected += $line }
    }
    Assert-True ($collected.Count -gt 0) "no job named $Job"
    return ($collected -join "`n")
}

# The steps of a job as { Name; Text }, in order. A step starts at "      - " (six spaces).
function Get-Steps {
    param([string]$JobText)
    $steps = @()
    $current = $null
    foreach ($line in ((Remove-CommentLines $JobText) -split "`n")) {
        if ($line -cmatch '\A      - ') {
            if ($null -ne $current) { $steps += $current }
            $current = @{ Name = ''; Lines = @($line) }
        } elseif ($null -ne $current) {
            $current.Lines += $line
        }
    }
    if ($null -ne $current) { $steps += $current }
    return @($steps | ForEach-Object {
        $text = $_.Lines -join "`n"
        $name = [regex]::Match($text, '(?m)^\s+(?:- )?name:\s*(.+?)\s*$').Groups[1].Value
        [pscustomobject]@{ Name = $name; Text = $text }
    })
}

# Every run: value (inline or block scalar) of a text, as one string each.
function Get-RunValues {
    param([string]$Text)
    $values = @()
    $lines = $Text -split "`n"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $match = [regex]::Match($lines[$index], '\A(\s*)(?:- )?run:\s*(.*)\z')
        if (-not $match.Success) { continue }
        $indent = $match.Groups[1].Value.Length
        $value = $match.Groups[2].Value
        if ($value -cmatch '\A[|>][+-]?\s*\z') {
            $block = @()
            for ($next = $index + 1; $next -lt $lines.Count; $next++) {
                $candidate = $lines[$next]
                if ($candidate.Trim().Length -gt 0 -and ($candidate.Length - $candidate.TrimStart().Length) -le $indent) { break }
                $block += $candidate
            }
            $values += ($block -join "`n")
        } else {
            $values += $value
        }
    }
    return $values
}

# The value of a one-line key of a step ("if", "id", "timeout-minutes", ...), or $null.
function Get-StepField {
    param($Step, [string]$Field)
    $match = [regex]::Match($Step.Text, '(?m)^\s+(?:- )?' + [regex]::Escape($Field) + ':\s*(.+?)\s*$')
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}

function Get-StepIndex {
    param($Steps, [string]$Pattern, [string]$What)
    $hits = @(for ($index = 0; $index -lt $Steps.Count; $index++) { if ($Steps[$index].Text -cmatch $Pattern) { $index } })
    Assert-True ($hits.Count -eq 1) "expected exactly one step for '$What' (pattern $Pattern) but found $($hits.Count)"
    return $hits[0]
}

function Assert-Before {
    param($Steps, [string]$FirstPattern, [string]$SecondPattern, [string]$What)
    $first = Get-StepIndex $Steps $FirstPattern $What
    $second = Get-StepIndex $Steps $SecondPattern $What
    Assert-True ($first -lt $second) "$What : the step at position $first must come before the step at position $second"
}

$AllWorkflows = @(Get-WorkflowFileNames)
$ExpectedWorkflows = @('ci.yml', 'pages.yml', 'release.yml')

    # ============================================================================================
    #  Every workflow
    # ============================================================================================
    Invoke-Case 'all workflows: the three known workflows exist and nothing else was added unseen' {
        Assert-Equal ($ExpectedWorkflows -join ',') ($AllWorkflows -join ',') 'workflow files'
    }

    foreach ($file in $AllWorkflows) {
        $text = Read-WorkflowText $file

        Invoke-Case "$file : every action is pinned to a full commit SHA with a version comment" {
            $uses = @($text -split "`n" | Where-Object { $_ -cmatch '\A\s*(- )?uses:' })
            Assert-True ($uses.Count -gt 0) 'no action used at all'
            foreach ($line in $uses) {
                $bare = $line.Trim() -creplace '\A- ', ''
                Assert-True ($bare -cmatch $PinnedUsesPattern) "not pinned to a SHA with a '# vX.Y.Z' comment: $bare"
            }
        }

        Invoke-Case "$file : no run: block contains an expression (tags, inputs and refs are user input, so values go through env)" {
            foreach ($run in (Get-RunValues $text)) {
                Assert-NotContains $run $ExpressionOpen 'a run: block'
            }
        }

        Invoke-Case "$file : the workflow states its permissions at the top and every job states its own" {
            Assert-True ($text -cmatch '(?m)^permissions:') 'no top-level permissions'
            foreach ($job in (Get-JobNames $text)) {
                Assert-True ((Get-JobText $text $job) -cmatch '(?m)^    permissions:') "job $job has no permissions of its own"
            }
        }

        Invoke-Case "$file : every job has a timeout" {
            foreach ($job in (Get-JobNames $text)) {
                Assert-True ((Get-JobText $text $job) -cmatch '(?m)^    timeout-minutes: [1-9][0-9]*\s*$') "job $job has no timeout-minutes"
            }
        }

        Invoke-Case "$file : never triggered by pull_request_target and never inherits secrets" {
            Assert-NotContains $text 'pull_request_target' 'workflow'
            Assert-NotContains $text 'secrets: inherit' 'workflow'
        }

        Invoke-Case "$file : follows the indentation convention the reader of this test relies on" {
            foreach ($job in (Get-JobNames $text)) {
                $jobText = Get-JobText $text $job
                Assert-True (@(Get-Steps $jobText).Count -gt 0) "job $job has no steps at the expected indentation"
            }
        }
    }

    # ============================================================================================
    #  ci.yml
    # ============================================================================================
    $ci = Read-WorkflowText 'ci.yml'
    $ciJob = Get-JobText $ci 'check'
    $ciSteps = @(Get-Steps $ciJob)
    $ciCode = Remove-CommentLines $ci

    Invoke-Case 'ci.yml: runs for pull requests to main and pushes to main, and for nothing else' {
        $on = Get-TopLevelBlock $ci 'on'
        Assert-True ($on -cmatch '(?m)^  pull_request:') 'no pull_request trigger'
        Assert-True ($on -cmatch '(?m)^  push:') 'no push trigger'
        Assert-Equal 2 @($on -split "`n" | Where-Object { $_ -cmatch '\A  [a-z_]+:' }).Count 'the number of triggers'
        Assert-Equal 2 @([regex]::Matches($on, 'branches:\s*\[main\]')).Count 'both triggers are limited to main'
    }

    Invoke-Case 'ci.yml: read-only token, no write permission anywhere, nothing that publishes' {
        Assert-True ((Get-TopLevelBlock $ci 'permissions') -cmatch 'contents: read') 'top-level permissions must be contents: read'
        Assert-True ($ciJob -cmatch '(?m)^    permissions:\s*\n\s+contents: read') 'the job must state contents: read'
        Assert-NotContains $ciCode ': write' 'ci.yml'
        foreach ($forbidden in @('gh release', 'action-gh-release', 'releases/', 'gh api')) { Assert-NotContains $ciCode $forbidden 'ci.yml' }
    }

    Invoke-Case 'ci.yml: runs on windows-latest' {
        Assert-True ($ciJob -cmatch '(?m)^    runs-on: windows-latest\s*$') 'the job must run on windows-latest'
    }

    Invoke-Case 'ci.yml: the steps run in the designed order, each exactly once' {
        $order = @(
            @{ Pattern = 'actions/checkout@'; What = 'checkout' }
            @{ Pattern = 'actions/setup-dotnet@'; What = 'setup-dotnet' }
            @{ Pattern = 'actions/setup-python@'; What = 'setup-python' }
            @{ Pattern = 'dotnet test'; What = 'engine-free unit tests' }
            @{ Pattern = 'Invoke-ScriptTests\.ps1'; What = 'script tests' }
            @{ Pattern = 'unittest discover -s \.github/stats'; What = 'stats tests' }
            @{ Pattern = 'Build-PluginArchive\.ps1'; What = 'packaging' }
            @{ Pattern = 'actions/upload-artifact@'; What = 'upload' }
        )
        for ($index = 1; $index -lt $order.Count; $index++) {
            Assert-Before $ciSteps $order[$index - 1].Pattern $order[$index].Pattern "$($order[$index - 1].What) before $($order[$index].What)"
        }
    }

    Invoke-Case 'ci.yml: .NET 8 and Python 3.12 are installed' {
        $dotnet = $ciSteps[(Get-StepIndex $ciSteps 'actions/setup-dotnet@' 'setup-dotnet')].Text
        Assert-True ($dotnet -cmatch "dotnet-version:\s*'?8\.0\.x'?") 'setup-dotnet must ask for 8.0.x'
        $python = $ciSteps[(Get-StepIndex $ciSteps 'actions/setup-python@' 'setup-python')].Text
        Assert-True ($python -cmatch "python-version:\s*'3\.12'") 'setup-python must ask for 3.12'
    }

    Invoke-Case 'ci.yml: the unit tests run only the engine-free category, from the real test project, without needing an x86 runtime' {
        $step = $ciSteps[(Get-StepIndex $ciSteps 'dotnet test' 'unit tests')].Text
        Assert-Contains $step 'Test/TestExecution/TestStandMCP.IntegrationTests.csproj' 'the test project path'
        Assert-True (Test-Path -LiteralPath (Join-Path $RepoRoot 'Test\TestExecution\TestStandMCP.IntegrationTests.csproj')) 'the test project exists at that path'
        Assert-True ($step -cmatch '--filter\s+"?Category=EngineFree"?') 'the filter must be Category=EngineFree'
        Assert-Contains $step '-p:PlatformTarget=AnyCPU' 'the x86 test host would need an x86 runtime the runner does not have'
    }

    Invoke-Case 'ci.yml: the script tests run under both hosts and a missing pwsh fails the run' {
        $step = $ciSteps[(Get-StepIndex $ciSteps 'Invoke-ScriptTests\.ps1' 'script tests')].Text
        Assert-True ($step -cmatch "TESTSTAND_MCP_REQUIRE_PWSH:\s*'?1'?") 'the step must set TESTSTAND_MCP_REQUIRE_PWSH=1'
    }

    Invoke-Case 'ci.yml: a hanging script is stopped after 600 s, no script starts in the last 600 s of a 1500 s budget, and the step cap outlives that budget so the FAIL summary prints' {
        $stepIndex = Get-StepIndex $ciSteps 'Invoke-ScriptTests\.ps1' 'script tests'
        $step = $ciSteps[$stepIndex]
        $call = [regex]::Match($step.Text, 'Invoke-ScriptTests\.ps1 -TimeoutSeconds ([0-9]+) -TotalSeconds ([0-9]+)')
        Assert-True $call.Success 'the script tests must pass -TimeoutSeconds and -TotalSeconds'
        $perScript = [int]$call.Groups[1].Value
        $total = [int]$call.Groups[2].Value
        Assert-Equal 600 $perScript 'the per-script timeout'
        Assert-True ($total -ge $perScript) 'the budget must allow at least one script'
        $stepMinutes = [int](Get-StepField $step 'timeout-minutes')
        Assert-True ($stepMinutes * 60 -gt $total) "the step cap ($stepMinutes min) must outlive the runner's own budget ($total s), or the summary is lost to a bare cancellation"
    }

    Invoke-Case 'ci.yml: every step that can hang has its own timeout, and the job outlives the SUM of them' {
        $capped = @($ciSteps | Where-Object { $_.Text -cmatch '(?m)^\s+run:' })
        Assert-True ($capped.Count -gt 0) "no run: step was found in ci.yml, so no timeout was checked"
        $sum = 0
        foreach ($step in $capped) {
            $minutes = Get-StepField $step 'timeout-minutes'
            Assert-True ($null -ne $minutes) "step '$($step.Name)' runs a command and has no timeout-minutes"
            $sum += [int]$minutes
        }
        $jobMinutes = [int][regex]::Match($ciJob, '(?m)^    timeout-minutes: ([0-9]+)\s*$').Groups[1].Value
        $marginMinutes = 5   # checkout, setup and upload have no cap of their own
        Assert-True ($jobMinutes -ge ($sum + $marginMinutes)) "the job allows $jobMinutes min but its capped steps add up to $sum min (plus $marginMinutes margin)"
        $dotnet = $ciSteps[(Get-StepIndex $ciSteps 'dotnet test' 'unit tests')]
        Assert-Equal '15' (Get-StepField $dotnet 'timeout-minutes') 'the unit test step cap'
    }

    Invoke-Case 'ci.yml: packaging uses version 0.0.0-ci into its own clean output folder and the artifact holds the release assets' {
        $pack = $ciSteps[(Get-StepIndex $ciSteps 'Build-PluginArchive\.ps1' 'packaging')].Text
        Assert-Contains $pack '-Version' 'the version argument'
        Assert-Contains $pack '-OutDir' 'a clean output folder'
        $upload = $ciSteps[(Get-StepIndex $ciSteps 'actions/upload-artifact@' 'upload')].Text
        Assert-Contains $upload $AssetGlob 'the assets are matched by the common prefix the release contract defines'
        Assert-True ($upload -cmatch 'if-no-files-found:\s*error') 'an empty upload must fail the check'
        Assert-NotContains $upload '/stage' 'the staging tree is not an asset'
    }

    Invoke-Case 'ci.yml: shared values live in workflow-level env, not repeated in steps' {
        $envBlock = Get-TopLevelBlock $ci 'env'
        Assert-True ($envBlock.Length -gt 0) 'the workflow has no env block'
        Assert-Contains $envBlock 'PACKAGE_VERSION' 'the CI version'
        Assert-Contains $envBlock 'PACKAGE_OUT_DIR' 'the output folder'
        Assert-Equal 1 @([regex]::Matches($ciCode, '0\.0\.0-ci')).Count 'the CI version is written once'
    }

    # ============================================================================================
    #  release.yml (two jobs: package builds with a read-only token, release publishes)
    # ============================================================================================
    $release = Read-WorkflowText 'release.yml'
    $releaseCode = Remove-CommentLines $release
    $packageJob = Get-JobText $release 'package'
    $releaseJob = Get-JobText $release 'release'
    $packageSteps = @(Get-Steps $packageJob)
    $releaseSteps = @(Get-Steps $releaseJob)
    $releaseEnv = Get-TopLevelBlock $releaseCode 'env'
    $packageCode = Remove-CommentLines $packageJob
    $releaseJobCode = Remove-CommentLines $releaseJob

    function Get-ReleaseStep {
        param([string]$Pattern, [string]$What)
        return $releaseSteps[(Get-StepIndex $releaseSteps $Pattern $What)]
    }

    function Get-PackageStep {
        param([string]$Pattern, [string]$What)
        return $packageSteps[(Get-StepIndex $packageSteps $Pattern $What)]
    }

    # The granted permissions of a job, as "name: level" strings.
    function Get-GrantedPermissions {
        param([string]$JobText)
        $block = [regex]::Match($JobText, '(?m)^    permissions:\s*\n((?:      .+\n?)+)').Groups[1].Value
        return @($block -split "`n" | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { ($_ -creplace '#.*\z', '').Trim() })
    }

    Invoke-Case 'release.yml: a pushed tag of any name starts it (so a malformed tag is answered), and a manual run takes the dry-run inputs' {
        $on = Get-TopLevelBlock $releaseCode 'on'
        Assert-True ($on -cmatch "(?m)^  push:\s*\n    tags:\s*\[\s*'\*\*'\s*\]") "push must trigger on tags: ['**'] (a narrower filter would never answer V1.9.0)"
        Assert-NotContains $on 'branches' 'the push trigger'
        Assert-Equal 2 @($on -split "`n" | Where-Object { $_ -cmatch '\A  [a-z_]+:' }).Count 'the number of triggers'
        Assert-True ($on -cmatch '(?m)^  workflow_dispatch:') 'no workflow_dispatch trigger'
        foreach ($input in @('tag', 'dry_run', 'inject_fault')) { Assert-True ($on -cmatch "(?m)^      ${input}:") "workflow_dispatch has no input $input" }
        Assert-True ($on -cmatch '(?s)dry_run:.*?type: boolean') 'dry_run must be a boolean'
        Assert-True ($on -cmatch '(?s)inject_fault:.*?type: choice') 'inject_fault must be a choice'
        Assert-True ($on -cmatch '(?s)inject_fault:.*?options:\s*\n\s+- none\s*\n\s+- corrupt-zip\s*\n\s+- corrupt-zip-x64\s*\n') 'inject_fault offers none, corrupt-zip and corrupt-zip-x64'
        Assert-True ($on -cmatch '(?s)      tag:.*?required: true') 'the rehearsal tag is required'
    }

    Invoke-Case 'release.yml: every run queues per ref (never in one shared queue, where a third run would cancel the pending one), and a running release is never cancelled' {
        $concurrency = Get-TopLevelBlock $releaseCode 'concurrency'
        Assert-True ($concurrency -cmatch '(?m)^  group:\s*release-\$\{\{ github\.ref \}\}\s*$') 'the group must be exactly release-<ref>, for a tag push and a dry run alike'
        Assert-NotContains $concurrency 'event_name' 'the concurrency group (a shared queue for tag pushes would drop a pending tag when a third arrives)'
        Assert-True ($concurrency -cmatch 'cancel-in-progress:\s*false') 'a release in progress must not be cancelled'
    }

    Invoke-Case 'release.yml: nothing by default; only the release job may write, the package job only reads' {
        Assert-True ((Get-TopLevelBlock $releaseCode 'permissions') -cmatch '(?m)^permissions:\s*\{\}\s*$') 'top-level permissions must be {}'
        Assert-Equal 'contents: write' ((Get-GrantedPermissions $releaseJob) -join '|') 'the permissions of the release job'
        Assert-Equal 'contents: read' ((Get-GrantedPermissions $packageJob) -join '|') 'the permissions of the package job'
        Assert-Equal 1 @([regex]::Matches($releaseCode, 'contents: write')).Count 'contents: write appears once, on the release job'
    }

    Invoke-Case 'release.yml: the two jobs are split by what they may do: build code only in package, the write token only in release' {
        Assert-True ($releaseJob -cmatch '(?m)^    needs: package\s*$') 'release must need package, so a failed package creates nothing'
        Assert-True ($packageJob -cnotmatch '(?m)^    needs:') 'package needs nothing'
        foreach ($job in @($packageJob, $releaseJob)) { Assert-True ($job -cmatch '(?m)^    runs-on: windows-latest\s*$') 'both jobs run on windows-latest' }
        foreach ($buildWord in @('dotnet', 'Build-PluginArchive', 'setup-dotnet', 'nuget', 'msbuild')) {
            Assert-NotContains $releaseJobCode $buildWord 'the release job (it holds the write token and runs no build)'
        }
        foreach ($releaseWord in @('gh api', 'gh release', 'GH_TOKEN', 'github.token', 'secrets.', 'contents: write')) {
            Assert-NotContains $packageCode $releaseWord 'the package job (it runs build code and gets no token)'
        }
        $packageScripts = @([regex]::Matches($packageCode, '\\build\\([A-Za-z-]+)\.ps1') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        Assert-True ($packageScripts.Count -gt 0) 'the package job calls no build script, so none was checked'
        foreach ($script in $packageScripts) {
            Assert-True (@('Assert-ReleaseTag', 'Build-PluginArchive') -ccontains $script) "the package job (no token) calls build\$script.ps1, which is not a gate or a build script"
        }
        Assert-Contains $packageCode 'Build-PluginArchive.ps1' 'the package job builds'
        foreach ($output in @('tag', 'version', 'rehearsal')) {
            Assert-True ($packageJob -cmatch "(?m)^      ${output}:\s*\`$\{\{ steps\.gate\.outputs\.${output} \}\}") "the package job passes the gate output $output on"
        }
        Assert-True ($releaseJob -cmatch '(?m)^      RELEASE_TAG:\s*\$\{\{ needs\.package\.outputs\.tag \}\}') 'the release job takes the tag the gate validated'
    }

    Invoke-Case 'release.yml: one tag packages every architecture and the release job checks the assets of every plugin' {
        $package = Get-PackageStep '(?m)^\s+id: package\s*$' 'package'
        Assert-Contains $package.Text 'foreach ($architecture in (Get-PluginArchitectures))' 'every architecture of the identity table is built'
        Assert-Contains $package.Text '-Architecture $architecture' 'each build names its architecture'
        Assert-Contains $package.Text 'Add-GitHubOutput -Name ''asset_prefixes'' -Value ((Get-PluginAssetPrefixes) -join '','')' 'the plugins come from the identity table'
        Assert-True ($packageJob -cmatch '(?m)^      asset_prefixes:\s*\$\{\{ steps\.package\.outputs\.asset_prefixes \}\}') 'the package job passes the plugins on'
        Assert-True ($releaseJob -cmatch '(?m)^      ASSET_PREFIXES:\s*\$\{\{ needs\.package\.outputs\.asset_prefixes \}\}') 'the release job takes them'
        foreach ($id in @('create', 'verify', 'latest')) {
            Assert-Contains (Get-ReleaseStep "(?m)^\s+id: $id\s*$" $id).Text '-AssetPrefixes $env:ASSET_PREFIXES' "the $id step names every plugin"
        }
    }

    Invoke-Case 'release.yml: the assets travel as a short-lived artifact and the release job starts from a fresh checkout of the tagged commit' {
        $upload = Get-PackageStep 'actions/upload-artifact@' 'upload release-assets'
        Assert-True ($upload.Text -cmatch '(?m)^\s+name: release-assets\s*$') 'the artifact is called release-assets'
        Assert-True ($upload.Text -cmatch 'if-no-files-found:\s*error') 'an empty upload must fail the package job'
        Assert-True ($upload.Text -cmatch '(?m)^\s+retention-days:\s*[1-3]\s*$') 'a short retention'
        Assert-Contains $upload.Text $AssetGlob 'the release assets, by the common prefix the release contract defines'
        $download = Get-ReleaseStep 'actions/download-artifact@' 'download release-assets'
        Assert-True ($download.Text -cmatch '(?m)^\s+name: release-assets\s*$') 'the release job downloads that artifact'
        $checkout = Get-ReleaseStep 'actions/checkout@' 'release checkout'
        Assert-True ($checkout.Text -cmatch 'ref:\s*\$\{\{ github\.sha \}\}') 'a fresh checkout at github.sha'
        Assert-True ($checkout.Text -cmatch 'persist-credentials:\s*false') 'the checkout leaves no credential behind'
        Assert-Before $releaseSteps 'actions/checkout@' 'actions/download-artifact@' 'checkout before download'
    }

    Invoke-Case 'release.yml: the package job runs in the designed order and the release job in its own, each step exactly once' {
        $packageOrder = @(
            @{ Pattern = 'actions/checkout@'; What = 'checkout' }
            @{ Pattern = '::error::A release is cut by pushing a tag'; What = 'refuse a manual real release' }
            @{ Pattern = 'Assert-ReleaseTag\.ps1'; What = 'tag gate' }
            @{ Pattern = 'A dry run needs a rehearsal tag'; What = 'require a rehearsal tag' }
            @{ Pattern = 'actions/setup-dotnet@'; What = 'setup-dotnet' }
            @{ Pattern = 'Build-PluginArchive\.ps1'; What = 'packaging' }
            @{ Pattern = 'actions/upload-artifact@'; What = 'upload the assets' }
        )
        for ($index = 1; $index -lt $packageOrder.Count; $index++) {
            Assert-Before $packageSteps $packageOrder[$index - 1].Pattern $packageOrder[$index].Pattern "$($packageOrder[$index - 1].What) before $($packageOrder[$index].What)"
        }
        Assert-Equal ($packageSteps.Count - 1) (Get-StepIndex $packageSteps 'actions/upload-artifact@' 'upload') 'the upload is the last step of the package job'
        $releaseOrder = @(
            @{ Pattern = 'actions/checkout@'; What = 'checkout' }
            @{ Pattern = 'actions/download-artifact@'; What = 'download the assets' }
            @{ Pattern = 'New-ReleaseDraft\.ps1'; What = 'create the draft' }
            @{ Pattern = '--clobber'; What = 'inject the fault' }
            @{ Pattern = '(?m)^\s+id: verify\s*$'; What = 'verify the draft' }
            @{ Pattern = '(?m)^\s+id: recheck\s*$'; What = 'recheck the assets before publishing' }
            @{ Pattern = '(?m)^\s+id: publish\s*$'; What = 'publish' }
            @{ Pattern = '(?m)^\s+id: recheck_after\s*$'; What = 'recheck the assets after publishing' }
            @{ Pattern = '(?m)^\s+id: latest\s*$'; What = 'fetch releases/latest' }
            @{ Pattern = 'Remove-RehearsalRelease\.ps1'; What = 'clean up the rehearsal' }
            @{ Pattern = 'actions/upload-artifact@'; What = 'upload own-downloads' }
        )
        for ($index = 1; $index -lt $releaseOrder.Count; $index++) {
            Assert-Before $releaseSteps $releaseOrder[$index - 1].Pattern $releaseOrder[$index].Pattern "$($releaseOrder[$index - 1].What) before $($releaseOrder[$index].What)"
        }
        Assert-Equal ($releaseSteps.Count - 1) (Get-StepIndex $releaseSteps 'actions/upload-artifact@' 'upload') 'the upload of own-downloads is the last step of the release job'
    }

    Invoke-Case 'release.yml: the tag is user input and reaches the gate only through env, the dry run only through a manual run' {
        Assert-True ($releaseEnv -cmatch "TAG:\s*\$\{\{ github\.event_name == 'workflow_dispatch' && inputs\.tag \|\| github\.ref_name \}\}") 'TAG must be the dispatch input or the pushed ref name'
        Assert-True ($releaseEnv -cmatch "DRY_RUN:\s*\$\{\{ github\.event_name == 'workflow_dispatch' && inputs\.dry_run \}\}") 'DRY_RUN must be true only for a manual run that asked for it'
        Assert-True ($releaseEnv -cmatch "INJECT_FAULT:\s*\$\{\{ github\.event_name == 'workflow_dispatch' && inputs\.inject_fault \|\| 'none' \}\}") 'INJECT_FAULT must be none for a push'
        $gate = (Get-PackageStep 'Assert-ReleaseTag\.ps1' 'tag gate').Text
        Assert-Contains $gate '-Tag $env:TAG' 'the gate gets the tag from the environment'
        Assert-Contains $gate '$rehearsal = ($env:DRY_RUN -eq ''true'')' 'a rehearsal is whatever DRY_RUN says, never the shape of the tag'
        Assert-Contains $gate '-Rehearsal:$rehearsal' 'the gate is told about the rehearsal'
    }

    Invoke-Case 'release.yml: a manual run that is not a dry run is refused before anything else, and a dry run needs a rehearsal tag' {
        $refuse = Get-PackageStep '::error::A release is cut by pushing a tag' 'refusal'
        Assert-True ((Get-StepField $refuse 'if') -cmatch "env\.EVENT_NAME == 'workflow_dispatch' && env\.DRY_RUN != 'true'") 'the refusal applies to a manual run without dry_run'
        $rehearsal = Get-PackageStep 'A dry run needs a rehearsal tag' 'rehearsal tag requirement'
        $condition = Get-StepField $rehearsal 'if'
        Assert-Contains $condition "env.DRY_RUN == 'true'" 'only for a dry run'
        Assert-Contains $condition "steps.gate.outputs.rehearsal != 'true'" 'fails when the gate did not see a rehearsal tag'
        Assert-Contains $releaseEnv 'EVENT_NAME:' 'the event name reaches the steps through env'
    }

    Invoke-Case 'release.yml: the draft is created by the script (leftover drafts cleared first, the created id reported), nothing publishes before the verification' {
        $create = Get-ReleaseStep '(?m)^\s+id: create\s*$' 'create the draft'
        Assert-Contains $create.Text 'New-ReleaseDraft.ps1' 'the draft is created by the tested script, not by inline gh calls'
        Assert-Contains $create.Text '-Repo $env:REPO' 'the repository of the run'
        Assert-Contains $create.Text '-Tag $env:RELEASE_TAG' 'the tag the gate validated'
        Assert-Contains $create.Text '-AssetDir $env:OUT_DIR' 'the folder the artifact was downloaded to'
        Assert-Contains $create.Text '-DryRun:$dryRun' 'a dry run creates a rehearsal draft at the commit'
        Assert-Contains $create.Text '$dryRun = ($env:DRY_RUN -eq ''true'')' 'a dry run is whatever DRY_RUN says, never the shape of the tag'
        Assert-True ($create.Text -cmatch 'TARGET_SHA:\s*\$\{\{ github\.sha \}\}') 'the commit a rehearsal draft points at'
        Assert-NotContains $create.Text 'gh release create' 'the step (creation, the deletion of a leftover draft and the id lookup live in the script)'
        Assert-NotContains $releaseCode 'action-gh-release' 'release.yml (gh is used, no third-party release action)'
        Assert-NotContains $releaseCode 'gh release edit' 'release.yml (publishing is by release id, never by tag name)'
        Assert-NotContains $releaseCode '--draft=false' 'release.yml'
        $publishIndex = Get-StepIndex $releaseSteps '(?m)^\s+id: publish\s*$' 'publish'
        for ($index = 0; $index -lt $publishIndex; $index++) {
            Assert-True ($releaseSteps[$index].Text -cnotmatch 'draft=false|make_latest|Publish-Release') "step $($releaseSteps[$index].Name) publishes before the verification"
        }
    }

    Invoke-Case 'release.yml: the draft is verified with the token as a user would download it, and every download is recorded' {
        $verify = (Get-ReleaseStep '(?m)^\s+id: verify\s*$' 'verify').Text
        Assert-Contains $verify 'Assert-PublishedRelease.ps1' 'the verifier script'
        Assert-Contains $verify '-Repo $env:REPO' 'the repository of the run, so a fork verifies itself'
        Assert-Contains $verify '-Tag $env:RELEASE_TAG' 'the tag the gate validated'
        Assert-Contains $verify '-Token $env:GH_TOKEN' 'a draft is readable only with the token'
        Assert-Contains $verify '-OwnDownloadsPath $env:OWN_DOWNLOADS_FILE' 'every verify attempt is recorded'
        Assert-Contains $verify '-Rehearsal:$rehearsal' 'a dry run verifies the rehearsal tag'
        Assert-True ($verify -cmatch 'GH_TOKEN:\s*\$\{\{ github\.token \}\}') 'the token comes from github.token, set on this step only'
        Assert-True ($releaseEnv -cnotmatch 'github\.token|secrets\.') 'no token in workflow-level env'
    }

    Invoke-Case 'release.yml: the asset ids are checked again just before and just after the publish call, by the script and never in a dry run' {
        $before = Get-ReleaseStep '(?m)^\s+id: recheck\s*$' 'recheck before'
        $after = Get-ReleaseStep '(?m)^\s+id: recheck_after\s*$' 'recheck after'
        foreach ($check in @(@{ Step = $before; Stage = 'BeforePublish' }, @{ Step = $after; Stage = 'AfterPublish' })) {
            $text = $check.Step.Text
            Assert-Contains $text 'Assert-ReleaseAssetSet.ps1' 'the comparison lives in a script, not in the YAML'
            Assert-Contains $text "-Stage $($check.Stage)" 'the stage'
            Assert-Contains $text '-Repo $env:REPO' 'the script reads the release itself, by the id (and refuses an id that is not a number)'
            Assert-NotContains $text 'gh api' 'the step (the id guard and the read are in the script)'
            Assert-Contains $text '-ExpectedAssetIds $env:ASSET_IDS' 'the verified asset ids are the reference'
            Assert-Contains $text '-ReleaseId $env:RELEASE_ID' 'the id the response must carry'
            Assert-True ($text -cmatch 'ASSET_IDS:\s*\$\{\{ steps\.verify\.outputs\.asset_ids \}\}') 'the reference is the verifier output'
            Assert-True ($text -cmatch 'RELEASE_ID:\s*\$\{\{ steps\.verify\.outputs\.release_id \}\}') 'the id is the verifier output'
            Assert-Contains (Get-StepField $check.Step 'if') "env.DRY_RUN != 'true'" 'never in a dry run'
        }
        Assert-Contains (Get-StepField $before 'if') "steps.verify.outcome == 'success'" 'the first check follows a successful verify'
        Assert-Contains (Get-StepField $after 'if') "steps.publish.outcome == 'success'" 'the second check follows the publish call'
    }

    Invoke-Case 'release.yml: publishing is by the verifier release id through the script, only after a successful verify and recheck, and never in a dry run' {
        $publish = Get-ReleaseStep '(?m)^\s+id: publish\s*$' 'publish'
        $condition = Get-StepField $publish 'if'
        Assert-Contains $condition "steps.verify.outcome == 'success'" 'only after the verify step succeeded'
        Assert-Contains $condition "steps.recheck.outcome == 'success'" 'only after the asset ids were checked again'
        Assert-Contains $condition "env.DRY_RUN != 'true'" 'never in a dry run'
        Assert-True ($publish.Text -cmatch 'RELEASE_ID:\s*\$\{\{ steps\.verify\.outputs\.release_id \}\}') 'the id is the verifier output'
        Assert-Contains $publish.Text 'Publish-Release.ps1 -Repo $env:REPO -ReleaseId $env:RELEASE_ID' 'the release is published by its id, by the script that decides make_latest'
        Assert-NotContains $publish.Text 'RELEASE_TAG' 'the publish step (a tag name could select another release)'
        Assert-NotContains $publish.Text 'gh api' 'the step (the PATCH, the id guard and the make_latest decision are in the script)'
        Assert-NotContains $publish.Text 'make_latest=true' 'the step (a fixed make_latest could move latest backwards)'
    }

    Invoke-Case 'release.yml: after publishing, releases/latest is fetched the way users fetch it and compared with the verified archive, which must be a 64-digit hash' {
        $latest = Get-ReleaseStep '(?m)^\s+id: latest\s*$' 'latest'
        $condition = Get-StepField $latest 'if'
        Assert-Contains $condition "steps.publish.outcome == 'success'" 'only after the publish step succeeded'
        Assert-Contains $condition "steps.recheck_after.outcome == 'success'" 'only when the live asset set is the verified one'
        Assert-Contains $condition "env.DRY_RUN != 'true'" 'never in a dry run'
        Assert-Contains $condition "steps.publish.outputs.make_latest == 'true'" 'only for a release that became the latest one (a lower version is published with make_latest=false and latest rightly serves another release)'
        Assert-True ($latest.Text -cmatch 'ARCHIVE_HASHES:\s*\$\{\{ steps\.verify\.outputs\.archive_hashes \}\}') 'the expected hashes are the verifier output'
        Assert-Contains $latest.Text '-Latest' 'the public latest download'
        Assert-Contains $latest.Text '-ExpectedArchiveHashes $env:ARCHIVE_HASHES' 'the verified archive digests are passed'
        Assert-Contains $latest.Text '-Repo $env:REPO' 'the repository of the run'
        Assert-Contains $latest.Text '-OwnDownloadsPath $env:OWN_DOWNLOADS_FILE' 'the public fetch is recorded'
        Assert-Contains $latest.Text '[string]::IsNullOrWhiteSpace($env:ARCHIVE_HASHES)' 'an unset output stops the step before the weaker check could run'
        Assert-True ($latest.Text.IndexOf('IsNullOrWhiteSpace', [StringComparison]::Ordinal) -lt $latest.Text.IndexOf('Assert-PublishedRelease.ps1', [StringComparison]::Ordinal)) 'the guard comes before the verifier call'
    }

    Invoke-Case 'release.yml: the injected fault corrupts the uploaded zip after the upload, for a dry run only' {
        $inject = Get-ReleaseStep '--clobber' 'inject'
        $condition = Get-StepField $inject 'if'
        Assert-Contains $condition "env.DRY_RUN == 'true'" 'only in a dry run'
        Assert-Contains $condition "env.INJECT_FAULT == 'corrupt-zip'" 'only when asked'
        Assert-Contains $condition "env.INJECT_FAULT == 'corrupt-zip-x64'" 'only when asked (x64)'
        Assert-Contains $inject.Text 'gh release upload' 'the zip asset is replaced on the draft'
        Assert-Contains $inject.Text '$ReleaseArchiveName' 'the asset name comes from the release contract module'
        Assert-Contains $inject.Text "(Get-PluginIdentity -Architecture 'x64').assetPrefix" 'the x64 asset name comes from the identity table'
    }

    Invoke-Case 'release.yml: a dry run deletes the draft it created (by id) and its tag in an always() step that can never touch a real release' {
        $cleanup = Get-ReleaseStep 'Remove-RehearsalRelease\.ps1' 'cleanup'
        $condition = Get-StepField $cleanup 'if'
        Assert-Contains $condition 'always()' 'cleanup runs after a failed verify too'
        Assert-Contains $condition "env.DRY_RUN == 'true'" 'dry runs only'
        Assert-Contains $condition "needs.package.outputs.rehearsal == 'true'" 'only for a tag the gate accepted as a rehearsal'
        Assert-True ($cleanup.Text -cmatch 'CREATED_RELEASE_ID:\s*\$\{\{ steps\.create\.outputs\.release_id \}\}') 'the id is the one the create step reported, not a lookup by tag'
        Assert-Contains $cleanup.Text '-ReleaseId $env:CREATED_RELEASE_ID' 'the script deletes that id only'
        Assert-Contains $cleanup.Text '-Tag $env:RELEASE_TAG' 'the rehearsal tag'
        Assert-NotContains $cleanup.Text 'gh api' 'the step (the deletes, the 404 handling and the exit code are in the script)'
        Assert-NotContains $cleanup.Text 'select(.draft' 'the step (a listing by tag could delete the draft of another run)'
    }

    Invoke-Case 'release.yml: own-downloads is uploaded under the exact name the collector reads, even when the run failed' {
        $upload = $releaseSteps[$releaseSteps.Count - 1]
        Assert-Equal "always() && env.DRY_RUN != 'true'" (Get-StepField $upload 'if') 'the upload must run on failure too, and never for a dry run (its deleted draft never reaches a snapshot)'
        Assert-True ($upload.Text -cmatch '(?m)^\s+overwrite: true\s*$') 'a re-run of the job uploads the same name again'
        Assert-True ($upload.Text -cmatch '(?m)^\s+name: own-downloads\s*$') 'the artifact must be named own-downloads'
        Assert-True ($upload.Text -cmatch 'path:\s*\$\{\{ env\.OWN_DOWNLOADS_FILE \}\}') 'the artifact holds the record file'
        Assert-True ($releaseEnv -cmatch '(?m)^  OWN_DOWNLOADS_FILE:\s*\S*[/\\]own-downloads\.json\s*$') 'the file is called own-downloads.json and sits outside the release assets folder'
        Assert-True ($releaseEnv -cnotmatch 'OWN_DOWNLOADS_FILE:\s*\$\{\{') 'a literal path, so the verifier and the upload cannot disagree'
        $collector = [IO.File]::ReadAllText((Join-Path $RepoRoot '.github\stats\collect.py'))
        Assert-Contains $collector 'OWN_DOWNLOADS_ARTIFACT = "own-downloads"' 'the collector reads the artifact of this name'
        Assert-Contains $collector 'OWN_DOWNLOADS_FILE = "own-downloads.json"' 'the collector reads the file of this name'
        Assert-Contains $collector 'RELEASE_WORKFLOW_PATH = ".github/workflows/release.yml"' 'the collector accepts runs of this workflow file'
    }

    Invoke-Case 'release.yml: every step that calls GitHub or a build script has its own timeout' {
        $calling = @(@($packageSteps) + @($releaseSteps) | Where-Object { $_.Text -cmatch '\\build\\[A-Za-z-]+\.ps1|gh (api|release)' })
        Assert-True ($calling.Count -gt 0) "no step that calls GitHub or a build script was found, so no timeout was checked"
        foreach ($step in $calling) {
            Assert-True ($null -ne (Get-StepField $step 'timeout-minutes')) "step '$($step.Name)' has no timeout-minutes"
        }
    }

    Invoke-Case 'workflows: every build script a workflow calls exists and takes every parameter the call passes' {
        $calls = @()
        foreach ($file in $AllWorkflows) {
            foreach ($block in @(Get-RunValues (Read-WorkflowText $file))) {
                $calls += @([regex]::Matches($block, '(?m)&\s+\.\\build\\([A-Za-z-]+)\.ps1([^\r\n]*)'))
            }
        }
        $distinct = @($calls | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        Assert-True ($distinct.Count -gt 0) "no build script call was found in the workflows, so nothing was checked"
        foreach ($call in $calls) {
            $scriptPath = Join-Path $RepoRoot "build\$($call.Groups[1].Value).ps1"
            Assert-True (Test-Path -LiteralPath $scriptPath) "build\$($call.Groups[1].Value).ps1 does not exist"
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$null)
            $known = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
            foreach ($passed in [regex]::Matches($call.Groups[2].Value, '(?<![\w-])-([A-Za-z][A-Za-z0-9]*)')) {
                Assert-True ($known -ccontains $passed.Groups[1].Value) "the workflow passes -$($passed.Groups[1].Value) to build\$($call.Groups[1].Value).ps1, which has no such parameter"
            }
        }
    }

    foreach ($file in @('ci.yml', 'release.yml')) {
        Invoke-Case "$file : every run: block is valid PowerShell (their default shell), because no runner is at hand to find a typo" {
            $text = Read-WorkflowText $file
            Assert-True ((Get-TopLevelBlock $text 'defaults') -cmatch 'shell:\s*powershell') 'the default shell must be powershell'
            $blocks = @(Get-RunValues $text)
            Assert-True ($blocks.Count -gt 0) 'no run: block found'
            foreach ($block in $blocks) {
                $tokens = $null
                $errors = $null
                [void][System.Management.Automation.Language.Parser]::ParseInput($block, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 $errors.Count "parse errors in a run: block of ${file}: $(@($errors | ForEach-Object { $_.Message }) -join '; ')`n$block"
            }
        }

        # The runner ends a PowerShell step with `exit $LASTEXITCODE` of the LAST native command, so a block that
        # lets a native command fail on purpose (a probe that may answer 404) would turn a good run red. Such logic
        # lives in a tested script that ends with an explicit exit (Remove-RehearsalRelease.ps1, see
        # ReleaseDraft.Tests.ps1), never in a run: block.
        Invoke-Case "$file : no run: block tolerates a failing native command (that logic is a tested script with an explicit exit code)" {
            foreach ($block in @(Get-RunValues (Read-WorkflowText $file))) {
                Assert-True ($block -cnotmatch 'ErrorActionPreference\s*=\s*.Continue.') "a run: block lets native commands fail: $block"
            }
        }
    }

    Invoke-Case 'release.yml and ci.yml: nothing is bound to one repository, so the fork rehearsal runs the same files' {
        foreach ($code in @($releaseCode, $ciCode)) {
            Assert-NotContains $code 'Zuehlke' 'a workflow'
            Assert-NotContains $code 'teststand-mcp/' 'a workflow'
        }
        Assert-True ($releaseEnv -cmatch 'REPO:\s*\$\{\{ github\.repository \}\}') 'the repository is github.repository'
    }
}
