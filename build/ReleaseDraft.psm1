# ============================================================================
#  ReleaseDraft.psm1 - the GitHub release operations of the release workflow that are not verification
#  (that is Assert-PublishedRelease.ps1): everything that creates, publishes or deletes a release.
#    New-ReleaseDraft         creates the DRAFT of a tag: first it clears the leftovers of an earlier,
#                             failed run of the same tag (drafts only; a PUBLISHED release of the tag is
#                             a failure), then it creates the draft and returns ITS id;
#    Publish-ReleaseById      publishes one release by its id and decides whether it becomes the latest
#                             release (Get-MakeLatestDecision: never a lower version over a higher one);
#    Remove-RehearsalRelease  the dry run's cleanup: deletes the draft THIS run created (by id) and the
#                             rehearsal tag, and nothing else;
#    Get-ReleaseBody          GET releases/{id}, for the asset-set check.
#  The only outside world is the gh CLI, behind ONE boundary: every function takes -Gh, a scriptblock
#  `param([string[]]$Arguments, [int]$TimeoutSeconds)` that returns { ExitCode; TimedOut; Stdout; Stderr }
#  (New-GhResult). Tests pass a fake; production uses gh from the PATH, authenticated by GH_TOKEN.
#  Failures go through Write-BuildFailure (BuildCommon.psm1).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')
Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1')

$script:GhTimeoutSeconds = 120
$script:UploadTimeoutSeconds = 900       # a draft is created with its assets in ONE call: two archives of tens of MB
$script:ListPageSize = 100
$script:MaxListPages = 10
$script:ReleaseIdPattern = '\A[0-9]+\z'
$script:ReleaseNotes = 'Built and verified by the release workflow. Install it through the Claude Code plugin marketplace.'
$script:NotFoundPattern = 'HTTP 404'

# ----------------------------------------------------------------------------
#  The gh boundary
# ----------------------------------------------------------------------------
function New-GhResult {
    [CmdletBinding()]
    param([int]$ExitCode = 0, [string]$Stdout = '', [string]$Stderr = '', [bool]$TimedOut = $false)
    return [pscustomobject]@{ ExitCode = $ExitCode; TimedOut = $TimedOut; Stdout = $Stdout; Stderr = $Stderr }
}

function Invoke-GhCli {
    param([string[]]$Arguments, [int]$TimeoutSeconds)
    $gh = Get-Command gh -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $gh) {
        Write-BuildFailure -What 'The gh CLI was not found on the PATH.' -Why 'The release workflow creates, publishes and deletes releases through gh.' -Fix 'Run it on a GitHub Actions runner (gh is installed there), or install the GitHub CLI.'
    }
    return Invoke-ChildProcess -FilePath $gh.Source -ArgumentList $Arguments -TimeoutSeconds $TimeoutSeconds
}

function Invoke-Gh {
    param([scriptblock]$Gh, [string[]]$Arguments, [int]$TimeoutSeconds = $script:GhTimeoutSeconds)
    if ($null -eq $Gh) { return (Invoke-GhCli -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds) }
    return (& $Gh $Arguments $TimeoutSeconds)
}

function Get-GhErrorText {
    param($Result)
    if ($Result.TimedOut) { return 'gh did not finish in time and was stopped' }
    $text = (@($Result.Stderr, $Result.Stdout) | Where-Object { $_ -and $_.Trim().Length -gt 0 } | ForEach-Object { $_.Trim() }) -join ' | '
    if ($text.Length -eq 0) { return "gh exited with code $($Result.ExitCode)" }
    return $text
}

function Test-GhNotFound {
    param($Result)
    return ($Result.ExitCode -ne 0 -and -not $Result.TimedOut -and "$($Result.Stderr)`n$($Result.Stdout)".Contains($script:NotFoundPattern))
}

