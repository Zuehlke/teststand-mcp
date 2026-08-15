# TestStand MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that exposes NI TestStand as a set of AI-callable tools. Connect Claude directly to your TestStand engine to create, edit, and run test sequences through natural language.

Developed by [Zühlke](https://www.zuehlke.com/en/industries/industrial-sector).

---

## Prerequisites

| Requirement | Details |
|---|---|
| NI TestStand | 2019 or later (2026 recommended) |
| .NET runtime | .NET 8 — x86 build of `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App` (all three required by the TestStand engine) |
| Build toolchain | .NET 8 SDK or newer — the ordinary **x64** SDK is fine; `-p:Platform=x86` is what produces the 32-bit executable. No x86 SDK needed (only the x86 *runtime* above). |
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

4. Restart Claude Desktop. The TestStand tools will appear automatically.

5. In your first message, ask Claude to connect to the engine:

   > *"Connect to the TestStand engine and open my sequence file."*

   Claude will call `connect_engine` automatically before any other tool.

---

## Key Features

### Engine & Station
- Connect / disconnect from the TestStand engine
- Read station info, globals, options, and process model
- Validate expressions and expand path macros
- Connect against an alternate **TestStand environment** (`.tsenv`) — see below

### Sequence File Management
- Open, create, save, and close `.seq` files
- Read and write file-level metadata and globals
- Choose the on-disk format per file — compressed `binary`, `xml` (git-diffable) or `ini`

### Whole-File Rebuild & Verification
- **Export / import a complete sequence file** (`export_sequence_file`, `import_sequence_file`) —
  writes the whole file as a JSON model and rebuilds it elsewhere: types with their attach state,
  file globals, every sequence with parameters/locals, and all steps with their full module
  configuration. This is the way to migrate, clone or bulk-edit a file; the granular tools are for
  surgical single edits
- **Native diff** (`diff_sequence_files`) — the same FileDiffer the Sequence Editor uses, with
  category/path/change-type filters and grouping
- **Verification tools** — `validate_sequence_plan` (checks a planned step list *before* building:
  unclosed blocks, undeclared variables, forbidden `Goto`/`Label`), `audit_sequence_references`
  (undeclared `Locals.`/`Parameters.`/`FileGlobals.` references in the built sequence) and
  `audit_type_consistency` (duplicate or mismatched type registrations — which a content diff
  cannot see)
- Clone a single sequence within a file or across files (`duplicate_sequence`), plus type
  definitions, file globals and file attributes

### Sequence Editing
- Insert, rename, duplicate, and delete sequences
- Insert, move, rename, delete, and enable/disable steps
- Set step expressions, preconditions, pass/fail actions, and loop settings
- Configure `MessagePopup`, `PropertyLoader`, `NumericLimitTest`, and `StringValueTest` steps
- Manage local variables, sequence parameters, and file globals — including typed, nested and
  array members, numeric representation/format and property flags
- Create and edit **enumeration data types** (`create_enum`, `add_enum_value`, `rename_enum_value`,
  `set_enum_values`, …) stored in the sequence file
- Full undo/redo support (including grouped undo transactions)

### Execution Control
- Start executions with any entry point (`Single Pass`, `Test UUTs`, custom sequences)
- Poll status, wait for completion, and retrieve structured results
- Break, resume, abort, restart, and terminate executions
- Step over / into / out at both execution and thread level
- Set and list breakpoints; monitor watch expressions
- **Live thread-context inspection** — read and write a running or paused thread's *runtime* state
  (`inspect_thread_context`, `evaluate_in_thread_context`, `get_runtime_variable`,
  `set_runtime_variable`, `get_runstate_summary`): live variable values, the execution cursor, and
  "Set Next Step" — a scope the ordinary expression tools cannot reach

### Adapters & Step Types
- Load/unload adapters (LabVIEW, CVI, .NET, Python)
- Inspect adapter details and module parameters
- List step types from loaded type palettes
- **Typed code-module configuration** — dedicated tools to configure a step's module per adapter
  (`configure_dotnet_module`, `configure_dll_module`, `configure_labview_module`,
  `configure_python_module`, `configure_sequence_call_module`); the step's adapter is switched
  automatically when needed, and the code module's parameter interface is loaded afterwards
  (the editor's "Load Prototype")
- Every module setting is **verified by reading it back**, so the result reports only what really
  landed on the step — a target that could not be resolved is named instead of silently accepted

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
  minimum-severity filter. Pass `async: true` and poll `get_analysis_status` for large files: the
  analyzer loads every step's code module, so a run can take minutes and exceed the MCP request
  window. A run that produces zero messages is reported as `resultSuspect` rather than "clean"

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

## TestStand Environments (`.tsenv`)

A station that hosts several products usually isolates each one's TestStand `CommonAppData`,
`Public` and `LocalAppData` directories in a separate **environment**. The Sequence Editor selects
one with its `/env <path.tsenv>` command-line switch; this server does the same thing in-process.

By default nothing changes: without an environment the server connects to the **global** one exactly
as before.

### Configuring it

The environment is applied when the engine is created and is then **fixed for the life of the server
process**, so it is a property of the server, not of a call. Configure it where the server is
defined and restart the server to change it.

**The recommended way — pin it in your MCP host's config** (`claude_desktop_config.json`,
`.mcp.json`, …), so it holds no matter which tool runs first:

```json
{
  "mcpServers": {
    "teststand": {
      "command": "C:\\path\\to\\TestStandMCP.exe",
      "args": ["--TestStand:EnvironmentPath=C:\\MyProduct\\Config\\MyProduct.tsenv"]
    }
  }
}
```

An environment variable does the same job if you prefer `env` over `args` — note the double
underscore, which is how .NET maps a nested key:

```json
"env": { "TESTSTAND_MCP_TestStand__EnvironmentPath": "C:\\MyProduct\\Config\\MyProduct.tsenv" }
```

**Or in `appsettings.json`** — the one **next to the executable**, which is the only one that is
read. The copy in the repository root is just the source; the build deploys it to the output
directory, so edit the source and rebuild rather than the deployed copy:

```jsonc
"TestStand": {
  "EnvironmentPath": "C:\\MyProduct\\Config\\MyProduct.tsenv",
  "EnvironmentAutoDetect": false,
  "ConnectTimeoutSeconds": 120
}
```

**Or per call** — useful for a one-off, but see the warning below:

```
connect_engine(tsenv_path: "C:\\MyProduct\\Config\\MyProduct.tsenv")
connect_engine(tsenv_path: "auto", tsenv_search_from: "C:\\MyProduct\\Components\\Sequences\\Main.seq")
```

> **`connect_engine(tsenv_path: …)` has to be the first engine call of the session.** Any other tool
> before it connects the engine implicitly — to the *global* environment — and the environment can no
> longer be changed afterwards; you then get an error telling you to restart the server. The config
> routes above have no such ordering requirement, which is why they are the recommended ones.

**Precedence**, highest first: the `connect_engine` argument → `--TestStand:EnvironmentPath=…` on the
command line → the `TESTSTAND_MCP_…` environment variable → `appsettings.json`. `EnvironmentAutoDetect`
and `ConnectTimeoutSeconds` have no tool parameter; they come from the three configuration channels
only, in the same order.

New parameters need a **fresh MCP session**: clients cache the tool catalog when the session starts,
so `tsenv_path` and `tsenv_search_from` only appear after reconnecting the server.

### How `auto` finds the file

`auto` walks up from the given `.seq` (or directory) and checks every ancestor **both in itself and
in its immediate subdirectories** — so the common layout

```
C:\Product\Config\Product.tsenv            <- the environment
C:\Product\Components\Sequences\Main.seq   <- the sequence files
```

resolves at `C:\Product`, even though `Config` is a *sibling* of the walked path and never an
ancestor of it. The scan is one level deep, the directory itself wins over its subdirectories, and
several `.tsenv` files at the same ancestor are reported as **ambiguous** rather than guessed. For a
layout this does not cover, name the file with `tsenv_path` instead.

Setting `EnvironmentAutoDetect: true` applies the same search to the first sequence file opened, so
callers need not pass anything — it is off by default because it pins the environment implicitly,
from a file path.

### Three things worth knowing

- **The environment is fixed for the life of the server process.** TestStand only accepts it before
  the engine is created, so `connect_engine` with a *different* `tsenv_path` is an error — restart
  the server to switch. A lazy reconnect after a server restart keeps the environment it had.
- **It is verified, not assumed.** After connecting, the engine is asked what it actually did
  (`GetEnvironmentPath`, plus the effective roots compared against their global counterparts). If the
  redirect did not take, the connect fails instead of silently working against the wrong
  `CommonAppData`. `get_engine_paths` reports `environmentPath`, `environmentActive` and the three
  effective directories.
- **A bad environment fails loudly and early.** A `.tsenv` whose `CommonAppData` TestStand has never
  initialized (no `Cfg\GeneralEngine.cfg`) makes the engine raise an interactive dialog no headless
  caller can answer. The file is validated up front, TestStand's own `CanInitializeEngine()` is asked
  before the engine is constructed, and the connect itself is bounded by `ConnectTimeoutSeconds` — so
  a misconfiguration returns an error naming the defect instead of hanging the session.

### It reaches the child processes too

Several tools do their work in a separate process that starts an engine **of its own**, so an
environment applied only in-process would leave them on the global station configuration — silently,
with no error to notice. All of them now receive it:

| Tool | Child process | How |
|---|---|---|
| `analyze_sequence_file`, `run_sequence_analyzer` | `AnalyzerApp.exe` | `/env` |
| `diff_sequence_files`, `compare_sequence_files` | `FileDiffer.exe` | `/env` |
| `launch_sequence_editor`, `open_file_in_editor`, `run_in_editor` | `SeqEdit.exe` | `/env` |
| `load_module_prototype` (isolated worker) | this server, re-launched | `--tsenv` |

Only the environment the engine **verified itself into** is forwarded, so a child can never be sent a
path the parent did not prove. Without an environment the command lines are byte-identical to what
they were before.

Two limits worth knowing. The prototype worker is dispatched before any configuration is built, so
the explicit argument is the *only* channel that reaches it — `appsettings.json` and the inherited
`TESTSTAND_MCP_…` variables do not. And `SeqEdit.exe` is single-instance: if an editor is already
running, your file opens in *that* instance and keeps the environment it was started with, which
`/env` cannot change — the server logs a warning instead of implying a match.

`open_sequence_file` additionally warns when a file belongs to a *different* environment than the one
the engine runs in. The file still opens; the warning exists because its process models, type
palettes and station globals resolve from another `CommonAppData`.

---

## Agents

Three Claude agents ship next to the executable (`.claude\agents\`) and build on the read-only
tools. Run `TestStandMCP.exe --setup-agents` once to make Claude Code see them in every project.

| Agent | Turns a `.seq` into |
|---|---|
| `teststand-doc-generator` | A **Word document** — title, real table of contents, one section per sequence with its parameter table and a flow-indented step listing (original TestStand icons, tinted), plus a rendered call-dependency diagram |
| `teststand-presentation-generator` | A single self-contained **HTML presentation** — Setup/Main/Cleanup phase cards, clickable subsequences, and a code-vs-flowchart compare view with the original step icons in full color |
| `teststand-sequence-builder` | A **new sequence** built from a flowchart or written test description, asking per step whether to link a `SequenceCall` or insert a placeholder |

Both generators are read-only toward TestStand and can be given the output language.

---

## Useful CLI Flags

```bat
TestStandMCP.exe --version       # Print version and exit
TestStandMCP.exe --list-tools    # Print all registered tool names and descriptions
TestStandMCP.exe --setup-agents  # Junction %USERPROFILE%\.claude\agents to the agents
                                 # shipped next to the exe, so Claude Code picks them
                                 # up in every project (see "Agents" above)
```

---

## Notes

- The server communicates over **stdin/stdout** using the MCP JSON-RPC protocol. Do not write anything else to stdout in a custom build.
- Logging is written to **stderr** and is visible in the separate console window that opens automatically.
- Always call `save_sequence_file` after editing a sequence to persist changes to disk.
- Use `NI_Flow_If / NI_Flow_Else / NI_Flow_End` for conditional branching — never `Goto/Label`.
- A `.seq` is written as compressed **binary** by default (`TOF1` magic, not text-searchable). Pass
  `file_format: "xml"` to `create_sequence_file` / `save_sequence_file` for a human-readable, git-diffable
  file; `get_file_properties` reports the current format, and `import_sequence_file` reproduces the
  exported file's format automatically.

---

## Build

`PlatformTarget=x86` is what makes the output a 32-bit executable, so it can load the in-process
32-bit TestStand COM server. The SDK itself may be x64:

```bat
dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86
```

The output executable is placed at:

```
bin\x86\Debug\net8.0-windows\TestStandMCP.exe
```

To rebuild after code changes, kill any running instance first — the engine keeps the file locked:

```bat
taskkill /F /IM TestStandMCP.exe
dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86
```

Run the integration tests (they drive a real TestStand engine, so TestStand must be installed):

```bat
dotnet test Test\TestExecution\TestStandMCP.IntegrationTests.csproj --configuration Debug --framework net8.0-windows
```

---

## License

See the `LICENSE` file in this repository.