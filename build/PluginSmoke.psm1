# ============================================================================
#  PluginSmoke.psm1 - the smoke tests of the STAGED exe (spec AC-3, AC-6), run on a machine with no
#  TestStand: every check starts the exe from the staged tree and nothing else.
#    Assert-VersionOutput        --version names the version, the tag, the commit and the architecture (stdout, exit 0)
#    Assert-ToolList             --list-tools --json equals the tools classified in tool-classes.json
#    Assert-InitializeRoundTrip  a stdio initialize returns serverInfo.version, and the startup log
#                                names the same version
#  Each process is started here with a timeout and stopped, with the programs it started, by its own
#  process id.
#
#  initialize does NOT connect the TestStand engine (read in src/McpServer.cs): HandleInitialize
#  negotiates the protocol version, logs, and answers with VersionInfo.Current. The engine is
#  connected only by a tool call (ConnectAsync / EnsureConnected in TestStandService), and
#  ApplyStationDefaults only stores the configured values. So this check needs no TestStand.
#
#  The startup log cannot be read from a redirected stderr: with stdin redirected, Program.cs
#  allocates its own console window and writes the banner and the log there (so a human can watch
#  it), and nothing reaches the redirected stderr. Assert-StartupLogVersion therefore reads stderr
#  when it carries a startup line, and otherwise checks in Program.cs that the startup log call
#  prints VersionInfo.Current.Version, the same value serverInfo reports (spec AC-3).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')
Import-Module (Join-Path $PSScriptRoot 'ToolClassification.psm1')
Import-Module (Join-Path $PSScriptRoot 'PluginStaging.psm1')

$script:CommandTimeoutSeconds = 60       # a cold first start under Defender can be slow (the spike measured 0.4 s)
$script:InitializeTimeoutSeconds = 60
$script:ExitAfterEofSeconds = 15
$script:StderrDrainMilliseconds = 5000
$script:ProgramSourceRelative = 'Program.cs'
$script:InitializeRequest = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"packaging-smoke","version":"0"}}}'
# The startup log call: LogInformation("... starting ...", ..., VersionInfo.Current.Version)
$script:StartupLogSourcePattern = 'LogInformation\([^\r\n]*starting[^\r\n]*VersionInfo\.Current\.Version'
$script:StartupLogLinePattern = '(?im)^.*\bstarting\b.*$'
$script:UntaggedMarker = '-dev'
$script:MetadataNoteLabel = 'note:'

# Runs the exe with arguments; fails when it times out or exits non-zero (exit 0 is part of every smoke test).
function Invoke-SmokeCommand {
    param([string]$ExePath, [string[]]$Arguments)
    $shown = "$([IO.Path]::GetFileName($ExePath)) $($Arguments -join ' ')"
    $result = Invoke-ChildProcess -FilePath $ExePath -ArgumentList $Arguments -TimeoutSeconds $script:CommandTimeoutSeconds
    Assert-ChildOutcome -Result $result -Name $shown -TimeoutSeconds $script:CommandTimeoutSeconds `
        -Why 'The staged exe must answer its informational flags at once, without an engine; if it hangs or fails it would not start for a user either.' `
        -Fix "Run it by hand from the staged tree: $shown"
    return $result
}

# True when $Text holds $Version as a whole version number: not glued to more digits or letters
# ("11.9.0", "1.9.0rc"), not continued as another version part ("1.9.0.1", "1.9.0-rc").
function Test-ContainsVersion {
    param([AllowEmptyString()][string]$Text, [string]$Version)
    return [regex]::IsMatch($Text, '(?<![0-9.])' + [regex]::Escape($Version) + '(?![0-9A-Za-z]|[.-][0-9A-Za-z])')
}

