# TestStandMCP — Behavior Rules for Claude

<!-- skill-imports:begin -->
## User-facing rules (imported from the plugin skill)

The rules a plugin user needs live in the `teststand-rules` skill, which is their single source: edit them there, never here. The imports below load them into every session in this repository. `build/Test-SkillSources.ps1` fails when a moved heading reappears in this file or an import dangles; it cannot see a rule restated in a sentence, so keep only the developer fact (measurement, history, test ID, source pointer) in the sections below and point to the skill file for the rule.

@plugin/skills/teststand-rules/SKILL.md
@plugin/skills/teststand-rules/references/rebuild.md
@plugin/skills/teststand-rules/references/sequence-design.md
@plugin/skills/teststand-rules/references/agents.md
@plugin/skills/teststand-rules/references/headless-and-tools.md
@plugin/skills/teststand-rules/references/tool-contracts.md
<!-- skill-imports:end -->

## Developer notes behind the imported rules

The skill states each rule for a plugin user: no dates, no test IDs, no class names. The facts below are the developer side of the same rules: measurements, history, the suites that pin them and the source that implements them. Each bullet names the skill file that holds the rule.

**Export/import and rebuild fidelity** (`rebuild.md`)
- Measured 2026-07-29 on 8 sequences of `TFW_MDC_com_Python.seq` (47 steps, 13 object-oriented Python steps, 5 LabVIEW `.lvlibp` steps, 1 cross-file SequenceCall): **3 MCP calls and ZERO FileDiffer differences inside the imported scope** (the only rows left are the 22 sequences not imported). The same rebuild with the granular tools took ~700 calls, 3 diff iterations and left 224 differences.
- A WHOLE-file rebuild reaches `identical: true`, verified 2026-07-29 on `TFW_Symphony_DutCom.seq` (17 sequences, 79 steps, 43 variables, 18 `.lvlibp` panes from TWO packed libraries, 4 cross-file calls, 2 ActiveX steps, 49 types) in 4 MCP calls with 0 warnings.
- An import measured 5.5 min before the passes were made cheap; `<dest>.import.json` exists because that outlives the MCP transport window.
- Subset import: the in-memory type list still lists a type the save just dropped, so the loss is detected AFTER a save+reload. `attach='preserve'` (what a 1:1 rebuild needs) attaches almost nothing, so a type survives only while still referenced.
- The safe defaults (`labview_panes='copy'`, `cross_file_prototypes='copy'`, `variables='copy'`, `keep_unused_types`) are locked down by tests in `T35`. The worker route for the cross-file cache is not viable: it starts its own engine and opens every callee, and one 3 MB callee (`Easy.Log.seq`) ran it into a 300 s timeout; `'copy'` reproduced the same 6 `Prototype` members in about a second.
- `DiffReportShaper.DefaultMaxResults` (150) sits on `Options`, so `compare_sequence_files` in native mode inherits the cap; `diff_sequence_files` and `compare_sequence_files mode=native` are the same diff.
- Connector-pane binding through `set_module_parameter`, measured: 31 differences became 68 (write everything), 41 (non-empty only), 39 (flag-aware), while writing `ViCall.Parms[i].ArgVal` and `UseDefaultValues` INDEPENDENTLY gave 9. A SequenceCall argument's `UseDef` has the same asymmetry.
- The two prototype-load kinds are mutually exclusive per server process (measured 2026-07-29): a LabVIEW pane load first makes the next SeqCall load return `prototypeLoaded:false` ("LoadPrototype could not resolve the target/module"), and the reverse order fails the same way.
- Residuals closed 2026-07-29, each by cloning from the source file instead of loading: the LabVIEW connector panes (pass 5), the cross-file `Prototype` cache (pass 7), and a step's authored `TS.AdditionalResultsHints` / `CustomResults` / `ErrorDialogOptions` for EVERY step (pass 6b). `copy_typedefs` reproduces every type correctly (0 `types` differences); the declarative route had lost the type INSTANCE, not the type.
- A named-type instance's ENUM member reading as explicitly set was long recorded as an irreducible API limit; it is not. Instantiating the type (`insert_local_variable dataType:"LogEvent"`) writes the default enumerator NAME out, so the member reads `[Debug]` where the editor-authored original has `{Debug}` (`get_property_tree`: `symbolicName:"Debug", isDefault:false` vs `symbolicName:"", isDefault:true`). Not writing the value does not help: the import already skips it (`isDefault:true` in the model, and `WriteEnumLeafExplicit` returns early on null ordinal+value). `variables='copy'` clones the variable and reproduces the state exactly.
- `NI.Analyzer.IgnoredMessages` is invisible to the engine API; only FileDiffer's raw reader sees it, as a lone `File Properties > Attributes` delete.
- Typed PropertyObject (2026-07-29): `Clone(path, CopyAllFlags)` then `SetPropertyObject` on a subtree whose node carries a NAMED TYPE re-registers a second instance of the type; the fix writes the leaf scalars (`SetValString`/`SetValNumber`/`SetValBoolean`) on the existing node. Measured: `modulesCloned` dropped 79 -> 47 and the dialog disappeared. The decision lives in `StepCopyPolicy` (scalar compared and written by value, empty list-like node skipped, object copy last resort), pinned by tests in `T35`. The FileDiffer reported `identical: true` for the file that raised the dialog. `audit_type_consistency` (2026-07-30) reads the file's `TypeUsageList` RAW, duplicates included, which `EnumerateFileTypeDefs` deliberately hides.
- File format (2026-07-31): `MEDELA_TFW`'s real files are XML, so a rebuild used to come out binary: content-identical yet 25 KB against 3.4 MB on `TFW_MDC_com_Python` (x133, pure serialization). `TestStandService` `ParseFileWritingFormat` / `ApplyFileWritingFormat`; pinned by `T36_FileFormatTests` (tests asserting the RAW bytes `TOF1` vs a UTF-8 BOM + `<?xml`). An unknown format name THROWS rather than falling back to binary.
- `.lvlibp` guard (2026-07-30): `InputGuards.IsPackedLibraryModulePath` plus the refusal message; `force_unsafe_inprocess:true` overrides. The in-process load raised the MSVC delay-load SEH `0xC06D007E` (the LabVIEW Run-Time `lvrt.dll`) with LabVIEW 2026 32-bit already started and responsive; it escapes managed `try/catch`, so the server PROCESS DIED and the NI Error Reporter appeared. `isolate:true` is crash-safe but cannot bind the running LabVIEW ADE, so it only times out (8 steps x 120 s, all timeouts; it also reads the file from DISK). An earlier note recommended `isolate:false` after one apparently good run; that advice caused a crash and is retracted.
- Per-sequence clone recipe: memory `teststand-whole-sequence-clone-rebuild-2026-07-08`.
- `set_step_property_flags` (measured 2026-10-24, TestStand 2026 Q3): a plain `SetFlags` REPLACES the bitfield. `0x4` then `0x80` (no `exact`) reads back `0x400080`, so `0x4` is gone; `exact=true` gives the same result; the engine keeps its own `0x400000` bit in both modes. The earlier "SetFlags only ever turns bits ON" descriptions and comments were wrong and are fixed; the `exact` path (`SetExactFlags`) stays as the verified write (read-back check, then `SetInstanceOverrideFlags`).

