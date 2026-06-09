# ============================================================================
#  TestStandMCP — pre-push test gate (invoked by .githooks/pre-push)
#  ---------------------------------------------------------------------------
#  1. Terminates running TestStand processes (they hold COM engine handles and
#     make the integration tests flaky via file-sharing violations).
#  2. Runs the integration test suite.
#  Exits 0 only when ALL tests pass — any other exit code blocks the push.
# ============================================================================

# Let $LASTEXITCODE — not an exception — carry dotnet's result, even under
# PowerShell 7's native-command error handling.
$ErrorActionPreference = 'Continue'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Repo root = parent of the .githooks directory this script lives in.
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProj = Join-Path $repoRoot 'Test\TestExecution\TestStandMCP.IntegrationTests.csproj'

Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  pre-push gate: TestStandMCP integration tests' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan

if (-not (Test-Path $testProj)) {
    Write-Host "  ERROR: test project not found at $testProj" -ForegroundColor Red
    exit 1
}

# ── 1) Free the engine: stop processes that hold COM handles ────────────────
$killed = $false
foreach ($name in @('TestStandMCP', 'SeqEdit')) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host ("  -> stopping {0}x {1}.exe (holds engine handles)" -f $procs.Count, $name) -ForegroundColor Yellow
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        $killed = $true
    }
}
# The engine releases OS file handles asynchronously after the process dies;
# give it a brief moment before the test host opens the engine.
if ($killed) { Start-Sleep -Milliseconds 700 }

# ── 2) Run the tests ────────────────────────────────────────────────────────
Write-Host '  -> dotnet test (Debug / net8.0-windows) ...' -ForegroundColor Yellow
Write-Host ''

& dotnet test $testProj --configuration Debug --framework net8.0-windows --nologo
$code = $LASTEXITCODE

Write-Host ''
if ($code -eq 0) {
    Write-Host '  PASS: all tests green - push continues.' -ForegroundColor Green
} else {
    Write-Host ("  FAIL: tests did not pass (exit {0}) - push aborted." -f $code) -ForegroundColor Red
    Write-Host '        Fix the tests, or bypass in an emergency: git push --no-verify' -ForegroundColor DarkGray
}

exit $code
