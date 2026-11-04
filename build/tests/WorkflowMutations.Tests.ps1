# ============================================================================
#  WorkflowMutations.Tests.ps1 - proves that the workflow contract (WorkflowContract.ps1) can FAIL.
#  A passing test that cannot fail protects nothing, and no actionlint or runner is at hand, so every rule
#  of the contract gets a mutation here: a copy of the workflows in a temp folder with ONE deliberate defect
#  (an unpinned action, a write token on the build job, publishing by tag name, a cleanup that is given the
#  wrong id, ...). The contract runs against the copy IN THIS PROCESS (no child process per mutation) and
#  must report at least one failed case whose name contains the rule that the defect breaks. The unmutated
#  copy must pass every case, so a broken table or a broken copy cannot make every row look "caught".
#  Each mutation is anchored on a stable token (a key, a value, an action pin), never on a run of spaces
#  or on a trailing comment: a re-flowed comment must not turn the whole table into "does not apply".
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\WorkflowMutations.Tests.ps1
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$WorkflowDir = Join-Path $RepoRoot '.github\workflows'
$MinimumMutations = 20
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
. (Join-Path $PSScriptRoot 'WorkflowContract.ps1')
$script:TempDirPrefix = 'WorkflowMutationsTests_'

$script:Mutations = @()

# One row of the table: Old must occur in the file (the FIRST occurrence is replaced) and New replaces it.
# Expect is a piece of the name of the contract case that the defect must break.
function Add-Mutation {
    param([string]$Name, [string]$File, [string]$Expect, [string]$Old, [string]$New)
    $script:Mutations += [pscustomobject]@{ Name = $Name; File = $File; Expect = $Expect; Old = $Old.Replace("`r`n", "`n"); New = $New.Replace("`r`n", "`n") }
}

# A temp folder with LF copies of every workflow, one of them edited.
function New-WorkflowCopy {
    param($Mutation)
    $dir = New-TempDir
    foreach ($file in (Get-ChildItem -LiteralPath $WorkflowDir -File -Filter '*.yml')) {
        $text = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n")
        if ($null -ne $Mutation -and $file.Name -ceq $Mutation.File) {
            $at = $text.IndexOf($Mutation.Old, [StringComparison]::Ordinal)
            if ($at -lt 0) { throw "the mutation '$($Mutation.Name)' does not apply: its text is not in $($Mutation.File):`n$($Mutation.Old)" }
            $text = $text.Substring(0, $at) + $Mutation.New + $text.Substring($at + $Mutation.Old.Length)
        }
        [IO.File]::WriteAllText((Join-Path $dir $file.Name), $text, $Utf8NoBom)
    }
    return $dir
}

# Runs the contract against a folder: one { Name; Passed; Message } per case. A contract that cannot even
# read the workflows (a job it needs is gone) is one failed case named 'the contract could not run'.
function Invoke-ContractAgainst {
    param([string]$Directory)
    $results = New-Object System.Collections.ArrayList
    $recorder = {
        param([string]$Name, [scriptblock]$Body)
        try { & $Body; [void]$results.Add(@{ Name = $Name; Passed = $true; Message = '' }) }
        catch { [void]$results.Add(@{ Name = $Name; Passed = $false; Message = $_.Exception.Message }) }
    }.GetNewClosure()
    try { Invoke-WorkflowContract -WorkflowDir $Directory -RepoRoot $RepoRoot -Case $recorder }
    catch { [void]$results.Add(@{ Name = 'the contract could not run'; Passed = $false; Message = $_.Exception.Message }) }
    return @($results)
}

$CheckoutPin = 'actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1'
$DownloadPin = 'actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333 # v8.0.2'
$SetupDotnetPin = 'actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0'

