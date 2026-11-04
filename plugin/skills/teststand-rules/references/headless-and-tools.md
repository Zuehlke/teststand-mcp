# Headless behaviour and tool rules

## Headless limitations are expected outcomes, not bugs

- An unhandled run-time error PAUSES the execution; it never becomes a terminal `Error`. TestStand's default action opens the interactive error dialog, nobody answers it headless, so the run sits `Paused` and `wait_for_execution` burns its whole timeout and returns `Paused`. That IS the failure report: read the error, then `terminate_execution`, or patch state with `set_runtime_variable` and resume. Raising the timeout never helps.
- A `MessagePopup` parks a thread the same way but reports `Running`. That makes it the deterministic way to hold a thread open for `inspect_thread_context`.
- `step_results` stay EMPTY for a direct sequence run. Only a process-model entry point fills the result list: `start_execution` with `"Single Pass"` runs unattended and yields step results. `"Test UUTs"` parks on the UUT serial dialog (and its report generator stalls on a `NONE` serial) unless you override `PreUUT`/`PostUUT` locally with `add_callback_override` plus `set_step_run_mode` `Skip`.
- Never poll a StationGlobal from a running sequence. A `While StationGlobals.Flag == 1` step loop pegs a core and starves a concurrent `set_station_global`; the write times out and can drop the MCP connection. Use an `NI_Wait` (`set_wait_time`) as the long-runner instead.
- `create_sync_object` and `create_batch_sync_object`: headless has no SyncManager, so errors are expected.
- `post_ui_message` and `add_report_section` need a live `execution_id` and only make sense during an execution; an unknown id is an error.
- `post_output_message` does work headless and appears in the engine output list.
- Undo and redo are not recorded by the headless API (they are a Sequence Editor feature). MCP edits cannot be undone; revert by performing the inverse operation.
- A `.lvlibp` pane is never loaded headless; see [rebuild.md](rebuild.md#lvlibp-panes-cloned-never-loaded).
- The engine connection reconnects by itself. If the MCP host restarts the server mid-session, the next call reconnects once with the default engine path, and mutating tools load their file on demand. A transient "Not connected" heals on the next call; an explicit `connect_engine` is harmless.
- Call `save_sequence_file` after every sequence change, and verify persistence by reopening the file with `open_sequence_file` and reading back.
- New tool names and new parameters appear only in a FRESH MCP session after the server was updated; the client caches the tool catalog at session start.

## Bitness

- Each plugin drives only the TestStand of its own bitness: the x86 plugin the 32-bit engine, the x64 plugin the 64-bit engine. The code modules a sequence calls (DLLs, .NET assemblies, LabVIEW, Python) must match that engine's bitness; a 32-bit DLL does not load into the 64-bit engine and the other way round. `get_engine_paths` reports which engine you are on (`versionString` ends in `32-bit` or `64-bit`).

## Expressions

- `check_expression` effectively needs a loaded file as context. Pass `sequence_file_path` of an open file; without it even a valid expression can fail to validate.
- `evaluate_expression` resolves against StationGlobals by default and against FileGlobals when a file path is given. To reference a FileGlobal by name, create it first with `set_property_value` (for example `value_type="number"`).

## Properties and variables

- `set_property_value` with `sequence_name=null` writes FileGlobals; with a sequence name it writes that sequence's Locals. The four scopes are StationGlobals (engine-wide), FileGlobals (one file), Locals and Parameters (one sequence).
- To set a STEP's own property use `set_step_property` (dotted path relative to the step, for example `VIModule.ViCall.VIPath` or `PortNumber`). `set_property_value` and `set_property` only reach FileGlobals and Locals, never a step. The `configure_*_module` tools only reach the adapter module, and `configure_labview_module` switches the adapter, which is wrong for utility steps that have no adapter (such as `NI_LV_RunVIAsynchronously`); it refuses those steps with a clear error. `set_step_property` writes the step property directly and leaves the adapter untouched. The path must already exist.
- Containers: create with `value_type="container"`, then set nested members by dotted path, for example `"MyCont.Inner"`. `delete_sub_property` removes a global or subproperty.
- Typed parameters and typed nested members: `insert_sequence_parameter` accepts enum, `reference`, `container` and array types (`name[]`), the same contract as `insert_local_variable`; there is no silent String fallback. To build a nested typed member inside a container local, call `insert_local_variable` with a dotted path (`"MyCont.Sub.Field"`), parent before child.
- Setting an ENUM value: `set_property_value(value_type="number", value=<n>)` and `set_local_variable` write an enum instance by its numeric value and keep the enum type. A parameter's default enum value is not reachable this way (the value tools target Locals and FileGlobals, never a sequence's Parameters); use `set_property_node` with scope `Parameters` ([tool-contracts.md](tool-contracts.md#property-nodes-in-any-scope)).
- A step's "comment" IS its `Description`; `set_step_comment` writes the field that reads back as `Description`.
- `get_numeric_limits` returns the keys `low_limit` and `high_limit`. A one-sided limit uses a `null` limit with a comparison such as `"LT"` or `"GT"`; a two-sided limit uses `"GELE"` and the like.
- Step-type and enum specifics: `UseStepUnloadOption` (module unload, value 5) is valid only at file or model level and TestStand rejects it on a single step; use values 1 to 4 per step. The accepted strings of the `set_step_*` tools are listed in their tool descriptions: use those, do not guess.
- Duplicate step names (several `End`, `If` or `Check...` steps): the by-name step tools hit the FIRST match only. Use the `Name#N` selector (Nth occurrence) or `@idx:N` (0-based group index).

## Validation before and after building

- Before building, run `validate_sequence_plan` on the exact `steps` array you will pass to `insert_steps_bulk`, with the planned `locals` AND `parameters`. It needs no engine. Errors: `E_UNCLOSED_BLOCK`, `E_UNMATCHED_END`, `E_ELSE_WITHOUT_IF`, `E_CASE_WITHOUT_SELECT`, `E_JUMP_OUTSIDE_LOOP`, `E_FORBIDDEN_TYPE` (Goto/Label), `E_UNDECLARED_LOCAL`, `E_UNDECLARED_PARAM` (only when a `parameters` list is supplied), `E_DUP_NAME`. Warnings, advisory only: `W_UNLINKED_CALLS` (unlinked `SequenceCall` placeholders are fine), `W_UNUSED_LOCAL`, and `W_UNKNOWN_TYPE` (a step type outside the built-in list; installed custom step types insert fine). Build only when `valid` is true.
- After building, run `audit_sequence_references`. It reads the real sequence (`ConditionExpr`, `ItemExpr`, `PreExpression`, `PostExpression`, `StatusExpression`) and flags every `Locals.X`, `Parameters.X` or `FileGlobals.X` that is not declared (`E_UNDECLARED_LOCAL`, `E_UNDECLARED_PARAM`, `E_UNDECLARED_FILEGLOBAL`). Omit `sequence_name` to audit every sequence. It is read-only and advisory, and it does not audit `StationGlobals`, `RunState.*` or `Step.*`. It also catches conditions written later with `set_flow_condition` or `set_step_expression`, which the pre-build check cannot see.
- While building, declare every `Locals.X` AND `Parameters.X` you reference (`insert_local_variable`, `insert_sequence_parameter`). A parameter written inside a subsequence and read back by the caller must be passed by reference (`pass_by_reference:true`).

## Sequence analyzer

- With `analyze_sequence_file` or `run_sequence_analyzer`, the analysis takes MINUTES when the code modules can actually be loaded: the "module is loadable" rule loads every step's module, so the duration depends on the station (Python and LabVIEW installed and running), not on the file. A 30-sequence file took several minutes. Budget the polling accordingly.
- Zero messages is not a clean file. If LabVIEW or the Python interpreter is unavailable, the analyzer bails out early, saves an empty project and still exits successfully. The counting rules fire on any file, so zero RAW messages is the tell. The result then carries `resultSuspect: true` plus a `note`. The flag keys off the raw count, so `min_severity='Error'` may legitimately filter to zero.
- The call is bounded by `timeout_seconds` (default 900); a stuck analyzer is killed and the call fails instead of hanging.
- Pass `async=true` whenever the file has LabVIEW `.lvlibp` steps on a cold module cache. The call returns immediately with `{jobId, status:"running"}`; poll `get_analysis_status(job_id)` until `status:"completed"`. The result has the same shape as the synchronous one (`totalMessages`, `errorCount`, `warningCount`, `informationCount`, `messages[]`, optional `groups[]`). A cold packed-library load can exceed the MCP transport window, and only async avoids that. Finished jobs are kept about ten minutes. If the analyzer process dies on a native fault, the job ends with `status:"error"` and the MCP server survives.

## Live thread-context inspection (run-time debugging)

A running or paused thread's run-time state (live variable values, the execution cursor) is reachable only through the thread-context tools. `get_property_tree` and `evaluate_expression` resolve against StationGlobals and never see the thread scope, and `get_local_variables` returns the static file default, not the live value.

- `inspect_thread_context` dumps a frame's live tree. `scope` is `runstate` (default), `locals`, `parameters`, `step`, `sequence` or `full`; `lookup_string` descends; `call_stack_index` picks a caller frame (0 = current, innermost).
- `evaluate_in_thread_context` evaluates any expression in the live frame scope (`Locals.X`, `RunState.NextStepIndex`, `Locals.C * 2`), which `evaluate_expression` cannot.
- `get_runtime_variable` and `set_runtime_variable` read and write one path. Writing `RunState.NextStepIndex` is the "Set Next Step" action; patch `Locals.X` before resuming; clear `RunState.SequenceError` or `GotoCleanup`. Only meaningful while the thread is paused or parked.
- `get_runstate_summary` is a curated flat snapshot (position, cursor, flags, SequenceError).
- The thread must be executing inside a sequence. No active frame, a bad `call_stack_index` or an unknown execution is an error.

## TestStand environments (`.tsenv`)

A station that hosts several products isolates each one's `CommonAppData`, `Public` and `LocalAppData` in an environment, the Sequence Editor's `/env <path.tsenv>` switch. The server applies one environment per PROCESS.

- Sources, in precedence order: `connect_engine(tsenv_path=...)`, then the already-active environment (so a lazy reconnect never drops back to global), then the `TestStand:EnvironmentPath` setting from config, environment variable or command line, then auto-detection when `TestStand:EnvironmentAutoDetect` is on.
- `tsenv_path='auto'` with `tsenv_search_from` walks up from that folder and checks each ancestor in itself AND one level down: real layouts keep the `.tsenv` in a sibling folder (`<root>\Config\X.tsenv` next to `<root>\Components\Sequences\*.seq`). The directory itself wins over its subdirectories; two candidates at one ancestor are ambiguous and give an error, never a guess; anything deeper than one level is not searched, so name `tsenv_path` instead.
- The environment cannot be switched in a running server. A second `connect_engine` naming a different `.tsenv` is an ERROR telling you to restart the server. A connect whose requested environment is not actually in effect fails instead of silently working against the wrong `CommonAppData`.
- `get_engine_paths` reports `environmentPath`, `environmentActive`, `environmentDetectedFrom` and the three effective roots. Check it to learn which environment you are in. An empty `environmentPath` means the global environment.
- `open_sequence_file` warns when the file belongs to another environment than the one the engine runs. It is a warning, not an error: the file opens, but its process models, type palettes and StationGlobals resolve from the other `CommonAppData`.
- The child processes follow the same environment: the analyzer, the diff tool and the Sequence Editor launched by the server all start with the verified environment. A running Sequence Editor is single-instance and keeps the environment it was started with; the server warns instead of implying a match, and which environment a running editor uses cannot be read back.
- The effect on an analysis is file-dependent: the environment changes the engine's search directories, but do not expect every analysis to change.
