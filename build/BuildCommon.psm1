# ============================================================================
#  BuildCommon.psm1 - the helpers every build script and build module shares:
#    - Write-BuildFailure / Get-ScriptFailureText: the one three-part failure
#      (What / Why / Fix) every build/*.ps1 reports;
#    - Resolve-BuildPath: user-supplied paths resolved against the PowerShell
#      location (NOT the process directory), before any Test-Path or .NET I/O;
#    - Read-Utf8Text, Read-JsonFile, Get-Utf8NoBomEncoding, Get-JsonProperty, Get-PropertyNames;
#    - Add-GitHubOutput: a step output for later workflow steps;
#    - Remove-BuildTempTree: temp cleanup that retries and warns;
#    - Invoke-ChildProcess / Invoke-BuildScript / Assert-ChildOutcome / Stop-ChildProcess: a child program with a
#      timeout, array arguments, optional stdin text and environment overrides, and captured output (its
#      process tree is stopped by id, never by image name);
#    - Invoke-Git: git in a repository, with the three-part failure of a missing git, a timeout or a bad exit;
#    - Get-RelativeFileList: the files of a tree, refusing junctions and symbolic links.
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
#  Text is written with explicit .NET UTF-8 (no BOM) APIs: never use
#  Set-Content -Encoding UTF8 (5.1 writes a BOM).
# ============================================================================
Set-StrictMode -Version 2.0

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:StrictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)   # throws on invalid bytes
$script:FailureLabelWidth = 6                                              # "What: ", "Why:  ", "Fix:  "
$script:FailureWhatPrefix = 'What: '
$script:OutputNamePattern = '\A[A-Za-z0-9_]+\z'

function Get-Utf8NoBomEncoding {
    [CmdletBinding()]
    param()
    return $script:Utf8NoBom
}

# Relative paths resolve against the PowerShell location, which differs from the process
# directory that [IO.Path]::GetFullPath uses once a script has called Set-Location.
function Resolve-BuildPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

# ----------------------------------------------------------------------------
#  Failure reporting
# ----------------------------------------------------------------------------
function Format-BuildFailure {
    param([string]$What, [string]$Why, [string]$Fix)
    $continuationIndent = ' ' * $script:FailureLabelWidth
    $format = {
        param([string]$Label, [string]$Text)
        $lines = @($Text -split '\r?\n')
        $first = ("{0,-$($script:FailureLabelWidth)}" -f ($Label + ':')) + $lines[0]
        $rest = @($lines | Select-Object -Skip 1 | ForEach-Object { $continuationIndent + $_ })
        return (@($first) + $rest) -join "`n"
    }
    return ((& $format 'What' $What), (& $format 'Why' $Why), (& $format 'Fix' $Fix)) -join "`n"
}

# Every failure of every build script has three parts: what failed, why it matters
# (including whether running it again can help), and how to fix it. An empty part is
# refused, so no failure can ship without one. Throws; the calling script prints the
# message (Get-ScriptFailureText) and exits non-zero.
function Write-BuildFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$What,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Why,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Fix
    )
    throw (Format-BuildFailure -What $What -Why $Why -Fix $Fix)
}

# What a script prints for a caught error: a Write-BuildFailure message as it is, anything else
# wrapped into the same three parts so even a bug in a script never prints a bare exception.
function Get-ScriptFailureText {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$ErrorRecord)
    $message = $ErrorRecord.Exception.Message
    if ($message.StartsWith($script:FailureWhatPrefix, [StringComparison]::Ordinal)) { return $message }
    return Format-BuildFailure -What "Unexpected error: $message" `
        -Why 'The script hit a problem it does not handle itself, so it reached no verdict. Running it again may help only if the cause is transient.' `
        -Fix ("Run it again; if it repeats, report it with this output.`n" + $ErrorRecord.ScriptStackTrace)
}

# ----------------------------------------------------------------------------
#  Reading
# ----------------------------------------------------------------------------
# Strict UTF-8 (invalid bytes fail). BOM POLICY: a leading BOM is kept as U+FEFF, never dropped,
# so a caller that rewrites the text stays byte-identical; a caller that parses it (headings,
# imports) strips it itself.
function Read-Utf8Text {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $resolved = Resolve-BuildPath $Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        Write-BuildFailure -What "File not found: $Path" `
            -Why 'The build reads this file to decide what to do, so it cannot continue without it.' `
            -Fix 'Check the path, and that you run the script from a complete checkout.'
    }
    try {
        return $script:StrictUtf8.GetString([IO.File]::ReadAllBytes($resolved))
    } catch [System.Text.DecoderFallbackException] {
        Write-BuildFailure -What "File is not valid UTF-8: $Path" `
            -Why 'The build reads text files as strict UTF-8; guessing another encoding would corrupt them silently.' `
            -Fix 'Re-save the file as UTF-8 (without BOM unless it already has one).'
    }
}

