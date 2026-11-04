# ============================================================================
#  update-notify.ps1 - SessionStart (startup) hook: tells the user, once a day, that a
#  newer plugin release exists. Windows PowerShell 5.1 and PowerShell 7. Never blocks:
#  it prints at most one systemMessage and exits 0; every failure is silent.
#
#  The latest version is the tag in the Location header of a no-redirect HEAD request
#  to <repo>/releases/latest (no API rate limit). The lookup, successful or failed, is
#  cached for 24 hours, and a version is announced at most once per 24 hours.
#  Silent when: the installed version is the 0.0.0 placeholder or not a plain X.Y.Z, it is
#  equal or higher than the latest, TESTSTAND_MCP_NO_UPDATE_CHECK=1, or anything fails.
#  A pre-release tag (v2.0.0-rc1) is never announced: only X.Y.Z tags count.
#
#  The plugin name and the repository come from the installed plugin.json. Injectable through
#  environment variables, so the tests never touch the network or the real profile:
#  TESTSTAND_MCP_RELEASES_BASE_URL (honoured only for an https URL on github.com and an http URL
#  on a loopback host, never one with user info), TESTSTAND_MCP_NOW (the clock),
#  TESTSTAND_MCP_STATE_DIR (the cache directory) and CLAUDE_PLUGIN_ROOT (the plugin root that holds
#  .claude-plugin/plugin.json).
# ============================================================================
Set-StrictMode -Version 2.0

try { . (Join-Path $PSScriptRoot 'hook-common.ps1') } catch { exit 0 }

$script:OptOutVariable = 'TESTSTAND_MCP_NO_UPDATE_CHECK'
$script:BaseUrlVariable = 'TESTSTAND_MCP_RELEASES_BASE_URL'
$script:AllowedHttpsHost = 'github.com'
$script:LatestReleasePath = '/releases/latest'
$script:HttpTimeoutMilliseconds = 3000   # the hook timeout is 10 s: shell start plus this stays well inside it
$script:UserAgent = 'teststand-mcp-update-notify'
$script:RedirectStatusCodes = @(301, 302, 303, 307, 308)
$script:StateFile = 'update-notify.json'
$script:PlaceholderVersion = [version]'0.0.0'
$script:VersionPattern = '\A[vV]?(\d+\.\d+\.\d+)\z'
$script:TagInLocationPattern = '/tag/([^/?#]+)(?:[?#].*)?\z'

# ----------------------------------------------------------------------------
#  Pure decisions
# ----------------------------------------------------------------------------
# "v1.10.0", "V1.8.6" or "1.2.3" as a [version]; $null for anything else (v1.x, 1.2, 1.2.3-rc1, overflow).
# Used for the release tag AND for the installed version, so both follow the same strict rule.
function Convert-VersionText {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return $null }
    $match = [regex]::Match($Text, $script:VersionPattern)
    if (-not $match.Success) { return $null }
    $parsed = $null
    if ([version]::TryParse($match.Groups[1].Value, [ref]$parsed)) { return $parsed }
    return $null
}

# The tag a Location header names (absolute or relative); $null when it names none.
function Get-TagFromLocation {
    param([AllowNull()][string]$Location)
    if ([string]::IsNullOrEmpty($Location)) { return $null }
    $match = [regex]::Match($Location, $script:TagInLocationPattern)
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}

# https on github.com, or http on a loopback host, judged on the PARSED url: a text prefix lets
# "http://localhost:@example.com/" through, whose host is example.com. A url with user info is refused.
function Test-AllowedBaseUrl {
    param($Url)
    if ($Url -isnot [string] -or $Url.Trim() -cne $Url) { return $false }
    $parsed = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$parsed)) { return $false }
    if ($parsed.UserInfo.Length -gt 0) { return $false }
    if ($parsed.Scheme -ceq 'https') { return ($parsed.Host -ceq $script:AllowedHttpsHost) }
    if ($parsed.Scheme -ceq 'http') { return $parsed.IsLoopback }
    return $false
}

