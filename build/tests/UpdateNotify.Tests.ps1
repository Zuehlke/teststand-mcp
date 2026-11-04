# ============================================================================
#  UpdateNotify.Tests.ps1 - plain PowerShell asserts for plugin/hooks/update-notify.ps1: one ASCII
#  message when a newer release exists, announced at most once per 24 hours, silent on every
#  failure. A local HttpListener stands in for github.com; the clock, the state folder, the
#  plugin root and the base URL are injected through environment variables.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\UpdateNotify.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  Set TESTSTAND_MCP_TIMING_CHECK=1 to also assert the wall-time bound of a hanging lookup.
#  This file is ASCII only on purpose (5.1 reads a BOM-less script as ANSI).
# ============================================================================
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'UpdateNotifyTests_'
. (Join-Path $PSScriptRoot 'HookTestSupport.ps1')

$NotifyScriptPath = Join-Path $HooksDir 'update-notify.ps1'
$NotifyStateFile = 'update-notify.json'
$MarketplaceName = (Get-Content -LiteralPath (Join-Path $RepoRoot '.claude-plugin\marketplace.json') -Raw | ConvertFrom-Json).name
$PluginJsonPath = Join-Path $RepoRoot 'plugin\.claude-plugin\plugin.json'
$NewerTag = 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v1.10.0'
$HttpTimeoutMilliseconds = 3000
$WallTimeBoundMilliseconds = 5000   # HTTP budget plus the shell start, checked only when TESTSTAND_MCP_TIMING_CHECK=1

# A plugin root holding .claude-plugin/plugin.json (name, version, repository), a state directory and
# a stub server. The repository URL is the stub server, so the hook finds it the way it does in production.
function New-NotifyFixture {
    param([string]$Installed = '1.9.0', [string]$Name = $PluginName, [string]$Mode = 'redirect', [string]$Location = $NewerTag,
          [int]$StatusCode = 500, [switch]$NoServer, [switch]$NoPluginJson, [string]$Repository = $null)
    $root = New-TempDir
    $pluginRoot = Join-Path $root 'plugin-root'
    [void](New-Item -ItemType Directory -Path (Join-Path $pluginRoot '.claude-plugin'))
    $server = $null
    $baseUrl = 'http://127.0.0.1:' + (Get-FreeLoopbackPort) + '/Zuehlke/teststand-mcp'   # nothing listens there
    if (-not $NoServer) { $server = Start-StubReleaseServer -Mode $Mode -Location $Location -StatusCode $StatusCode; $baseUrl = $server.BaseUrl }
    if (-not $NoPluginJson) {
        $repositoryUrl = if ($Repository) { $Repository } else { $baseUrl }
        $json = [ordered]@{ name = $Name; version = $Installed; repository = $repositoryUrl } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText((Join-Path $pluginRoot '.claude-plugin\plugin.json'), $json, $Utf8NoBom)
    }
    return [pscustomobject]@{ PluginRoot = $pluginRoot; State = (Join-Path $root 'state'); Server = $server; BaseUrl = $baseUrl }
}

function Invoke-Notify {
    param($Fixture, [DateTimeOffset]$Now = $FixedNow, [hashtable]$Extra = @{})
    $envOverrides = @{
        CLAUDE_PLUGIN_ROOT              = $Fixture.PluginRoot
        TESTSTAND_MCP_STATE_DIR         = $Fixture.State
        TESTSTAND_MCP_NOW               = $Now.ToString('o')
        TESTSTAND_MCP_RELEASES_BASE_URL = $null     # the hook finds the repository in plugin.json
        TESTSTAND_MCP_NO_UPDATE_CHECK   = $null
        CLAUDE_PLUGIN_DATA              = $null
    }
    foreach ($key in $Extra.Keys) { $envOverrides[$key] = $Extra[$key] }
    $result = Invoke-HookScript -ScriptPath $NotifyScriptPath -StdinText '{"hook_event_name":"SessionStart","source":"startup"}' -EnvOverrides $envOverrides
    $json = $null; $message = $null
    if ($result.Stdout.Trim().Length -gt 0) {
        $json = $result.Stdout | ConvertFrom-Json
        if ($null -ne $json.PSObject.Properties['systemMessage']) { $message = $json.systemMessage }
    }
    $result | Add-Member -NotePropertyName Json -NotePropertyValue $json
    $result | Add-Member -NotePropertyName Message -NotePropertyValue $message
    return $result
}

