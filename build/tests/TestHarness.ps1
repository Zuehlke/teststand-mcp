# ============================================================================
#  TestHarness.ps1 - the plain PowerShell test harness every build/tests/*.Tests.ps1
#  file dot-sources:   . (Join-Path $PSScriptRoot 'TestHarness.ps1')
#  It holds Invoke-Case (one PASS / FAIL line per case), the common asserts (including the three-part
#  failure check), the file and environment helpers every test file needs (Write-TextFile,
#  Use-Environment, Invoke-Script), New-TempDir and the end-of-run summary + exit code. The output
#  format is the same in every test file:
#      PASS  <name>                          FAIL  <name>
#                                                  <message>
#      (blank line)  N case(s) FAILED  |  All cases passed
#  A test file sets $script:TempDirPrefix (before its first New-TempDir) to name its temp folders.
#  Compatible with Windows PowerShell 5.1 and PowerShell 7. ASCII only (5.1 reads a BOM-less
#  script as ANSI).
# ============================================================================
$script:Failures = 0
$script:TempDirs = @()
$script:TempDirPrefix = 'Tests_'
$script:HarnessUtf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "PASS  $Name"
    } catch {
        $script:Failures++
        Write-Host "FAIL  $Name"
        Write-Host "      $($_.Exception.Message)"
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$What = 'value')
    if ($Expected -cne $Actual) { throw "$What : expected [$Expected] but got [$Actual]" }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Contains {
    param([string]$Text, [string]$Needle, [string]$What = 'text')
    if ($Text.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) { throw "$What does not contain [$Needle]. Text was:`n$Text" }
}

function Assert-NotContains {
    param([string]$Text, [string]$Needle, [string]$What = 'text')
    if ($Text.IndexOf($Needle, [StringComparison]::Ordinal) -ge 0) { throw "$What unexpectedly contains [$Needle]. Text was:`n$Text" }
}

# The three parts of every build failure: labelled What / Why / Fix lines, each with content.
function Assert-ThreePartFailure {
    param([string]$Text)
    foreach ($label in @('What', 'Why', 'Fix')) {
        Assert-True ($Text -cmatch "(?m)^${label}: +\S") "failure output has no '${label}:' line with content. Output was:`n$Text"
    }
}

# Writes UTF-8 without a BOM, creating the parent folder.
function Write-TextFile {
    param([string]$Path, [string]$Text)
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
    [IO.File]::WriteAllText($Path, $Text, $script:HarnessUtf8NoBom)
}

# Runs a body with environment variables set (a $null value removes one), then restores them.
function Use-Environment {
    param([hashtable]$Values, [scriptblock]$Body)
    $saved = @{}
    foreach ($name in $Values.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        foreach ($name in $Values.Keys) { [Environment]::SetEnvironmentVariable($name, $Values[$name]) }
        & $Body
    } finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}

# Runs a script in-process; returns its exit code and everything it printed. EVERY failing run must
# print the three parts, so that is checked here, once, for every script and every case.
function Invoke-Script {
    param([string]$Path, [hashtable]$Arguments = @{})
    $global:LASTEXITCODE = 0
    $lines = @(& $Path @Arguments *>&1 | ForEach-Object { "$_" })
    $run = @{ ExitCode = $global:LASTEXITCODE; Output = ($lines -join "`n") }
    if ($run.ExitCode -ne 0) {
        try { Assert-ThreePartFailure $run.Output } catch { throw "$(Split-Path -Leaf $Path) exited $($run.ExitCode) without a three-part failure: $($_.Exception.Message)" }
    }
    return $run
}

# A fresh temp folder (removed by Remove-TestTempDirs). $Suffix lets a test put a space or a
# non-ASCII letter into the path.
function New-TempDir {
    param([string]$Suffix = '')
    $dir = Join-Path ([IO.Path]::GetTempPath()) ($script:TempDirPrefix + [Guid]::NewGuid().ToString('N') + $Suffix)
    [void](New-Item -ItemType Directory -Path $dir)
    $script:TempDirs += $dir
    return $dir
}

function Remove-TestTempDirs {
    foreach ($dir in $script:TempDirs) {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# Prints the summary and ends the process: exit 1 when any case failed, else 0.
function Complete-TestRun {
    if ($script:Failures -gt 0) {
        Write-Host ''
        Write-Host "$script:Failures case(s) FAILED"
        exit 1
    }
    Write-Host ''
    Write-Host 'All cases passed'
    exit 0
}
