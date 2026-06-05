# TestStand MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that exposes NI TestStand as a set of AI-callable tools. Connect Claude directly to your TestStand engine to create, edit, and run test sequences through natural language.

Developed by [Zühlke](https://www.zuehlke.com/en/industries/industrial-sector).

---

## Prerequisites

| Requirement | Details |
|---|---|
| NI TestStand | 2019 or later (2026 recommended) |
| .NET runtime | .NET 8 — x86 build of `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App` (all three required by the TestStand engine) |
| Build toolchain | .NET 8 SDK (x86) — `dotnet --info` should report `Architecture: x86` |
| Platform | Windows **x86** (the TestStand engine COM server is 32-bit) |
| Any AI of your choice | E.g. a paid version of Claude with access to Claude Code in the Desktop App |



---

## Integration into Claude Desktop - Getting Started with the TestStand MCP Server

This MCP Server can be used with any AI tool that supports MCPs. The instructions below describe the Claude Desktop case on Windows:

1. Download the latest binary from the [releases section](https://github.com/Zuehlke/teststand-mcp/tags) (or build the project, see below)

2. Open (or create) the Claude Desktop configuration file:

   ```
   %APPDATA%\Claude\claude_desktop_config.json
   ```

3. Add the `teststand` entry under `mcpServers`:

   ```json
   {
     "mcpServers": {
       "teststand": {
         "command": "C:\\your_download_path\\TestStandMCP.exe"
       }
     }
   }
   ```

   Adjust the path to match your actual download or build output location.

3. Restart Claude Desktop. The TestStand tools will appear automatically.

4. In your first message, ask Claude to connect to the engine:

   > *"Connect to the TestStand engine and open my sequence file."*

   Claude will call `connect_engine` automatically before any other tool.

---

## Key Features

### Engine & Station
- Connect / disconnect from the TestStand engine
- Read station info, globals, options, and process model
- Validate expressions and expand path macros

### Sequence File Management
- Open, create, save, and close `.seq` files
- Compare two sequence files (structured diff)
- Read and write file-level metadata and globals

### Sequence Editing
- Insert, rename, duplicate, and delete sequences
- Insert, move, rename, delete, and enable/disable steps
- Set step expressions, preconditions, pass/fail actions, and loop settings
- Configure `MessagePopup`, `PropertyLoader`, `NumericLimitTest`, and `StringValueTest` steps
- Manage local variables, sequence parameters, and file globals
- Full undo/redo support (including grouped undo transactions)

### Execution Control
- Start executions with any entry point (`Single Pass`, `Test UUTs`, custom sequences)
- Poll status, wait for completion, and retrieve structured results
- Break, resume, abort, restart, and terminate executions
- Step over / into / out at both execution and thread level
- Set and list breakpoints; monitor watch expressions

### Adapters & Step Types
- Load/unload adapters (LabVIEW, CVI, .NET, Python)
- Inspect adapter details and module parameters
- List step types from loaded type palettes
- **Typed code-module configuration** — dedicated tools to configure a step's module per adapter
  (`configure_dotnet_module`, `configure_dll_module`, `configure_labview_module`,
  `configure_python_module`, `configure_sequence_call_module`); the step's adapter is switched
  automatically when needed

### Reporting & Results
- Generate HTML/XML/TXT reports for completed executions
- Save reports to disk or retrieve full report text
- Export results using configured result schemas

### Synchronization Objects
- Create and manage Semaphores, Mutexes, Queues, Notifications, and Rendezvous objects

### Sequence Editor GUI
- Launch and close the TestStand Sequence Editor
- Open sequence files in the editor for visual inspection
- Start executions directly from the editor GUI

### Search & Analysis
- Search steps by name, type, expression, or comment
- **Native find / replace** (`find_in_file`, `replace_in_file`) using the TestStand search engine —
  regex, whole-word and case options; replace operates on string-valued properties
- Run the NI Sequence Analyzer and return messages sorted by severity
- **Detailed analysis** (`analyze_sequence_file`) — typed messages with severity counts and a
  minimum-severity filter

### User & Privilege Management
- List users and read the currently logged-in user
- Create and delete users, set passwords, check whether a login name exists
- List a user's enabled privileges and test a specific privilege

### Output & UI Messages
- Post, list, and clear engine output messages (visible in the editor's Output pane)
- Post UI messages to a running execution's thread (for custom operator interfaces)

### Station Configuration & Data
- Manage engine **search directories** (list, add, remove)
- Edit **custom data types** — add, list, and remove fields
- Read and write CSV files via the TestStand **CSV record streams**
- Create result-log helpers, batch-sync objects, and set up interactive step execution
  (model/execution-bound features; availability depends on engine context)

---

## Useful CLI Flags

```bat
TestStandMCP.exe --version     # Print version and exit
TestStandMCP.exe --list-tools  # Print all registered tool names and descriptions
```

---

## Notes

- The server communicates over **stdin/stdout** using the MCP JSON-RPC protocol. Do not write anything else to stdout in a custom build.
- Logging is written to **stderr** and is visible in the separate console window that opens automatically.
- Always call `save_sequence_file` after editing a sequence to persist changes to disk.
- Use `NI_Flow_If / NI_Flow_Else / NI_Flow_End` for conditional branching — never `Goto/Label`.

---

## Release Notes

### V0.2.0
Migrated the server from **.NET Framework 4.8 to .NET 8** (`net8.0-windows`, x86):
- NI interop now via the `NationalInstruments.TestStand.API` NuGet (netstandard2.0) instead of
  the .NET-Framework PIA, which does not load under .NET 8 (root cause of the engine connect failure).
- Host declares the `Microsoft.WindowsDesktop.App` + `Microsoft.AspNetCore.App` framework references
  the engine requires to load the .NET Core runtime in-process.
- Clean shutdown: the process now hard-terminates on exit, avoiding the `0xC0000409` crash and WER
  dialog caused by finalizing lingering TestStand / NILM COM objects.

### V0.0.4
Adds the medium-priority API gaps plus UI messages — **16 new tools**:
- **Output & UI messages**: `post_output_message`, `get_output_messages`,
  `clear_output_messages`, `post_ui_message`.
- **Search directories**: `get_search_directories`, `add_search_directory`,
  `remove_search_directory`.
- **Custom data-type editing**: `add_data_type_field`, `get_data_type_fields`,
  `remove_data_type_field`.
- **CSV record streams**: `write_csv_lines`, `read_csv_lines`.
- **Execution/model-bound helpers** (best-effort, availability depends on context):
  `create_result_log`, `create_batch_sync_object`, `run_steps_interactively`,
  `add_report_section`.
- Covered by integration tests (fixtures `T15`–`T19`). Notes: the native sequence-file
  *differ* is not usefully exposed by the COM interop (the existing `compare_sequence_files`
  already provides a structured diff), and parametric *sweep* has no standalone COM factory;
  synchronization objects require an engine that exposes a SyncManager.

### V0.0.3
Closes the highest-value gaps against the TestStand gRPC / NuGet API surface — **16 new tools**:
- **User & privilege management** (`get_users`, `get_current_user`, `user_name_exists`,
  `create_user`, `delete_user`, `set_user_password`, `get_user_privileges`,
  `check_user_privilege`) via `Engine.UsersFile` / `User`.
- **Native find / replace** (`find_in_file`, `replace_in_file`) via `PropertyObject.Search` /
  `SearchMatch`, with regex, whole-word and case options. Find covers names, comments and values;
  replace operates on string-valued properties.
- **Typed code-module configuration** (`configure_dotnet_module`, `configure_dll_module`,
  `configure_labview_module`, `configure_python_module`, `configure_sequence_call_module`) —
  dedicated, adapter-aware tools instead of only the generic `set_module_parameter`.
- **Detailed sequence analysis** (`analyze_sequence_file`) — severity counts and a
  minimum-severity filter on top of the existing analyzer run.
- Each area is covered by integration tests (fixtures `T11`–`T14`).

### V0.0.2
- Added `teststand-sequence-builder` subagent for guided sequence creation from flowcharts or test descriptions.
- `set_sequence_call_target` now stores the target as a path relative to the source sequence file (no more absolute paths persisted).
- Fixed `delete_sequence`: resolved the COM `DISP_E_TYPEMISMATCH` error by dispatching `RemoveSequence` with an integer index.

---

## License

See `LICENSE` file in this repository.
---

## Build

Build with the **x86 .NET 8 SDK** (the TestStand engine COM server is 32-bit):

```bat
dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86
```

The output executable is placed at:

```
bin\x86\Debug\net8.0-windows\TestStandMCP.exe
```

To rebuild after code changes, kill any running instance first:

```bat
taskkill /F /IM TestStandMCP.exe
dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86
```

---

> **Beta Notice**
>
> This is a **beta version**. The software is provided as-is for evaluation and development purposes.
> No warranty is given and no liability is accepted for correctness, reliability, or fitness for any
> particular purpose. Use in production test systems is at your own risk.