# --- release.yml ---------------------------------------------------------------------------------------
Add-Mutation 'an action pinned by a movable tag' 'release.yml' 'every action is pinned' $DownloadPin 'actions/download-artifact@v8'
Add-Mutation 'a pin without its version comment' 'release.yml' 'every action is pinned' $SetupDotnetPin 'actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68'
Add-Mutation 'an expression inside a run: block' 'release.yml' 'no run: block contains an expression' "Write-Host '::error::A release is cut by pushing a tag vX.Y.Z." "Write-Host '`${{ github.ref_name }}'; Write-Host '::error::A release is cut by pushing a tag vX.Y.Z."
Add-Mutation 'the release job loses its write permission' 'release.yml' 'only the release job may write' "      contents: write" "      contents: read"
Add-Mutation 'the package job gets a write token' 'release.yml' 'only the release job may write' "    permissions:`n      contents: read`n    outputs:" "    permissions:`n      contents: write`n    outputs:"
Add-Mutation 'an extra permission on the release job' 'release.yml' 'only the release job may write' "      contents: write" "      contents: write`n      id-token: write"
Add-Mutation 'the release job no longer needs the package job' 'release.yml' 'the two jobs are split' "    needs: package`n" ''
Add-Mutation 'a dotnet step in the job that holds the write token' 'release.yml' 'the two jobs are split' "      - name: Download the release assets built by the package job" "      - uses: $SetupDotnetPin`n      - name: Download the release assets built by the package job"
Add-Mutation 'a release script in the package job, which has no token' 'release.yml' 'the two jobs are split' "      - name: Install .NET 8" "      - name: Publish early`n        run: |`n          & .\build\Publish-Release.ps1 -Repo `$env:REPO -ReleaseId 1`n      - name: Install .NET 8"
Add-Mutation 'the tag filter narrowed to v*' 'release.yml' 'a pushed tag of any name starts it' "push:`n    tags: ['**']" "push:`n    tags: ['v*']"
Add-Mutation 'tag pushes share one queue again' 'release.yml' 'queues per ref' 'group: release-${{ github.ref }}' "group: release-`${{ github.event_name == 'push' && 'publish' || github.ref }}"
Add-Mutation 'a running release may be cancelled' 'release.yml' 'queues per ref' 'cancel-in-progress: false' 'cancel-in-progress: true'
Add-Mutation 'publishing by tag name' 'release.yml' 'publishing is by the verifier release id' '& .\build\Publish-Release.ps1 -Repo $env:REPO -ReleaseId $env:RELEASE_ID' '& .\build\Publish-Release.ps1 -Repo $env:REPO -ReleaseId $env:RELEASE_TAG'
Add-Mutation 'publishing with a make_latest that is fixed in the workflow' 'release.yml' 'publishing is by the verifier release id' '& .\build\Publish-Release.ps1' 'gh api -X PATCH "repos/$env:REPO/releases/$env:RELEASE_ID" -F draft=false -f make_latest=true; & .\build\Publish-Release.ps1'
Add-Mutation 'publishing without the asset recheck' 'release.yml' 'publishing is by the verifier release id' " && steps.verify.outcome == 'success' && steps.recheck.outcome == 'success'" " && steps.verify.outcome == 'success'"
Add-Mutation 'publishing in a dry run' 'release.yml' 'publishing is by the verifier release id' "if: env.DRY_RUN != 'true' && steps.verify.outcome == 'success' && steps.recheck.outcome == 'success'" "if: steps.verify.outcome == 'success' && steps.recheck.outcome == 'success'"
Add-Mutation 'the first asset check asks for the wrong stage' 'release.yml' 'checked again just before and just after' '-Stage BeforePublish' '-Stage AfterPublish'
Add-Mutation 'the second asset check forgets the verified ids' 'release.yml' 'checked again just before and just after' '-ExpectedAssetIds $env:ASSET_IDS -ReleaseId $env:RELEASE_ID -Stage AfterPublish' '-ReleaseId $env:RELEASE_ID -Stage AfterPublish'
Add-Mutation 'an asset check that reads the release list instead of the id' 'release.yml' 'checked again just before and just after' '& .\build\Assert-ReleaseAssetSet.ps1 -Repo $env:REPO -ExpectedAssetIds $env:ASSET_IDS -ReleaseId $env:RELEASE_ID -Stage BeforePublish' '& .\build\Assert-ReleaseAssetSet.ps1 -ReleaseJson (gh api "repos/$env:REPO/releases") -ExpectedAssetIds $env:ASSET_IDS -ReleaseId $env:RELEASE_ID -Stage BeforePublish'
Add-Mutation 'the final fetch without the verified digest' 'release.yml' 'after publishing, releases/latest is fetched' ' -ExpectedArchiveHashes $env:ARCHIVE_HASHES' ''
Add-Mutation 'the final fetch also runs for a release that was not made the latest one' 'release.yml' 'after publishing, releases/latest is fetched' " && steps.recheck_after.outcome == 'success' && steps.publish.outputs.make_latest == 'true'" " && steps.recheck_after.outcome == 'success'"
Add-Mutation 'the final fetch without the digest guard' 'release.yml' 'after publishing, releases/latest is fetched' 'if ([string]::IsNullOrWhiteSpace($env:ARCHIVE_HASHES)) {' 'if ($false) {'
Add-Mutation 'one architecture packaged only' 'release.yml' 'packages every architecture' 'foreach ($architecture in (Get-PluginArchitectures)) {' "foreach (`$architecture in @('x86')) {"
Add-Mutation 'the verifier no longer told about the x64 plugin' 'release.yml' 'packages every architecture' '-Token $env:GH_TOKEN -AssetPrefixes $env:ASSET_PREFIXES' '-Token $env:GH_TOKEN'
Add-Mutation 'the x64 fault dropped' 'release.yml' 'takes the dry-run inputs' "          - corrupt-zip-x64`n" ''
Add-Mutation 'the draft creation inlined again' 'release.yml' 'the draft is created by the script' '& .\build\New-ReleaseDraft.ps1' 'gh release create $env:RELEASE_TAG --repo $env:REPO --draft; & .\build\New-ReleaseDraft.ps1'
Add-Mutation 'a dry run decided by the shape of the tag when the draft is created' 'release.yml' 'the draft is created by the script' "`$dryRun = (`$env:DRY_RUN -eq 'true')" "`$dryRun = (`$env:RELEASE_TAG -like '*-rehearsal')"
Add-Mutation 'the cleanup is not always()' 'release.yml' 'always() step' "if: always() && env.DRY_RUN == 'true' && needs.package.outputs.rehearsal == 'true'" "if: env.DRY_RUN == 'true' && needs.package.outputs.rehearsal == 'true'"
Add-Mutation 'the cleanup may run for a real release' 'release.yml' 'always() step' "if: always() && env.DRY_RUN == 'true' && needs.package.outputs.rehearsal == 'true'" "if: always() && needs.package.outputs.rehearsal == 'true'"
Add-Mutation 'the cleanup is given another id than the one the create step reported' 'release.yml' 'deletes the draft it created' '-ReleaseId $env:CREATED_RELEASE_ID' '-ReleaseId $env:RELEASE_ID'
Add-Mutation 'the cleanup id is not the create step output' 'release.yml' 'deletes the draft it created' 'CREATED_RELEASE_ID: ${{ steps.create.outputs.release_id }}' 'CREATED_RELEASE_ID: ${{ steps.verify.outputs.release_id }}'
Add-Mutation 'the cleanup lists drafts by tag again' 'release.yml' 'deletes the draft it created' '& .\build\Remove-RehearsalRelease.ps1' 'gh api "repos/$env:REPO/releases" --jq ".[] | select(.draft == true)"; & .\build\Remove-RehearsalRelease.ps1'
Add-Mutation 'the cleanup tolerates failing native commands' 'release.yml' 'tolerates a failing native command' '& .\build\Remove-RehearsalRelease.ps1' "`$ErrorActionPreference = 'Continue'; & .\build\Remove-RehearsalRelease.ps1"
Add-Mutation 'own-downloads not uploaded after a failure' 'release.yml' 'own-downloads is uploaded' "        if: always() && env.DRY_RUN != 'true'`n        uses: actions/upload-artifact" "        if: env.DRY_RUN != 'true'`n        uses: actions/upload-artifact"
Add-Mutation 'own-downloads uploaded for a dry run' 'release.yml' 'own-downloads is uploaded' "        if: always() && env.DRY_RUN != 'true'`n        uses: actions/upload-artifact" "        if: always()`n        uses: actions/upload-artifact"
Add-Mutation 'own-downloads cannot be re-uploaded by a re-run' 'release.yml' 'own-downloads is uploaded' '          overwrite: true' '          overwrite: false'
Add-Mutation 'own-downloads uploaded under another name' 'release.yml' 'own-downloads is uploaded' 'name: own-downloads' 'name: downloads'
Add-Mutation 'the asset artifact kept for 90 days' 'release.yml' 'short-lived artifact' 'retention-days: 1' 'retention-days: 90'
Add-Mutation 'the release job checks out something else than the tagged commit' 'release.yml' 'short-lived artifact' 'ref: ${{ github.sha }}' 'ref: main'
Add-Mutation 'the asset upload glob is no longer the contract prefix' 'release.yml' 'short-lived artifact' '/teststand-mcp*' '/release-assets*'
Add-Mutation 'the fault injection runs outside a dry run' 'release.yml' 'injected fault' "if: env.DRY_RUN == 'true' && (env.INJECT_FAULT" "if: (env.INJECT_FAULT"
Add-Mutation 'the verify step without a timeout' 'release.yml' 'has its own timeout' "        timeout-minutes: 30`n        run: |`n          `$rehearsal = (`$env:DRY_RUN -eq 'true')`n          & .\build\Assert-PublishedRelease.ps1 -Repo" "        run: |`n          `$rehearsal = (`$env:DRY_RUN -eq 'true')`n          & .\build\Assert-PublishedRelease.ps1 -Repo"
Add-Mutation 'a typo in a run: block' 'release.yml' 'valid PowerShell' 'if ([string]::IsNullOrWhiteSpace($env:ARCHIVE_HASHES)) {' 'if ([string]::IsNullOrWhiteSpace($env:ARCHIVE_HASHES) {'
Add-Mutation 'a build script called with a parameter it does not have' 'release.yml' 'takes every parameter' '-ReleaseId $env:RELEASE_ID' '-ReleaseIdentifier $env:RELEASE_ID'
Add-Mutation 'a build script that does not exist' 'release.yml' 'takes every parameter' '& .\build\Publish-Release.ps1' '& .\build\Publish-Everything.ps1'
Add-Mutation 'the workflow bound to one repository' 'release.yml' 'nothing is bound to one repository' 'REPO: ${{ github.repository }}' 'REPO: Zuehlke/teststand-mcp'
Add-Mutation 'a rehearsal decided by the shape of the tag' 'release.yml' 'the tag is user input' "`$rehearsal = (`$env:DRY_RUN -eq 'true')`n          `$mainRef" "`$rehearsal = (`$env:TAG -like '*-rehearsal')`n          `$mainRef"
Add-Mutation 'the gate no longer checks that the commit is on main' 'release.yml' 'the tag is user input' ' -Rehearsal:$rehearsal -MainRef $mainRef' ' -Rehearsal:$rehearsal'
Add-Mutation 'a real release skips the main check' 'release.yml' 'the tag is user input' "else { 'refs/remotes/origin/main' }" "else { '' }"
Add-Mutation 'the main check by a short name a tag can shadow' 'release.yml' 'the tag is user input' "else { 'refs/remotes/origin/main' }" "else { 'origin/main' }"
Add-Mutation 'the package checkout is shallow' 'release.yml' 'the tag is user input' '          fetch-depth: 0 # the gate' '          fetch-depth: 1 # the gate'
Add-Mutation 'the token in workflow-level env' 'release.yml' 'verified with the token' "  DOTNET_NOLOGO: 'true'`n" "  DOTNET_NOLOGO: 'true'`n  GH_TOKEN: `${{ github.token }}`n"

