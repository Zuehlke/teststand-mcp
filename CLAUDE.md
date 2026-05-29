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
NI_Flow_While    →  While loop
NI_Flow_DoWhile  →  Do-While loop
NI_Flow_For      →  For loop
NI_Flow_ForEach  →  ForEach loop
NI_Flow_End      →  End of the loop
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
  `NI_Flow_Select`, `NI_Flow_Case`, `NI_Flow_Break`, `NI_Flow_Continue`
- Tests: `NumericLimitTest`, `StringValueTest`, `PassFailTest`, `NI_MultipleNumericLimitTest`
- Actions: `Statement`, `Action`, `MessagePopup`, `CallExecutable`, `SequenceCall`
- Legacy (avoid): `Goto`, `Label`

---

## General Conventions

- **Sequence file for tests:** Always use `DemoTestsequenz.seq`
- **MCP server restart:** After code changes always rebuild yourself:
  `taskkill //F //IM TestStandMCP.exe` + `dotnet build --configuration Debug --framework net48 -p:Platform=x86`
- **After every sequence change:** Call `save_sequence_file`