# Under Set-StrictMode a missing property throws a terse engine error, so look it up explicitly.
function Get-JsonProperty {
    [CmdletBinding()]
    param($Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# A JSON file as an object. An invalid file fails (What / Why / Fix) naming the file; $Why says what
# the caller cannot do without it.
function Read-JsonFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Why)
    $text = Read-Utf8Text $Path
    try {
        return ($text | ConvertFrom-Json)
    } catch {
        Write-BuildFailure -What "Invalid JSON in ${Path}: $($_.Exception.Message)" -Why $Why -Fix 'Fix the JSON syntax of the file.'
    }
}

# The property names of a JSON object (empty for a missing or non-object value).
function Get-PropertyNames {
    [CmdletBinding()]
    param($Object)
    if ($null -eq $Object) { return @() }
    return @($Object.PSObject.Properties | ForEach-Object { $_.Name })
}

# ----------------------------------------------------------------------------
#  GitHub Actions
# ----------------------------------------------------------------------------
# Appends "name=value" to $GITHUB_OUTPUT (UTF-8, no BOM: the runner would read a BOM as part of the
# first key). Does nothing outside GitHub Actions. Name and value are validated because a newline in
# either would inject a further output.
function Add-GitHubOutput {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][AllowEmptyString()][string]$Value)
    if ($Name -cnotmatch $script:OutputNamePattern) {
        Write-BuildFailure -What "Invalid step output name '$Name'." -Why 'Output names are limited to letters, digits and underscores.' -Fix 'Use a name such as release_id.'
    }
    if ($Value -match '[\r\n]') {
        Write-BuildFailure -What "The value of step output '$Name' contains a line break." -Why 'A line break would let the value inject a second output.' -Fix 'Pass a single-line value.'
    }
    if ([string]::IsNullOrEmpty($env:GITHUB_OUTPUT)) { return }
    [IO.File]::AppendAllText((Resolve-BuildPath $env:GITHUB_OUTPUT), "$Name=$Value`n", $script:Utf8NoBom)
}

# ----------------------------------------------------------------------------
#  Child processes
# ----------------------------------------------------------------------------
$script:ProcessSettleMilliseconds = 5000   # how long a stopped process, or the pipes of an exited one, may take to settle

# One argument as a Windows command-line element: quoted when it holds a space or a quote, with the
# backslash rules of CommandLineToArgvW. Windows PowerShell 5.1 has no ProcessStartInfo.ArgumentList,
# so every caller passes an ARRAY and this quotes each element (a path is data, never script text).
function ConvertTo-ArgumentElement {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    if ($Text.Length -gt 0 -and $Text -notmatch '[\s"]') { return $Text }
    $escaped = [regex]::Replace($Text, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)\z', '$1$1')
    return '"' + $escaped + '"'
}

# A start-info with all three streams redirected, no window and UTF-8 text. $EnvOverrides: name -> value;
# a $null value REMOVES the variable from the child.
function New-ChildProcessStartInfo {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$ArgumentList = @(), [string]$WorkingDirectory, [hashtable]$EnvOverrides = @{})
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath
    $info.Arguments = (@($ArgumentList | ForEach-Object { ConvertTo-ArgumentElement $_ })) -join ' '
    if ($WorkingDirectory) { $info.WorkingDirectory = Resolve-BuildPath $WorkingDirectory }
    # UseShellExecute is false below, which is what makes the variables of the child settable.
    foreach ($name in $EnvOverrides.Keys) {
        if ($null -eq $EnvOverrides[$name]) { [void]$info.EnvironmentVariables.Remove($name) }
        else { $info.EnvironmentVariables[$name] = [string]$EnvOverrides[$name] }
    }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $script:Utf8NoBom
    $info.StandardErrorEncoding = $script:Utf8NoBom
    return $info
}