# --- ci.yml --------------------------------------------------------------------------------------------
Add-Mutation 'ci: an action pinned by a movable tag' 'ci.yml' 'ci.yml : every action is pinned' $CheckoutPin 'actions/checkout@v7'
Add-Mutation 'ci: a write token' 'ci.yml' 'ci.yml: read-only token' "    permissions:`n      contents: read`n    steps:" "    permissions:`n      contents: write`n    steps:"
Add-Mutation 'ci: a job timeout shorter than the sum of its capped steps' 'ci.yml' 'job outlives the SUM' 'timeout-minutes: 100' 'timeout-minutes: 40'
Add-Mutation 'ci: no per-script timeout' 'ci.yml' 'a hanging script is stopped' ' -TimeoutSeconds 600' ''
Add-Mutation 'ci: no total budget for the script tests' 'ci.yml' 'a hanging script is stopped' ' -TotalSeconds 1500' ''
Add-Mutation 'ci: a script test step cap shorter than its own budget' 'ci.yml' 'a hanging script is stopped' "        timeout-minutes: 30`n        run: |`n          & .\build\Invoke-ScriptTests.ps1" "        timeout-minutes: 20`n        run: |`n          & .\build\Invoke-ScriptTests.ps1"
Add-Mutation 'ci: the unit tests without a timeout' 'ci.yml' 'every step that can hang has its own timeout' "        timeout-minutes: 15`n        run: dotnet test" '        run: dotnet test'
Add-Mutation 'ci: a missing pwsh no longer fails the run' 'ci.yml' 'the script tests run under both hosts' "TESTSTAND_MCP_REQUIRE_PWSH: '1'" "TESTSTAND_MCP_REQUIRE_PWSH: '0'"
Add-Mutation 'ci: triggered by pull_request_target' 'ci.yml' 'pull_request_target' "  pull_request:`n    branches: [main]" "  pull_request_target:`n    branches: [main]"
Add-Mutation 'ci: the wrong test category' 'ci.yml' 'the unit tests run only' 'Category=EngineFree' 'Category=Integration'
Add-Mutation 'ci: an x86 test host' 'ci.yml' 'the unit tests run only' ' -p:PlatformTarget=AnyCPU' ''

