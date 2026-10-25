# ============================================================================
#  Publish-Release.ps1 - publishes the verified draft release with the given id and says whether it became
#  the latest release. The release workflow runs it after the verification and the asset-set check; the
#  work is Publish-ReleaseById in ReleaseDraft.psm1:
#    - the id must be a number (an unset verifier output stops here, before any API call), the release
#      must still be a DRAFT of a real vX.Y.Z tag (a rehearsal draft is never published);
#    - releases/latest is read, and the release is marked the latest one only when its version is not lower
#      than the current latest release's (a lower version is published with make_latest=false, so a tag
#      pushed out of order does not move "latest" backwards; two runs publishing at the same moment can). The chosen value is printed: make_latest=<true|false>,
#      and written as the step output make_latest: the check that releases/latest serves the new archive
#      runs only when it is true.
#    -Repo owner/name   -ReleaseId <the verifier's release_id output>
#  The release is addressed by its id, never by its tag name.
#  Test seam: -Gh (a scriptblock, see ReleaseDraft.psm1). The parameters are not Mandatory on purpose.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$Repo = '',
    [string]$ReleaseId = '',
    [scriptblock]$Gh
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'ReleaseDraft.psm1') -Force
    Publish-ReleaseById -Repo $Repo -ReleaseId $ReleaseId -Gh $Gh
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