**Agents** (`agents.md`)
- The builder rule exists because `AskUserQuestion` is unavailable to spawned subagents; the orchestrator opens `.claude/agents/teststand-sequence-builder.md` and runs its workflow itself.
- Doc generator: `scripts/generate_teststand_doc.py` (data JSON, dependency diagram via headless Edge/Chrome, `.docx` via python-docx; run with the `py` launcher, plain `python` is not on PATH).
- Presentation generator: `scripts/generate_teststand_presentation.py` plus `scripts/presentation_template.html`; icons via Pillow from `...\National Instruments\TestStand*\Components`; modelled on `.Demo_jcm/TSmcp_demo/index.html` but WITHOUT the QR code / NI-Gold-Partner image. TestStand 2026's Sequence Editor renders its UI with embedded Chromium (CEF), opaque to Windows UI automation (memory `teststand-seqedit-cef-no-automation`); manual PNGs plus `_manifest.json` can be embedded via `--shots-dir`.

**Headless, validation and tool rules** (`headless-and-tools.md`, `sequence-design.md`; the IDs are the suites that pin each fact)
- Expressions `T01`, `T08`, `T20`. Properties and variables `T20`, `T30`, `T33`, `T04`; `configure_labview_module` refuses None-adapter utility steps since 2026-07.
- Headless pause and popups `T24`, `T25`; `step_results` and process-model entry points `T27` (memory `teststand-process-model-entry-points`); sync objects `T19` (`InvalidOperationException` / `NotSupportedException`); `post_ui_message` / `add_report_section` with an unknown id raise `KeyNotFoundException` (`T15`, `T19`); `post_output_message` `T15`; undo/redo `T08` (`CanUndo` false on a fresh file). The `wait_for_execution` / `get_execution_status` descriptions state the `Paused` trap, and `set_station_global` warns against being polled.
- The auto-reconnect is `EnsureConnected`: a one-shot lazy reconnect with the default engine path.
- Step-type and enum specifics `T05`, `T06` (`UseStepUnloadOption` value 5), `T04`, `T10`; user management `T11`.
- Live thread context (`T29`): the access path is `Thread.GetSequenceContext(frame).AsPropertyObject()`, the ThisContext tree. No active frame raises `InvalidOperationException`, a bad `call_stack_index` `ArgumentOutOfRangeException`, an unknown execution `KeyNotFoundException`.
- Validation: `validate_sequence_plan` `T10`, `T31`; `audit_sequence_references` `T28` (pure logic in `ReferenceAuditor`); since 2026-07 the plan validator also checks `Parameters.X` when a `parameters` list is passed. The Select/Case structure is enforced by `E_CASE_WITHOUT_SELECT` / `E_UNCLOSED_BLOCK`; flow-step configurability was established by a property-tree audit.
- Analyzer measured 2026-07-29: ~511 s on the 30-sequence `TFW_MDC_com_Python.seq` once Python 3.11 and LabVIEW were installed and running, versus seconds before when every load failed instantly (a 360 s poll budget looked exactly like a hang). The counting rules are `NI_SequenceFileCount` / `NI_SequenceCount` / `NI_StepCount`. The old 120 s timeout was dead code: `proc.StandardOutput.ReadToEnd()` blocks until the child closes the pipe, so `WaitForExit(120_000)` afterwards always returned true; the pipes now drain via `ReadToEndAsync` and `WaitForExit` enforces `timeout_seconds` (default 900), killing the process tree.
- Both bitnesses (2026-10-25, plan `plans/dual-bitness-plugins.md`): the unchanged code built and ran for x64 in a spike (connect, Single Pass `Passed`, diff, analyzer against 64-bit TestStand); after the analyzer template fix the suite was 660/660 on each engine. That fix (`cab876a`): the built-in analyzer project header needs `buildversion`, or the engine rejects it as "not a property object file" (-17100) and `AnalyzerApp.exe` blocks on a modal dialog. Only a station without a saved `MyAnalyzerProject.tsaproj` uses the template, which is why it showed on 32-bit TestStand 2026 only. Measured through the engine API on both bitnesses, no dialog. Station record: `docs/validation/dual-bitness.md`.
- Analyzer async (2026-07-09): `AnalyzerJob` / `StartAnalyzerJob` / `GetAnalysisStatusAsync` in `TestStandService`, jobs kept in `_analyzerJobs` ~10 min, mirroring the `load_module_prototype` async-job infrastructure. No isolated worker is needed: the fault-prone module loads already happen inside `AnalyzerApp.exe`, which a native `.lvlibp` fault kills without taking the server down. Verified by `T14.AnalyzeDetailed_Async_ReturnsJobThenSameResultAsSync`.

---

## Rebuild-efficiency batch (2026-07-29) — behaviour changes to know

