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
- **Containers:** create with `value_type="container"`, then set nested members via a
  dotted path, e.g. `"MyCont.Inner"`. `delete_sub_property` removes a global/subproperty. (`T20`.)
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
  `insert_steps_bulk`. Error codes: `E_UNCLOSED_BLOCK`, `E_UNMATCHED_END`,
  `E_ELSE_WITHOUT_IF`, `E_JUMP_OUTSIDE_LOOP`, `E_FORBIDDEN_TYPE` (Goto/Label),
  `E_UNDECLARED_LOCAL`, `E_DUP_NAME`. Warnings (advisory only): `W_UNLINKED_CALLS`
  (unlinked `SequenceCall` placeholders are fine), `W_UNUSED_LOCAL`. Build only when
  `valid==true`. (`T10`.)
