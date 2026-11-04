---
name: teststand-rules
description: Rules for ANY work on NI TestStand sequence files through the TestStand MCP tools - building sequences from a flowchart or description, inserting and editing steps and properties, flow control (If, loops, Select/Case), rebuilding, migrating or cloning .seq files, comparing and verifying files, LabVIEW .lvlibp steps, configuring step modules, documenting or presenting a sequence, running sequences headless, the sequence analyzer, and the teststand-sequence-builder, teststand-doc-generator and teststand-presentation-generator agents. Load it before the first TestStand tool call.
---

# TestStand rules

## Never do

- Never run the `teststand-sequence-builder` as a subagent. Run its workflow in the main thread: its per-step `AskUserQuestion` prompts do not work in a subagent, and every step silently degrades to a plain `Statement` placeholder.
- Never use `Goto` or `Label` for control flow. Use the `NI_Flow_*` step types (`NI_Flow_If`, `NI_Flow_While`, `NI_Flow_For`, `NI_Flow_Select`, ...): they are structured and survive later edits.
- Never load a `.lvlibp` pane by any route: the in-process load kills the server and the isolated worker only times out. Clone it with `copy_step_module` or the `import_sequence_file` default.
- Never accept a rebuild on the diff alone. Run `diff_sequence_files` for the content AND `audit_type_consistency` for the type registry (the file's type definitions with their attach state); the diff cannot see type conflicts.

## Details

- [references/rebuild.md](references/rebuild.md) - rebuilding, migrating and cloning `.seq` files, file format, `.lvlibp` panes, verification.
- [references/sequence-design.md](references/sequence-design.md) - flow-control structure, Select/Case blocks, default step type, step types.
- [references/agents.md](references/agents.md) - building from a flowchart, the documentation and presentation generators.
- [references/headless-and-tools.md](references/headless-and-tools.md) - headless limitations, variables and expressions, validation, analyzer, live debugging, environments.
- [references/tool-contracts.md](references/tool-contracts.md) - how the write tools behave: enums, step and property-node editing, module configuration, prototype loading, .NET steps, bulk writers.