# Stops ONE process AND the programs it started, by its id (never by image name: another process of
# the same program may be somebody else's). taskkill /T walks only the tree below that id, so a
# wrapper (cmd.exe, a build tool) cannot leave its child running behind a timeout.
function Stop-ChildProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    if ($Process.HasExited) { return }
    $killer = New-Object System.Diagnostics.ProcessStartInfo
    $killer.FileName = Join-Path $env:SystemRoot 'System32\taskkill.exe'
    $killer.Arguments = "/T /F /PID $($Process.Id)"
    $killer.UseShellExecute = $false
    $killer.CreateNoWindow = $true
    $killer.RedirectStandardOutput = $true
    $killer.RedirectStandardError = $true
    try {
        $started = [Diagnostics.Process]::Start($killer)
        try {
            [void]$started.StandardOutput.ReadToEndAsync()
            [void]$started.StandardError.ReadToEndAsync()
            [void]$started.WaitForExit($script:ProcessSettleMilliseconds)
        } finally { $started.Dispose() }
    } catch {
        # taskkill is unavailable: the single-process stop below still ends the process itself
    }
    if (-not $Process.HasExited) { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue }
    [void]$Process.WaitForExit($script:ProcessSettleMilliseconds)
}

# Runs a program to completion with a hard timeout and both output streams captured. Its stdin gets
# -StdinText (UTF-8, no BOM) when that is given, and is then closed: without it the program sees EOF at once.
# -EnvOverrides changes the child's environment (a $null value removes a variable). A non-zero exit does not
# throw; a timeout stops that process and says so.
function Invoke-ChildProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds = 120,
        [AllowEmptyString()][string]$StdinText,
        [hashtable]$EnvOverrides = @{}
    )
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start((New-ChildProcessStartInfo -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -EnvOverrides $EnvOverrides))
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try {
            if ($PSBoundParameters.ContainsKey('StdinText')) {
                $stdinBytes = $script:Utf8NoBom.GetBytes($StdinText)
                $process.StandardInput.BaseStream.Write($stdinBytes, 0, $stdinBytes.Length)
            }
            $process.StandardInput.Close()
        } catch [System.IO.IOException] {
            # the program stopped reading (an oversized input) and exited first: that is its answer, not a failure here
        }
        $finished = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $finished) { Stop-ChildProcess $process }
        # A grandchild that inherited the pipes can hold them open after the program has exited: take what arrived.
        $drained = [Threading.Tasks.Task]::WaitAll(@($stdout, $stderr), $script:ProcessSettleMilliseconds)
        return [pscustomobject]@{
            ExitCode  = $(if ($finished) { $process.ExitCode } else { $null })
            TimedOut  = (-not $finished)
            Stdout    = $(if ($drained) { $stdout.Result } else { '' })
            Stderr    = $(if ($drained) { $stderr.Result } else { '' })
            ElapsedMs = $clock.ElapsedMilliseconds
        }
    } finally {
        Stop-ChildProcess $process
        $process.Dispose()
    }
}

# The non-blank lines of a text: the first or the last $Count of them, or "(no output)".
function Get-OutputExcerpt {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Text, [ValidateSet('First', 'Last')][string]$Mode = 'First', [int]$Count = 8)
    $lines = @(($Text -split '\r?\n') | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { return '(no output)' }
    $picked = if ($Mode -eq 'First') { @($lines | Select-Object -First $Count) } else { @($lines | Select-Object -Last $Count) }
    return ($picked -join "`n")
}

# Turns a finished child process into a build failure: a timeout, or a non-zero exit code, with the
# program's own output attached. Returns quietly when it exited 0 in time.
function Assert-ChildOutcome {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Result, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$Why, [Parameter(Mandatory)][string]$Fix,
        [ValidateSet('First', 'Last')][string]$Mode = 'First', [int]$Count = 8
    )
    $excerpt = Get-OutputExcerpt -Text ($Result.Stdout + "`n" + $Result.Stderr) -Mode $Mode -Count $Count
    if ($Result.TimedOut) {
        Write-BuildFailure -What "$Name did not finish within $TimeoutSeconds s and was stopped.`n$excerpt" -Why $Why -Fix $Fix
    }
    if ($Result.ExitCode -ne 0) {
        Write-BuildFailure -What "$Name failed (exit code $($Result.ExitCode)):`n$excerpt" -Why $Why -Fix $Fix
    }
}