function Assert-GhSucceeded {
    param($Result, [string]$What, [string]$Why, [string]$Fix)
    if ($Result.TimedOut -or $Result.ExitCode -ne 0) { Write-BuildFailure -What "${What}: $(Get-GhErrorText $Result)" -Why $Why -Fix $Fix }
}

function Assert-ReleaseId {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$ReleaseId, [string]$For)
    if ($ReleaseId -cnotmatch $script:ReleaseIdPattern) {
        $shown = Get-PrintableTag $ReleaseId
        Write-BuildFailure -What "The verification did not report the id of the release it verified (got '$shown'), so there is nothing safe to $For." `
            -Why 'A release is addressed by its numeric id, never by its tag name: the id is what the verification read, and a stale release with the same tag can then never be the one that is touched. Running it again will not help.' `
            -Fix 'Check that the verification step wrote release_id to its step output, then start a new release run.'
    }
}

# ----------------------------------------------------------------------------
#  Reading releases
# ----------------------------------------------------------------------------
# { Id; Tag; Draft } of every release of the repository (newest first), read page by page. A draft is listed only for
# a token that may write the repository contents.
function Get-ReleaseRecords {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [scriptblock]$Gh)
    $records = @()
    for ($page = 1; $page -le $script:MaxListPages; $page++) {
        $result = Invoke-Gh $Gh @('api', "repos/$Repo/releases?per_page=$($script:ListPageSize)&page=$page", '--jq', '.[] | {id, tag_name, draft} | tojson')
        Assert-GhSucceeded $result "Could not list the releases of $Repo" `
            'The list shows the earlier releases of a tag; without it a leftover draft or a published release of the same tag cannot be told apart. Running it again may help if the cause is transient (network, rate limit); it will not help for a token that may not write the repository contents.' `
            'Check the network and that the token has contents: write, then re-run the failed job.'
        $lines = @($result.Stdout -split '\r?\n' | Where-Object { $_.Trim().Length -gt 0 })
        foreach ($line in $lines) {
            $item = $line | ConvertFrom-Json
            $records += [pscustomobject]@{ Id = [string](Get-JsonProperty $item 'id'); Tag = [string](Get-JsonProperty $item 'tag_name'); Draft = ((Get-JsonProperty $item 'draft') -eq $true) }
        }
        if ($lines.Count -lt $script:ListPageSize) { return $records }
    }
    Write-BuildFailure -What "$Repo has more than $($script:MaxListPages * $script:ListPageSize) releases, and the list was cut off." `
        -Why 'A leftover draft or a published release of the tag could be beyond the part that was read.' `
        -Fix 'Delete old releases, or raise MaxListPages in build\ReleaseDraft.psm1.'
}

# The body of GET releases/{id} as text (what Assert-ReleaseAssetSet compares).
function Get-ReleaseBody {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [Parameter(Mandatory)][AllowEmptyString()][string]$ReleaseId, [scriptblock]$Gh)
    Assert-RepoSlug $Repo
    Assert-ReleaseId $ReleaseId 'compare'
    $result = Invoke-Gh $Gh @('api', "repos/$Repo/releases/$ReleaseId")
    Assert-GhSucceeded $result "Could not read release $ReleaseId of $Repo" `
        'The release body is what its asset ids are compared with. Running it again may help if the cause is transient (network, rate limit).' `
        'Check the network and the token, then re-run the failed job.'
    return $result.Stdout
}