function Assert-VersionOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExePath, [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][string]$Commit, [Parameter(Mandatory)][string]$Arch
    )
    $result = Invoke-SmokeCommand $ExePath @('--version')
    $problems = @()
    # The first line is "<product> <version>"; the version must be its last word (the tag line also holds "1.9.0" inside "v1.9.0").
    $firstWord = @(($result.Stdout -split '\r?\n')[0].Trim() -split '\s+')[-1]
    if (-not [string]::Equals($firstWord, $Version, [StringComparison]::Ordinal)) { $problems += "its first line ends with '$firstWord', not '$Version'" }
    foreach ($expected in @($Tag, $Commit)) {
        if ($result.Stdout.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) { $problems += "it does not name '$expected'" }
    }
    $archMatch = [regex]::Match($result.Stdout, '(?m)^\s*arch:\s*(\S+)')
    $reportedArch = if ($archMatch.Success) { $archMatch.Groups[1].Value } else { '(none)' }
    if (-not [string]::Equals($reportedArch, $Arch, [StringComparison]::Ordinal)) { $problems += "it reports arch '$reportedArch' but the archive is for '$Arch'" }
    if ($Version.IndexOf($script:UntaggedMarker, [StringComparison]::Ordinal) -lt 0 -and $result.Stdout.IndexOf($script:UntaggedMarker, [StringComparison]::Ordinal) -ge 0) {
        $problems += "it reports an untagged build ('$script:UntaggedMarker')"
    }
    if ($result.Stdout.IndexOf($script:MetadataNoteLabel, [StringComparison]::Ordinal) -ge 0) {
        $problems += 'it carries a release-metadata note, so VERSION.txt was not read'
    }
    if ($problems.Count -gt 0) {
        Write-BuildFailure -What "The staged exe's --version output is wrong ($($problems -join '; ')):`n$(Get-OutputExcerpt $result.Stdout)" `
            -Why 'Users and support read --version to learn which release they run; the release must name its own version, tag, commit and architecture (an exe of the wrong bitness cannot load its TestStand).' `
            -Fix 'Check VERSION.txt in the staged tree and the -Version, -Tag, -Commit and -Architecture passed to Build-PluginArchive.ps1, then package again.'
    }
}

function Assert-ToolList {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ExePath, [Parameter(Mandatory)][string]$ClassesPath, [Parameter(Mandatory)][string]$RegistryPath)
    $result = Invoke-SmokeCommand $ExePath @('--list-tools', '--json')
    try {
        $listed = @($result.Stdout | ConvertFrom-Json | ForEach-Object { $_ })
    } catch {
        Write-BuildFailure -What "--list-tools --json did not print a JSON array: $(Get-OutputExcerpt $result.Stdout)" `
            -Why 'The release checks and the hook classification read this list; text on stdout that is not JSON breaks them.' `
            -Fix 'Run the staged exe by hand with --list-tools --json and check that nothing else writes to stdout (see TryRunInformationalCommand in Program.cs).'
    }
    $classified = Get-ClassifiedToolNames -ClassesPath $ClassesPath
    $registered = @(Get-RegisteredToolNames -RegistryPath $RegistryPath)
    $unclassified = @($listed | Where-Object { $classified -cnotcontains $_ })
    $notListed = @($classified | Where-Object { $listed -cnotcontains $_ })
    if ($unclassified.Count -gt 0 -or $notListed.Count -gt 0 -or $listed.Count -ne $registered.Count) {
        $detail = @("the exe lists $($listed.Count) tools, the registry has $($registered.Count)")
        if ($unclassified.Count -gt 0) { $detail += "listed by the exe but not classified: $($unclassified -join ', ')" }
        if ($notListed.Count -gt 0) { $detail += "classified but not listed by the exe: $($notListed -join ', ')" }
        Write-BuildFailure -What "--list-tools --json does not match plugin/hooks/tool-classes.json ($($detail -join '; '))." `
            -Why 'The auto-allow hook decides per tool name; a tool the exe serves but the classification lacks would prompt unreviewed, and a stale entry hides that a tool was removed.' `
            -Fix 'Put every registered tool in exactly one of the allow and prompt lists of plugin/hooks/tool-classes.json (build\tests\ToolClassification.Tests.ps1 names the rule), then package again.'
    }
}

# Starts the exe, sends one initialize request on stdin, and returns the reply line and what reached
# stderr. An exe that never answers is stopped at once (no wait for it to exit by itself).
function Invoke-InitializeExchange {
    param([string]$ExePath, [int]$TimeoutSeconds)
    $process = [Diagnostics.Process]::Start((New-ChildProcessStartInfo -FilePath $ExePath))
    try {
        $stderr = $process.StandardError.ReadToEndAsync()
        $reply = $process.StandardOutput.ReadLineAsync()
        $request = (Get-Utf8NoBomEncoding).GetBytes($script:InitializeRequest + "`n")
        $answered = $false
        try {
            $process.StandardInput.BaseStream.Write($request, 0, $request.Length)
            $process.StandardInput.BaseStream.Flush()
            $answered = $reply.Wait($TimeoutSeconds * 1000)
        } catch [System.IO.IOException] {
            # the exe ended before it read the request: reported by the caller as "no reply"
        }
        if ($answered) {
            $process.StandardInput.Close()
            [void]$process.WaitForExit($script:ExitAfterEofSeconds * 1000)
        }
        Stop-ChildProcess $process
        $errorText = ''
        if ($stderr.Wait($script:StderrDrainMilliseconds)) { $errorText = $stderr.Result }
        return [pscustomobject]@{ Answered = $answered; Reply = $(if ($answered) { $reply.Result } else { $null }); Stderr = $errorText }
    } finally {
        Stop-ChildProcess $process
        $process.Dispose()
    }
}