Driven by the ~700-call rebuild measured in the developer notes above. See memory `teststand-rebuild-efficiency-batch-2026-07-29`. The rules these changes produced are in `rebuild.md` and `tool-contracts.md`.

- **`copy_typedefs` preserves the source's attach state** (`attach='preserve'` is the DEFAULT; `'all'` is the old behaviour). It used to attach all 59 types where the original embeds 7 — a guaranteed diff and a forced restart of the rebuild.
- **Enum writes always land as EXPLICITLY SET (`[val]`).** `set_property_value` / `set_property_node` / `create_step_property` resolve an ordinal to its enumerator NAME first — file type list → engine-wide → read back off the property itself — because ONLY the by-name write clears TestStand's type-default flag. Previously an ordinal write silently produced `{val}` whenever the enum type was not yet in the target file's TypeUsageList (41 spurious diffs in one rebuild). `get_property_tree` derives `isDefault` per enum leaf from the symbolic name reading back EMPTY.
- Writing flags `0` on a type INSTANCE is not a no-op for TestStand: it marks the instance as overridden and turns `{val}` into `[val]` on its members (observed on a `LogEvent` local whose `LogLevel` matched the original until a redundant flags write). The rebuild model therefore writes flags only when a bit is set.
- `configure_python_module` sets the object-oriented settings through raw `TS.SData.PythonCall.*` writes; before that they needed ~15 `set_step_property` calls per step.
- `configure_sequence_call_module` writes the retained `SData.SFPath` AFTER the prototype load, which blanks it. A `SequenceArgument` keeps its state in SUBPROPERTIES; its own `Flags` NUMBER is what the FileDiffer shows as the argument's "Flags" (NOT the entry's PropFlags). A prototype load overwrites `Flags`/`ParamRepresentation` from the callee and renames entries after the callee's CURRENT parameters, so the import rebuilds the whole list in order rather than patching it.
- Numeric representation and format: wide integers are READABLE since the fix (they surfaced as `Empty` because `GetValNumber` rejects them).
- `set_step_property_flags` / `set_property_node` `exact` / `clear_flags` route through `SetExactFlags` (see the flags measurement above).
- Bulk writers (`insert_sequences_bulk`, `insert_variables_bulk`, `set_property_nodes`, `set_module_parameters`): measured on a 21-step/11-KB file, one `Save()` ≈ 26 ms, building all 21 steps without saving ≈ 4.7 ms; the same build via single-op tools (~27 saves) ≈ 630 ms vs. bulk (3 saves) ≈ 71 ms — 89 % less, and the gap grows with file size. 35 tools offer `save:false`. Engine start is expensive but amortized — it is a cached singleton, only paid again after a server rebuild.
- `get_property_tree` addressing by name replaced raw `Data.Seq[i].Main[j]` paths; `get_module_parameters` reads Python arguments (`TS.SData.PythonCall.Parameters`), which returned `[]` for every Python step before.

## Rules moved OUT of this file and INTO the server (2026-07-30)

Six rules that lived here as prose are now guards, defaults or a tool, so they hold for any client and any fresh session. Do not re-add the prose versions.

- The packed-library in-process load is REFUSED (see the `.lvlibp` guard above); `configure_labview_module` skips its own auto-load for such a VI with a `note`. The tool descriptions no longer recommend `isolate:false` — that advice had survived its retraction and was the one place where following the catalog killed the server.
- `diff_sequence_files` caps its rows by default (`DiffReportShaper.DefaultMaxResults`; `max_results=0` restores unlimited).
- `create_sequence_file(overwrite=true)` performs the close → retry-delete → create dance; `import_sequence_file` removes the destination's leftover `MainSequence` when the model has none (`remove_default_main_sequence`, default true; reported as `defaultMainSequenceRemoved`); `audit_type_consistency` is the only automated check for the type-registration conflicts the FileDiffer cannot see.
- Stale tool descriptions fixed: `duplicate_sequence` no longer advertises itself as the whole-file rebuild path, `import_sequence_file` reports the measured 0 differences instead of "one, an API limit", `diff_sequence_files` no longer calls the LabVIEW panes irreducible, and `copy_step_module` documents its minimal-write policy.

## More rules moved INTO the server (2026-07-31)

- The on-disk format is a first-class property (`file_format`, `fileFormat`); implementation and tests are in the file-format note above.
- `wait_for_execution` / `get_execution_status` now state the headless `Paused` trap, and `set_station_global` warns against being polled.

## TestStand ENVIRONMENTS (`.tsenv`) — engine internals (2026-08-14, issue #35)

The user-facing half (sources and their precedence, `tsenv_path='auto'`, the restart rule, what `get_engine_paths` and `open_sequence_file` report) is in the imported `headless-and-tools.md`. This section is the developer half: how the server makes an environment take effect and proves it did.

The server applies an **environment** (the Sequence Editor's `/env <path.tsenv>` switch) in-process via `EngineInitializationSettings.SetEnvironmentPath`, on the engine thread immediately before the activation (`ApplyEnvironmentBeforeActivation`), because the call throws once an engine exists.

- **Why it cannot be switched.** One engine per process, `SetEnvironmentPath` throws afterwards, and the last `ShutDown(final:true)` tears down NI licensing. Do not try `Engine.LoadEnvironment` — it works by relaunching the application, useless for an in-process headless server.
- **Never trust the setter — the server reads back.** `CanInitializeEngine()` is asked BEFORE the engine is created (TestStand's own check, which the `Cfg\GeneralEngine.cfg` file probe only approximates), and afterwards `GetEnvironmentPath()` plus `TestStandPath_CommonAppData` vs `TestStandPath_GlobalCommonAppData` prove the redirect took. **Measured on the global environment: `GetEnvironmentPath()` returns EMPTY and the three roots equal their `Global*` counterparts**, so an empty read-back is the honest "no environment", not a failed read. A requested environment that fails both signals makes the connect FAIL rather than silently work against the wrong `CommonAppData`.
- The mismatch warning of `open_sequence_file` is only computed when the process is environment-aware, so a station without environments sees nothing new. Config keys: `TestStand:EnvironmentPath`, `TestStand:EnvironmentAutoDetect`; auto-detect checks each ancestor in itself AND one level down because the real layouts keep the `.tsenv` in a SIBLING folder (`<root>\Config\X.tsenv` next to `<root>\Components\Sequences\*.seq`), which a parents-only walk never reaches.
- **`ConnectAsync` is BOUNDED** (`TestStand:ConnectTimeoutSeconds`, default 120). It used to wait forever; a modal dialog on the engine thread — the classic symptom of an uninitialized `CommonAppData` — hung the tool call and the whole session. On expiry the service latches "restart required" instead of starting a second engine next to the parked one.
- **The CHILD PROCESSES start their OWN engine** (issue #35 follow-up), so an environment applied only in-process leaves them on the global configuration, silently: `AnalyzerApp.exe`, `FileDiffer.exe` and `SeqEdit.exe` all take the same **`/env <path>`** switch — leading flag, space-separated, path quoted (`TestStandEnvironmentLocator.PrependEnvSwitch`). The isolated prototype worker takes **`--tsenv`**. Only the VERIFIED `ActiveEnvironmentPath` is forwarded.
  - **The worker's only channel is that argument.** `Program.cs` dispatches `--load-prototype-worker` BEFORE the `ConfigurationBuilder` runs, so `appsettings.json`, the inherited `TESTSTAND_MCP_` variables and the parent's command line are all invisible to it.
  - **Measured (2026-08-15):** a bogus `/env` hangs `AnalyzerApp.exe` on a modal "Sequence Analyzer" dialog (so the switch IS consumed; a valid one exits 0) — harmless here only because the forwarded path was already verified at connect. The environment demonstrably changes the engine's **SearchDirectories** (`C:\Users\Public\Documents\NI\TestStand …` globally vs. the product root under the environment), which is how the analyzer resolves modules differently. One real file produced IDENTICAL messages either way.
  - **`SeqEdit.exe` is single-instance:** a running editor keeps the environment it was started with and `/env` cannot retarget it. Which environment a running editor uses cannot be read back — its UI is CEF (see `teststand-seqedit-cef-no-automation`).
- Without a `.tsenv` **not one new COM call happens** — the global path is byte-identical to before.

## Working style: ponytail (lazy senior dev mode)

Always on for coding tasks (writing, fixing, refactoring, reviewing, choosing dependencies). Level is **full** unless the user says "ponytail lite", "ponytail ultra", or turns it off with "stop ponytail" / "normal mode". The same rules are the `ponytail` skill (`/ponytail lite|full|ultra`).

You are a lazy senior developer. The best code is the code never written. Solve the whole problem with the least new code, and write a reply a busy human understands in one read. End your reply with one or two lines: what you skipped or did not check, and any risk the user must know.

### Before you write

Read the task and the code it touches. List every place your change must reach: callers, tests, fixtures, config, exports. Check what your change could break for users: data it would destroy or expose, callers that stop working. That is scope. Extra features are not.

### The smallest complete change

Take the first option that fully works:

1. Does it need to exist? Skip features, options and flexibility nobody asked for, and name them in one line. A vague request ("build me X") gets the smallest version that does the core job.
2. Already in this codebase (a helper, component, service, pattern)? Use it the way the surrounding code does.
3. Standard library or a platform feature? Use it, unless the project has its own. A house component beats a native widget.
4. An installed dependency? Use it. Never add a dependency for a few lines.
5. Can it be one line a reader gets at a glance? One line.
6. Otherwise: the minimum code that works.

- Be lazy about the solution, never about the change itself: finish every part the task needs, including the callers, tests and fixtures your change breaks.
- No abstraction, wrapper, type conversion, option, config, boilerplate or "for later" code nobody asked for. Keep values in the form the platform already gives you. Deletion beats addition. Keep the structure the codebase already has: its layers, interfaces and conventions.
- The shortest working diff wins, once you know everything it must touch. A one-liner that needs decoding is not short.
- Comment only the why the code cannot show, in one line.
- Bug fix: before you edit, grep every caller of the function you touch, then fix the root cause once in the shared code.
- Code you move or merge keeps its error handling and validation.
- Between options of equal size, take the one that is correct on edge cases.
- Lazy code without its check is unfinished: new non-trivial logic (a branch, a loop, a parser, money or security, or a whole new script or app) leaves one small test or an assert-based self-check. Trivial changes need none.
- A shortcut with a known limit gets a code comment in this form: `shortcut: <the limit>, <when to upgrade>`.

Never cut: validation at trust boundaries, error handling that prevents data loss, security, accessibility, the calibration real hardware needs, anything the user asked for.

### Levels

| Level | Behavior |
|-------|----------|
| **lite** | Build what was asked. Name the smaller option in one line and let the user pick. |
| **full** | The rules above. Default. |
| **ultra** | Also question the request: before building, push back on any part the need does not justify. |

## General Conventions

- **Sequence file for tests:** Always use `DemoTestsequenz.seq`
- **`.Demo_jcm/` is ANONYMIZED demo material — keep it that way.** Those sequences are derived from a
  real customer file and carry a codename instead of the product. When you build, clone or comment
  anything in there: keep the existing codename, and write comments that state the FUNCTIONAL purpose
  only — never the customer, the real product name, the physical measurand or concrete measurement
  values. Sensitive detail lives in the leaf subsequences; if you only scaffold the top sequence, leave
  those as empty stubs. The folder is gitignored on purpose — do NOT record the codename↔product
  mapping in this file or any other tracked file. (The mapping itself is in memory
  `demo-jcm-anonymization-ufo-symphony`.) Comments round-trip through Windows-1252, so ASCII
  punctuation only.
- **MCP server restart:** After code changes always rebuild yourself:
  `taskkill //F //IM TestStandMCP.exe` + `dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86`
  (Target framework is **net8.0-windows / x86**. The TestStand engine requires the host's
  runtimeconfig to declare `Microsoft.WindowsDesktop.App` + `Microsoft.AspNetCore.App` — both
  are wired via `FrameworkReference` in the .csproj. Output: `bin\x86\Debug\net8.0-windows\`.
  For 64-bit TestStand build with `-p:Platform=x64`; output `bin\x64\Debug\net8.0-windows\`.)
- **New tool NAMES, enum values and optional params need a FRESH MCP session.** The client caches
  the tool catalog and argument schema at session start and rejects the new values with
  `invalid_enum_value` until then. This applies to every batch below.

---

## TestStand Behavioral Facts (proven by the integration tests)

These are hard-won behaviors from the `Test/TestExecution` suite (T01–T21,
`TestBase`, `TestDataBuilder`). Treat them as **known solution paths** — do not
re-discover them by trial-and-error. Per-tool gotchas already live in the tool
descriptions; the cross-cutting rules below apply regardless of which tool you call.

### Calling the TestStand COM API from C# (applies to every tool you add)
- **Any interop method with an `out`/`ref` parameter MUST be called on a TYPED reference.** Via
  `dynamic` or `Type.InvokeMember` the byref params do not marshal: the call throws, a surrounding
  best-effort `catch` swallows it, and the tool returns a plausible DEFAULT instead of an error. Every
  instance of this class of bug was silent and expensive: `Execution.GetStates(out,out)` made **every**
  execution report `Stopped` (so `wait_for_execution` returned instantly), `Thread.GetSequenceContext(0,
  out id)` emptied `get_execution_threads`/`get_thread_status`/`get_thread_call_stack`, and
  `Engine.FindFile` returned the literal `"True"` where a path was expected. A `var x = ...` taken off a
  `dynamic` re-poisons the chain, so cast at the boundary.
- Same rule for the **enum/type APIs**: `TypeUsageList.GetTypeDefinition` throws
  `TargetParameterCountException` under the dynamic binder, and the typed `LabVIEWAdapter` cast is what
  makes `SetServerInfo`/`Initialize` do anything at all (late-bound they no-op silently).
- Related signature traps: `Execution.Restart(bool breakOnEntry)` takes an argument; a `Thread`'s depth
  is **`CallStackSize`** (no `StackDepth`) and it has **no `State`** — read the owning execution's run
  state. (`T23`–`T29`; memories `teststand-getstates-reflection-fails`, `teststand-findfile-modal-prompt`.)
- **A "success" flag from the interop is not evidence — READ THE VALUE BACK.** Two measured cases in
  `configure_dotnet_module` alone (2026-08-03): `SetAssembly` was called as `SetAssembly(path, true)`
  when the signature is `SetAssembly(DotNetModuleAssemblyLocations location, string path)`, so it threw
  into a `catch` that "recovered" by setting a property named `Assembly` — which does not exist — while
  `appliedSettings` reported the path as applied; and **`DotNetModule.LoadMemberInfo` returns `true`
  even for an assembly path that does not exist** (`C:\Dummy\MyAssembly.dll`), so it cannot be used to
  decide whether a member resolved. The real check is `DotNetModule.Calls[0].IsCallValid(out reason)`
  (plus `DotNetModule.AssemblyWarnings` when the reason comes back empty). Report only what a read-back
  confirms; anything else belongs in the result's `note`. Pinned by `T13`.
- **The other four `configure_*_module` tools were AUDITED against their typed interfaces AND the step
  property tree (2026-08-03) — they are correct, do not re-investigate.** Verified landing spots:
  `configure_dll_module` → `CommonCModule.ModulePath`/`.FunctionName` → `TS.SData.Call.LibPath`/`.Func`;
  `configure_labview_module` → `LabVIEWModule.VIPath` → `TS.SData.ViCall.VIPath`;
  `configure_python_module` → `PythonModule.ModulePath`/`.FunctionOrAttributeName` plus the raw
  `TS.SData.PythonCall.*` writes for the object-oriented settings (note the leaf is
  **`ClassInstanceLocation`** while the typed property is `ClassInstanceLocationExpr`);
  `configure_sequence_call_module` → `SeqName`/`UseCurFile`/`SFPath`/`ThreadOpt`/`AsyncThreadExpr`.
  All now go through `TrySetAndVerifyModuleProp` (set + read back), and `T13` asserts the resulting
  TREE, not just `AppliedSettings`. The `|| "ModulePath"` / `|| "FunctionName"` fallbacks they used to
  carry were dead code — those properties do not exist on `LabVIEWModule`/`PythonModule`/`CVIModule`.

### Where a .NET step keeps its configuration (`T13`, 2026-08-03)
The rules for driving a .NET step are in `tool-contracts.md`; these are the facts behind them.
- The member to invoke lives in **`TS.SData.Calls[0]`** (`ClassName`, `MemberName`, `MemberType`, `Static`, `Params`), NOT in `TS.SData.FunctionName` — that root property stays EMPTY even on a correctly configured step, so do not assert on it. The assembly, however, IS at the root: **`TS.SData.AssemblyPath`** (+ `AssemblyLocation`: 0 = file on disk, 1 = GAC).
- `MemberType` must be **1** (`DotNetMember_CallMethod`). At the default **0** (`DoNotCall`) the step executes as a **silent no-op that still reports `Passed`** — the failure mode that hid the bug above.
- `NameOfMethodToCreate` is a REAL settable property for the adapter's code GENERATION, not the member to call. Writing the method name there succeeds and configures nothing.
- Do **not** pre-set `MemberFlags`. Try the untouched lookup first and fall back to the Static bit (`1`) only when `IsCallValid` rejects the result; `configure_dotnet_module` does this and reports `staticFallbackUsed`. A static member can validate with `Calls[0].Static` still `false`.

#### Member resolution has THREE tiers — the module-level one resolves almost nothing (2026-08-15, issue #37)
`DotNetModule.LoadMemberInfo` + `IsCallValid` — the whole resolution logic until then — only accepts a member whose prototype it can match without help. Measured on a purpose-built assembly (7-point signature matrix): with the flags untouched **nothing** resolved, with the Static bit **only bare `NoArgsVoid()`**; every member with a parameter or a non-void return failed with *"Prototype does not match that found for member '&lt;name&gt;'"* although the member exists. So a normal method was unreachable and the step ran as the silent `Passed` no-op above.
- The tier that works is the CALL level: **`DotNetCall.LoadPrototypeFromSignature(nameOrSignature, allowMemberNameMatching:true, 0)`** on `Calls[0]` — the API behind the Edit .NET Call dialog. It resolved 1- and 2-argument members, non-void returns and `out` parameters, and populates `Calls[0].Params`. A member that does not exist returns `false`, so failure stays honest.
- `configure_dotnet_module` runs all three tiers in order (untouched → Static bit → signature); **`load_module_prototype` runs the SAME three** because the generic `Module.LoadPrototype` does not resolve a .NET member — it throws, which used to surface as `prototypeLoaded:false` with an empty interface. An already-valid interface survives the re-load (pinned by `T13`) because the flags are restored and a failed attempt leaves the step as it was.
- The signature form of `method_name` **does** work (issue #37 claims it does not; measured otherwise, `T13`): it SELECTS the member, and the step still stores the plain `Calls[0].MemberName` the engine executes off. The exact spelling is what `DotNetAdapter.GetMemberNames` returns; the trailing ` (static)` of its `sigs` list is a display suffix. `GetMemberNames(location, asm, class, options, out names, out sigs)`: **`options=0` = static members + constructors, `options=1` = instance members**, so a wrong flag looks like a missing member.
- An INSTANCE member alone is refused (*"is not valid as the first call in the invocation because it is an instance member that requires an object"*). `create_object=true` builds the CALL CHAIN: `Calls[0]` the constructor, `Calls[1]` the member invoked on that object; `dispose_object` lands as `CallDispose` on the constructor's returned object. `T13` runs one for real: `Triple(4)` on a constructed instance writes 12.
  - `Calls` is EMPTY on a fresh step (indexing it throws "Cannot index an empty array"), so the chain is built with `Calls.New(i)` + `LoadPrototypeFromSignature` per entry and REBUILT rather than patched on a re-configure. The object is handed between entries **implicitly** — there is no expression to plumb, and `CreateObject` flips to true by itself.
  - **Calling into an object that already EXISTS elsewhere is not reachable.** Measured both ways: `<Use Existing Object>` cannot be loaded by signature (returns false) and `DotNetModule.ClassReference` does not persist (reads back empty). Do not re-investigate without a new idea.

#### A .NET step's parameters live in `Calls[i].Params` (2026-08-15, issue #37)
Never in the flat `Module.Parameters` container — `get_module_parameters` therefore returned `[]` for every .NET step. Entries are prefixed `<member>.` when `Calls` has more than one element (`InstanceOps.Return Value`, `Triple.a`, …).
- Leaves per entry: `Name`, **`ArgVal`** (the binding expression — for `Return Value` the DESTINATION), `Type` (numeric `DotNetParameterTypes`, e.g. 12 = Double), `TypeName` (class/struct/enum only), `Flags`, `IsOptional`, `CallDispose`, …
- **There is no Direction leaf** — direction sits in the `Flags` bits: measured `0` for an input, `6` for `Return Value`, `10` for an `out` parameter ⇒ bit `4` = return, bit `2` = output.
- **No "use default" companion flag** (measured 2026-08-15): the typed `DotNetParameter.ValueExpr` writes the same slot as the tree's `ArgVal`, and `UseDefaultValue` neither changes with it nor appears in the step tree. The raw route (`set_step_property` on `TS.SData.Calls[0].Params[i].ArgVal`) is equivalent to `set_module_parameter`.
- `T13` proves the whole chain by RUNNING a step: `Add(2,3)` with its return value bound to a StationGlobal reads back 5 (raw route) and `Add(4,5)` → 9 via `set_module_parameter` — the only evidence that distinguishes a real call from the no-op.

### Engine lifecycle & file handling
- **Single in-process engine only.** A second engine cannot be torn down cleanly while the first one lives → the host hangs on exit. The MCP server uses exactly one engine; never spin up a second. (`T01`, see also memory `teststand-testhost-teardown-hang`.)
- **An execution only advances while the ENGINE-CREATION thread pumps Windows messages.** TestStand posts progress to a hidden window owned by that thread; the server therefore owns the engine on one dedicated, persistent MTA thread running a continuous pump. A pump on any other thread does not work (apartment is irrelevant). Do not move engine creation into a transient `Task.Run` — that was the original "executions never run" bug. (Memory `teststand-execution-needs-waitforendex-pump`.)
- **`create_sequence_file(overwrite=true)`:** the engine releases the OS file handle *asynchronously*, so the tool does the close → retry-delete (≈5× / ~300 ms) → create dance itself. Without `overwrite` a pre-existing or still-loaded file fails with a sharing violation, which is the honest outcome for an accidental overwrite.

### 1:1 file rebuild fidelity ceilings (`T33`; see memory `teststand-1to1-rebuild-fidelity`)
**SUPERSEDED (2026-07-29).** These were the ceilings of the granular per-step approach; `export_sequence_file` + `import_sequence_file` reach 0 differences inside the imported scope (`rebuild.md`). History only:
- `{val}` vs `[val]` stopped being a ceiling once every enum write resolved the ordinal to its enumerator NAME (see the rebuild-efficiency batch).
- Duplicate step names used to need a rename-and-rename-back dance; the `Name#N` and `@idx:N` selectors replaced it.
- CLOSED: number Representation (UInt64) / NumberFormat (on `insert_local_variable` / `insert_sequence_parameter` / `set_property_node`, and readable); PropFlags on Locals/Globals/Params members (`set_property_node` `flags` + `clear_flags`); a Parameter's default value (`set_property_node` scope=`Parameters`), its comment (`set_parameter_comment`) and its nested container members.

### Enumeration internals (`T22`)
The tools and their rules are in `tool-contracts.md`.
- **An enum is a real named TypeDef, NOT a file-root subproperty.** `NewSubProperty(PropValType_Enum)` is rejected ("Unrecognized value") and `UpdateEnumerators` on a plain Number throws ("Expected Enumeration, found Number"). Create via `Engine.NewDataType(PropValType_Enum)` → set `Name` → `TypeUsageList.InsertType(type, 0, CustomDataTypes)` → `SetIsTypeAttachedToFile(idx, true)` (so it persists embedded) → `UpdateEnumerators`. Because enums live in the **TypeUsageList** they do not appear in `get_data_types`, which lists file-root subproperties.
- **Write vs read formats differ.** `UpdateEnumerators` takes an ARRAY of containers, each with `EnumeratorName`/`EnumeratorValue` (+`OldEnumeratorName` for renames; it REPLACES the whole list). But the `Enumerators` getter returns enum-TYPED values — read each one's name with `GetValString("", PropOption_CoerceToString=128)` and number with `GetValNumber("", PropOption_CoerceToNumber=64)`, else "Expected type String/Number. Found type <Enum>."
- **Use typed interop, never `dynamic`, for `TypeUsageList`/`PropertyObject` here.** The C# dynamic-COM binder throws `TargetParameterCountException` on `GetTypeDefinition` and mis-binds others. A `var tul = ...` off a `dynamic sf` silently re-poisons the chain.

### Step-editing internals (`T31`/`T32`)
The tools and their rules are in `tool-contracts.md`.
- `create_step_property` resolves named types engine-wide (fallback `Engine.NewPropertyObject` + `SetPropertyObject(PropOption_InsertIfMissing)`) and authors `TS.SData.ViCall.Parms`, `…Parms[i].ArrayClusterEls`, `TS.AdditionalResultsHints`, `Result.TimeoutOccurred`. Array elements are instantiated with the array's ELEMENT type.
- Array elements are paired BY NAME by the editor/FileDiffer (`[i] Name`); `create_step_property(array_elements)` creates them unnamed, hence `rename_step_property`. `get_property_tree` reports the name as `elementName`.
- `set_step_property` / `create_step_property` `unescape:true` exists for the VIDescriptions that carry a bare CR.
- **Prototype auto-load on module config (ALL adapters):** every typed module-config tool runs `Module.LoadPrototype(0)` **after** it sets the target. **Order matters** — the load is centralized in `ConfigureModuleAsync` to run strictly after the VI-path/target is set; without it the interface stays empty. For SequenceCall the load also enforces `UseDef ⇔ empty Expr` (unbound args `UseDef=True`, bound `False`) — matching a genuine call for the FileDiffer. `set_sequence_call_target` / `insert_steps_bulk` also load the SeqCall prototype; `set_module_parameter` loads it when the named arg is missing. The auto-load inside the `configure_*_module` tools runs in-process and synchronously, which is why the packed-library guard skips it (see the `.lvlibp` guard in the developer notes).
- **`load_module_prototype` internals (2026-07-09).** A LabVIEW load attaches to or starts LabVIEW — the same slow work the editor's "Reload Prototype" does — and can exceed the MCP transport's ~60 s window (`-32001`), hence the async job (`get_prototype_load_status`). **Root cause of the original crash (from the NuGet `…Interop.AdapterAPI`):** headless, with no explicit server, the LabVIEW adapter's **AutoDetect** resolves a LabVIEW **Run-Time** (`lvrt.dll`) that can't be bound in-process → the delay-load SEH `0xC06D007E`. The editor works because it uses the running LabVIEW **development** environment (**ExecServer**, ActiveX — no RTE delay-load). So before a load the server routes the adapter to the ExecServer: `GetAdapterByKeyName(key)` → **cast to the typed `LabVIEWAdapter`** (the `Initialize`/`Get`/`SetServerInfo` methods live on THAT interface, NOT the generic `Adapter` dispinterface — which is why an earlier late-bound `adapter.Initialize()` silently no-op'd) → `SetServerInfo(ExecServerDeferred,"LabVIEW")` + `Initialize()`, restored afterward (`labview_server`: `deferred` default, `exec`, `rte` = legacy AutoDetect, `auto` = leave untouched). This helps a plain `.vi`; it does NOT make a packed-library VI loadable (measured twice, 2026-07-29: `isolate:true` timed out on 8 steps x 120 s, `isolate:false` killed the server). The only route for a `.lvlibp` pane is `copy_step_module`. What the worker still buys is crash containment for a non-packed VI (`prototypeLoaded:false`, `workerOutcome:"crashed"`/`"timeout"`, server lives). Non-LabVIEW adapters (SequenceCall/.NET/DLL·CVI/ActiveX) always run fast, in-process, synchronously. The result carries `executionMode` (`in-process`|`worker`), `workerOutcome`, `jobId`, `status`; on success the worker SAVES to disk and the parent reloads it, bounded by `timeout_seconds` (default 120; on expiry its process tree is killed).
- **SILENT worker death — no WER box AND no NI Error Reporter dialog.** `SetErrorMode` alone only hides the OS fault box; NI's green "…encountered a problem and needs to close" reporter is an IN-PROCESS unhandled-exception hook. The worker installs layered guards up front (`LoadPrototypeWorker.InstallSilentDeathGuards`): the decisive one is a **vectored exception handler** (`AddVectoredExceptionHandler(first=1)`) that, on the MSVC delay-load SEH family (`0xC06Dxxxx` — high word `0xC06D`, so it never fires on ordinary handled C++/CLR exceptions `0xE06D…`/`0xE0434352`), calls `TerminateProcess` DURING first-chance dispatch — before any frame-based/unhandled handler, i.e. before NI's hook can start the dialog. Plus `SetErrorMode`, `WerSetFlags(NO_UI)` + `WerAddExcludedApplication`, CRT `_set_abort_behavior(0,…)`, and a `SetUnhandledExceptionFilter` backstop for other fatal faults. Verified: a raised `0xC06D007E` dies in ~4 s (not a timeout) with ZERO lingering `WerFault.exe`/NIER processes, and the real load still returns `not-loaded` cleanly (the VEH does not false-fire on the handled path). Test hook: env `TESTSTAND_MCP_LP_SIMULATE_CRASH` = `raise` (RaiseException → exercises the VEH) or `1` (direct TerminateProcess).
- With element names mirrored, a full tool-driven rebuild of TFW_DemoModule.seq diffs **identical (0 differences)** against the original (`T32`).

### 1:1-rebuild tool-gap batch (2026-07-07)
Closed the residual gaps found rebuilding `TFW_MCP_Test.seq`; the rules are in `tool-contracts.md`.
- Enum leaves used to read back as `Unknown`/`Empty`/`0` because a plain read throws on enums; they read as `valueType:"Enum"`, `value:{ordinal, symbolicName}` now. A value set by raw ORDINAL reads back with an empty `symbolicName` until reload; set by NAME (or the authored default) keeps it.
- `value_type='enum'` (+ `type_name`) on `set_property_value` and `create_step_property` creates the member as an instance of the named enum typedef and sets its ordinal/name (CoerceToEnum), so an enum container-member gets its real type, not an anonymous container.
- `set_file_global` used to write "true"/"false" as a String, which never persisted; it now types a boolean correctly and PRESERVES an existing global's authored type. `set_file_global_comment` is the FileGlobals twin of `set_local_variable_comment`.
- `compare_sequence_files` defaults to `mode='native'` (the FileDiffer, == `diff_sequence_files`); `mode='structural'` is the old in-process compare, now self-labelled (`fidelity`/`note`).
- `set_step_property` auto-detect peeks the target type first (writing "True"/"False" to a String expression prop used to fail with "Expected Boolean, found String").
- A nameless `Label` (empty name = blank spacer) auto-gets `TS.Icon="ni_blank.ico"` + step flag `0x4000000` on insert (named labels keep `label.ico`); bulk insert allows an empty name for a Label.
- Comment/description encoding: the guard used to over-warn (it checked `≤0xFF`) and is now Windows-1252-accurate. This branch uses a hardcoded 1252-extras table; another branch fixed it via `WideCharToMultiByte` — prefer that on merge.

### 1:1-rebuild tool-gap batch — wave 2 (2026-07-07)
Closes the P1 analyzer errors and the LabVIEW-metadata diffs.
- `set_property_value value_type='named_type'` instantiates a FULL typed instance via `Engine.NewPropertyObject(NamedType)` + `SetPropertyObject(InsertIfMissing)` (examples of such containers: `TFW_DB_TestCasesLimits`, `VisionSensorSbsi_Reply_Payload`, `Error`). It is the ONLY way a nested container member gets its named type instead of an anonymous `Container`, the cause of every `NI_ExpressionEvaluationError "Expected <Type>, found Container"`. `value_type='enum'` takes its value as the `ordinal` param (numeric, preferred) or `value`; same on `create_step_property`.
- **`copy_step_module` (2026-07-08 fix — see [[teststand-copystepmodule-clone-fix]]):** it deep-copies `TS.SData` (a SequenceCall's ActualArgs, a RunVIAsync's `SeqCallStepAdditions` incl. `Parameter0..3`, an adapter module) + the step-own `VIModule` (ViCall metadata) and aligns the adapter — no LabVIEW needed. Each subtree is `PropertyObject.Clone(path, 0x20000000 PropOption_CopyAllFlags)`d BEFORE `SetPropertyObject`: a live source object still has a parent and TestStand rejects it ("already has a parent object. You must first clone the item"), which had made `copiedPaths` come back empty. It ALSO clones the authored step-config subtrees a fresh insert doesn't instantiate from the step-type template — `TS.AdditionalResultsHints`, `TS.CustomResults`, `TS.ErrorDialogOptions`, `Result.TimeoutOccurred` (each copied only if present on the source) — so a NON-adapter step like an `NI_Wait` reproduces faithfully too (its wait TARGET still needs `configure_wait`, as that lives in step-root props, not `TS.SData`). Verified: rebuilding only `Init`+`Close` of `TFW_Symphony_EOL.seq` diffs 0 under both sequences incl. all three `.lvlibp` LabVIEW steps.
- `set_step_property` resolves `property_path='Icon'` → `TS.Icon` and `'Flags'` → the step's flag bitfield via `SetFlags` (both used to error "Unknown variable or property name").

### 1:1-rebuild tool-gap batch — wave 3 (2026-07-09): scope-generic property-tree CRUD
Closed the last write/delete/flag gap on the **`Parameters.*`** level; see memory `teststand-property-node-crud-2026-07-09`. The rules are in `tool-contracts.md`.
- **Roots per scope:** `Parameters`/`Locals` → `Sequence.Parameters`/`.Locals` (need `sequence_name`); `FileGlobals` → `FileGlobalsDefaultValues`; `StationGlobals` → `Engine.Globals` (commits via `CommitGlobalsToDisk`, `file_path` unused); `SequenceFile` → `AsPropertyObject()`.
- `set_property_node` reuses the SAME creation switch as `set_property_value` + `create_step_property`. PropFlags are applied LAST through `SetFlags` (which replaces the bitfield, see the flags measurement above); `132`/`0x84` = `0x04` PassByReference + `0x80`. A flags-only call never clobbers the value (closes the Local/Parameter-submember PropFlags residual, e.g. `Job1.SetJob_Reply_Payload` `0x4` vs `0x0`). `DeleteSubProperty` accepts a dotted path, so `delete_property_node` handles a whole parameter and a nested submember in one call.
- **Verified end-to-end** rebuilding the anonymous-container parameter `MDC_cmd` of `_MDC_com` (`TFW_MDC_com_Python.seq`): the 3 target diffs (Flags, Request subtree, Response subtree) drop to 0 with **zero** newly-introduced diffs; enums reproduce identical (`[Command] (0)` / `[GetKfMdcProtocolVersion] (4629)` / `[GetHousingTemperature] (4630)`).