function Assert-NotifySilent {
    param($Result, [string]$What = 'hook')
    Assert-Equal 0 $Result.ExitCode "$What exit code (stderr: $($Result.Stderr))"
    Assert-Equal '' $Result.Stdout "$What stdout"
    Assert-Equal '' $Result.Stderr "$What stderr"
}

# Writes the hook's own state file, to put it into a state no sequence of runs reaches in one step.
function Set-NotifyState {
    param($Fixture, [Nullable[long]]$LookupAgeSeconds = $null, [bool]$LookupOk = $true, [string]$Latest = '',
          [string]$Announced = '', [Nullable[long]]$AnnouncedAgeSeconds = $null)
    $state = [ordered]@{}
    if ($null -ne $LookupAgeSeconds) { $state['lookupEpoch'] = $FixedNow.ToUnixTimeSeconds() - $LookupAgeSeconds; $state['lookupOk'] = $LookupOk; $state['latestVersion'] = $Latest }
    if ($null -ne $AnnouncedAgeSeconds) { $state['announcedVersion'] = $Announced; $state['announcedEpoch'] = $FixedNow.ToUnixTimeSeconds() - $AnnouncedAgeSeconds }
    [void][IO.Directory]::CreateDirectory($Fixture.State)
    [IO.File]::WriteAllText((Join-Path $Fixture.State $NotifyStateFile), ($state | ConvertTo-Json -Compress), $Utf8NoBom)
}

