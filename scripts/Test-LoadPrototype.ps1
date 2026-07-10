<#
.SYNOPSIS
  Standalone diagnostic for load_module_prototype on a LabVIEW (.lvlibp) step.
  Drives the TestStand MCP server over stdio (JSON-RPC) WITHOUT Claude, runs the async
  prototype load, polls to completion, verifies the ViCall against a reference file, and
  writes a full transcript. Run this on the PC that has the RUNNING LabVIEW / Sequence Editor.

.DESCRIPTION
  Prerequisites on the target PC:
    * LabVIEW is running and the Sequence Editor has loaded this .lvlibp VI at least once
      (so the adapter can attach to the running LabVIEW via ExecServer/ActiveX).
    * The current build with the fix is present (the script rebuilds by default).

  The key diagnostic is the "note" of each result: it now carries an
  [lv-route: cast=ok; prev=...; set=...; init=ok] tag telling you whether the typed
  LabVIEWAdapter cast + SetServerInfo/Initialize succeeded.

.EXAMPLE
  # Supply your own .seq with a LabVIEW .lvlibp step, and a reference to diff against:
  powershell -ExecutionPolicy Bypass -File .\scripts\Test-LoadPrototype.ps1 `
    -File 'C:\Path\To\Sequences\YourSequence.seq' -Ref 'C:\Path\To\Sequences\YourReference.seq'

.EXAMPLE
  # skip the rebuild, give LabVIEW more time, also try the in-process (non-isolated) path:
  .\scripts\Test-LoadPrototype.ps1 -File '...\YourSequence.seq' -Ref '...\YourReference.seq' `
    -SkipBuild -PollSeconds 300 -TryInProcess