# The startup log names the version. When stderr carries a startup line it must hold the version as
# a whole number; when it carries none (the log goes to the exe's own console, see the module
# header) the source must log VersionInfo.Current.Version in a LogInformation call.
function Assert-StartupLogVersion {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$StderrText, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$ProgramSourcePath)
    $why = 'The startup log is what a user pastes into a bug report; it must name the release that wrote it.'
    $startupLines = @([regex]::Matches($StderrText, $script:StartupLogLinePattern) | ForEach-Object { $_.Value })
    if ($startupLines.Count -gt 0) {
        if (Test-ContainsVersion ($startupLines -join "`n") $Version) { return }
        Write-BuildFailure -What "The startup log (stderr) does not name version ${Version}:`n$(Get-OutputExcerpt ($startupLines -join "`n"))" -Why $why `
            -Fix 'Check that Program.cs logs VersionInfo.Current.Version in its startup line, then package again.'
    }
    if ((Read-Utf8Text $ProgramSourcePath) -cnotmatch $script:StartupLogSourcePattern) {
        Write-BuildFailure -What "No startup line reached stderr, and $ProgramSourcePath has no LogInformation call that prints VersionInfo.Current.Version in its startup line." -Why $why `
            -Fix 'Log the version in the startup line of Program.cs: logger.LogInformation("{Product} v{Version} starting...", VersionInfo.ProductName, VersionInfo.Current.Version);'
    }
}

function Assert-InitializeRoundTrip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExePath, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$ProgramSourcePath,
        [int]$TimeoutSeconds = $script:InitializeTimeoutSeconds
    )
    $exchange = Invoke-InitializeExchange -ExePath $ExePath -TimeoutSeconds $TimeoutSeconds
    if (-not $exchange.Answered) {
        Write-BuildFailure -What "The staged exe did not answer an MCP initialize request within $TimeoutSeconds s (stderr: $(Get-OutputExcerpt $exchange.Stderr))." `
            -Why 'This is the first thing Claude Code does with the server; if it is not answered the plugin shows as failed to start.' `
            -Fix 'Start the staged exe by hand (bin\TestStandMCP.exe) with the initialize request on stdin and read what it prints.'
    }
    try {
        $actual = (($exchange.Reply | ConvertFrom-Json).result.serverInfo.version)
    } catch {
        $actual = "(unreadable reply: $($exchange.Reply))"
    }
    if (-not [string]::Equals([string]$actual, $Version, [StringComparison]::Ordinal)) {
        Write-BuildFailure -What "The initialize reply carries serverInfo.version '$actual' but version $Version was built." `
            -Why 'Clients show and log serverInfo.version; it must be the release the user installed.' `
            -Fix 'Check that McpServer.cs answers with VersionInfo.Current.ToServerInfo(), then package again.'
    }
    Assert-StartupLogVersion -StderrText $exchange.Stderr -Version $Version -ProgramSourcePath $ProgramSourcePath
}

# All three smoke tests against the staged exe.
function Invoke-SmokeTests {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$StageDir, [Parameter(Mandatory)][hashtable]$Release, [Parameter(Mandatory)][string]$RepoRoot)
    $exe = Get-StagedExePath -StageDir $StageDir
    $tools = Get-ToolSourcePath -RepoRoot $RepoRoot
    Assert-VersionOutput -ExePath $exe -Version $Release.version -Tag $Release.tag -Commit $Release.commit -Arch $Release.arch
    Assert-ToolList -ExePath $exe -ClassesPath $tools.Classes -RegistryPath $tools.Registry
    Assert-InitializeRoundTrip -ExePath $exe -Version $Release.version -ProgramSourcePath (Join-Path $RepoRoot $script:ProgramSourceRelative)
}

Export-ModuleMember -Function Assert-VersionOutput, Assert-ToolList, Assert-StartupLogVersion, Assert-InitializeRoundTrip, Invoke-SmokeTests, Test-ContainsVersion