# ----------------------------------------------------------------------------
#  Creating the draft
# ----------------------------------------------------------------------------
# Deletes every DRAFT that carries exactly this tag (the leftover of a failed run: "Re-run failed jobs" would
# otherwise create a second draft with the same tag, which the verifier refuses). A PUBLISHED release with the
# tag is a failure and nothing is deleted. Returns the ids of the deleted drafts.
function Clear-StaleDrafts {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [Parameter(Mandatory)][AllowEmptyString()][string]$Tag, [scriptblock]$Gh)
    $same = @(Get-ReleaseRecords -Repo $Repo -Gh $Gh | Where-Object { $_.Tag -ceq $Tag })
    $published = @($same | Where-Object { -not $_.Draft })
    if ($published.Count -gt 0) {
        Write-BuildFailure -What "A published release of $Repo already carries the tag $Tag (id $($published[0].Id))." `
            -Why 'A tag names exactly one release. A second release under it would make verifying and publishing ambiguous, and a published release must never be replaced. Running it again will not help.' `
            -Fix 'Fix forward: release the next patch version (see CONTRIBUTING). Never reuse a published tag.'
    }
    $deleted = @()
    foreach ($draft in $same) {
        if ($draft.Id -cnotmatch $script:ReleaseIdPattern) {
            Write-BuildFailure -What "The leftover draft of $Tag has the unusable id '$($draft.Id)'." -Why 'It cannot be deleted through the API without its numeric id.' -Fix "Delete the draft by hand (gh release delete $Tag, or in the web UI) and re-run the failed job."
        }
        $result = Invoke-Gh $Gh @('api', '-X', 'DELETE', "repos/$Repo/releases/$($draft.Id)")
        Assert-GhSucceeded $result "Could not delete the leftover draft $($draft.Id) of $Tag" `
            'The new draft would carry the same tag as the old one, and the verifier refuses two releases for one tag. Running it again may help if the cause is transient.' `
            "Delete the draft by hand (gh release delete $Tag, or in the web UI) and re-run the failed job."
        Write-Host "Deleted the leftover draft $($draft.Id) of $Tag."
        $deleted += $draft.Id
    }
    return $deleted
}

