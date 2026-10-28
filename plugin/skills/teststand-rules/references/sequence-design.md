# Sequence design rules

## Flow control: always `NI_Flow_*` step types

Every branch and loop is built from `NI_Flow_*` steps. They are structured and readable, and they survive later edits; `Goto`/`Label` produce spaghetti code that breaks when a sequence is modified.

Branching:

```
NI_Flow_If      entry into an If condition
NI_Flow_ElseIf  additional condition (optional)
NI_Flow_Else    else path
NI_Flow_End     end of the If block
```

Loops:

```
NI_Flow_While       while loop
NI_Flow_DoWhile     do-while loop
NI_Flow_For         for loop
NI_Flow_ForEach     for-each loop
NI_Flow_SweepLoop   iterates over a parameter range
NI_Flow_StreamLoop  iterates while a data source streams
NI_Flow_End         end of the loop
```

Forbidden, except for a justified exception:

```
Goto   only for a pattern that cannot be expressed with NI_Flow_* (for example a backward jump that is not a loop)
Label  only as the target of such a Goto
```

`NI_Flow_Break` and `NI_Flow_Continue` must sit physically inside a loop block, or `validate_sequence_plan` rejects the plan (error codes: [headless-and-tools.md](headless-and-tools.md#validation-before-and-after-building)).

## Default step type when unclear

When it is not clear which step type a step should be, default to `SequenceCall`, not `Statement`.

- It applies to every action or placeholder step whose type no other rule decides (not a flow step, not a wait, not a test step).
- The step may stay an unlinked SequenceCall placeholder (no target) while the target subsequence is not known; the user links it later. A `Statement` placeholder is never the intended result.
- The deterministic rules still win first: `NI_Flow_*` for branching and loops, `NI_Wait` for waits and delays, a test step such as `PassFailTest` for checks. `SequenceCall` is only the fallback for the otherwise ambiguous plain action.

## Structure of an If/Else block

`ElseIf` and `Else` are clauses of one `If` and share a single closing `NI_Flow_End`.

```
[0] User_Enters_Credentials   Statement
[1] Check_Credentials         Statement
[2] If_Credentials_Valid      NI_Flow_If      condition: Locals.CredentialsValid == True
[3]   Log_User_In             Statement       (true path)
[4]   Redirect_To_Dashboard   Statement
[5] Else_Invalid              NI_Flow_Else
[6]   Show_Error_Message      Statement       (false path)
[7]   Return_To_Login_Form    Statement
[8] End_If                    NI_Flow_End
```

## Structure of a Select/Case block

Each `NI_Flow_Case` opens its OWN block and needs its OWN `NI_Flow_End`. This is the asymmetry against `If`: every Case is a block of its own. A single `NI_Flow_End` for the whole Select makes TestStand nest the cases inside one another instead of rendering them as siblings. So n cases need n case-ends plus one select-end.

```
[0] Select_State    NI_Flow_Select   ItemExpr (set_flow_condition): Locals.State
[1]   Case_A        NI_Flow_Case     case value(s): "A"
[2]     Do_A        Statement
[3]   End_Case_A    NI_Flow_End      closes Case_A
[4]   Case_B        NI_Flow_Case     "B"
[5]     Do_B        Statement
[6]   End_Case_B    NI_Flow_End      closes Case_B
[7] End_Select      NI_Flow_End      closes Select
```

`validate_sequence_plan` enforces this: a Case whose parent block is not the Select (because the previous Case was not closed) is rejected, and so is any unclosed Case or Select.

## Step types (`get_step_types` lists what is installed)

- Flow control: `NI_Flow_If`, `NI_Flow_ElseIf`, `NI_Flow_Else`, `NI_Flow_End`, `NI_Flow_While`, `NI_Flow_DoWhile`, `NI_Flow_For`, `NI_Flow_ForEach`, `NI_Flow_SweepLoop`, `NI_Flow_StreamLoop`, `NI_Flow_Select`, `NI_Flow_Case`, `NI_Flow_Break`, `NI_Flow_Continue`
- Test steps: `NumericLimitTest`, `StringValueTest`, `PassFailTest`, `NI_MultipleNumericLimitTest`
- Actions: `Statement`, `Action`, `MessagePopup`, `CallExecutable`, `SequenceCall`
- Legacy, avoid: `Goto`, `Label`

## Which flow steps can be configured headless

The mandate above is about structure; this is about filling a step in.

- Fully covered by tools: `If`/`ElseIf`/`Else`/`End`; `While`/`DoWhile` (the condition goes to `ConditionExpr`); `For` (`configure_for_loop`, or the bulk fields `init_expr`, `expression`, `increment_expr`); `ForEach` (`configure_foreach_loop`, or the bulk fields `array_expr`, `element_expr`); `Select`/`Case` (including the bulk field `is_default`); `Break`/`Continue` (the validator enforces the loop context).
- `NI_Flow_SweepLoop` and `NI_Flow_StreamLoop` have NO configure tool. A Sweep keeps its own `Parameters[]` list (`NI_SweepParameter` entries, not the sequence's Parameters) plus Input and Output containers; a Stream keeps its data source plus `IterationExpr`. Reach them with `set_step_property`, or author them in the editor. Inserting one is fine; do not assume a `configure_*` call can fill it in.
