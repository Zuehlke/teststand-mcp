# TestStand MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that exposes NI TestStand as a set of AI-callable tools. Connect Claude directly to your TestStand engine to create, edit, and run test sequences through natural language.

Developed by [Zühlke](https://www.zuehlke.com/en/industries/industrial-sector).

---

## Prerequisites

| Requirement | Details |
|---|---|
| NI TestStand | 2019 or later (2026 recommended) |
| .NET Framework | 4.8 |
| Build toolchain | .NET SDK (for building from source) |
| Platform | Windows x86 / x64 |

---

## Build

```bat
dotnet build --configuration Debug --framework net48 -p:Platform=x86
```

The output executable is placed at:

```
bin\x86\Debug\net48\TestStandMCP.exe
```

To rebuild after code changes, kill any running instance first:

```bat
taskkill /F /IM TestStandMCP.exe
dotnet build --configuration Debug --framework net48 -p:Platform=x86
```

---

## Integration into Claude Desktop

1. Open (or create) the Claude Desktop configuration file:

   ```
   %APPDATA%\Claude\claude_desktop_config.json
   ```

2. Add the `teststand` entry under `mcpServers`:

   ```json
   {
     "mcpServers": {
       "teststand": {
         "command": "C:\\your_path\\TestStandMCP\\bin\\x86\\Debug\\net48\\TestStandMCP.exe"
       }
     }
   }
   ```

   Adjust the path to match your actual build output location.

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
- Run the NI Sequence Analyzer and return messages sorted by severity

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

## License

See `LICENSE` file in this repository.

---

> **Beta Notice**
>
> This is a **beta version**. The software is provided as-is for evaluation and development purposes.
> No warranty is given and no liability is accepted for correctness, reliability, or fitness for any
> particular purpose. Use in production test systems is at your own risk.