# A PowerShell script run under the SAME host that runs the caller (powershell.exe or pwsh), with the
# argument prefix every build script is started with.
function Invoke-BuildScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptPath, [string[]]$ArgumentList = @(),
        [int]$TimeoutSeconds = 120, [string]$WorkingDirectory
    )
    $hostPath = (Get-Process -Id $PID).Path
    return Invoke-ChildProcess -FilePath $hostPath -TimeoutSeconds $TimeoutSeconds -WorkingDirectory $WorkingDirectory `
        -ArgumentList (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $ArgumentList)
}

# ----------------------------------------------------------------------------
#  Git
# ----------------------------------------------------------------------------
# Runs git against a repository ("git -C <RepoRoot> <Arguments>", quote-path off so file names stay raw).
# A missing git, a timeout or a non-zero exit is a three-part failure: What names the command and carries
# its own output, Why and Fix are the caller's (what the result is needed for, and how to get it by hand).
# -SoftFail is for a caller that has a fallback: any such problem returns $null instead of failing.
function Invoke-Git {
    [CmdletBinding(DefaultParameterSetName = 'Fail')]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory, ParameterSetName = 'Fail')][string]$Why,
        [Parameter(Mandatory, ParameterSetName = 'Fail')][string]$Fix,
        [Parameter(Mandatory, ParameterSetName = 'Soft')][switch]$SoftFail,
        [int]$TimeoutSeconds = 60
    )
    $git = Get-Command git -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $git) {
        if ($SoftFail) { return $null }
        Write-BuildFailure -What 'git was not found on the PATH.' -Why $Why -Fix "Install Git for Windows and open a new shell. $Fix"
    }
    $gitArguments = @('-c', 'core.quotePath=off', '-C', $RepoRoot) + $Arguments
    if ($SoftFail) {
        try { $result = Invoke-ChildProcess -FilePath $git.Source -TimeoutSeconds $TimeoutSeconds -ArgumentList $gitArguments } catch { return $null }
        if ($result.TimedOut -or $result.ExitCode -ne 0) { return $null }
        return $result
    }
    $result = Invoke-ChildProcess -FilePath $git.Source -TimeoutSeconds $TimeoutSeconds -ArgumentList $gitArguments
    Assert-ChildOutcome -Result $result -Name "git $($Arguments -join ' ')" -TimeoutSeconds $TimeoutSeconds -Why $Why -Fix $Fix
    return $result
}

# ----------------------------------------------------------------------------
#  Files
# ----------------------------------------------------------------------------
# The names (relative to $Dir, forward slashes, ordinal order) of every file below a directory, empty
# for a missing directory. A junction or symbolic link anywhere below is refused: it would stage or
# hash files that live outside the tree, and what is outside is not what was reviewed.
function Get-RelativeFileList {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Dir)
    $resolved = Resolve-BuildPath $Dir
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { return , [string[]]@() }
    $root = [IO.Path]::GetFullPath($resolved).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($item in (Get-ChildItem -LiteralPath $resolved -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-BuildFailure -What "$($item.FullName) is a junction or symbolic link inside $Dir." `
                -Why 'A link makes the build read files from somewhere else than the reviewed tree, so the archive could contain content nobody checked in.' `
                -Fix "Delete the link (for a junction: rmdir `"$($item.FullName)`") and put real files there."
        }
        if (-not $item.PSIsContainer) { $names.Add($item.FullName.Substring($root.Length).Replace('\', '/')) }
    }
    $sorted = $names.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return , $sorted
}

# ----------------------------------------------------------------------------
#  Temp cleanup
# ----------------------------------------------------------------------------
# A scanner or a lingering handle can make the first recursive delete fail; retry, then warn
# with the path that is left behind rather than failing a verified release over it.
function Remove-BuildTempTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [int]$Attempts = 3, [int]$DelayMilliseconds = 200)
    $resolved = Resolve-BuildPath $Path
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $resolved)) { return }
        try {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -lt $Attempts -and $DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds ($DelayMilliseconds * $attempt) }
        }
    }
    if (Test-Path -LiteralPath $resolved) { Write-Warning "Could not delete the temporary directory, delete it by hand: $resolved" }
}

Export-ModuleMember -Function Get-Utf8NoBomEncoding, Resolve-BuildPath, Write-BuildFailure, Get-ScriptFailureText,
    Read-Utf8Text, Read-JsonFile, Get-JsonProperty, Get-PropertyNames, Add-GitHubOutput, Remove-BuildTempTree,
    ConvertTo-ArgumentElement, New-ChildProcessStartInfo, Stop-ChildProcess, Invoke-ChildProcess,
    Get-OutputExcerpt, Assert-ChildOutcome, Invoke-BuildScript, Invoke-Git, Get-RelativeFileList
