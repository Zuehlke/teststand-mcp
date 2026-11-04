# ============================================================================
#  Remove-RehearsalRelease.ps1 - the cleanup of a dry run of the release workflow. It deletes
#    - the draft THIS run created, by the id New-ReleaseDraft.ps1 reported (-ReleaseId). A draft it did
#      not create is never touched, a published release never, and an empty id means "this run created
#      no draft": nothing is deleted;
#    - the rehearsal tag, if something created it. Only an HTTP 404 means "absent"; any other failure of
#      the check is a failure.
#  Every step is tried even when an earlier one failed. The exit code is explicit: 0 when everything that
#  had to go is gone, 1 with the three parts otherwise (a tolerated 404 must never leave the run red, and a
#  failed delete must never leave it green).
#    -Repo owner/name   -Tag v<X.Y.Z>-rehearsal (anything else is refused)   -ReleaseId <created draft id>
#  Test seam: -Gh (a scriptblock, see ReleaseDraft.psm1). The parameters are not Mandatory on purpose.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$Repo = '',
    [string]$Tag = '',
    [string]$ReleaseId = '',
    [scriptblock]$Gh
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'ReleaseDraft.psm1') -Force
    Remove-RehearsalRelease -Repo $Repo -Tag $Tag -ReleaseId $ReleaseId -Gh $Gh
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
