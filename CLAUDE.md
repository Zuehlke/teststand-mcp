# TestStandMCP — Behavior Rules for Claude

## Rebuilding a .seq 1:1 — `export_sequence_file` + `import_sequence_file` FIRST

For a whole-file reproduction, migration or bulk edit, use the export/import pair. It is the
**default** path; the granular tools are for surgical single edits.

```
export_sequence_file(file_path)                    → writes <file>.model.json, returns a summary
create_sequence_file(dest)
import_sequence_file(model_path, dest_file_path)   → rebuilds everything, returns counts + warnings[]
delete_sequence(dest, "MainSequence")              → the model has none
diff_sequence_files(orig, dest, summary_only=true) → verify
```

Measured on `TFW_MDC_com_Python.seq` (30 sequences, 131 steps, 13 object-oriented Python steps,
8 LabVIEW `.lvlibp` steps): **9 MCP calls, 0 import warnings, 9 FileDiffer differences — and ZERO in
the LabVIEW connector panes.** The same rebuild done with the granular tools took ~700 calls, 3 diff
iterations and left 224 differences.

**The `.lvlibp` connector pane IS reproducible headless — it is NOT an irreducible residual.** Getting
there depends on three ordering rules that are easy to get wrong (all measured on this file):
1. **A SequenceCall `LoadPrototype` disables LabVIEW VI loads for the REST OF THE PROCESS.** After one
   such load, every LabVIEW pane load fails with "could not resolve the target/module"; the identical
   call in a process that has not done one succeeds in ~5 s. So: load ALL VI panes BEFORE configuring
   any SequenceCall. `import_sequence_file` does this in separate passes.
2. **The VI load must run IN-PROCESS (`isolate:false`).** The crash-isolated worker is a separate
   process that does not inherit the attachment to the running LabVIEW ADE, tries to start its own and
   times out every time — 8 steps × 120 s, all failed, while in-process takes ~5 s per step.
3. **The step must be SAVED to disk before the load** (the worker path reads from disk; a load after a
   run of `save:false` edits reports the step as out of range, which reads like an unloadable VI).

- The model is written to DISK, not returned inline (a 30-sequence model is ~350 KB) — pass the path
  straight to import. `inline=true` only for a single sequence.
- The model round-trips: file comment/version, every type WITH its attach state, file globals, and per
  sequence its description/result-recording, parameters, locals (nested members, enum ordinal +
  symbolic name + default state, PropFlags, numeric representation/format, comments) and all steps
  with their properties and complete module configuration.
- Import order is fixed and matters: types → file globals → ALL sequences with their interfaces →
  ONLY THEN steps, so every callee's parameters exist before a caller's prototype is loaded.
- `warnings[]` names every item that could not be applied. A non-empty list means a partial import —
  read it, do not assume success from the counts.

**Reading a big diff:** always start `diff_sequence_files(..., summary_only=true)`. Every response
carries `byCategory` / `byChangeType` / `bySequence` over ALL differences; the individual rows are the
expensive part (600+ of them blows the tool-result budget). Then drill in with `include_categories` /
`exclude_categories` / `path_filter` / `change_types` / `max_results` and
`group_by='category'|'sequence'`. `exclude_categories=['labview_vicall']` hides the block that cannot
be reproduced headlessly at all.

**Connector-pane BINDINGS need their own writer.** `set_module_parameter` always clears a control's
`UseDefaultValues` as a side effect, so binding every parameter through it flips that flag wherever the
source keeps the VI's own default (a remembered expression next to "use default" — the same asymmetry as
a SequenceCall argument's `UseDef`). Measured: 31 differences became 68 (write everything), 41
(non-empty only), 39 (flag-aware) — while writing `ViCall.Parms[i].ArgVal` and `UseDefaultValues`
INDEPENDENTLY gave 9. For 1:1 work set those two properties directly, not via `set_module_parameter`.

### Residuals after export/import (the only ones left; 9 on the reference file)
- **1× a named-type instance's ENUM member reads as explicitly-set.** Both instantiation routes
  (`NewSubProperty(NamedType)` and `Engine.NewPropertyObject`+`SetPropertyObject`) produce `[val]`
  where the editor produces `{val}`. API limitation, cosmetic.
- **6× a cross-file SequenceCall's cached `Prototype`** keeps the callee's parameter DEFAULTS
  (`LogProperty {""}`); a prototype load recreates the names but not the values, and preloading the
  callee file does not change it.
- **2× a step's authored `TS.AdditionalResultsHints` / `CustomResults` arrays** are not part of the
  model; use `copy_step_module` when they matter.
- **`NI.Analyzer.IgnoredMessages`** is invisible to the engine API (see below), so the rebuild shows a
  few extra analyzer warnings the original suppresses.

## Rebuilding a .seq 1:1 (whole-sequence clone) — the older per-sequence path

To reproduce an existing sequence file (a "rebuild"), prefer the **whole-sequence
clone** over the per-step insert+configure dance. `duplicate_sequence` deep-clones a
whole sequence (steps, modules, locals, parameters, comment, all settings) — within a
file or **cross-file** via `target_file_path`. Recipe:

1. `create_sequence_file` (the new file)
2. `copy_typedefs` (all types) — so cloned sequences/globals resolve their types by GUID
3. `duplicate_sequence` source→target for **each** sequence, in source order, same name
4. `delete_sequence` the default `MainSequence`
5. `copy_file_globals` — file globals belong to no sequence, so the clone misses them
6. `copy_file_attributes` + `set_file_properties` (comment/version)
7. `save_sequence_file`, then **verify with `diff_sequence_files`** (the native FileDiffer)

**Verification semantics** (`diff_sequence_files` / `compare_sequence_files mode=native`
are the SAME diff — use `diff_sequence_files`):
- In the diff values, `{val}` = TYPE-DEFAULT (not explicitly set), `[val]` = EXPLICITLY
  set. An enum/member already at its type default must be **left unset** — setting it
  flips `{val}`→`[val]` and creates a spurious diff.
