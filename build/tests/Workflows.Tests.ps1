# ============================================================================
#  Workflows.Tests.ps1 - plain PowerShell asserts that pin the CONTRACT of the GitHub Actions workflows
#  (.github/workflows/*.yml). The rules themselves are in WorkflowContract.ps1; this file runs them against
#  the repository's real workflows, and WorkflowMutations.Tests.ps1 proves that they can fail.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\Workflows.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails. ASCII only on purpose.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
. (Join-Path $PSScriptRoot 'WorkflowContract.ps1')
$script:TempDirPrefix = 'WorkflowsTests_'

try {
    Invoke-WorkflowContract -WorkflowDir (Join-Path $RepoRoot '.github\workflows') -RepoRoot $RepoRoot
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun