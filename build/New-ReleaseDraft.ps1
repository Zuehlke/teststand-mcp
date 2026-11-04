# ============================================================================
#  New-ReleaseDraft.ps1 - creates the DRAFT release of a tag with the release assets of a folder.
#  The release workflow runs it; the work is New-ReleaseDraft in ReleaseDraft.psm1. In this order:
#    - the folder holds exactly the required assets of the tag's version (no missing, no extra file);
#    - every earlier DRAFT of exactly this tag is deleted (the leftover of a failed run: "Re-run failed
#      jobs" would otherwise create a second draft, which the verifier refuses); a PUBLISHED release of
#      the tag is a failure, and nothing is deleted;
#    - the draft is created and ITS id is written as the step output release_id (also when the creation
#      failed half-way), so the dry run's cleanup deletes exactly that draft.
#    -Repo owner/name   -Tag vX.Y.Z (or v<X.Y.Z>-rehearsal with -DryRun)   -AssetDir <folder>
#    -AssetPrefixes     the plugins the release carries (comma-separated; default teststand-mcp alone);
#                       the folder must hold exactly their four assets each.
#    -DryRun            the tag is a rehearsal tag that does not exist: the draft names it and points at
#                       -Target (a commit SHA); without -DryRun the pushed tag must exist (--verify-tag).
#  Test seam: -Gh (a scriptblock, see ReleaseDraft.psm1). The parameters are not Mandatory on purpose: an
#  unset workflow value arrives as an empty string, and a prompt would hang the run instead of failing it.
#  Windows PowerShell 5.1 and PowerShell 7 compatible.
# ============================================================================
param(
    [string]$Repo = '',
    [string]$Tag = '',
    [string]$AssetDir = '',
    [string]$Target = '',
    [string[]]$AssetPrefixes,
    [switch]$DryRun,
    [scriptblock]$Gh
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1') -Force
try {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'ReleaseDraft.psm1') -Force
    if ([string]::IsNullOrWhiteSpace($AssetDir)) {
        Write-BuildFailure -What 'No asset folder was given.' -Why 'It holds the files that become the assets of the release.' -Fix 'Pass -AssetDir with the folder the package job wrote the assets to.'
    }
    [void](New-ReleaseDraft -Repo $Repo -Tag $Tag -AssetDir $AssetDir -Target $Target -DryRun:$DryRun -Gh $Gh -AssetPrefixes (ConvertTo-AssetPrefixList $AssetPrefixes))
    exit 0
} catch {
    Write-Host (Get-ScriptFailureText -ErrorRecord $_)
    exit 1
}