- A lone **`File Properties > Attributes`** delete (e.g. `NI.Analyzer.IgnoredMessages`)
  is IRREDUCIBLE: the engine API never loads those file attributes into memory (only
  FileDiffer's raw reader sees them), so no tool can read or reproduce them. Treat an
  Attributes-only diff as a functional 100% match (`identical=false` is expected).

See memory `teststand-whole-sequence-clone-rebuild-2026-07-08`.

## CRITICAL: How to build sequences from a flowchart/description

The `teststand-sequence-builder` workflow is **interactive** — it asks the user
per step whether to link a `SequenceCall` (target file + subsequence) or insert a
plain placeholder. That per-step question uses `AskUserQuestion`.

**`AskUserQuestion` is NOT available to spawned subagents** (it depends on the
main-conversation UI). Therefore:

- **NEVER** delegate sequence-building to the `teststand-sequence-builder` via the
  Agent/Task tool. If spawned as a subagent, every linking question fails silently
  and all steps degrade to Statement placeholders — exactly the failure to avoid.
- **ALWAYS run the builder workflow in the MAIN conversation thread.** When the
  user asks to "build a sequence from a flowchart" (or "use the Seq agent"), open
  `.claude/agents/teststand-sequence-builder.md`, read its workflow, and execute
  those steps yourself in the main thread — so the per-step `AskUserQuestion`
  linking prompts actually reach the user.

## Documenting sequence files (teststand-doc-generator)

The `teststand-doc-generator` agent turns a `.seq` file into a modern Word
documentation: title + short file description, real Word TOC, one section per
sequence (description, parameter table with By Value / By Reference, compact
flow-indented step listing with the Setup/Main/Cleanup groups preserved and
each step's original TestStand icon tinted monochrome in the document accent
color), and a rendered diagram of the call dependencies between the sequences.

- **Unlike the builder, this agent MAY (and should) be spawned as a subagent**
  via the Agent tool — it is non-interactive and strictly READ-ONLY toward
  TestStand.
- **Ask the document language FIRST — in the MAIN thread.** Before spawning
  the agent, ask the user via `AskUserQuestion` which language the
  documentation should be written in (offer at least Deutsch / English, with
  the language of the user's request as the recommended first option; "Other"
  free text covers further languages). The subagent cannot ask —
  `AskUserQuestion` does not work in subagents. Skip the question only when
  the user already stated the language in their request.
- Pass in the task prompt: the `.seq` path (required), the document language
  (from the question above; `de`/`en` built in, other languages via the
  script's `labels` override), optionally output `.docx` path and custom title.
- The heavy lifting is deterministic: `scripts/generate_teststand_doc.py`
  (data JSON → dependency diagram via headless Edge/Chrome → .docx via
  python-docx; run with the `py` launcher — plain `python` is not on PATH).
  The agent only collects data via the read-only MCP tools and runs the script.

## Presenting sequence files (teststand-presentation-generator)

The `teststand-presentation-generator` agent turns a `.seq` file into a modern,
interactive **HTML presentation** (dark glassmorphism single-page app): a header
with the sequence/source, Setup/Main/Cleanup phase cards, clickable subsequences
that open a detail overlay, and a "Code & Flowchart" compare view. The flowchart
nodes use the **original TestStand step icons in full color** (pulled from the
local install) and reconstruct loops (While/For/…) and branches (If/Select/Case)
as nested blocks. Output is ONE **self-contained `.html`** (icons embedded as
base64) — shareable as a single file. It is the visual counterpart to the
`teststand-doc-generator` (Word). Modelled on `.Demo_jcm/TSmcp_demo/index.html`,
but **without** the QR code / NI-Gold-Partner image.

- **MAY (and should) be spawned as a subagent** via the Agent tool — non-interactive
  and strictly READ-ONLY toward TestStand.
- **Ask the presentation language FIRST — in the MAIN thread** (same rule as the
  doc-generator: `AskUserQuestion` does not work in subagents). `de`/`en` built in.
- Pass in the task prompt: the `.seq` path (required), the language, optionally the
  output `.html` path and a custom title.
- **No live SeqEdit screenshots.** TestStand 2026's Sequence Editor renders its UI
  with embedded Chromium (CEF) → opaque to Windows UI automation, so per-sequence
  screenshots cannot be captured reliably/automatically (see memory
  `teststand-seqedit-cef-no-automation`). The compare view's right pane is a
  **rendered TestStand-style step listing** (real icons, flow indentation,
  Setup/Main/Cleanup groups) built from the data — always works, needs no editor.
  If someone supplies a folder of manually-captured PNGs + `_manifest.json`, the
  generator can embed them via the optional `--shots-dir`.
- The heavy lifting is deterministic:
  `scripts/generate_teststand_presentation.py` (data JSON + `scripts/presentation_template.html`
  → self-contained `.html`; real icons via Pillow from `…\National Instruments\TestStand*\Components`;
  run with the `py` launcher). No headless browser needed (unlike the doc-generator).
  The agent only collects data via the read-only MCP tools and runs the script.

## Sequence Design Rules

### Flow Control: If/Else always with NI_Flow_* Step Types

When creating TestStand sequences via the MCP tools, the following mandatory rules apply:

**ALWAYS use for conditional branching:**
```
NI_Flow_If      →  Entry into an If-condition
NI_Flow_ElseIf  →  Additional condition (optional)
NI_Flow_Else    →  Else path
NI_Flow_End     →  End of the If-block
```

**ALWAYS use for loops:**
```
NI_Flow_While      →  While loop
NI_Flow_DoWhile    →  Do-While loop
NI_Flow_For        →  For loop
NI_Flow_ForEach    →  ForEach loop
NI_Flow_SweepLoop  →  Sweep loop (iterates over a parameter range)
NI_Flow_StreamLoop →  Stream loop (iterates while a data source streams)
NI_Flow_End        →  End of the loop
```

**FORBIDDEN (except justified exceptions):**
```
Goto   →  Only allowed for patterns that cannot be expressed with NI_Flow_*
           (e.g. backward jumps that are not a loop)
Label  →  Only as a target for the mentioned Goto exceptions
```

**Rationale:**
- `NI_Flow_*` are structured, readable, and maintainable constructs
- `Goto/Label` produce spaghetti code, are hard to follow, and
  error-prone during sequence modifications

### Default Step Type When Unclear

**When it is not clear which step type a step should be, default to
`SequenceCall`** (instead of `Statement`).

- Applies to every "action"/placeholder step whose concrete type is not
  otherwise determined by a rule (e.g. not a flow step, not a Wait, not a
  Check — see the semantic auto-mapping rules).
- The `SequenceCall` may be left **unresolved** (no target) when the target
  subsequence is not yet known — the user links it later.
- The deterministic rules still take precedence: `NI_Flow_*` for branching/
  loops, `NI_Wait` for waits/delays, result-template/`PassFailTest` for
  checks. `SequenceCall` is only the fallback for the otherwise-ambiguous
  "plain action" case where `Statement` was previously used.

### Correct Structure for an If/Else Block:

```
[0] User_Enters_Credentials   Statement
[1] Check_Credentials         Statement
[2] If_Credentials_Valid      NI_Flow_If      → Condition: Locals.CredentialsValid == True
[3]   Log_User_In             Statement       → (True path)
[4]   Redirect_To_Dashboard   Statement
[5] Else_Invalid              NI_Flow_Else
[6]   Show_Error_Message      Statement       → (False path)
[7]   Return_To_Login_Form    Statement
[8] End_If                    NI_Flow_End
```

### Correct Structure for a Select/Case Block:

**Each `NI_Flow_Case` opens its OWN block and needs its OWN `NI_Flow_End`** —
this is the key asymmetry vs. `If`: `NI_Flow_ElseIf`/`NI_Flow_Else` are *clauses*
of one `If` and share a single closing `End`, but every `Case` is its own block.
A single `End` for the whole `Select` makes TestStand **nest** the cases inside
one another instead of rendering them as siblings.

```
[0] Select_State    NI_Flow_Select   → ItemExpr (set_flow_condition): Locals.State
[1]   Case_A        NI_Flow_Case     → case value(s): "A"
[2]     Do_A        Statement
[3]   End_Case_A    NI_Flow_End      → closes Case_A
[4]   Case_B        NI_Flow_Case     → "B"
[5]     Do_B        Statement
[6]   End_Case_B    NI_Flow_End      → closes Case_B
[7] End_Select      NI_Flow_End      → closes Select
```

`validate_sequence_plan` enforces this: a `Case` whose parent block is not the
`Select` (because the previous `Case` was not closed) raises
`E_CASE_WITHOUT_SELECT`, and any unclosed `Case`/`Select` raises
`E_UNCLOSED_BLOCK`. So `n` cases ⇒ `n` case-`End`s + 1 select-`End`.

### All Available Step Types (from get_step_types):
- Flow Control: `NI_Flow_If`, `NI_Flow_ElseIf`, `NI_Flow_Else`, `NI_Flow_End`,
  `NI_Flow_While`, `NI_Flow_DoWhile`, `NI_Flow_For`, `NI_Flow_ForEach`,
  `NI_Flow_SweepLoop`, `NI_Flow_StreamLoop`,
  `NI_Flow_Select`, `NI_Flow_Case`, `NI_Flow_Break`, `NI_Flow_Continue`
- Tests: `NumericLimitTest`, `StringValueTest`, `PassFailTest`, `NI_MultipleNumericLimitTest`
- Actions: `Statement`, `Action`, `MessagePopup`, `CallExecutable`, `SequenceCall`
- Legacy (avoid): `Goto`, `Label`

---

## Rebuild-efficiency batch (2026-07-29) — behaviour changes to know

Driven by the ~700-call rebuild above. See memory `teststand-rebuild-efficiency-batch-2026-07-29`.

- **`copy_typedefs` now PRESERVES the source's attach state** (`attach='preserve'` is the new DEFAULT;
  `'all'` is the old behaviour, `'none'` attaches nothing). It used to attach all 59 types where the
  original embeds 7 — a guaranteed diff and a forced restart of the rebuild.
- **Enum writes always land as EXPLICITLY SET (`[val]`).** `set_property_value` / `set_property_node` /
  `create_step_property` resolve an ordinal to its enumerator NAME first — file type list → engine-wide
  → read back off the property itself — because ONLY the by-name write clears TestStand's type-default
  flag. Previously an ordinal write silently produced `{val}` whenever the enum type was not yet in the
  target file's TypeUsageList (41 spurious diffs in one rebuild).
- **Conversely: do NOT write a value the original leaves at its type default.** `get_property_tree` now
  reports `isDefault` per enum leaf (derived from the symbolic name reading back EMPTY). Writing a
  default-valued member — even a redundant `flags=0` write on a type instance — flips `{val}`→`[val]`.
- **`configure_python_module` is complete.** It now also sets `class_name`,
  `class_instance_location`, `operation_type`/`operation_scope`, `python_version`,
  `virtual_env_path`, `use_adapter_interpreter_settings` and the whole `parameters[]` argument list
  ({name, type, value}) in ONE call. Object-oriented Python steps were previously unreachable
  (~15 `set_step_property` calls each). NOTE: the analyzer's "Python version cannot be empty" ERROR is
  about the STATION's Python Adapter configuration, not this step property — it is present on the
  original file too.
- **`configure_sequence_call_module` writes the retained `SData.SFPath`** (defaults to the current
  file's own name) AFTER the prototype load, which blanks it. `stored_file_path` reproduces a STALE
  path — real files carry paths from before a rename.
- **Numeric REPRESENTATION and format are settable**: `representation` (`float64`/`int64`/`uint64`) and
  `number_format` (e.g. `%#.4x`) on `insert_local_variable`, `insert_sequence_parameter` and
  `set_property_node`; `0x…` literals accepted. This is functional, not cosmetic — a Float64 parameter
  fed a UInt64 argument is a hard `NI_ExpressionEvaluationError`. Wide integers are also READABLE now
  (they used to surface as `Empty` because `GetValNumber` rejects them).
- **`delete_step_property`** removes a step subproperty by dotted path (e.g. one surplus
  `TS.SData.ActualArgs.<name>`). **`set_step_property_flags`/`set_property_node` take `exact`/
  `clear_flags`** to ASSIGN a bitfield instead of OR-ing. (`SetFlags` does in fact replace — the older
  "OR-only" note was wrong.)
- **A `SequenceArgument` keeps its state in SUBPROPERTIES** — `UseDef`, `Expr`, `ParamType`,
  `ParamRepresentation` and its own `Flags` NUMBER (what the FileDiffer shows as the argument's
  "Flags"; NOT the entry's PropFlags). `UseDef` is independent of `Expr`: the editor keeps a remembered
  expression while still using the default. A prototype load overwrites `Flags`/`ParamRepresentation`
  from the callee and renames entries after the callee's CURRENT parameters, so import rebuilds the
  whole list in order rather than patching it (argument ORDER is compared positionally).
- **Bulk writers**: `insert_sequences_bulk`, `insert_variables_bulk`, `set_property_nodes`,
  `set_module_parameters` — one call, one save, applied in array order.
- **`get_property_tree` addresses a sequence/step BY NAME** (`sequence_name` / `step_group` /
  `step_name`); the raw path is `Data.Seq[i].Main[j]` and there is no `Sequences` node.
- **`get_module_parameters` reads Python arguments** (`TS.SData.PythonCall.Parameters`); it returned
  `[]` for every Python step before.
- **Reminder:** new tool NAMES and new enum values/optional params need a FRESH MCP session — the
  client caches the tool catalog at session start.

## General Conventions

- **Sequence file for tests:** Always use `DemoTestsequenz.seq`
- **MCP server restart:** After code changes always rebuild yourself:
  `taskkill //F //IM TestStandMCP.exe` + `dotnet build --configuration Debug --framework net8.0-windows -p:Platform=x86`
  (Target framework is **net8.0-windows / x86**. The TestStand engine requires the host's
  runtimeconfig to declare `Microsoft.WindowsDesktop.App` + `Microsoft.AspNetCore.App` — both
  are wired via `FrameworkReference` in the .csproj. Output: `bin\x86\Debug\net8.0-windows\`.)
- **After every sequence change:** Call `save_sequence_file`

---

## TestStand Behavioral Facts (proven by the integration tests)

These are hard-won behaviors from the `Test/TestExecution` suite (T01–T21,
`TestBase`, `TestDataBuilder`). Treat them as **known solution paths** — do not
re-discover them by trial-and-error. Per-tool gotchas already live in the tool
descriptions; the cross-cutting rules below apply regardless of which tool you call.

### Engine lifecycle & file handling
- **Single in-process engine only.** A second engine cannot be torn down cleanly
  while the first one lives → the host hangs on exit. The MCP server uses exactly
  one engine; never spin up a second. (`T01`, see also memory `teststand-testhost-teardown-hang`.)
- **Recreating a `.seq` that already exists:** the engine releases the OS file
  handle *asynchronously*. The working pattern (from `TestDataBuilder.Step00`):
  1. `close_sequence_file` for any loaded file matching the path,
  2. delete from disk inside a short retry loop (≈5× with a ~300 ms back-off),
  3. then `create_sequence_file`. Deleting without closing first throws a sharing violation.
- **Verify persistence by re-opening:** after `save_sequence_file`, call
  `open_sequence_file` again and read back to confirm the write→save→reload round-trip.

### Expressions
- **`check_expression` effectively requires a loaded file as context.** Pass
  `sequence_file_path` to an already created/open file; without it even a valid
  expression can fail to validate. (`T01`, `T08`.)
- **`evaluate_expression` context:** StationGlobals by default; FileGlobals when a
  file path is given. To reference a FileGlobal by name, create it first via
  `set_property_value` (e.g. `value_type="number"`). (`T20`.)

### Properties & variables
- `set_property_value` with **`sequence_name=null` → FileGlobals**; with a sequence
  name → that sequence's **Locals**. (`T20`.)
- **To set a STEP's own property, use `set_step_property`** (dotted path relative to the
  step, e.g. `VIModule.ViCall.VIPath`, `PortNumber`). `set_property_value`/`set_property`
  only reach Globals/Locals, never a step; the `configure_*_module` tools only reach the
  adapter module (and `configure_labview_module` switches the adapter — wrong for None-adapter
  utility steps like `NI_LV_RunVIAsynchronously`; since 2026-07 it **refuses** those steps with
  a clear error instead of corrupting them). `set_step_property` writes the step property
  directly and leaves the adapter untouched. Path must already exist. (`T30`.)
- **Containers:** create with `value_type="container"`, then set nested members via a
  dotted path, e.g. `"MyCont.Inner"`. `delete_sub_property` removes a global/subproperty. (`T20`.)
- **Typed params & typed nested Locals members:** `insert_sequence_parameter` accepts enum /
  `reference` / `container` / array types (`name[]`) — same contract as `insert_local_variable`,
  NO silent String fallback. To build a nested typed member inside a container Local, call
  `insert_local_variable` with a **dotted path** (`"MyCont.Sub.Field"`, data_type = enum/named/
  builtin) parent-before-child. (`T33`.)
- **Setting an ENUM value:** `set_property_value(value_type="number", value=<n>)` and
  `set_local_variable` now write an enum instance by its numeric value (the server coerces via
  `PropOption_CoerceToEnum`) and **preserve** the enum type — a plain set otherwise throws
  "Expected type X. Found type Number/String". Param default enum values are NOT reachable this
  way (value tools target Locals/FileGlobals, never a sequence's Parameters). (`T33`.)
- **A step's "comment" IS its `Description`** — `set_step_comment` writes the field
  that reads back as `Description`. (`T04`, `TestDataBuilder`.)
- **Numeric limits:** `get_numeric_limits` returns the public contract keys
  `low_limit` / `high_limit` (not the raw TS property names). One-sided limits use a
  `null` limit + a comparison like `"LT"`/`"GT"`; two-sided uses `"GELE"` etc. (`T05`, `TestDataBuilder`.)

### Headless limitations = EXPECTED outcomes (not bugs)
- `create_sync_object` / `create_batch_sync_object`: headless has no SyncManager →
  `InvalidOperationException` / `NotSupportedException` are expected. (`T19`.)
- `post_ui_message` / `add_report_section`: require a **live `execution_id`**; an
  unknown id raises `KeyNotFoundException`. Only meaningful during an execution. (`T15`, `T19`.)
- `post_output_message` **does** work headless and appears in the engine output list. (`T15`.)
- **Undo/redo is not auto-recorded** by the headless API (it's a Sequence Editor
  feature). `CanUndo` is false on a fresh file; MCP edits won't be undoable. Revert by
  performing the inverse operation explicitly. (`T08`.)
- **LabVIEW `ViCall` prototypes DO materialize headless — this entry used to say the opposite and
  that was wrong (2026-07-29).** A `.lvlibp` VI's connector pane loads via `load_module_prototype`
  when three conditions hold: the step is SAVED to disk, the load runs `isolate:false` (in-process,
  ExecServer-routed), and no SequenceCall prototype load has happened in this process yet (see the
  rebuild section at the top for the measurements). Only then does `set_module_parameter` have
  Labels to bind. What genuinely does NOT work is `configure_labview_module`'s built-in auto-load:
  it goes in-process through the adapter's AutoDetect → LabVIEW Run-Time and faults, so pass
  `load_prototype:false` there and call `load_module_prototype` afterwards.
- **The engine connection auto-reconnects.** If the MCP host restarts the server mid-session,
  `EnsureConnected` does a one-shot lazy reconnect (default engine path) before failing; mutating
  tools also auto-load their file on demand. So a transient "Not connected" self-heals on the next
  call — no manual `connect_engine` needed (though it's still harmless).

### 1:1 file rebuild fidelity ceilings (`T33`; see memory `teststand-1to1-rebuild-fidelity`)
**MOSTLY SUPERSEDED (2026-07-29).** These were the ceilings of the granular per-step approach. Use
`export_sequence_file` + `import_sequence_file` (top of this file) — 9 differences on the reference
file — and read the notes below only when hand-editing.
- **FileDiffer `[val]` vs `{val}`** = an explicitly-set value vs the type-default flag. NO LONGER a
  ceiling: every enum write now resolves the ordinal to its enumerator NAME, which is what stores it
  explicitly. The inverse is the live hazard — writing a value the original leaves at its type default
  flips `{val}`→`[val]`. `get_property_tree` reports `isDefault` per enum leaf so you can tell.
- **Duplicate step names** (multiple `End`/`If`/`Check…`): the by-name step-config tools hit the
  FIRST match only. Use the `Name#N` (Nth occurrence) or `@idx:N` (0-based group index) selectors —
  the rename-and-rename-back dance this used to prescribe is no longer needed.
- **CLOSED, no longer residuals:** number Representation (UInt64) / NumberFormat (now on
  `insert_local_variable` / `insert_sequence_parameter` / `set_property_node`, and readable);
  PropFlags on Locals/Globals/Params members (`set_property_node` `flags` + `clear_flags`);
  a Parameter's default value (`set_property_node` scope=`Parameters`), its comment
  (`set_parameter_comment`) and its nested container members.
- **Still a ceiling:** embedding an UNUSED type definition, a cross-file SequenceCall's cached
  `Prototype` parameter defaults, a step's authored `TS.AdditionalResultsHints` /`CustomResults`
  arrays (use `copy_step_module`), and the enum default marker on a named-type instance's member.

### Live thread-context inspection (runtime debugging)
Reading a **running/paused** thread's RUNTIME state (live variable values, the RunState
execution cursor) needs the thread-context tools — NOT `get_property_tree` /
`evaluate_expression`, which resolve against engine Globals and never see the thread scope
(`get_local_variables` likewise returns the static file default, not the live value). The
access path is `Thread.GetSequenceContext(frame).AsPropertyObject()` == the ThisContext tree
(`Locals`/`Parameters`/`FileGlobals`/`RunState`/`Step`/`Sequence`). Tools (`T29`):
- `inspect_thread_context` — dump a frame's live tree; `scope` = `runstate` (default) /
  `locals` / `parameters` / `step` / `sequence` / `full`, `lookup_string` to descend,
  `call_stack_index` for a caller frame (0 = current/innermost).
- `evaluate_in_thread_context` — evaluate any expression in the live frame scope
  (`Locals.X`, `RunState.NextStepIndex`, `Locals.C * 2`) — the scope `evaluate_expression`
  cannot reach.
- `get_runtime_variable` / `set_runtime_variable` — typed read / write of one path.
  Writing `RunState.NextStepIndex` is the "Set Next Step" action; patch `Locals.X` before
  resuming; clear `RunState.SequenceError`/`GotoCleanup`. Only meaningful while PAUSED/parked.
- `get_runstate_summary` — curated flat snapshot (position + cursor + flags + SequenceError).
- **The thread must be executing inside a sequence.** No active frame → `InvalidOperationException`;
  bad `call_stack_index` → `ArgumentOutOfRangeException`; unknown execution → `KeyNotFoundException`.
  A MessagePopup parks a thread headless (Running) — the deterministic way to hold a frame open.

### Step-type / enum specifics
- **`UseStepUnloadOption` (module unload, value 5) is only valid at file/model level —
  TestStand rejects it on an individual step.** Use values 1–4 per step. (`T06`.)
- The enum string sets accepted by `set_step_*` tools are enumerated in their tool
  descriptions and exercised in `T05`/`T06` — use those, don't guess.
- **`NI_Flow_Break` / `NI_Flow_Continue` must be physically inside a loop block**, or
  the plan validator raises `E_JUMP_OUTSIDE_LOOP`. (`T04`, `T10`.)

### User management
- **Always use `persist:false`** for test/experimental users — it edits only the
  in-memory users file and never touches `users.ini` on disk. (`T11`.)

### Enumeration data types (create/modify/delete)
Tools: `create_enum`, `get_enum_values`, `set_enum_values` (bulk replace),
`add_enum_value`, `remove_enum_value`, `rename_enum_value`, `delete_enum`. An enum is a
named numeric data type (name→value constants) stored **in the sequence file**. Values
`{name, value?}`; an omitted `value` auto-assigns C-style (previous+1 from 0); `add` uses
max+1. (`T22`.)
- **An enum is a real named TypeDef, NOT a file-root subproperty.** `NewSubProperty(
  PropValType_Enum)` is rejected ("Unrecognized value") and `UpdateEnumerators` on a plain
  Number throws ("Expected Enumeration, found Number"). Create via `Engine.NewDataType(
  PropValType_Enum)` → set `Name` → `TypeUsageList.InsertType(type, 0, CustomDataTypes)` →
  `SetIsTypeAttachedToFile(idx, true)` (so it persists embedded) → `UpdateEnumerators`.
  Because enums live in the **TypeUsageList**, they do NOT appear in `get_data_types`
  (which lists file-root subproperties); use `get_enum_values`.
- **Write vs read formats differ.** `UpdateEnumerators` takes an ARRAY of containers, each
  with `EnumeratorName`/`EnumeratorValue` (+`OldEnumeratorName` for renames; it REPLACES the
  whole list). But the `Enumerators` getter returns enum-TYPED values — read each one's name
  with `GetValString("", PropOption_CoerceToString=128)` and number with `GetValNumber("",
  PropOption_CoerceToNumber=64)`, else "Expected type String/Number. Found type <Enum>."
- **Use typed interop, never `dynamic`, for `TypeUsageList`/`PropertyObject` here.** The C#
  dynamic-COM binder throws `TargetParameterCountException` on `GetTypeDefinition` and
  mis-binds others. A `var tul = ...` off a `dynamic sf` silently re-poisons the chain.

### Pre-build validation
- `validate_sequence_plan` is engine-free; run it on the exact `steps` array before
  `insert_steps_bulk` (pass the planned `locals` AND `parameters`). Error codes:
  `E_UNCLOSED_BLOCK`, `E_UNMATCHED_END`, `E_ELSE_WITHOUT_IF`, `E_JUMP_OUTSIDE_LOOP`,
  `E_FORBIDDEN_TYPE` (Goto/Label), `E_UNDECLARED_LOCAL`, `E_UNDECLARED_PARAM` (only when a
  `parameters` list is supplied), `E_DUP_NAME`. Warnings (advisory only): `W_UNLINKED_CALLS`
  (unlinked `SequenceCall` placeholders are fine), `W_UNUSED_LOCAL`, and
  `W_UNKNOWN_TYPE` (a step type outside the builtin whitelist — installed custom
  types like `NI_LV_RunVIAsynchronously` insert fine, so this never blocks a build).
  Build only when `valid==true`. (`T10`, `T31`.)

### Authoring-complete step editing (1:1 file rebuilds; `T31`/`T32`)
- **`create_step_property`** creates NEW subproperties on a step by dotted path:
  `number`/`boolean`/`string`/`container`/`reference` (Object Reference), `named_type`
  (+`type_name`, e.g. `SequenceArgument`, `ErrorDialogOptions`, `Error`), and
  `array_elements` (+`num_elements`) which creates/resizes typed arrays — elements are
  instantiated with the array's ELEMENT type (authors `TS.SData.ViCall.Parms`,
  `…Parms[i].ArrayClusterEls`, `TS.AdditionalResultsHints`, `Result.TimeoutOccurred`).
  Named types resolve engine-wide (fallback `Engine.NewPropertyObject` +
  `SetPropertyObject(PropOption_InsertIfMissing)`); requesting a named type on an
  existing node of a DIFFERENT type RETYPES it in place. Idempotent otherwise.
- **`set_step_property_flags`** sets raw PropFlags (SetFlags) on any step property —
  e.g. `0x4` PassByReference on Prototype members, `0x200000` module marker.
- **`rename_step_property`** sets a step property's NAME (PropertyObject.Name).
  **Array elements can be NAMED** — ViCall.Parms entries carry the connector-pane label
  as their element name, the editor/FileDiffer display them as `[i] Name` and **pair
  array elements BY that name**. `create_step_property(array_elements)` creates elements
  unnamed → always set each element's name afterwards, or the differ shows the whole
  array as same-named Delete/Insert pairs despite identical content. `get_property_tree`
  reports element names via the node's `elementName` field.
- `set_step_property`/`create_step_property` accept `unescape:true` to decode
  `\r \n \t \\ \uXXXX` — the only way to write bare control chars (VIDescriptions with CR).
- `insert_file_global`/`insert_local_variable` accept `'reference'` (Object Reference)
  and named custom types; there is NO silent String fallback anymore.
- `set_module_parameter` binds LabVIEW connector-pane parms by Label (`'error out.status'`
  descends into clusters; writes ArgVal + clears UseDefaultValues) and, for SequenceCall,
  binds `ActualArgs.<name>.Expr` + clears that arg's `UseDef`.
  `get_module_parameters` reads ViCall.Parms / VIModule.ViCall.Parms / ActualArgs.
- **Prototype auto-load on module config (ALL adapters):** every typed module-config tool now runs
  `Module.LoadPrototype(0)` (the engine's "Load Prototype") **after** it sets the target — so the
  step's parameter interface is populated in one call: `configure_labview_module` (VI connector
  pane → `ViCall.Parms`), `configure_dll_module`, `configure_dotnet_module`, `configure_python_module`
  (function prototype → `Module.Parameters`) and `configure_sequence_call_module` (callee args →
  `TS.SData.ActualArgs`). `set_sequence_call_target` / `insert_steps_bulk` also load the SeqCall
  prototype; `set_module_parameter` loads it when the named arg is missing. **Order matters** — the
  load is centralized in `ConfigureModuleAsync` to run strictly after the VI-path/target is set;
  without it the interface stays empty. For SequenceCall the load also enforces `UseDef ⇔ empty Expr`
  (unbound args `UseDef=True`, bound `False`) — matching a genuine call for the FileDiffer. All five
  configure tools now RETURN the loaded interface in the result's **`parameters`** list, and each
  takes an optional **`load_prototype`** (default `true`) to skip the load when the target is not yet
  reachable. Unresolvable targets (unlinked placeholder / missing or not-yet-loaded file / a VI in an
  unloadable `.lvlibp` headless) skip silently — so the target must be loadable (loaded or on the
  search path) for the interface to materialise headless.
- **`load_module_prototype`** — adapter-agnostic standalone tool = the editor's "Load Prototype"
  button on any step (LabVIEW / DLL·CVI / .NET / **ActiveX** / SequenceCall). Two uses: (1) after a
  `configure_*` with `load_prototype:false`, once the target is reachable; (2) **RE-SYNC a caller
  after the target's own interface changed** — e.g. a subsequence's Parameters were edited, or a
  DLL/VI/ActiveX signature changed — so the caller's arguments match again. Does NOT change the
  adapter; non-destructive (existing bindings matched by name). Returns `{stepName, adapter,
  prototypeLoaded, note, executionMode, workerOutcome, parameters[]}` — `prototypeLoaded:false` + a
  `note` when the target could not be resolved. (New tool NAME → needs a fresh MCP session to appear
  in the catalog after rebuild.)
  - **ASYNC + ExecServer-routed worker by default (2026-07-09; see [[teststand-loadprototype-lvlibp-crash-isolation]]).**
    A LabVIEW load attaches to/starts LabVIEW — the SAME slow work the editor's "Reload Prototype"
    does — and can exceed the MCP transport's ~60s window (`-32001`). So a LabVIEW load runs
    **asynchronously by default**: `load_module_prototype` returns immediately with `{jobId,
    status:"running"}`; poll **`get_prototype_load_status(job_id)`** until `status:"completed"`.
  - **ROOT CAUSE + the real fix (from the NuGet `…Interop.AdapterAPI`).** Headless, with no explicit
    server, the LabVIEW adapter's **AutoDetect** resolves a LabVIEW **Run-Time** (`lvrt.dll`) that
    can't be bound in-process → the delay-load SEH `0xC06D007E`. The editor works because it uses the
    running LabVIEW **development** environment (**ExecServer**, ActiveX — no RTE delay-load). So before
    the load we now **route the adapter to the ExecServer** and connect: `GetAdapterByKeyName(key)` →
    **cast to the typed `LabVIEWAdapter`** (the `Initialize`/`Get`/`SetServerInfo` methods live on THAT
    interface, NOT the generic `Adapter` dispinterface — which is why an earlier late-bound
    `dynamic adapter.Initialize()` silently no-op'd) → `SetServerInfo(ExecServerDeferred,"LabVIEW")` +
    `Initialize()`, restored afterward. Configurable via **`labview_server`**: `deferred` (default,
    running ADE launched on first use), `exec` (connect now), `rte` (legacy AutoDetect), `auto` (leave
    config untouched).
  - **The isolated worker is the DEFAULT (`isolate:true`) but CANNOT actually load a LabVIEW VI —
    pass `isolate:false` for LabVIEW (corrected 2026-07-29).** The claim that ExecServer's ActiveX
    lets the worker "attach to the running LabVIEW just like an in-process load" is WRONG: the worker
    is a separate process, does not inherit the attachment to the running LabVIEW ADE, tries to start
    its own and TIMES OUT. Measured on `TFW_MDC_com_Python.seq`: 8 steps × 120 s, every one a timeout
    (LabVIEW finally came up after the 7th) vs **~5 s per step and a correct 19-parameter pane
    in-process**. Also: the worker reads the file from DISK, so a load after a run of `save:false`
    edits reports the step as out of range — which reads like an unloadable VI but is an unsaved file.
    What the worker still buys is crash containment: a process-fatal native fault (`0xC06D007E`,
    escapes managed `try/catch`) kills only the child → `prototypeLoaded:false`,
    `executionMode:"worker"`, `workerOutcome:"crashed"`/`"timeout"`, server lives. In-process is
    ExecServer-routed (which is what avoids that fault in the first place) but does NOT contain one.
    `copy_step_module` remains the fallback for a genuinely unloadable `.lvlibp`.
  - **SILENT worker death — no WER box AND no NI Error Reporter dialog.** `SetErrorMode` alone only
    hides the OS fault box; NI's green "…encountered a problem and needs to close" reporter is an
    IN-PROCESS unhandled-exception hook. The worker installs layered guards up front (in
    `LoadPrototypeWorker.InstallSilentDeathGuards`): the decisive one is a **vectored exception handler**
    (`AddVectoredExceptionHandler(first=1)`) that, on the MSVC delay-load SEH family (`0xC06Dxxxx` —
    high word `0xC06D`, so it never fires on ordinary handled C++/CLR exceptions `0xE06D…`/`0xE0434352`),
    calls `TerminateProcess` DURING first-chance dispatch — before any frame-based/unhandled handler,
    i.e. before NI's hook can start the dialog. Plus `SetErrorMode`, `WerSetFlags(NO_UI)` +
    `WerAddExcludedApplication`, CRT `_set_abort_behavior(0,…)`, and a `SetUnhandledExceptionFilter`
    backstop for other fatal faults. Verified: a raised `0xC06D007E` dies in ~4s (not a timeout) with
    ZERO lingering `WerFault.exe`/NIER processes, and the real load still returns `not-loaded` cleanly
    (the VEH does not false-fire on the handled path). Test hook: env `TESTSTAND_MCP_LP_SIMULATE_CRASH`
    = `raise` (RaiseException → exercises the VEH) or `1` (direct TerminateProcess). The worker is
    bounded by `timeout_seconds` (default 120; on expiry its process tree is killed →
    `workerOutcome:"timeout"`); on success it SAVES to disk and the parent reloads it.
  - **Params & result.** `async` (default true for LabVIEW / false otherwise), `isolate` (default
    true), `labview_server` (default `deferred`), `timeout_seconds` (default 120). Non-LabVIEW adapters (SequenceCall/.NET/DLL·CVI/ActiveX)
    always run fast, **in-process, synchronously** — no behaviour change, no regression. Result carries
    `executionMode` (`in-process`|`worker`), `workerOutcome`, `jobId`, `status`. NOTE: the auto-load
    inside the `configure_*_module` tools still runs in-process synchronously — use `load_prototype:false`
    + `load_module_prototype` (async) or `copy_step_module` for a `.lvlibp` there. New tool NAME
    `get_prototype_load_status` + the `async` param → need a fresh MCP session after rebuild.
- With element names mirrored, a full tool-driven rebuild of TFW_DemoModule.seq
  diffs **identical (0 differences)** against the original (`T32`).

### Sequence Analyzer is ASYNC-capable (cold `.lvlibp` timeout fix; 2026-07-09)
- `analyze_sequence_file` / `run_sequence_analyzer` accept **`async=true`**: the call returns
  IMMEDIATELY with `{jobId, status:"running"}`; poll **`get_analysis_status(job_id)`** until
  `status:"completed"` — same structured shape (`totalMessages`, `errorCount`/`warningCount`/
  `informationCount`, `messages[]`, optional `groups[]`). Use it whenever the file has LabVIEW
  `.lvlibp` steps on a **cold** module cache: the analyzer's "module is loadable" rule loads every
  step's code module, and a cold packed-library VI load can exceed the MCP transport's ~60s window
  (`-32001`). A tool-side timeout can't lift that cap — **async is the only real fix** (mirrors the
  `load_module_prototype` async-job infra: `AnalyzerJob`/`StartAnalyzerJob`/`GetAnalysisStatusAsync`
  in `TestStandService`, `_analyzerJobs` retained ~10 min).
- **No isolated worker needed for the analyzer** — the slow/fault-prone module loads already happen
  inside **`AnalyzerApp.exe`**, a separate process the analysis spawns. A native `.lvlibp` fault
  kills that child → the job ends `status:"error"`, never taking the MCP server down. The background
  job only decouples the RPC response from the analysis duration (same `Task.Run` context the sync
  path already used). Default is still synchronous (`async=false`) — abwärtskompatibel. Verified
  `T14.AnalyzeDetailed_Async_ReturnsJobThenSameResultAsSync` (jobId → poll → parity with sync).
- New tool NAME `get_analysis_status` + the `async` param → **need a fresh MCP session** after the
  rebuild (the client caches the tool catalog + arg schema at session start).

### Post-build validation (reference audit)
- `audit_sequence_references` reads the ACTUAL built sequence — `ConditionExpr` / `ItemExpr` /
  `PreExpression` / `PostExpression` / `StatusExpression` — and flags every `Locals.X` /
  `Parameters.X` / `FileGlobals.X` reference that is **not declared** in that sequence's
  locals/parameters (or the file globals). Codes: `E_UNDECLARED_LOCAL`, `E_UNDECLARED_PARAM`,
  `E_UNDECLARED_FILEGLOBAL`. Returns `{valid, issueCount, issues[], stats{}}`. Omit
  `sequence_name` to audit every sequence in the file. Read-only/advisory — it reports, it never
  modifies. Other scopes (`StationGlobals`, `RunState.*`, `Step.*`) are intentionally not audited.
- **Run it AFTER building** and after any `set_flow_condition` / `set_step_expression`. It is the
  complement to `validate_sequence_plan`, which (pre-build) checks `Locals.X` in the build PLAN and
  — since 2026-07 — also `Parameters.X` **when you pass a `parameters` list** (`E_UNDECLARED_PARAM`;
  omit the list to keep the old locals-only behaviour). What the pre-build validator still cannot see
  are conditions written LATER via `set_flow_condition`/`set_step_expression` (those land in
  `ConditionExpr`/`ItemExpr`, outside the plan). The audit reads the real sequence, so it catches
  BOTH paths. (`T28`; pure logic in `ReferenceAuditor`.)
- **Therefore, while building, declare every `Locals.X` AND `Parameters.X` you reference**
  (`insert_local_variable` / `insert_sequence_parameter`) and pass the planned `parameters` to
  `validate_sequence_plan` so it catches a missing `Parameters`. A parameter written inside a
  sub-sequence and read back by the caller must be passed **by reference** (`pass_by_reference:true`).

### 1:1-rebuild tool-gap batch (2026-07-07)
Closed the residual gaps found rebuilding `TFW_MCP_Test.seq`. All verified live except where noted.
- **Enum leaf reads** — `get_file_globals` / `get_property_object` / `get_property_tree` return an
  enum leaf as `valueType:"Enum"`, `value:{ordinal, symbolicName}` (a plain read throws on enums, so
  they used to come back `Unknown`/`Empty`/`0`). NUANCE: a value set by raw ORDINAL reads back with
  an empty `symbolicName` until reload; set by NAME (or the authored default) keeps it — the ordinal
  is always right.
- **`value_type='enum'`** on `set_property_value` (+ new `type_name`) and `create_step_property`
  (`type_name` existed): creates the member as an instance of the named enum typedef and sets its
  ordinal/name (CoerceToEnum) — so an enum container-member gets its real type, not an anonymous
  container. NEW enum values/params ⇒ need a fresh MCP session (see caveat below).
- **`set_file_global`** now types a boolean correctly (was writing "true"/"false" as a String →
  never persisted); it PRESERVES an existing global's authored type. NEW **`set_file_global_comment`**
  (FileGlobals twin of `set_local_variable_comment`; dotted path reaches container members).
- **`compare_sequence_files` DEFAULTS to `mode='native'`** (the authoritative FileDiffer, ==
  `diff_sequence_files`). Use it to VERIFY a rebuild. `mode='structural'` is the old fast in-process
  compare — now self-labelled (`fidelity`/`note`); its `totalDifferences==0` does NOT prove identity.
- **`@idx:N` step selector** (0-based group index) added alongside `Name#N`; works in EVERY by-name
  step tool (`set_step_*`, `set_module_parameter`, `configure_*`).
- **`set_step_property` auto-detect peeks the target type first** — writing "True"/"False" to a
  String expression prop (`ActualArgs.<arg>.Expr`, any `*.Expr`) stays a String (no more "Expected
  Boolean, found String"); string set also retries CoerceToEnum for enum-by-label.
- **Nameless `Label`** (empty name = blank spacer) auto-gets `TS.Icon="ni_blank.ico"` + step flag
  `0x4000000` on insert (named labels keep `label.ico`); bulk now allows an empty name for a Label.
- **Action step calling a subsequence** — set adapter `Sequence` (via `change_step_adapter` or
  `insert_step`/`insert_steps_bulk` `adapter='Sequence'`), then `configure_sequence_call_module`
  (works on ANY Sequence-Adapter step; the step keeps its `Action` type).
- **Comment/description encoding is Windows-1252, NOT lossy for `–`/`—`/`…`/`•`/`€`/`™`/curly quotes**
  — those SURVIVE a round-trip; only truly-non-1252 chars (`→` `✓` emoji/CJK) become `?`. The guard
  was over-warning (checked `≤0xFF`); now Windows-1252-accurate. (This branch uses a hardcoded
  1252-extras table; another branch fixed it via `WideCharToMultiByte` — prefer that on merge.)
- **CAVEAT — the MCP client validates args against the schema cached at session start.** New tool
  NAMES *and* new ENUM VALUES / optional PARAMS (`value_type='enum'`, compare `mode`,
  `set_property_value` `type_name`, the `Sequence` adapter) only appear after a FRESH session; before
  that they are rejected with `invalid_enum_value`. Rebuild the server, then start a new session.

### 1:1-rebuild tool-gap batch — wave 2 (2026-07-07)
Closes the P1 analyzer errors and the LabVIEW-metadata diffs.
- **Nested member with a named/enum type — `set_property_value value_type='named_type'` (+`type_name`)**
  creates a member as a FULL instance of a file-defined type (a container like
  `TFW_DB_TestCasesLimits` / `VisionSensorSbsi_Reply_Payload` / `Error`, or a named leaf),
  MATERIALISING the type's fields (like the editor) via `Engine.NewPropertyObject(NamedType)` +
  `SetPropertyObject(InsertIfMissing)` — so afterwards you only set the fields that differ. This is
  the ONLY way a nested container member gets its named type instead of an anonymous `Container`
  (the cause of every `NI_ExpressionEvaluationError "Expected <Type>, found Container"`).
  `value_type='enum'` (+`type_name`) creates an enum leaf inside a container — pass its value as the
  new **`ordinal`** param (numeric, preferred) or `value` (ordinal or symbolic name). Same on
  `create_step_property`. **Recipe:** `copy_typedefs` first (so the type exists), then
  `set_property_value(named_type)` for each typed container member, then set only the non-default
  fields; enum leaves via `value_type='enum'` + `ordinal`.
- **LabVIEW `.lvlibp` module metadata — `copy_step_module`.** VIs in a packed library can't load
  headless, so `load_module_prototype` returns `prototypeLoaded:false` and the connector pane
  (`ViCall.Parms`, Namespace, VI Description, Connector-Pane-Checksum) cannot be regenerated. Instead,
  **copy the cached module subtree from the SOURCE `.seq` step** onto the rebuilt step:
  `copy_step_module` deep-copies `TS.SData` (a SequenceCall's ActualArgs, a RunVIAsync's
  `SeqCallStepAdditions` incl. `Parameter0..3`, an adapter module) + the step-own `VIModule`
  (ViCall metadata), and aligns the adapter — no LabVIEW needed. Run
  `copy_typedefs` first. **(2026-07-08 fix — see [[teststand-copystepmodule-clone-fix]]):** each
  subtree is now `PropertyObject.Clone(path, 0x20000000 PropOption_CopyAllFlags)`d BEFORE
  `SetPropertyObject` — a live source object still has a parent and TestStand rejects it ("already
  has a parent object. You must first clone the item"), which had made `copiedPaths` come back empty.
  It ALSO clones the authored step-config subtrees a fresh insert doesn't instantiate from the
  step-type template — `TS.AdditionalResultsHints`, `TS.CustomResults`, `TS.ErrorDialogOptions`,
  `Result.TimeoutOccurred` (each copied only if present on the source) — so a NON-adapter step like
  an `NI_Wait` reproduces faithfully too (its wait TARGET still needs `configure_wait`, as that lives
  in step-root props, not `TS.SData`). Verified: rebuilding only `Init`+`Close` of
  `TFW_Symphony_EOL.seq` diffs 0 under both sequences incl. all three `.lvlibp` LabVIEW steps.
  (Reading a VI's connector-pane bindings: `get_module_parameters` returns
  Label→ArgVal per parameter; the VI-level metadata reads via `get_property_tree` scoped with
  `lookup_string` to the step's `…ViCall` — scope it, don't dump the whole `TS` at low depth, or the
  node budget truncates it.)
- **`set_step_property` now resolves `Icon` and `Flags`.** `property_path='Icon'` → `TS.Icon` (the
  step icon file, e.g. `ni_blank.ico`); `property_path='Flags'` → the step's flag bitfield via
  `SetFlags` (decimal or `0x`-hex, e.g. `0x4000000` to blank a nameless Label's icon). Previously both
  errored "Unknown variable or property name". (Nameless-Label auto-blanking + bulk empty-name Label
  landed in wave 1.)

### 1:1-rebuild tool-gap batch — wave 3 (2026-07-09): scope-generic property-tree CRUD
Closed the last write/delete/flag gap on the **`Parameters.*`** level (and nested submembers under
any scope). The read side already had `get_property_tree`; these are the symmetric writers. Both are
**scope-generic** — `scope` ∈ `Parameters | Locals | FileGlobals | StationGlobals | SequenceFile`.
See memory `teststand-property-node-crud-2026-07-09`.
- **`set_property_node`** — create/set a node (and optionally its PropFlags) at a dotted
  `lookup_string` relative to the scope root. Reuses the SAME creation switch as `set_property_value`
  + `create_step_property` (`number/string/boolean/container/reference/named_type/enum/array_elements`):
  `named_type` (+`type_name`) instantiates a FULL typed instance (fields materialise, like the editor,
  via `Engine.NewPropertyObject`+`SetPropertyObject(InsertIfMissing)`) so a container member gets its
  real type instead of an anonymous `Container`; `enum` (+`type_name`, value via `ordinal` preferred)
  resolves ordinal→enumerator NAME so it stores explicit `[val]` (matches the editor-authored original,
  no spurious `{val}` diff); `array_elements` (+`num_elements`) sizes a typed array. Missing
  intermediate containers auto-create (`create_missing_parents`, default true). `flags` applies raw
  PropFlags via `SetFlags` (OR semantics — e.g. `132`/`0x84` = `0x04` PassByReference + `0x80`). A value
  is written only when supplied → a **flags-only** call never clobbers the value (closes the
  Local/Parameter-submember PropFlags residual, e.g. `Job1.SetJob_Reply_Payload` `0x4` vs `0x0`).
- **`delete_property_node`** — the missing `delete_sequence_parameter`, generalised. `scope=Parameters`
  + a top-level name removes a whole parameter (+ its structure); a nested `lookup_string`
  (e.g. `MDC_cmd.Request.Cmd`) surgically removes one submember. The scope-generic counterpart of
  `delete_sub_property` (Locals/FileGlobals only). `DeleteSubProperty` accepts a dotted path, so both
  forms are one call.
- **Roots per scope:** `Parameters`/`Locals` → `Sequence.Parameters`/`.Locals` (need `sequence_name`);
  `FileGlobals` → `FileGlobalsDefaultValues`; `StationGlobals` → `Engine.Globals` (commits via
  `CommitGlobalsToDisk`, `file_path` unused); `SequenceFile` → `AsPropertyObject()`.
- **Verified end-to-end** rebuilding the anonymous-container parameter `MDC_cmd` of `_MDC_com`
  (`TFW_MDC_com_Python.seq`): the 3 target diffs (Flags, Request subtree, Response subtree) drop to 0
  with **zero** newly-introduced diffs; enums reproduce identical (`[Command] (0)` /
  `[GetKfMdcProtocolVersion] (4629)` / `[GetHousingTemperature] (4630)`). New tool NAMES → need a
  FRESH MCP session to appear in the catalog after the rebuild.
