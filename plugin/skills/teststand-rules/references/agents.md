# The TestStand agents

Three agents ship with the plugin: `teststand-sequence-builder` (the builder), `teststand-doc-generator` and `teststand-presentation-generator`. Two rules decide how to start them: the builder runs in the main thread, and the two generators need the language asked first.

## How to build sequences from a flowchart or description

The builder's workflow is interactive: for each step it asks the user whether to link a `SequenceCall` (target file plus subsequence) or to leave an unlinked SequenceCall placeholder. That per-step question uses `AskUserQuestion`, which is NOT available to spawned subagents because it depends on the main conversation's UI.

- Never delegate sequence building to the builder through the Agent or Task tool. As a subagent, every linking question fails silently and all steps degrade to plain `Statement` placeholders, which is exactly the failure to avoid.
- Always run the builder workflow in the main conversation thread. When the user asks to build a sequence from a flowchart, or to use the sequence agent, open the agent definition of the builder, read its workflow and execute those steps yourself, so the per-step `AskUserQuestion` prompts reach the user.
- Everything the builder inserts follows [sequence-design.md](sequence-design.md): `NI_Flow_*` for control flow, `SequenceCall` as the default step type.

## Documenting sequence files (`teststand-doc-generator`)

The agent turns a `.seq` file into a Word document: a title and short file description, a real table of contents, one section per sequence (description, parameter table with By Value / By Reference, a compact flow-indented step listing that keeps the step groups (Setup, Main, Cleanup), each step's original TestStand icon tinted in the document accent color), and a rendered diagram of the call dependencies between the sequences.

- Unlike the builder, this agent MAY and should be spawned as a subagent through the Agent tool: it is non-interactive and strictly read-only toward TestStand.
- Ask the document language first, in the main thread. Before spawning the agent, ask the user via `AskUserQuestion` which language the documentation should be written in. Offer at least German and English, with the language of the user's request as the recommended first option; "Other" free text covers further languages. The subagent cannot ask. Skip the question only when the user already stated the language.
- Pass in the task prompt: the `.seq` path (required), the document language (`de` and `en` are built in; other languages need the generator script's `labels` override), and optionally the output `.docx` path and a custom title.
- The heavy lifting is a deterministic script (data JSON, then a dependency diagram rendered by headless Edge or Chrome, then the `.docx` via python-docx), run through the `py` launcher because plain `python` is not on the PATH. The agent only collects data through the read-only MCP tools and runs the script.

## Presenting sequence files (`teststand-presentation-generator`)

The agent turns a `.seq` file into an interactive HTML presentation (dark single-page app): a header with the sequence and source, phase cards for the step groups (Setup, Main, Cleanup), clickable subsequences that open a detail overlay, and a "Code & Flowchart" compare view. The flowchart nodes use the original TestStand step icons in full color (taken from the local install) and rebuild loops (While, For, ...) and branches (If, Select, Case) as nested blocks. The output is ONE self-contained `.html` with the icons embedded as base64, so it can be shared as a single file. It is the visual counterpart to the Word documentation.

- It MAY and should be spawned as a subagent through the Agent tool: non-interactive and strictly read-only toward TestStand.
- Ask the presentation language first, in the main thread, by the same rule as for the documentation (`AskUserQuestion` does not work in subagents). `de` and `en` are built in.
- Pass in the task prompt: the `.seq` path (required), the language, and optionally the output `.html` path and a custom title.
- There are no live Sequence Editor screenshots. TestStand's editor renders its UI with embedded Chromium, which Windows UI automation cannot drive, so per-sequence screenshots cannot be captured reliably. The compare view's right pane is a rendered TestStand-style step listing (real icons, flow indentation, step groups) built from the data; it always works and needs no editor. If you supply a folder of manually captured PNGs plus a `_manifest.json`, the generator can embed them through its optional `--shots-dir` argument.
- The heavy lifting is a deterministic script (data JSON plus an HTML template to the self-contained `.html`; the icons are extracted with Pillow from the TestStand `Components` folder), run through the `py` launcher. No headless browser is needed. The agent only collects data through the read-only MCP tools and runs the script.
