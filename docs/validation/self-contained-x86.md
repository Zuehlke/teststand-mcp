# Release exe layout — station spike

**Decision: self-contained FOLDER layout** (`dotnet publish -r win-x86 --self-contained true`,
no `PublishSingleFile`). It is the default of `build/Build-PluginArchive.ps1 -Layout`.

Measured 2026-10-24 on station `BGTAS-STAGING-L`: TestStand 2026 Q3 Patch 1 (26.2.0.49157) 32-bit,
.NET SDK 10.0.401, installed x86 runtime `Microsoft.NETCore.App 8.0.30`. Each layout was started
as an MCP stdio server by a driver script and taken through the four flows of spec AC-2.

| | single-file | **folder** | framework-dependent (control) |
|---|---|---|---|
| publish size | 172 MB, 7 files | 179 MB, 407 files | 5.5 MB, 29 files |
| first cold start (to `initialize` reply) | 4 236 ms | **408 ms** | 394 ms |
| 1. `connect_engine` | pass | pass | pass |
| 2. create + `open_sequence_file` | pass | pass | pass |
| 3. `load_module_prototype isolate:true` | `worker` / `not-loaded` | `worker` / `not-loaded` | `worker` / `not-loaded` |
| 4. `start_execution` → wait | `Stopped` / `Passed` | `Stopped` / `Passed` | `Stopped` / `Passed` |
| loaded `coreclr.dll` | `C:\Program Files (x86)\dotnet\shared\Microsoft.NETCore.App\8.0.30\` | **`<publish dir>\coreclr.dll`** | `C:\Program Files (x86)\dotnet\shared\…` |
| runtime evidence (AC-2) | **fail** | **pass** | n/a (control) |

## Why single-file fails although every flow passed

A single-file exe links the runtime into the apphost, so its own runtime never shows up as a
`coreclr.dll` module. The `coreclr.dll` that *does* appear came from the **installed** runtime,
loaded by the TestStand engine itself: the engine hosts .NET in-process and resolves the runtime
from the host's `runtimeconfig.json`. A single-file app has no such file on disk, so the engine
falls back to the machine-wide install. On a station without the x86 .NET 8 runtime that
fallback has nothing to load. Single-file is therefore not actually self-contained against the
engine, and it costs ~4 s of extraction on every cold start.

The folder layout ships `TestStandMCP.runtimeconfig.json` (with `includedFrameworks`) and the
runtime beside the exe. The engine picks the bundled `coreclr.dll`, so no installed runtime is
involved.

## Flow 3 — what it proves

`isolate` applies only to LabVIEW steps. Every other adapter loads in-process. So flow 3 uses a
LabVIEW step whose VI does not exist, and the load returns `executionMode: "worker"` with
`workerOutcome: "not-loaded"` and the worker's own note ("LoadPrototype could not resolve the
target/module"). That result line comes from the child process. The worker was relaunched from
`Environment.ProcessPath`, started its own engine, opened the file and reported back, which is
the relaunch property AC-2 needs. `.lvlibp` panes are not covered.

## `AppContext.BaseDirectory` paths

`get_engine_paths` reported `mcpServerDirectory` and `scriptsDirectory` as the publish directory
and its `scripts\` subfolder for every layout. `appsettings.json` and `scripts/**` therefore resolve
next to the exe in the folder layout as they do in a normal build.

## Start budget

The MCP server start timeout is 30 s (`MCP_TIMEOUT` default, see
[claude-code-plugin-facts.md](claude-code-plugin-facts.md)), so the budget is 15 s. The folder
layout's 0.4 s is far inside it. Defender real-time scanning was on (station default); a first
start after download may be slower while it scans the new files.

## Reproducing

The driver used is `Invoke-Spike.ps1` (session scratch). It must run from **32-bit** PowerShell
(`C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`), because a 64-bit process cannot
list a 32-bit process's modules, and `coreclr.dll` then reads as absent.
