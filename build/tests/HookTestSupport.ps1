# ============================================================================
#  HookTestSupport.ps1 - what the plugin hook test files share (dot-source it AFTER
#  TestHarness.ps1):  paths, a process runner, the platform-guard station fixture and the
#  local release-server stub. It never touches the real %LOCALAPPDATA% or ~/.claude: every
#  hook runs with TESTSTAND_MCP_STATE_DIR in a temp folder.
#  ASCII only (5.1 reads a BOM-less script as ANSI): non-ASCII characters are built from code points.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$HooksDir = Join-Path $RepoRoot 'plugin\hooks'
$IdentityModulePath = Join-Path $RepoRoot 'build\PluginIdentity.psm1'
$ToolClassesPath = Join-Path $HooksDir 'tool-classes.json'
$HostPath = (Get-Process -Id $PID).Path
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$FixedNow = [DateTimeOffset]::new(2026, 10, 24, 12, 0, 0, [TimeSpan]::Zero)   # the injected clock (TESTSTAND_MCP_NOW)
$Hours = 3600
$ProcessTimeoutSeconds = 120

Import-Module (Join-Path $RepoRoot 'build\BuildCommon.psm1') -Force   # Invoke-ChildProcess, ConvertTo-ArgumentElement
Import-Module $IdentityModulePath -Force
$IdentityPrefix = (Get-PluginIdentity).pluginToolPrefix
$PluginName = (Get-PluginIdentity).name

# ---- processes ----------------------------------------------------------------------------------
# Runs a program with stdin text and a controlled environment (BuildCommon's Invoke-ChildProcess, which quotes
# each argument like a spawned "args" list); never fails on a non-zero exit, but kills it and throws when it
# runs longer than two minutes. $EnvOverrides: name -> value; a $null value REMOVES the variable from the child.
function Invoke-HookProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [AllowEmptyString()][string]$StdinText = '',
        [hashtable]$EnvOverrides = @{}
    )
    $result = Invoke-ChildProcess -FilePath $FilePath -ArgumentList $ArgumentList -StdinText $StdinText -EnvOverrides $EnvOverrides -TimeoutSeconds $ProcessTimeoutSeconds
    if ($result.TimedOut) { throw "$FilePath ran longer than $ProcessTimeoutSeconds s and was killed" }
    return $result
}

# Runs a PowerShell script under the SAME host that runs this file.
function Invoke-HookScript {
    param(
        [Parameter(Mandatory)][string]$ScriptPath,
        [AllowEmptyString()][string]$StdinText = '',
        [hashtable]$EnvOverrides = @{}
    )
    return Invoke-HookProcess -FilePath $HostPath -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) -StdinText $StdinText -EnvOverrides $EnvOverrides
}

# The hook input a tool call produces: tool name, tool_input and (unless $null) the session's cwd.
function ConvertTo-HookInput {
    param([string]$ToolName, $ToolInput = @{}, [AllowNull()][string]$Cwd = $null)
    $request = [ordered]@{ hook_event_name = 'PreToolUse'; tool_name = $ToolName; tool_input = $ToolInput }
    if ($null -ne $Cwd) { $request['cwd'] = $Cwd }
    return ($request | ConvertTo-Json -Compress -Depth 6)
}
function Assert-AsciiOnly {
    param([string]$Text, [string]$What = 'output')
    foreach ($char in $Text.ToCharArray()) {
        if ([int]$char -gt 127) { throw "$What contains the non-ASCII character U+$('{0:X4}' -f [int]$char)" }
    }
}

# How many times a text occurs in another (ordinal).
function Get-OccurrenceCount {
    param([string]$Text, [string]$Needle)
    $count = 0; $index = 0
    while (($index = $Text.IndexOf($Needle, $index, [StringComparison]::Ordinal)) -ge 0) { $count++; $index += $Needle.Length }
    return $count
}

# A copy of plugin/hooks in a fresh temp folder, minus the named files.
function New-HooksCopy {
    param([string[]]$Without = @(), [string]$Suffix = '')
    $dir = New-TempDir $Suffix
    Get-ChildItem -LiteralPath $HooksDir -File | Where-Object { $Without -notcontains $_.Name } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $dir
    }
    return $dir
}

# A plugin root whose plugin.json carries the given name (or the given raw text).
function New-PluginRootFixture {
    param([string]$Name, [string]$RawJson = $null)
    $root = New-TempDir
    $text = if ($RawJson) { $RawJson } else { '{"name": "' + $Name + '", "version": "0.0.0"}' }
    Write-TextFile (Join-Path $root '.claude-plugin\plugin.json') $text
    return $root
}