# The message to show, or $null: only a latest version above the installed one that was not announced
# within the last 24 hours.
function Get-UpdateMessage {
    param([version]$Installed, [AllowNull()][version]$Latest, [string]$AnnouncedVersion, [long]$AnnouncedEpoch, [long]$NowEpoch,
          [Parameter(Mandatory)][string]$PluginName)
    if ($null -eq $Latest -or $Latest -le $Installed) { return $null }
    $wasAnnouncedRecently = ($AnnouncedVersion -ceq [string]$Latest) -and (Test-IsFresh $AnnouncedEpoch $NowEpoch $script:DayInSeconds)
    if ($wasAnnouncedRecently) { return $null }
    $lines = @(
        "TestStand MCP update available: you have $Installed, the latest release is $Latest.",
        'To update, run these two commands in order:',
        "  1. claude plugin marketplace update $($script:MarketplaceName)",
        "  2. claude plugin update $PluginName",
        'Then restart Claude Code. You can ignore this message when marketplace auto-update is on: it updates the plugin by itself.',
        "To stop these checks, set $($script:OptOutVariable)=1."
    )
    return ($lines -join "`n")
}

# ----------------------------------------------------------------------------
#  I/O
# ----------------------------------------------------------------------------
function Enable-HookTls12 {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

# @{ Name; Version; RepositoryUrl } from plugin.json; $null when the file is missing or unreadable, or the
# version is not a plain X.Y.Z or the name is empty. RepositoryUrl may be $null.
function Get-InstalledPlugin {
    param([string]$PluginRoot)
    try {
        $path = Join-Path $PluginRoot $script:PluginJsonRelativePath
        $json = [IO.File]::ReadAllText($path, $script:Utf8NoBom) | ConvertFrom-Json
        $version = Convert-VersionText ([string](Get-HookProperty $json 'version'))
        $name = [string](Get-HookProperty $json 'name')
        if ($null -eq $version -or [string]::IsNullOrWhiteSpace($name)) { return $null }
        return @{ Name = $name; Version = $version; RepositoryUrl = (Get-HookProperty $json 'repository') }
    } catch {
        return $null
    }
}

# The repository URL to look the release up at: the test override when it is an allowed URL, else
# the repository of plugin.json when that is an allowed URL, else $null.
function Get-ReleaseBaseUrl {
    param($Plugin)
    $override = Get-HookEnvironmentText $script:BaseUrlVariable
    if (Test-AllowedBaseUrl $override) { return $override }
    if (Test-AllowedBaseUrl $Plugin.RepositoryUrl) { return $Plugin.RepositoryUrl }
    return $null
}

# A HEAD request that does not follow redirects, cut off after the HTTP timeout (the whole call,
# connecting included). @{ StatusCode; Location }, or $null on any failure.
function Invoke-HeadRequest {
    param([Parameter(Mandatory)][string]$Url)
    $response = $null
    try {
        $request = [Net.HttpWebRequest][Net.WebRequest]::Create($Url)
        $request.Method = 'HEAD'
        $request.AllowAutoRedirect = $false
        $request.Timeout = $script:HttpTimeoutMilliseconds
        $request.ReadWriteTimeout = $script:HttpTimeoutMilliseconds
        $request.UserAgent = $script:UserAgent
        $request.Proxy = [Net.WebRequest]::GetSystemWebProxy()
        $request.Proxy.Credentials = [Net.CredentialCache]::DefaultCredentials
        $pending = $request.GetResponseAsync()
        if (-not $pending.Wait($script:HttpTimeoutMilliseconds)) {
            $request.Abort()
            return $null
        }
        $response = $pending.Result
        return @{ StatusCode = [int]$response.StatusCode; Location = $response.Headers['Location'] }
    } catch {
        return $null
    } finally {
        if ($null -ne $response) { $response.Close() }
    }
}

# The latest released version, or $null when the lookup fails or the redirect is not a clean X.Y.Z tag.
function Get-LatestVersion {
    param([Parameter(Mandatory)][string]$BaseUrl)
    Enable-HookTls12
    $answer = Invoke-HeadRequest ($BaseUrl.TrimEnd('/') + $script:LatestReleasePath)
    if ($null -eq $answer -or $script:RedirectStatusCodes -notcontains $answer.StatusCode) { return $null }
    return (Convert-VersionText (Get-TagFromLocation $answer.Location))
}

# The latest version from the cache when the last lookup is under 24 hours old (a failed lookup is cached
# as "no version"), else from the network. Returns @{ Latest; StateToWrite }: StateToWrite is the whole new
# state after a network lookup (the announcement is carried over), and $null when the cache answered.
function Resolve-LatestVersion {
    param($State, [long]$NowEpoch, [Parameter(Mandatory)][string]$BaseUrl)
    $lookupEpoch = ConvertTo-EpochOrZero (Get-HookProperty $State 'lookupEpoch')
    if (Test-IsFresh $lookupEpoch $NowEpoch $script:DayInSeconds) {
        $cached = $null
        if (Get-HookProperty $State 'lookupOk') { [void][version]::TryParse([string](Get-HookProperty $State 'latestVersion'), [ref]$cached) }
        return @{ Latest = $cached; StateToWrite = $null }
    }
    $latest = Get-LatestVersion $BaseUrl
    $carriedState = @{
        announcedVersion = [string](Get-HookProperty $State 'announcedVersion')
        announcedEpoch   = (ConvertTo-EpochOrZero (Get-HookProperty $State 'announcedEpoch'))
        lookupEpoch      = $NowEpoch
        lookupOk         = ($null -ne $latest)
        latestVersion    = $(if ($null -ne $latest) { [string]$latest } else { '' })
    }
    return @{ Latest = $latest; StateToWrite = $carriedState }
}

# The state after an announcement: the lookup that produced the version, plus what was announced when.
function New-AnnouncedState {
    param($State, [version]$Latest, [long]$NowEpoch)
    Invoke-HookSimulation 'announced-state'
    return @{
        lookupEpoch      = (ConvertTo-EpochOrZero (Get-HookProperty $State 'lookupEpoch'))
        lookupOk         = $true
        latestVersion    = [string]$Latest
        announcedVersion = [string]$Latest
        announcedEpoch   = $NowEpoch
    }
}

function Invoke-UpdateNotify {
    if (Test-HookFlag $script:OptOutVariable) { return }
    $plugin = Get-InstalledPlugin (Get-PluginRoot)
    if ($null -eq $plugin -or $plugin.Version -le $script:PlaceholderVersion) { return }
    $baseUrl = Get-ReleaseBaseUrl $plugin
    if ($null -eq $baseUrl) { return }

    $nowEpoch = (Get-HookNow).ToUnixTimeSeconds()
    $state = Read-HookState $script:StateFile
    $lookup = Resolve-LatestVersion $state $nowEpoch $baseUrl
    if ($null -ne $lookup.StateToWrite) {
        [void](Write-HookState $script:StateFile $lookup.StateToWrite)
        $state = [pscustomobject]$lookup.StateToWrite
    }

    $message = Get-UpdateMessage -Installed $plugin.Version -Latest $lookup.Latest -PluginName $plugin.Name `
        -AnnouncedVersion ([string](Get-HookProperty $state 'announcedVersion')) `
        -AnnouncedEpoch (ConvertTo-EpochOrZero (Get-HookProperty $state 'announcedEpoch')) -NowEpoch $nowEpoch
    if ($null -eq $message) { return }

    Write-HookOutput @{ systemMessage = $message }
    # Recorded after the output is written, so a failing write can never cost the message.
    [void](Write-HookState $script:StateFile (New-AnnouncedState $state $lookup.Latest $nowEpoch))
}

# Dot-sourcing (the tests) loads the functions only.
if ($MyInvocation.InvocationName -ne '.') { Invoke-SilentHook { Invoke-UpdateNotify } }
