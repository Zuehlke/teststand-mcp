# Both bitnesses on one station

**Result: both plugins pass against their own TestStand.** The x86 plugin (`teststand-mcp`) drives
32-bit TestStand, the x64 plugin (`teststand-mcp-x64`) drives 64-bit TestStand, and neither touches
the other's install.

Measured 2026-10-25 on station `BGTAS-STAGING-L`, with both editions of TestStand 2026 Q3 Patch 1
(26.2.0.49157) installed side by side:

- 32-bit: `C:\Program Files (x86)\National Instruments\TestStand 2026`
- 64-bit: `C:\Program Files\National Instruments\TestStand 2026`

The source is commit `97bb93d` (branch `claude/mcp-installation-workflow-88cdb4`). Later commits are
re-run on both bitnesses by the pre-push gate on every push; the counts below are from `97bb93d`.

## 1. The integration suite, once per bitness

`.githooks/run-tests.ps1` (the pre-push gate) found both installs and ran `Test/TestExecution` twice:

| Run | Build | Result | Duration |
|---|---|---|---|
| x86 | default platform, `bin\Debug\net8.0-windows` | 663 passed, 0 failed, 0 skipped | 1 min 35 s |
| x64 | `-p:Platform=x64`, `bin\x64\Debug\net8.0-windows` | 663 passed, 0 failed, 0 skipped | 1 min 26 s |

No test needed a bitness-specific skip. The engine banner of each run named the matching TestStand
directory.

## 2. The installed archives

Both archives were built from the same commit with
`build/Build-PluginArchive.ps1 -Version 0.0.0-ci -Architecture <x86|x64>`. Each built in about
33 s and passed its own local release verification. Each zip was then extracted into a fresh
folder and its `bin\TestStandMCP.exe` was driven as an MCP stdio server through one session of
13 tool calls.

| Flow | x86 exe | x64 exe |
|---|---|---|
| `connect_engine` + `get_engine_paths` | `versionString` "... 32-bit", Bin under `Program Files (x86)` | "... 64-bit", Bin under `Program Files` |
| create, insert steps, `open_sequence_file` | pass | pass |
| `load_module_prototype isolate:true` (LabVIEW step, missing VI) | `worker` / `not-loaded` | `worker` / `not-loaded` |
| `run_sequence` | `Stopped` / `Passed` | `Stopped` / `Passed` |
| `diff_sequence_files` (two different files) | 6 differences | 6 differences |
| `analyze_sequence_file` | 0 errors, 3 info (the counting rules) | 0 errors, 3 info |

In both cases `mcpServerDirectory` and `scriptsDirectory` pointed into the extracted folder.

A second pass recorded the executable path of every helper process while the flows ran
(`Win32_Process`). It showed which install each exe used:

| Helper | started by the x86 exe | started by the x64 exe |
|---|---|---|
| `FileDiffer.exe` | `C:\Program Files (x86)\...\TestStand 2026\Bin` | `C:\Program Files\...\TestStand 2026\Bin` |
| `AnalyzerApp.exe` | `C:\Program Files (x86)\...\TestStand 2026\Bin` | `C:\Program Files\...\TestStand 2026\Bin` |
| prototype worker (`--load-prototype-worker`) | the extracted x86 `TestStandMCP.exe` | the extracted x64 `TestStandMCP.exe` |

The server's own log does not name the helper paths (it writes to the exe's console, not to
stderr), so this process listing is the evidence.

Timings from the second pass:

| Call | x86 | x64 |
|---|---|---|
| `connect_engine` | 1.5 s | 2.5 s |
| `load_module_prototype` (worker) | 52.6 s | 9.6 s |
| `diff_sequence_files` | 12.1 s | 8.5 s |
| `analyze_sequence_file` | 6.5 s | 5.5 s |

The x86 worker took 23 s in the first pass and 52.6 s in the second. Both are under its 120 s
bound. The run-to-run spread looks like LabVIEW attach time, not a bitness effect; it was not
investigated.

## Not covered

- Real `.lvlibp` panes, and a VI that actually loads. Only the worker's `not-loaded` path ran.
- A station with only ONE edition installed. The gate's skip branches and the platform guard's
  "other bitness only" warning are covered by script tests (`.githooks/run-tests.ps1 -DryRun`,
  `PlatformGuard.Tests.ps1`), not by a real install.
- Installing through Claude Code from the marketplace. That is fork rehearsal round 3.
- Which runtime the self-contained x64 exe loads (the `coreclr.dll` path) and its cold-start time. Both
  were measured for x86 only (`self-contained-x86.md`); x64 uses the same publish settings.
