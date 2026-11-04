# ============================================================================
#  Assert-ReleaseTag.ps1 - the release tag gate. A tag must be exactly
#  v<major>.<minor>.<patch>. Exit 0 and (when GITHUB_OUTPUT is set) append
#  "version=<X.Y.Z>" and "tag=<vX.Y.Z>" for later workflow steps; otherwise print
#  what is wrong, why the format is required and the retag commands in a safe
#  order, and exit 1. The same verdict locally and in CI.
#  Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Assert-ReleaseTag.ps1 -Tag v1.9.0
#  In CI pass the tag through the environment, never inline: tag names are user input.
#    -Tag $env:TAG
#  -Rehearsal also accepts v<X.Y.Z>-rehearsal (the dry-run tag); it adds "rehearsal=true" to the
#  output, and "version" is still the plain X.Y.Z.
#  -Commit is the commit the retag commands point at (default: GITHUB_SHA when set).
#  -MainRef refs/remotes/origin/main also requires -Commit to be on that branch (git merge-base
#  --is-ancestor in -RepoRoot, default the repository of this script), so a tag on an unmerged commit
#  publishes nothing. It must be a full ref: a short name such as origin/main can be shadowed by a pushed
#  TAG of that name, which git resolves first.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [AllowNull()][AllowEmptyString()][string]$Tag = '',
    [string]$Commit = $env:GITHUB_SHA,
    [switch]$Rehearsal,
    [string]$MainRef = '',
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force

    $result = ConvertFrom-ReleaseTag -Tag $Tag -AllowRehearsal:$Rehearsal
    if (-not $result.IsValid) {
        $failure = Get-TagRejection -Result $result -Commit $Commit
        Write-BuildFailure @failure
    }

    if ($MainRef) {
        $why = "Only a commit on $MainRef is released: a tag on any other commit would publish code that never went through a pull request to every user. No asset was built or published."
        $retag = "Merge the change into $MainRef, then move the tag to the merged commit (PowerShell):`n" +
            "  1. git push origin --delete refs/tags/$($result.Tag)`n  2. git tag -d -- $($result.Tag)`n" +
            "  3. git tag $($result.Tag) <commit on $MainRef>`n  4. git push origin $($result.Tag)"
        if (-not $MainRef.StartsWith('refs/', [StringComparison]::Ordinal)) {
            Write-BuildFailure -What "-MainRef '$MainRef' is not a full ref." -Why "A short name can be shadowed by a tag or local branch of the same name, which git resolves first; a tag named like the branch would then pass any commit." -Fix 'Pass the full ref, for example -MainRef refs/remotes/origin/main.'
        }
        if ($Commit -cnotmatch $ReleaseCommitPattern) {
            Write-BuildFailure -What "No commit to check against $MainRef (-Commit was '$Commit', in CI GITHUB_SHA)." -Why $why -Fix 'Pass -Commit with the 40-character SHA the tag points at.'
        }
        foreach ($rev in @($Commit, $MainRef)) {
            [void](Invoke-Git -RepoRoot $RepoRoot -Arguments @('rev-parse', '--verify', '--quiet', "$rev^{commit}") -Why $why `
                -Fix "Fetch the full history first (in the workflow: actions/checkout with fetch-depth: 0), then run the gate again.")
        }
        # Exit 1 is the answer "not an ancestor"; anything else is a failed check, not a reason to retag.
        $ancestry = Invoke-ChildProcess -FilePath 'git' -ArgumentList @('-C', $RepoRoot, 'merge-base', '--is-ancestor', $Commit, $MainRef) -TimeoutSeconds 60
        if ($ancestry.ExitCode -eq 1) {
            Write-BuildFailure -What "The tag $($result.Tag) points at $Commit, which is not on $MainRef." -Why $why -Fix $retag
        }
        if ($ancestry.ExitCode -ne 0) {
            Write-BuildFailure -What "git merge-base --is-ancestor could not check $Commit against $MainRef (exit $($ancestry.ExitCode), timed out: $($ancestry.TimedOut)): $($ancestry.Stderr.Trim())" -Why $why -Fix 'Run the gate again; the tag itself may be fine.'
        }
        Write-Host "Commit $Commit is on $MainRef."
    }

    Add-GitHubOutput -Name 'version' -Value $result.Version
    Add-GitHubOutput -Name 'tag' -Value $result.Tag
    if ($result.IsRehearsal) { Add-GitHubOutput -Name 'rehearsal' -Value 'true' }
    Write-Host "Tag $($result.Tag) is valid: version $($result.Version)"
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