try {
    Import-Module (Join-Path $RepoRoot 'build\ReleaseAssets.psm1') -Force   # $ReleaseRepoSlug

    Invoke-ServerCase 'notify: an installed version behind the latest release prints one ASCII message with the two commands in order' {
        $fixture = New-NotifyFixture
        $result = Invoke-Notify $fixture
        Assert-Equal 0 $result.ExitCode "exit code (stderr: $($result.Stderr))"
        Assert-Equal '' $result.Stderr 'stderr'
        Assert-AsciiOnly $result.Stdout 'stdout'
        Assert-True ($null -ne $result.Message) 'expected a systemMessage'
        Assert-Equal 1 (Get-OccurrenceCount $result.Stdout '"systemMessage"') 'systemMessage occurrences in the raw output'
        Assert-Equal 1 @($result.Json.PSObject.Properties).Count 'output fields'
        foreach ($part in '1.9.0', '1.10.0', 'restart Claude Code', 'auto-update') { Assert-Contains $result.Message $part 'message' }
        $first = "1. claude plugin marketplace update $MarketplaceName"
        $second = "2. claude plugin update $PluginName"
        Assert-Contains $result.Message $first 'step 1'
        Assert-Contains $result.Message $second 'step 2'
        Assert-True ($result.Message.IndexOf($first, [StringComparison]::Ordinal) -lt $result.Message.IndexOf($second, [StringComparison]::Ordinal)) 'step 1 comes before step 2'
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests'
        Assert-Equal 'HEAD' $fixture.Server.Stats.RequestMethod 'request method'
        Assert-Equal '/Zuehlke/teststand-mcp/releases/latest' $fixture.Server.Stats.RequestPath 'request path'
    }

    Invoke-Case 'state: without CLAUDE_PLUGIN_DATA each plugin keeps its cache in LOCALAPPDATA\<its own name>' {
        $localAppData = New-TempDir
        foreach ($name in @((Get-PluginIdentity -Architecture 'x86').name, (Get-PluginIdentity -Architecture 'x64').name)) {
            $fixture = New-NotifyFixture -Name $name -NoServer
            $dir = Use-Environment @{ CLAUDE_PLUGIN_ROOT = $fixture.PluginRoot; TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $null; LOCALAPPDATA = $localAppData } {
                . (Join-Path $HooksDir 'hook-common.ps1')
                Get-HookStateDir
            }
            Assert-Equal (Join-Path $localAppData $name) $dir "state folder of $name"
        }
    }

    Invoke-ServerCase 'notify: the plugin name in the update command comes from plugin.json' {
        $fixture = New-NotifyFixture -Name 'some-other-plugin'
        Assert-Contains (Invoke-Notify $fixture).Message '2. claude plugin update some-other-plugin' 'step 2'
    }

    Invoke-ServerCase 'notify: the same version is announced once, then not again 3 hours later (and no second request)' {
        $fixture = New-NotifyFixture
        Assert-True ($null -ne (Invoke-Notify $fixture).Message) 'first run announces'
        Assert-NotifySilent (Invoke-Notify $fixture -Now $FixedNow.AddHours(3)) 'second run'
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests (the lookup is cached for 24 h)'
    }

    $SilentRows = @(
        @{ Name = 'the installed version equals the latest';           Installed = '1.10.0'; Mode = 'redirect'; Location = $NewerTag; Requests = 1 }
        @{ Name = 'the latest is lower than the installed version';    Installed = '1.11.0'; Mode = 'redirect'; Location = $NewerTag; Requests = 1 }
        @{ Name = 'the installed version is the 0.0.0 placeholder';    Installed = '0.0.0';  Mode = 'redirect'; Location = $NewerTag; Requests = 0 }
        @{ Name = 'the installed version is not a version';            Installed = 'dev';    Mode = 'redirect'; Location = $NewerTag; Requests = 0 }
        @{ Name = 'the installed version has two parts (1.9)';         Installed = '1.9';    Mode = 'redirect'; Location = $NewerTag; Requests = 0 }
        @{ Name = 'the installed version has four parts (1.9.0.1)';    Installed = '1.9.0.1'; Mode = 'redirect'; Location = $NewerTag; Requests = 0 }
        @{ Name = 'the installed version is a pre-release (1.9.0-rc1)'; Installed = '1.9.0-rc1'; Mode = 'redirect'; Location = $NewerTag; Requests = 0 }
        @{ Name = 'the lookup returns a server error';                 Installed = '1.9.0';  Mode = 'status';   StatusCode = 500; Requests = 1 }
        @{ Name = 'the lookup returns 404';                            Installed = '1.9.0';  Mode = 'status';   StatusCode = 404; Requests = 1 }
        @{ Name = 'the lookup answers 200 and no redirect';            Installed = '1.9.0';  Mode = 'status';   StatusCode = 200; Requests = 1 }
        @{ Name = 'the redirect has no Location header';               Installed = '1.9.0';  Mode = 'nolocation'; Requests = 1 }
        @{ Name = 'the redirect is an unparsable text';                Installed = '1.9.0';  Mode = 'redirect'; Location = '/banana'; Requests = 1 }
        @{ Name = 'the redirect does not name a tag';                  Installed = '1.9.0';  Mode = 'redirect'; Location = 'https://github.com/Zuehlke/teststand-mcp/releases'; Requests = 1 }
        @{ Name = 'the redirect names a malformed tag (v1.x)';         Installed = '1.9.0';  Mode = 'redirect'; Location = 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v1.x'; Requests = 1 }
        @{ Name = 'the redirect names a two-part tag (v2.0)';          Installed = '1.9.0';  Mode = 'redirect'; Location = 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v2.0'; Requests = 1 }
        @{ Name = 'the redirect names a pre-release tag (v2.0.0-rc1)'; Installed = '1.9.0';  Mode = 'redirect'; Location = 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v2.0.0-rc1'; Requests = 1 }
        @{ Name = 'the redirect names an overflowing tag';             Installed = '1.9.0';  Mode = 'redirect'; Location = 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v99999999999.0.0'; Requests = 1 }
    )
    foreach ($row in $SilentRows) {
        Invoke-ServerCase "notify: silent when $($row.Name)" {
            $fixtureArguments = @{ Installed = $row.Installed; Mode = $row.Mode }
            if ($row.ContainsKey('Location')) { $fixtureArguments.Location = $row.Location }
            if ($row.ContainsKey('StatusCode')) { $fixtureArguments.StatusCode = $row.StatusCode }
            $fixture = New-NotifyFixture @fixtureArguments
            Assert-NotifySilent (Invoke-Notify $fixture)
            Assert-Equal $row.Requests $fixture.Server.Stats.RequestCount 'requests'
        }
    }

    Invoke-ServerCase 'notify: TESTSTAND_MCP_NO_UPDATE_CHECK=1 prints nothing and makes no request' {
        $fixture = New-NotifyFixture
        Assert-NotifySilent (Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_NO_UPDATE_CHECK = '1' })
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests'
    }

    Invoke-ServerCase 'notify: any other value of the opt-out variable does not switch the check off' {
        Assert-True ($null -ne (Invoke-Notify (New-NotifyFixture) -Extra @{ TESTSTAND_MCP_NO_UPDATE_CHECK = '0' }).Message) 'expected the announcement'
    }

    Invoke-ServerCase 'notify: a missing plugin.json prints nothing and makes no request' {
        $fixture = New-NotifyFixture -NoPluginJson
        Assert-NotifySilent (Invoke-Notify $fixture)
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests'
    }

    Invoke-ServerCase 'notify: a server that is not listening is silent' {
        Assert-NotifySilent (Invoke-Notify (New-NotifyFixture -NoServer))
    }

    # --- where the repository comes from ----------------------------------------------------------------
    Invoke-Case 'notify: the repository of plugin.json is the release repository named by ReleaseAssets (default base URL)' {
        $repository = (Get-Content -LiteralPath $PluginJsonPath -Raw | ConvertFrom-Json).repository
        Assert-Equal ('https://github.com/' + $ReleaseRepoSlug) $repository 'plugin.json repository'
    }

    Invoke-ServerCase 'notify: an override URL outside https://github.com/ and loopback is ignored, the plugin.json repository is used' {
        $fixture = New-NotifyFixture
        $result = Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_RELEASES_BASE_URL = 'https://evil.example.com/Zuehlke/teststand-mcp' }
        Assert-Contains $result.Message '1.10.0' 'message'
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests to the plugin.json repository'
    }

    Invoke-Case 'notify: no allowed repository anywhere (override and plugin.json both foreign) means no request and silence' {
        $fixture = New-NotifyFixture -NoServer -Repository 'https://evil.example.com/x'
        Assert-NotifySilent (Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_RELEASES_BASE_URL = 'https://evil.example.com/y' })
    }

    Invoke-ServerCase 'notify: an allowed loopback override beats the plugin.json repository' {
        $fixture = New-NotifyFixture -Repository 'https://github.com/Zuehlke/teststand-mcp'
        $result = Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_RELEASES_BASE_URL = $fixture.BaseUrl }
        Assert-Contains $result.Message '1.10.0' 'message'
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests to the override'
    }

    Invoke-Case 'notify (pure): Test-AllowedBaseUrl accepts https on github.com and http on a loopback host, judged by the parsed host and not by a text prefix' {
        . $NotifyScriptPath
        foreach ($good in 'https://github.com/Zuehlke/teststand-mcp', 'HTTPS://GITHUB.COM/x', 'http://127.0.0.1:8080/x', 'http://localhost:8080/x', 'http://127.0.0.1/x', 'http://[::1]:8080/x') {
            Assert-Equal $true (Test-AllowedBaseUrl $good) "[$good]"
        }
        foreach ($bad in 'http://github.com/x', 'https://github.com.evil.io/x', 'https://evil.example.com/x', 'http://example.com:80/x', 'ftp://github.com/x', '', ' ') {
            Assert-Equal $false (Test-AllowedBaseUrl $bad) "[$bad]"
        }
        Assert-Equal $false (Test-AllowedBaseUrl $null) 'null'
        Assert-Equal $false (Test-AllowedBaseUrl 42) 'a number'
    }

    # A prefix test lets "http://localhost:" through for a URL whose real host is somewhere else; the host is read from the parsed URL.
    Invoke-Case 'notify (pure): Test-AllowedBaseUrl refuses a URL that starts like an allowed one but names another host, or carries user info' {
        . $NotifyScriptPath
        foreach ($bad in 'http://localhost:@example.com/x', 'http://127.0.0.1:@example.com/x', 'http://localhost:80@example.com/x', 'https://github.com.evil.example/x',
                         'https://github.com@evil.example/x', 'https://user@github.com/x', 'https://user:secret@github.com/x', 'http://localhost@127.0.0.1:8080/x',
                         'http://localhost.evil.example:8080/x', 'http://127.0.0.1.evil.example:8080/x', ' https://github.com/x', "https://github.com/x`n", 'https://github.com:evil/x') {
            Assert-Equal $false (Test-AllowedBaseUrl $bad) "[$bad]"
        }
    }

    # --- time -------------------------------------------------------------------------------------------
    Invoke-ServerCase 'notify: a lookup that hangs is cut off after the 3 second HTTP timeout and exits 0 silently' {
        $fixture = New-NotifyFixture -Mode hang
        $result = Invoke-Notify $fixture
        Assert-NotifySilent $result
        Assert-True ($result.ElapsedMs -ge 2500) "returned after $($result.ElapsedMs) ms: the hook did not wait for the 3 s timeout"
        if ($env:TESTSTAND_MCP_TIMING_CHECK -eq '1') { Assert-True ($result.ElapsedMs -lt $WallTimeBoundMilliseconds) "$($result.ElapsedMs) ms exceeds the $WallTimeBoundMilliseconds ms budget" }
    }

    Invoke-ServerCase 'notify: after a timed-out lookup the failure is cached - an hour later it is silent and makes no request' {
        $fixture = New-NotifyFixture -Mode hang
        [void](Invoke-Notify $fixture)
        $count = $fixture.Server.Stats.RequestCount
        Assert-Equal 1 $count 'requests in the first run'
        $later = Invoke-Notify $fixture -Now $FixedNow.AddHours(1)
        Assert-NotifySilent $later 'second run'
        Assert-Equal $count $fixture.Server.Stats.RequestCount 'requests after the second run'
    }

    Invoke-Case 'notify: the HTTP timeout constant is 3 seconds and TLS 1.2 is set explicitly' {
        . $NotifyScriptPath
        Assert-Equal $HttpTimeoutMilliseconds $script:HttpTimeoutMilliseconds 'timeout (ms)'
        Enable-HookTls12
        Assert-True (([Net.ServicePointManager]::SecurityProtocol -band [Net.SecurityProtocolType]::Tls12) -ne 0) 'TLS 1.2 flag'
    }

    Invoke-ServerCase 'notify: an upper-case tag in the redirect (V1.8.6) is read as 1.8.6' {
        $fixture = New-NotifyFixture -Installed '1.8.0' -Location 'https://github.com/Zuehlke/teststand-mcp/releases/tag/V1.8.6'
        $result = Invoke-Notify $fixture
        Assert-Contains $result.Message '1.8.6' 'message'
        Assert-NotContains $result.Message 'V1.8.6' 'the V is stripped'
    }

    Invoke-ServerCase 'notify: a relative Location (path only) is understood' {
        Assert-Contains (Invoke-Notify (New-NotifyFixture -Location '/Zuehlke/teststand-mcp/releases/tag/v1.10.0')).Message '1.10.0' 'message'
    }

    Invoke-ServerCase 'notify: versions compare numerically (1.10.0 is newer than 1.9.0)' {
        Assert-True ($null -ne (Invoke-Notify (New-NotifyFixture -Installed '1.9.0')).Message) '1.10.0 > 1.9.0'
        Assert-Equal $null (Invoke-Notify (New-NotifyFixture -Installed '1.10.0' -Location 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v1.9.0')).Message '1.9.0 < 1.10.0'
    }

    # --- cache boundaries (the state is written directly: no run sequence reaches "stale lookup, fresh announcement") ---
    Invoke-ServerCase 'notify cache: a lookup that failed 2 hours ago makes no request and prints nothing' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (2 * $Hours) -LookupOk $false
        Assert-NotifySilent (Invoke-Notify $fixture)
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests'
    }

    Invoke-ServerCase 'notify cache: a lookup that failed 25 hours ago looks up again' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -LookupOk $false
        $result = Invoke-Notify $fixture
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests'
        Assert-Contains $result.Message '1.10.0' 'message'
    }

    Invoke-ServerCase 'notify cache: a fresh successful lookup (3 h old, latest 1.10.0, never announced) announces without a request' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (3 * $Hours) -Latest '1.10.0'
        $result = Invoke-Notify $fixture
        Assert-Contains $result.Message '1.10.0' 'message'
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests'
    }

    Invoke-ServerCase 'notify cache: a failed lookup is cached too, so an offline station pays the timeout once per 24 hours' {
        $fixture = New-NotifyFixture -Mode status -StatusCode 500
        Assert-NotifySilent (Invoke-Notify $fixture) 'first run'
        Assert-NotifySilent (Invoke-Notify $fixture -Now $FixedNow.AddHours(2)) 'second run'
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests after 2 h'
        Assert-NotifySilent (Invoke-Notify $fixture -Now $FixedNow.AddHours(25)) 'third run'
        Assert-Equal 2 $fixture.Server.Stats.RequestCount 'requests after 25 h'
    }

    Invoke-ServerCase 'notify cache boundary: the lookup is cached up to 23 h 59 m 59 s and repeated at exactly 24 h' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (24 * $Hours - 1) -LookupOk $false
        Assert-NotifySilent (Invoke-Notify $fixture) 'just under 24 h'
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests just under 24 h'
        Set-NotifyState $fixture -LookupAgeSeconds (24 * $Hours) -LookupOk $false
        [void](Invoke-Notify $fixture)
        Assert-Equal 1 $fixture.Server.Stats.RequestCount 'requests at exactly 24 h'
    }

    Invoke-ServerCase 'notify cache: 1.10.0 announced 25 hours ago and still the latest is announced again' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -Latest '1.10.0' -Announced '1.10.0' -AnnouncedAgeSeconds (25 * $Hours)
        Assert-Contains (Invoke-Notify $fixture).Message '1.10.0' 'message'
    }

    Invoke-ServerCase 'notify cache boundary: an announcement is repeated at exactly 24 h and not at 23 h 59 m 59 s' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -Latest '1.10.0' -Announced '1.10.0' -AnnouncedAgeSeconds (24 * $Hours - 1)
        Assert-NotifySilent (Invoke-Notify $fixture) 'just under 24 h'
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -Latest '1.10.0' -Announced '1.10.0' -AnnouncedAgeSeconds (24 * $Hours)
        Assert-Contains (Invoke-Notify $fixture).Message '1.10.0' 'exactly 24 h'
    }

    Invoke-ServerCase 'notify cache: 1.10.0 announced 3 hours ago and 1.11.0 now the latest announces 1.11.0' {
        $fixture = New-NotifyFixture -Location 'https://github.com/Zuehlke/teststand-mcp/releases/tag/v1.11.0'
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -Latest '1.10.0' -Announced '1.10.0' -AnnouncedAgeSeconds (3 * $Hours)
        $result = Invoke-Notify $fixture
        Assert-Contains $result.Message '1.11.0' 'message'
        Assert-NotContains $result.Message '1.10.0' 'the older announcement is not repeated'
    }

    Invoke-ServerCase 'notify cache: an announcement stamped in the future (clock moved back) does not suppress the message' {
        $fixture = New-NotifyFixture
        Set-NotifyState $fixture -LookupAgeSeconds (25 * $Hours) -Latest '1.10.0' -Announced '1.10.0' -AnnouncedAgeSeconds (-2 * $Hours)
        Assert-Contains (Invoke-Notify $fixture).Message '1.10.0' 'message'
    }

    Invoke-ServerCase 'notify cache: a corrupt state file is ignored, the lookup runs and the state is repaired' {
        $fixture = New-NotifyFixture
        [void][IO.Directory]::CreateDirectory($fixture.State)
        [IO.File]::WriteAllText((Join-Path $fixture.State $NotifyStateFile), '{ nope', $Utf8NoBom)
        Assert-Contains (Invoke-Notify $fixture).Message '1.10.0' 'message'
        Assert-NotifySilent (Invoke-Notify $fixture -Now $FixedNow.AddHours(1)) 'second run after the repair'
    }

    Invoke-ServerCase 'notify cache: an unwritable state directory still announces and exits 0' {
        $fixture = New-NotifyFixture
        [IO.File]::WriteAllText($fixture.State, 'a file where the state directory should be', $Utf8NoBom)
        $result = Invoke-Notify $fixture
        Assert-Equal 0 $result.ExitCode "exit code (stderr: $($result.Stderr))"
        Assert-Contains $result.Message '1.10.0' 'message'
    }

    Invoke-ServerCase 'notify: the state goes to CLAUDE_PLUGIN_DATA when TESTSTAND_MCP_STATE_DIR is unset' {
        $fixture = New-NotifyFixture
        $pluginData = Join-Path (Split-Path -Parent $fixture.PluginRoot) 'plugin-data'
        [void](Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $pluginData })
        Assert-True (Test-Path -LiteralPath (Join-Path $pluginData $NotifyStateFile)) 'state file in CLAUDE_PLUGIN_DATA'
    }

    Invoke-ServerCase 'notify: the plugin root falls back to the folder above the script when CLAUDE_PLUGIN_ROOT is unset' {
        $fixture = New-NotifyFixture
        $result = Invoke-Notify $fixture -Extra @{ CLAUDE_PLUGIN_ROOT = $null }   # the repo's plugin.json is the 0.0.0 placeholder
        Assert-NotifySilent $result
        Assert-Equal 0 $fixture.Server.Stats.RequestCount 'requests'
    }

    Invoke-ServerCase 'notify: a failing state write cannot cost the message (the output is written first)' {
        $fixture = New-NotifyFixture
        $result = Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_HOOK_SIMULATE = 'announced-state' }
        Assert-Equal 0 $result.ExitCode "exit code (stderr: $($result.Stderr))"
        Assert-Contains $result.Message '1.10.0' 'the message was printed before the failure'
    }

    Invoke-ServerCase 'notify: the simulated failure is ignored without TESTSTAND_MCP_STATE_DIR, so production cannot trip it' {
        $fixture = New-NotifyFixture
        $pluginData = Join-Path (Split-Path -Parent $fixture.PluginRoot) 'plugin-data'
        $result = Invoke-Notify $fixture -Extra @{ TESTSTAND_MCP_HOOK_SIMULATE = 'announced-state'; TESTSTAND_MCP_STATE_DIR = $null; CLAUDE_PLUGIN_DATA = $pluginData }
        Assert-Contains $result.Message '1.10.0' 'the message'
        Assert-True (Test-Path -LiteralPath (Join-Path $pluginData $NotifyStateFile)) 'the announcement was recorded: the failure was never raised'
    }

    # --- the pure decisions ------------------------------------------------------------------------------
    Invoke-Case 'notify (pure): Convert-VersionText accepts X.Y.Z with an optional v or V and refuses everything else' {
        . $NotifyScriptPath
        Assert-Equal '1.10.0' ([string](Convert-VersionText 'v1.10.0')) 'v1.10.0'
        Assert-Equal '1.8.6' ([string](Convert-VersionText 'V1.8.6')) 'V1.8.6'
        Assert-Equal '1.2.3' ([string](Convert-VersionText '1.2.3')) '1.2.3'
        foreach ($bad in 'v1.x', 'v1.2', 'v1.2.3.4', 'v1.2.3-rc1', '', ' ', 'vv1.2.3', 'v-1.2.3', 'v1.2.3 ', 'latest', 'v99999999999.0.0') {
            Assert-Equal $null (Convert-VersionText $bad) "text [$bad]"
        }
        Assert-Equal $null (Convert-VersionText $null) 'null'
    }

    Invoke-Case 'notify (pure): Get-UpdateMessage decides only on versions and the announcement age' {
        . $NotifyScriptPath
        $now = 1000000000L
        $day = 24 * 3600
        $message = { param($installed, $latest, $announced, $announcedEpoch) Get-UpdateMessage -Installed ([version]$installed) -Latest $(if ($latest) { [version]$latest } else { $null }) `
                        -AnnouncedVersion $announced -AnnouncedEpoch $announcedEpoch -NowEpoch $now -PluginName 'p' }
        Assert-Equal $null (& $message '1.9.0' $null '' 0) 'no latest'
        Assert-Equal $null (& $message '1.9.0' '1.9.0' '' 0) 'equal'
        Assert-Equal $null (& $message '1.10.0' '1.9.9' '' 0) 'lower'
        Assert-True ($null -ne (& $message '1.9.0' '1.10.0' '' 0)) 'newer'
        Assert-Equal $null (& $message '1.9.0' '1.10.0' '1.10.0' ($now - $day + 1)) 'announced just under 24 h ago'
        Assert-True ($null -ne (& $message '1.9.0' '1.10.0' '1.10.0' ($now - $day))) 'announced exactly 24 h ago'
        Assert-True ($null -ne (& $message '1.9.0' '1.11.0' '1.10.0' ($now - 60))) 'a newer version than the announced one'
    }

    Invoke-Case 'notify (pure): Resolve-LatestVersion answers from a fresh cache without a request and leaves the state alone' {
        . $NotifyScriptPath
        $now = 1000000000L
        $unreachable = 'http://127.0.0.1:1/none'
        $fresh = [pscustomobject]@{ lookupEpoch = ($now - 60); lookupOk = $true; latestVersion = '1.10.0' }
        $answer = Resolve-LatestVersion $fresh $now $unreachable
        Assert-Equal '1.10.0' ([string]$answer.Latest) 'cached latest'
        Assert-Equal $null $answer.StateToWrite 'nothing to write'
        $failed = [pscustomobject]@{ lookupEpoch = ($now - 60); lookupOk = $false; latestVersion = '' }
        Assert-Equal $null (Resolve-LatestVersion $failed $now $unreachable).Latest 'cached failure'
        $garbage = [pscustomobject]@{ lookupEpoch = ($now - 60); lookupOk = $true; latestVersion = 'banana' }
        Assert-Equal $null (Resolve-LatestVersion $garbage $now $unreachable).Latest 'unparsable cached version'
    }

    Invoke-Case 'notify (pure): Resolve-LatestVersion after a stale cache returns the new state with the announcement carried over' {
        . $NotifyScriptPath
        $now = 1000000000L
        $stale = [pscustomobject]@{ lookupEpoch = ($now - 90000); lookupOk = $true; latestVersion = '1.9.0'; announcedVersion = '1.9.0'; announcedEpoch = ($now - 7200) }
        $answer = Resolve-LatestVersion $stale $now 'http://127.0.0.1:1/none'    # nothing listens: the lookup fails
        Assert-Equal $null $answer.Latest 'failed lookup'
        Assert-Equal $false $answer.StateToWrite.lookupOk 'lookupOk'
        Assert-Equal $now $answer.StateToWrite.lookupEpoch 'lookupEpoch'
        Assert-Equal '1.9.0' $answer.StateToWrite.announcedVersion 'announcement carried over'
        Assert-Equal ($now - 7200) $answer.StateToWrite.announcedEpoch 'announcement time carried over'
    }
}
finally {
    Stop-AllStubServers
    Remove-Module ReleaseAssets -Force -ErrorAction SilentlyContinue   # its read-only variables need -Force
    Remove-Module PluginIdentity -ErrorAction SilentlyContinue
    Remove-TestTempDirs
}

Complete-TestRun