# ---- the platform-guard station ------------------------------------------------------------------
# A synthetic station: temp Program Files roots and stub py / python launchers on a private PATH. Each
# stub appends its arguments to a log, prints the text in its *_OUT variable and exits with *_EXIT.
function New-GuardFixture {
    param(
        [switch]$NoTestStand, [switch]$NoBrowser, [switch]$TestStand64,
        [AllowNull()][string]$PyOut = '1 1', [int]$PyExit = 0,
        [AllowNull()][string]$PythonOut = $null, [int]$PythonExit = 0,
        [string]$StubBody = ''
    )
    $root = New-TempDir
    $programFiles = Join-Path $root 'pf'
    $programFilesX86 = Join-Path $root 'pf86'
    $bin = Join-Path $root 'bin'
    $state = Join-Path $root 'state'
    foreach ($dir in $programFiles, $programFilesX86, $bin) { [void](New-Item -ItemType Directory -Path $dir) }
    # The default station has 32-bit TestStand only; -TestStand64 adds the 64-bit install under the 64-bit root.
    if (-not $NoTestStand) { [void](New-Item -ItemType Directory -Path (Join-Path $programFilesX86 'National Instruments\TestStand 2024')) }
    if ($TestStand64) { [void](New-Item -ItemType Directory -Path (Join-Path $programFiles 'National Instruments\TestStand 2024')) }
    if (-not $NoBrowser) {
        $edgeDir = Join-Path $programFiles 'Microsoft\Edge\Application'
        [void](New-Item -ItemType Directory -Path $edgeDir)
        [IO.File]::WriteAllText((Join-Path $edgeDir 'msedge.exe'), '', $Utf8NoBom)
    }
    $stubs = @{ py = $PyOut; python = $PythonOut }
    foreach ($name in $stubs.Keys) {
        if ($null -eq $stubs[$name]) { continue }
        $upper = $name.ToUpperInvariant()
        $body = "@echo off`r`necho %*>>`"%STUB_${upper}_LOG%`"`r`n${StubBody}echo %STUB_${upper}_OUT%`r`nexit /b %STUB_${upper}_EXIT%`r`n"
        [IO.File]::WriteAllText((Join-Path $bin "$name.cmd"), $body, $Utf8NoBom)
    }
    $envMap = @{
        TESTSTAND_MCP_STATE_DIR           = $state
        TESTSTAND_MCP_NOW                 = $FixedNow.ToString('o')
        TESTSTAND_MCP_NO_PLATFORM_WARNING = $null
        TestStand                         = $null
        TestStand64                       = $null
        CLAUDE_PLUGIN_ROOT                = $null
        CLAUDE_PLUGIN_DATA                = $null
        ProgramFiles                      = $programFiles
        ProgramW6432                      = $programFiles
        'ProgramFiles(x86)'               = $programFilesX86
        PATH                              = "$bin;$env:SystemRoot\System32"
        STUB_PY_OUT                       = $PyOut
        STUB_PY_EXIT                      = $PyExit
        STUB_PY_LOG                       = (Join-Path $root 'py.log')
        STUB_PYTHON_OUT                   = $PythonOut
        STUB_PYTHON_EXIT                  = $PythonExit
        STUB_PYTHON_LOG                   = (Join-Path $root 'python.log')
    }
    return [pscustomobject]@{
        Env = $envMap; State = $state; Root = $root; Bin = $bin; ProgramFiles = $programFiles; ProgramFilesX86 = $programFilesX86
        PyLog = $envMap['STUB_PY_LOG']; PythonLog = $envMap['STUB_PYTHON_LOG']
    }
}

# The lines a stub interpreter logged (one per start); empty when it never ran.
function Get-StubLog {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return , [string[]]@() }
    return , [string[]]@([IO.File]::ReadAllLines($Path))
}

# Runs the guard; .Json is the parsed stdout (or $null), .Message its systemMessage (or $null), .Context its additionalContext.
function Invoke-Guard {
    param($Fixture, [hashtable]$Extra = @{}, [DateTimeOffset]$Now = $FixedNow, [string]$ScriptPath = (Join-Path $HooksDir 'platform-guard.ps1'))
    $envOverrides = @{}
    foreach ($key in $Fixture.Env.Keys) { $envOverrides[$key] = $Fixture.Env[$key] }
    $envOverrides['TESTSTAND_MCP_NOW'] = $Now.ToString('o')
    foreach ($key in $Extra.Keys) { $envOverrides[$key] = $Extra[$key] }
    $result = Invoke-HookScript -ScriptPath $ScriptPath -StdinText '{"hook_event_name":"SessionStart","source":"startup"}' -EnvOverrides $envOverrides
    $json = $null; $message = $null; $context = $null
    if ($result.Stdout.Trim().Length -gt 0) {
        $json = $result.Stdout | ConvertFrom-Json
        if ($null -ne $json.PSObject.Properties['systemMessage']) { $message = $json.systemMessage }
        if ($null -ne $json.PSObject.Properties['hookSpecificOutput']) { $context = $json.hookSpecificOutput.additionalContext }
    }
    $result | Add-Member -NotePropertyName Json -NotePropertyValue $json
    $result | Add-Member -NotePropertyName Message -NotePropertyValue $message
    $result | Add-Member -NotePropertyName Context -NotePropertyValue $context
    return $result
}

# ---- the local release server ---------------------------------------------------------------------
# A local HttpListener stands in for github.com/<repo>/releases/latest. It runs in its own runspace and
# answers every request according to $Mode: redirect (302 + Location), status (a plain status code),
# nolocation (302 without a Location header) or hang (holds the request for 20 s). It does NOT simulate
# a blackholed connect (a packet drop before the TCP handshake): a hang here is "accepted, never answered".
$StubServerSource = {
    param($Prefix, $Mode, $Location, $StatusCode, $Stats)
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add($Prefix)
    try {
        $listener.Start()
    } catch {
        $Stats.Failed = $true
        return
    }
    $Stats.Ready = $true
    try {
        while (-not $Stats.Stop) {
            $pending = $listener.GetContextAsync()
            while (-not $pending.Wait(50)) { if ($Stats.Stop) { return } }
            $context = $pending.Result
            $Stats.RequestCount = $Stats.RequestCount + 1
            $Stats.RequestMethod = $context.Request.HttpMethod
            $Stats.RequestPath = $context.Request.Url.AbsolutePath
            switch ($Mode) {
                'redirect'   { $context.Response.StatusCode = 302; $context.Response.RedirectLocation = $Location; $context.Response.Close() }
                'status'     { $context.Response.StatusCode = $StatusCode; $context.Response.Close() }
                'nolocation' { $context.Response.StatusCode = 302; $context.Response.Close() }
                'hang' {
                    $waited = 0
                    while ($waited -lt 20000 -and -not $Stats.Stop) { Start-Sleep -Milliseconds 100; $waited += 100 }
                    $context.Response.Abort()
                }
            }
        }
    } finally {
        $listener.Abort()
    }
}

# A port that was free a moment ago; retries when binding the probe socket fails.
function Get-FreeLoopbackPort {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $probe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        try {
            $probe.Start()
            return $probe.LocalEndpoint.Port
        } catch {
            if ($attempt -eq 5) { throw }
        } finally {
            $probe.Stop()
        }
    }
}

$script:StubServers = @()

function Start-StubReleaseServer {
    param([ValidateSet('redirect', 'status', 'nolocation', 'hang')][string]$Mode, [string]$Location = '', [int]$StatusCode = 500)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $port = Get-FreeLoopbackPort
        $stats = [hashtable]::Synchronized(@{ Ready = $false; Failed = $false; Stop = $false; RequestCount = 0; RequestMethod = ''; RequestPath = '' })
        $shell = [powershell]::Create()
        [void]$shell.AddScript($StubServerSource).AddArgument("http://127.0.0.1:$port/").AddArgument($Mode).AddArgument($Location).AddArgument($StatusCode).AddArgument($stats)
        $handle = $shell.BeginInvoke()
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not $stats.Ready -and -not $stats.Failed -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 20 }
        $server = [pscustomobject]@{ BaseUrl = "http://127.0.0.1:$port/Zuehlke/teststand-mcp"; Stats = $stats; Shell = $shell; Handle = $handle }
        if ($stats.Ready) {
            $script:StubServers += $server
            return $server
        }
        Stop-StubReleaseServer $server   # a bind conflict (or a slow start): try another port
    }
    throw 'the stub release server did not start'
}

function Stop-StubReleaseServer {
    param($Server)
    if ($Server.Stats.Stop) { return }
    $Server.Stats.Stop = $true
    try { [void]$Server.Shell.EndInvoke($Server.Handle) } catch { }
    $Server.Shell.Dispose()
}

function Stop-AllStubServers {
    foreach ($server in $script:StubServers) { Stop-StubReleaseServer $server }
    $script:StubServers = @()
}

# A case that starts stub servers: they are stopped when the case ends, however it ends.
function Invoke-ServerCase {
    param([string]$Name, [scriptblock]$Body)
    try { Invoke-Case $Name $Body } finally { Stop-AllStubServers }
}