try {
    Invoke-Case 'mutations: the table has at least 20 named defects and every one names the rule it breaks' {
        Assert-True ($script:Mutations.Count -ge $MinimumMutations) "only $($script:Mutations.Count) mutations; at least $MinimumMutations are required"
        foreach ($mutation in $script:Mutations) {
            Assert-True ($mutation.Expect.Length -gt 0 -and $mutation.Name.Length -gt 0) 'every mutation has a name and an expected rule'
            Assert-True ($mutation.Old -cne $mutation.New) "the mutation '$($mutation.Name)' changes nothing"
        }
        Assert-Equal $script:Mutations.Count @($script:Mutations | ForEach-Object { $_.Name } | Select-Object -Unique).Count 'mutation names are unique'
    }

    Invoke-Case 'mutations: no anchor contains a run of spaces inside a line (alignment padding before a comment must not break the table)' {
        foreach ($mutation in $script:Mutations) {
            Assert-True ($mutation.Old -cnotmatch '\S {2,}\S') "the anchor of '$($mutation.Name)' contains a run of spaces inside a line"
        }
    }

    Invoke-Case 'control: an unmutated copy of the workflows passes every case of the contract (a broken copy cannot make every row look caught)' {
        $results = @(Invoke-ContractAgainst (New-WorkflowCopy $null))
        $failed = @($results | Where-Object { -not $_.Passed })
        Assert-True ($results.Count -gt 0) "the contract ran no case at all"
        Assert-Equal 0 $failed.Count "failed cases:`n$(@($failed | ForEach-Object { "$($_.Name): $($_.Message)" }) -join "`n")"
    }

    foreach ($mutation in $script:Mutations) {
        $current = $mutation
        Invoke-Case "mutation: $($current.Name) -> fails '$($current.Expect)'" {
            $results = @(Invoke-ContractAgainst (New-WorkflowCopy $current))
            $failed = @($results | Where-Object { -not $_.Passed })
            Assert-True ($failed.Count -gt 0) 'the defect was not caught: every case of the contract passed'
            $named = @($failed | Where-Object { $_.Name.Contains($current.Expect) })
            Assert-True ($named.Count -gt 0) "the contract failed, but no failed case names '$($current.Expect)'. Failed cases:`n$(@($failed | ForEach-Object { $_.Name }) -join "`n")"
        }
    }
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