# Creates the DRAFT release of $Tag with every file of $AssetDir and returns its id (also written as the step
# output release_id, even when the creation failed half-way, so a cleanup can delete exactly that draft).
# -DryRun: the tag is a rehearsal tag that does not exist, so the draft names it and points at $Target; a real
# release insists on the tag that was pushed (--verify-tag).
function New-ReleaseDraft {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [Parameter(Mandatory)][AllowEmptyString()][string]$Tag, [Parameter(Mandatory)][AllowEmptyString()][string]$AssetDir,
        [string]$Target = '', [switch]$DryRun, [scriptblock]$Gh, [string[]]$AssetPrefixes = @($ReleaseAssetPrefix)
    )
    Assert-RepoSlug $Repo
    $parsed = ConvertFrom-ReleaseTag -Tag $Tag -AllowRehearsal:$DryRun
    if (-not $parsed.IsValid) {
        $failure = Get-TagRejection -Result $parsed -Commit $Target
        Write-BuildFailure @failure
    }
    if ($DryRun -and -not $parsed.IsRehearsal) {
        Write-BuildFailure -What "A dry run was asked to create a draft for the tag $Tag, which is not a rehearsal tag." `
            -Why 'A dry run must never create or delete anything under the tag of a real release.' -Fix 'Pass a tag of the form v<X.Y.Z>-rehearsal.'
    }
    if ($DryRun -and $Target -cnotmatch $ReleaseCommitPattern) {
        Write-BuildFailure -What "A dry run needs the full 40-character commit SHA to point its draft at, and got '$(Get-PrintableTag $Target)'." -Why 'The rehearsal tag does not exist, so the draft names it and targets this commit; an abbreviation or a branch name could resolve to another commit.' -Fix 'Pass -Target with the commit SHA of the run (github.sha).'
    }
    Assert-ReleaseAssetFiles -Directory $AssetDir -Version $parsed.Version -Prefixes $AssetPrefixes

    [void](Clear-StaleDrafts -Repo $Repo -Tag $Tag -Gh $Gh)

    $assets = @(Get-ChildItem -LiteralPath (Resolve-BuildPath $AssetDir) -File | Sort-Object Name | ForEach-Object { $_.FullName })
    $arguments = @('release', 'create', $Tag) + $assets + @('--repo', $Repo, '--draft', '--title', $Tag, '--notes', $script:ReleaseNotes)
    if ($DryRun) { $arguments += @('--target', $Target) } else { $arguments += '--verify-tag' }
    $create = Invoke-Gh $Gh $arguments $script:UploadTimeoutSeconds

    # After the clean-up above, a draft with this tag can only be the one this call made, whether or not it finished.
    $mine = @()
    $lookupFailure = $null
    try { $mine = @(Get-ReleaseRecords -Repo $Repo -Gh $Gh | Where-Object { $_.Tag -ceq $Tag -and $_.Draft }) } catch { $lookupFailure = $_.Exception.Message }
    if ($mine.Count -eq 1) { Add-GitHubOutput -Name 'release_id' -Value $mine[0].Id }

    Assert-GhSucceeded $create "gh could not create the draft release $Tag in $Repo" `
        'The assets are uploaded with the draft in one call; a failure part-way leaves a draft with some of them, which the next run deletes first. Running it again may help if the cause is transient (network, rate limit).' `
        'Re-run the failed job (a leftover draft of this tag is deleted first).'
    if ($mine.Count -ne 1) {
        $why = if ($null -ne $lookupFailure) { "the lookup failed: $lookupFailure" } else { "$($mine.Count) drafts carry the tag" }
        Write-BuildFailure -What "The draft release $Tag was created but its id could not be determined ($why)." `
            -Why 'The id is what the cleanup and the later steps address the draft by, never the tag name. Running it again will not help until the draft is gone.' `
            -Fix "Delete the draft (gh release delete $Tag, or in the web UI) and re-run the failed job."
    }
    Write-Host "Created the draft release $Tag (id $($mine[0].Id)) with $($assets.Count) assets."
    return $mine[0].Id
}

# ----------------------------------------------------------------------------
#  Publishing
# ----------------------------------------------------------------------------
# Compares X.Y.Z strings numerically without a number type (a component may be longer than any integer).
function Compare-VersionText {
    param([string]$Left, [string]$Right)
    $leftParts = @($Left -split '\.')
    $rightParts = @($Right -split '\.')
    for ($index = 0; $index -lt [Math]::Max($leftParts.Count, $rightParts.Count); $index++) {
        $l = if ($index -lt $leftParts.Count) { $leftParts[$index].TrimStart('0') } else { '' }
        $r = if ($index -lt $rightParts.Count) { $rightParts[$index].TrimStart('0') } else { '' }
        if ($l.Length -ne $r.Length) { return [Math]::Sign($l.Length - $r.Length) }
        $order = [string]::CompareOrdinal($l, $r)
        if ($order -ne 0) { return [Math]::Sign($order) }
    }
    return 0
}

# The decision whether publishing $Tag may move releases/latest. $LatestTag is the tag releases/latest names now
# (empty when there is none). A release of a LOWER version than the latest one is published without taking the
# label away, so a tag pushed out of order does not move "latest" backwards. Not atomic: two runs that
# read releases/latest before either one publishes can still end on the lower one (no compare-and-set).
function Get-MakeLatestDecision {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Tag, [AllowEmptyString()][AllowNull()][string]$LatestTag)
    $current = ConvertFrom-ReleaseTag -Tag $Tag
    if (-not $current.IsValid) { throw "Get-MakeLatestDecision needs a valid release tag but got '$Tag'" }
    if ([string]::IsNullOrWhiteSpace($LatestTag)) {
        return [pscustomobject]@{ MakeLatest = 'true'; Reason = "no release is the latest one yet, so $Tag becomes it." }
    }
    $shownLatest = Get-PrintableTag $LatestTag
    $latest = ConvertFrom-ReleaseTag -Tag $LatestTag
    if (-not $latest.IsValid) {
        return [pscustomobject]@{ MakeLatest = 'true'; Reason = "the current latest release '$shownLatest' is not a vX.Y.Z version, so it cannot outrank $Tag." }
    }
    if ((Compare-VersionText $current.Version $latest.Version) -lt 0) {
        return [pscustomobject]@{ MakeLatest = 'false'; Reason = "$Tag is a lower version than the current latest release $shownLatest, so latest stays where it is." }
    }
    return [pscustomobject]@{ MakeLatest = 'true'; Reason = "$Tag is not lower than the current latest release $shownLatest." }
}

# The tag releases/latest names now, or $null when the repository has no latest release (a 404).
function Get-LatestReleaseTag {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [scriptblock]$Gh)
    $result = Invoke-Gh $Gh @('api', "repos/$Repo/releases/latest", '--jq', '.tag_name')
    if (Test-GhNotFound $result) { return $null }
    Assert-GhSucceeded $result "Could not read the latest release of $Repo" `
        'Whether the new release may become the latest one depends on it, and publishing blind could move latest backwards. The draft is untouched. Running it again may help if the cause is transient.' `
        'Re-run the failed job.'
    return $result.Stdout.Trim()
}

# Publishes the draft with this id (never a tag name) and prints whether it became the latest release.
function Publish-ReleaseById {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ReleaseId, [Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [scriptblock]$Gh)
    Assert-RepoSlug $Repo
    Assert-ReleaseId $ReleaseId 'publish'
    $release = (Get-ReleaseBody -Repo $Repo -ReleaseId $ReleaseId -Gh $Gh) | ConvertFrom-Json
    if ((Get-JsonProperty $release 'draft') -ne $true) {
        Write-BuildFailure -What "Release $ReleaseId is not a draft." -Why 'Only the verified draft is published; a release that is already public was published by someone else. Running it again will not help.' -Fix 'Look at the Releases page, and start a new release run if the release is wrong.'
    }
    $tag = [string](Get-JsonProperty $release 'tag_name')
    $parsed = ConvertFrom-ReleaseTag -Tag $tag
    if (-not $parsed.IsValid) {
        Write-BuildFailure -What "Release $ReleaseId carries the tag '$(Get-PrintableTag $tag)', which is not a release tag vX.Y.Z." `
            -Why 'Only a release of a real version is ever published; a rehearsal draft must never go live. Running it again will not help.' -Fix 'Delete the draft and start a new release run from a tag vX.Y.Z.'
    }
    $decision = Get-MakeLatestDecision -Tag $tag -LatestTag (Get-LatestReleaseTag -Repo $Repo -Gh $Gh)
    Write-Host "make_latest=$($decision.MakeLatest): $($decision.Reason)"
    $result = Invoke-Gh $Gh @('api', '-X', 'PATCH', "repos/$Repo/releases/$ReleaseId", '-F', 'draft=false', '-f', "make_latest=$($decision.MakeLatest)", '--jq', '.draft')
    Assert-GhSucceeded $result "Could not publish release $ReleaseId of $Repo" `
        'The draft is untouched, so nothing is live. Running it again may help if the cause is transient (network, rate limit).' `
        'Re-run the failed job.'
    if ($result.Stdout.Trim() -cne 'false') {
        Write-BuildFailure -What "Release $ReleaseId is still a draft after the publish call." -Why 'The API accepted the call but the release did not go live. Running it again may help.' -Fix 'Look at the Releases page, and re-run the failed job.'
    }
    Add-GitHubOutput -Name 'make_latest' -Value $decision.MakeLatest   # the final latest check only makes sense for a release that became the latest one
    Write-Host "Published release $ReleaseId ($tag)."
}

