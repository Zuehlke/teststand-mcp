# TestStandMCP — Behavior Rules for Claude

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
- **LabVIEW `ViCall` prototypes don't materialize headless.** A step's VI is in a `.lvlibp`
  that can't load without LabVIEW, so the connector-pane (`ViCall.Parms`) stays empty, Namespace/
  VI Description/Connector-Pane-Checksum are blank/`-1`, and `set_module_parameter` cannot bind
  (nothing to match by Label). Expected — configure the VI path via `configure_labview_module`
  and accept the empty prototype.
- **The engine connection auto-reconnects.** If the MCP host restarts the server mid-session,
  `EnsureConnected` does a one-shot lazy reconnect (default engine path) before failing; mutating
  tools also auto-load their file on demand. So a transient "Not connected" self-heals on the next
  call — no manual `connect_engine` needed (though it's still harmless).

### 1:1 file rebuild fidelity ceilings (`T33`; see memory `teststand-1to1-rebuild-fidelity`)
- **FileDiffer `[val]` vs `{val}`** = an explicitly-set value vs the type-default flag. Enum
  defaults written via the tools show `{val}`; editor-authored originals show `[val]` — so they
  still diff even when the numeric value is IDENTICAL. Treat as cosmetic.
- **Duplicate step names** (multiple `End`/`If`/`Check…`): the by-name step-config tools hit the
  FIRST match only. To configure the 2nd+ occurrence, temporarily give it a unique name, configure,
  then `rename_step` back (rename allows a duplicate target name).
- **Not tool-reachable (accept as residual):** number Representation (UInt64) / NumberFormat,
  PropFlags on Locals/Globals/Params members, a Parameter's default value / comment / nested
  container members, and embedding unused type definitions.

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
- **SequenceCall prototype auto-load:** setting a SeqCall target (`configure_sequence_call_module`,
  `set_sequence_call_target`, `insert_steps_bulk`) — and `set_module_parameter` when the named arg
  is missing — now calls `Module.LoadPrototype(0)` (the engine's "Load Prototype"). When the target
  RESOLVES, `TS.SData.ActualArgs` gets one correctly-typed `SequenceArgument` per callee parameter
  (real `ParamType`/`ParamRepresentation`/`Flags`) plus the cached `Prototype` container; the helper
  then enforces `UseDef ⇔ empty Expr` (unbound args `UseDef=True`, bound `False`) — matching a genuine
  call for the FileDiffer. Unresolvable targets (unlinked placeholder / missing file / not-yet-loaded)
  skip silently and fall back to a bare on-demand entry. So the target file must be loadable (loaded
  or on the search path) for the full prototype to materialise headless.
- With element names mirrored, a full tool-driven rebuild of TFW_DemoModule.seq
  diffs **identical (0 differences)** against the original (`T32`).

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
