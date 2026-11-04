# ============================================================================
#  Assert-ReleaseAssetSet.ps1 - checks that a release holds exactly the assets that were verified.
#  The release workflow runs it twice: just BEFORE the verified draft is published (-Stage BeforePublish:
#  still a draft, the same asset ids) and just AFTER the publish call (-Stage AfterPublish: no longer a
#  draft, the same asset ids). Exit 0, or one three-part failure and exit 1; an AfterPublish failure says
#  that the release is live and names the rollback. The comparison itself is Assert-ReleaseAssetSet in
#  ReleaseVerdict.psm1.
#    -ReleaseId         the id the verifier reported (its release_id output); must be a number
#    -ExpectedAssetIds  the verifier's step output asset_ids, {"<name>": <id>, ...}
#    -Repo              owner/name: the release is read with gh (GET releases/{id}) by that id
#    -ReleaseJson       alternatively the response body of GET releases/{id}, already read (used instead of
#                       -Repo)
#  The parameters are not Mandatory on purpose: an unset step output arrives as an empty string, and a
#  prompt would hang the run instead of failing it with the three parts.
#  Test seam: -Gh (a scriptblock, see ReleaseDraft.psm1).
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$ReleaseJson = '',
    [string]$ExpectedAssetIds = '',
    [string]$ReleaseId = '',
    [ValidateSet('BeforePublish', 'AfterPublish')][string]$Stage = 'BeforePublish',
    [string]$Repo = '',
    [scriptblock]$Gh
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'ReleaseVerdict.psm1') -Force
    if ([string]::IsNullOrWhiteSpace($ReleaseJson) -and -not [string]::IsNullOrWhiteSpace($Repo)) {
        Import-Module (Join-Path $PSScriptRoot 'ReleaseDraft.psm1') -Force
        Assert-RepoSlug $Repo
        $ReleaseJson = Get-ReleaseBody -Repo $Repo -ReleaseId $ReleaseId -Gh $Gh   # refuses a release id that is not a number
    }
    Assert-ReleaseAssetSet -ReleaseJson $ReleaseJson -ExpectedAssetIds $ExpectedAssetIds -ReleaseId $ReleaseId -Stage $Stage
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