# ----------------------------------------------------------------------------
#  The dry run's cleanup
# ----------------------------------------------------------------------------
# Deletes the draft THIS run created ($ReleaseId, from New-ReleaseDraft) and the rehearsal tag. Never a draft it did
# not create, never a published release, never the tag of a real release. Every step is tried even when an earlier one
# failed; one failure at the end reports them all.
function Remove-RehearsalRelease {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Repo, [Parameter(Mandatory)][AllowEmptyString()][string]$Tag, [AllowEmptyString()][string]$ReleaseId = '', [scriptblock]$Gh)
    Assert-RepoSlug $Repo
    $parsed = ConvertFrom-ReleaseTag -Tag $Tag -AllowRehearsal
    if (-not ($parsed.IsValid -and $parsed.IsRehearsal)) {
        Write-BuildFailure -What "The cleanup was asked to delete under the tag '$(Get-PrintableTag $Tag)', which is not a rehearsal tag." `
            -Why 'The cleanup deletes a tag, so it only ever touches v<X.Y.Z>-rehearsal, never the tag of a real release.' -Fix 'Pass the rehearsal tag of the dry run.'
    }
    $failures = @()

    if ([string]::IsNullOrEmpty($ReleaseId)) {
        Write-Host 'This run created no draft, so no draft is deleted.'
    } elseif ($ReleaseId -cnotmatch $script:ReleaseIdPattern) {
        $failures += "the id of the created draft ('$(Get-PrintableTag $ReleaseId)') is not a number, so no draft was deleted"
    } else {
        $read = Invoke-Gh $Gh @('api', "repos/$Repo/releases/$ReleaseId")
        if (Test-GhNotFound $read) {
            Write-Host "The draft $ReleaseId is already gone."
        } elseif ($read.TimedOut -or $read.ExitCode -ne 0) {
            $failures += "could not read the draft $ReleaseId before deleting it: $(Get-GhErrorText $read)"
        } else {
            $release = $null
            try { $release = $read.Stdout | ConvertFrom-Json } catch { $failures += "the draft $ReleaseId did not read back as JSON" }
            if ($null -ne $release) {
                if ((Get-JsonProperty $release 'draft') -ne $true -or (Get-JsonProperty $release 'tag_name') -cne $Tag) {
                    $failures += "release $ReleaseId is not a draft of $Tag, so it was left alone"
                } else {
                    $delete = Invoke-Gh $Gh @('api', '-X', 'DELETE', "repos/$Repo/releases/$ReleaseId")
                    if ($delete.TimedOut -or $delete.ExitCode -ne 0) { $failures += "could not delete the rehearsal draft ${ReleaseId}: $(Get-GhErrorText $delete)" }
                    else { Write-Host "Deleted the rehearsal draft $ReleaseId." }
                }
            }
        }
    }

    # A draft has no git tag yet; a tag only exists when something created it, and then it must go too.
    # Only a 404 means "absent"; any other failure is a failure.
    $probe = Invoke-Gh $Gh @('api', "repos/$Repo/git/ref/tags/$Tag")
    if (-not $probe.TimedOut -and $probe.ExitCode -eq 0) {
        $delete = Invoke-Gh $Gh @('api', '-X', 'DELETE', "repos/$Repo/git/refs/tags/$Tag")
        if ($delete.TimedOut -or $delete.ExitCode -ne 0) { $failures += "could not delete the rehearsal tag ${Tag}: $(Get-GhErrorText $delete)" }
        else { Write-Host "Deleted the rehearsal tag $Tag." }
    } elseif (Test-GhNotFound $probe) {
        Write-Host "No tag $Tag exists, so none is deleted."
    } else {
        $failures += "could not check whether the rehearsal tag $Tag exists: $(Get-GhErrorText $probe)"
    }

    if ($failures.Count -gt 0) {
        Write-BuildFailure -What "The dry run could not clean up after itself: $($failures -join '; ')." `
            -Why 'A draft or tag that is left behind would collide with the next dry run of the same tag. Running it again may help if the cause is transient.' `
            -Fix "Delete the leftovers by hand: gh release delete $Tag --repo $Repo, and git push origin --delete refs/tags/$Tag."
    }
}

Export-ModuleMember -Function New-GhResult, Get-ReleaseRecords, Get-ReleaseBody, Clear-StaleDrafts, New-ReleaseDraft,
    Get-MakeLatestDecision, Get-LatestReleaseTag, Publish-ReleaseById, Remove-RehearsalRelease, Assert-ReleaseId