#>
[CmdletBinding()]
param(
  # ProjectDir defaults to the repo root (the parent of this script's 'scripts' folder) so the
  # script is portable — no hard-coded machine path. $Exe is derived from it below when left blank.
  [string]$ProjectDir = (Split-Path -Parent $PSScriptRoot),
  [string]$Exe        = "",
  # REQUIRED: supply your own sequence file (a .seq containing the LabVIEW .lvlibp step to load) and
  # a reference .seq to diff its ViCall against. The placeholders below are intentionally invalid.
  [string]$File       = "C:\Path\To\Sequences\YourSequence.seq",
  [string]$Ref        = "C:\Path\To\Sequences\YourReference.seq",
  [string]$Seq        = "Init",
  [string]$Group      = "Main",
  [string]$Step       = "Start Module",
  [int]   $PollSeconds = 240,
  [string]$LabViewServer = "deferred",     # deferred | exec | rte | auto
  [switch]$SkipBuild,
  [switch]$TryInProcess                    # also run isolate=false (NOT crash-contained) as a diagnostic
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$Transcript = Join-Path $env:TEMP "lp_diag_$stamp.log"

function Log([string]$m) {
  $line = ('{0}  {1}' -f (Get-Date -Format 'HH:mm:ss'), $m)
  Write-Host $line
  Add-Content -LiteralPath $Transcript -Value $line -Encoding UTF8
}
function LogObj([string]$label, $obj) {
  $json = ($obj | ConvertTo-Json -Depth 30)
  Log ("{0}:`n{1}" -f $label, $json)
}

Log "=== load_module_prototype diagnostic ==="
Log "Transcript: $Transcript"

# Resolve the server exe from the (portable) project dir unless the caller overrode it explicitly.
if ([string]::IsNullOrWhiteSpace($Exe)) {
  $Exe = Join-Path $ProjectDir "bin\x86\Debug\net8.0-windows\TestStandMCP.exe"
}

# Fail fast with a clear message if the required sequence paths were left at their placeholders.
foreach ($p in @(@{n='File';v=$File}, @{n='Ref';v=$Ref})) {
  if ($p.v -like 'C:\Path\To\*') {
    throw "Parameter -$($p.n) is still the placeholder ('$($p.v)'). Pass a real .seq path."
  }
}

# ── 0) build (unless skipped) ───────────────────────────────────────────────
if (-not $SkipBuild) {
  Log "Stopping any running TestStandMCP.exe and rebuilding..."
  Get-Process TestStandMCP -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Push-Location $ProjectDir
  try {
    & dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
  } finally { Pop-Location }
  Log "Build OK."
}
if (-not (Test-Path -LiteralPath $Exe)) { throw "EXE not found: $Exe" }
Log ("EXE: {0}  (built {1})" -f $Exe, (Get-Item -LiteralPath $Exe).LastWriteTime)
if (-not (Test-Path -LiteralPath $File)) { throw "Sequence file not found: $File" }

# ── work copy in the SAME folder (keeps the relative .lvlibp search path valid) ──
$work = Join-Path ([IO.Path]::GetDirectoryName($File)) ([IO.Path]::GetFileNameWithoutExtension($File) + "_psverify.seq")
Copy-Item -LiteralPath $File -Destination $work -Force
Log "Work copy: $work"

# ── start the MCP server (stdio). stderr is NOT redirected so its log window stays visible;
#    the key routing diagnostic comes back in each result's 'note' via stdout anyway. ──
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.Arguments = "--Logging:LogLevel:Default=Information"
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$proc = [System.Diagnostics.Process]::Start($psi)
Log "Server started (PID $($proc.Id))."

$script:reqId = 0
function Send-Raw($obj) {
  $json = ($obj | ConvertTo-Json -Compress -Depth 30)
  $proc.StandardInput.WriteLine($json)
  $proc.StandardInput.Flush()
}
function Read-Response([int]$expectId, [int]$timeoutMs) {
  $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
  while ($true) {
    if ($proc.HasExited) { return [pscustomobject]@{ __dead = $true; code = $proc.ExitCode } }
    $remain = [int][Math]::Max(200, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    $t = $proc.StandardOutput.ReadLineAsync()
    if (-not $t.Wait($remain)) {
      if ([DateTime]::UtcNow -ge $deadline) { return [pscustomobject]@{ __timeout = $true } }
      continue
    }
    $line = $t.Result
    if ($null -eq $line) { return [pscustomobject]@{ __dead = $true; code = $proc.ExitCode } }
    $line = $line.Trim()
    if ($line -eq '' -or $line -eq 'null') { continue }        # skip notification echoes
    try { $o = $line | ConvertFrom-Json } catch { continue }
    if (($o.PSObject.Properties.Name -contains 'id') -and ($o.id -eq $expectId)) { return $o }
  }
}
function Invoke-ToolText([string]$name, $toolArgs, [int]$timeoutMs = 60000) {
  # NOTE: the parameter is $toolArgs, NOT $args — $args is a reserved PowerShell automatic
  # variable (the unbound-argument array) and would serialize as [] instead of the object.
  $script:reqId++; $id = $script:reqId
  Send-Raw @{ jsonrpc = '2.0'; id = $id; method = 'tools/call'; params = @{ name = $name; arguments = $toolArgs } }
  $resp = Read-Response $id $timeoutMs
  if ($resp.PSObject.Properties.Name -contains '__dead')    { Log ("!! SERVER EXITED (code=0x{0:X8}) during '{1}'" -f $resp.code, $name); return $null }
  if ($resp.PSObject.Properties.Name -contains '__timeout') { Log "!! TIMEOUT waiting for '$name'"; return $null }
  if ($resp.PSObject.Properties.Name -contains 'error')     { Log "!! tool '$name' error: $($resp.error.message)"; return $null }
  return $resp.result.content[0].text
}
function Invoke-ToolJson([string]$name, $toolArgs, [int]$timeoutMs = 60000) {
  $text = Invoke-ToolText $name $toolArgs $timeoutMs
  if ($null -eq $text) { return $null }
  try { return ($text | ConvertFrom-Json) } catch { return [pscustomobject]@{ __text = $text } }
}
function Send-Notify([string]$method) { Send-Raw @{ jsonrpc = '2.0'; method = $method; params = @{} } }
function Server-Alive { return -not $proc.HasExited }
function ParamCount($p) { if ($null -eq $p) { return -1 }; if ($p -is [array]) { return $p.Count }; if ($p.parameters) { return @($p.parameters).Count }; return -1 }

$result = [ordered]@{}
try {
  # ── handshake ──
  $script:reqId++; $hsId = $script:reqId
  Send-Raw @{ jsonrpc='2.0'; id=$hsId; method='initialize'; params=@{ protocolVersion='2024-11-05'; capabilities=@{}; clientInfo=@{ name='ps-diag'; version='1' } } }
  [void](Read-Response $hsId 30000)
  Send-Notify 'initialized'

  # ── connect + open work copy ──
  Log "connect_engine ..."
  $c = Invoke-ToolText 'connect_engine' @{} 120000
  Log "  -> $c"
  if (-not (Server-Alive)) { throw "Server died during connect_engine." }
  [void](Invoke-ToolJson 'open_sequence_file' @{ file_path = $work } 120000)

  # ── params BEFORE ──
  $before = Invoke-ToolJson 'get_module_parameters' @{ file_path=$work; sequence_name=$Seq; step_group=$Group; step_name=$Step } 60000
  Log ("Params BEFORE: {0}" -f (ParamCount $before))

  # ── 1) DEFAULT path: worker + async + ExecServer routing ──
  Log "load_module_prototype (default: worker + async, labview_server=$LabViewServer) ..."
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $start = Invoke-ToolJson 'load_module_prototype' @{ file_path=$work; sequence_name=$Seq; step_group=$Group; step_name=$Step; labview_server=$LabViewServer; timeout_seconds=$PollSeconds } 60000
  Log ("  immediate return in {0:N2}s" -f $sw.Elapsed.TotalSeconds)
  LogObj "  start-result" $start
  $job = $null; if ($start) { $job = $start.jobId }

  $final = $start
  if ($job) {
    Log "polling get_prototype_load_status (job $job) ..."
    $pollDeadline = [DateTime]::UtcNow.AddSeconds($PollSeconds + 30)
    while ($true) {
      if (-not (Server-Alive)) { Log "!! server died while polling"; break }
      Start-Sleep -Seconds 4
      $st = Invoke-ToolJson 'get_prototype_load_status' @{ job_id = $job } 60000
      if ($null -eq $st) { break }
      Log ("   status={0} loaded={1} outcome={2}" -f $st.status, $st.prototypeLoaded, $st.workerOutcome)
      if ($st.status -ne 'running') { $final = $st; break }
      if ([DateTime]::UtcNow -ge $pollDeadline) { Log "!! poll deadline reached"; $final = $st; break }
    }
  }
  LogObj "FINAL result (worker/async)" $final

  # ── params AFTER + diff (if it loaded) ──
  if ($final -and $final.prototypeLoaded -eq $true) {
    [void](Invoke-ToolText 'save_sequence_file' @{ file_path = $work } 60000)
    $after = Invoke-ToolJson 'get_module_parameters' @{ file_path=$work; sequence_name=$Seq; step_group=$Group; step_name=$Step } 60000
    Log ("Params AFTER: {0}" -f (ParamCount $after))
    $mi = Invoke-ToolJson 'get_step_module_info' @{ file_path=$work; sequence_name=$Seq; step_group=$Group; step_name=$Step } 60000
    LogObj "step_module_info" $mi
    $d = Invoke-ToolJson 'diff_sequence_files' @{ file_path_1=$work; file_path_2=$Ref } 300000
    if ($d -and $d.changes) {
      $vic = @($d.changes | Where-Object { ("$($_.path) $($_.name)") -match 'Start Module' -and ("$($_.path) $($_.name)") -match 'ViCall|Parms|Namespace|Checksum|VI Description|Broadcast|error in|error out' })
      Log ("Start-Module ViCall-related diffs vs reference: {0}" -f $vic.Count)
      foreach ($c in $vic) { Log ("   [{0}] {1} | f1={2} f2={3}" -f $c.changeType, $c.name, $c.file1Value, $c.file2Value) }
      $result.vicallDiffs = $vic.Count
    }
    $result.paramsAfter = (ParamCount $after)
  }

  # ── 2) OPTIONAL diagnostic: in-process (not crash-contained) — surfaces the routing note on the
  #      main server and reveals whether in-process faults. Do this LAST (may crash the server). ──
  if ($TryInProcess -and (Server-Alive)) {
    Log "DIAGNOSTIC: load_module_prototype isolate=false async=false (in-process; may crash) ..."
    $ip = Invoke-ToolJson 'load_module_prototype' @{ file_path=$work; sequence_name=$Seq; step_group=$Group; step_name=$Step; isolate=$false; async=$false; labview_server=$LabViewServer; timeout_seconds=$PollSeconds } (($PollSeconds + 30) * 1000)
    if ($null -eq $ip -and -not (Server-Alive)) { Log "!! IN-PROCESS load CRASHED the server (exit 0x$('{0:X8}' -f $proc.ExitCode))." }
    else { LogObj "in-process result" $ip }
  }

  $result.serverAliveAtEnd = (Server-Alive)
  $result.finalStatus      = if ($final) { $final.status } else { $null }
  $result.workerOutcome    = if ($final) { $final.workerOutcome } else { $null }
  $result.prototypeLoaded  = if ($final) { $final.prototypeLoaded } else { $null }
  $result.note             = if ($final) { $final.note } else { $null }
}
catch {
  Log "EXCEPTION: $($_.Exception.Message)"
}
finally {
  try { if (-not $proc.HasExited) { $proc.StandardInput.Close() } } catch {}
  try { if (-not $proc.WaitForExit(8000)) { $proc.Kill() } } catch {}
  try { Remove-Item -LiteralPath $work -Force -ErrorAction SilentlyContinue } catch {}
}

Log "==================== SUMMARY ===================="
LogObj "summary" ([pscustomobject]$result)
Log "ACCEPTANCE: prototypeLoaded=true, workerOutcome not crashed/timeout, ViCall diffs = 0, server alive."
Log "Full transcript saved to: $Transcript"
Log "Bitte diese Datei zuruecksenden, falls es nicht klappt: $Transcript"
