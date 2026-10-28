<!-- spec-version: 14.0.1 -->
# Spec: Plugin Install and Release Workflow

**Format:** dev-team specs 14.0.1 · **Date:** 2026-10-24 · **Reference:** [Zuehlke/labview-mcp](https://github.com/Zuehlke/labview-mcp) (`release.yml`, `.claude-plugin/marketplace.json`, `plugin/`)

> **Amended by [dual-bitness-plugins.md](dual-bitness-plugins.md):** the x86-only constraint, the
> one-plugin marketplace and AC-7's four assets now hold per plugin, for `teststand-mcp` (x86) and
> `teststand-mcp-x64` (x64).

## Intent Description

Today a TestStand MCP user installs the server by hand. They download a zip that someone built
locally and uploaded (`TestStandMCP_V1.8.6.zip`, 1.7 MB, framework-dependent), install three x86
.NET 8 runtimes, and edit `claude_desktop_config.json`. Nothing tells them when a newer release
exists. The repository has no CI, its tags mix `V` and `v`, and every build reports `1.0.0`.

This change makes TestStand MCP installable as a Claude Code plugin, the same way labview-mcp is.
Two commands install it, and updates arrive without the user downloading or configuring anything.
Releases come from one GitHub Actions workflow that runs when a `vX.Y.Z` tag is pushed. That
workflow builds a self-contained x86 executable and packs it with the agents, hooks and rules
skill into a plugin archive. It publishes the archive under a fixed asset name, so the marketplace
manifest never needs editing. A session hook tells users when their installed version is behind
the latest release. The README leads with the new install route.

The aim is the same as for labview-mcp: one build, one artefact, and an install that can always
say which version it is. A user on the plugin route and a user who downloads the zip get the same
bytes.

## Architecture Specification

### New and changed components

| Component | Location | Role |
|---|---|---|
| Marketplace manifest | `.claude-plugin/marketplace.json` | Marketplace `zuehlke-teststand` with one plugin `teststand-mcp`. Its `archive` source points at `https://github.com/Zuehlke/teststand-mcp/releases/latest/download/teststand-mcp.zip`. |
| Plugin source tree | `plugin/` | `.claude-plugin/plugin.json` (version placeholder `0.0.0`), `.mcp.json` (server key `teststand`, command `${CLAUDE_PLUGIN_ROOT}/bin/TestStandMCP.exe`), `hooks/`, `skills/`. |
| Plugin-flavour agents | staged `agents/*.md` (not committed) | Generated from `.claude/agents/*.md` while the archive is staged. The `mcp__teststand__` prefix becomes `mcp__plugin_teststand-mcp_teststand__`, and `CLAUDE.md` references become references to the `teststand-rules` skill. |
| Rules skill | `plugin/skills/teststand-rules/SKILL.md` | The canonical home of the **user-facing** tool-usage rules from `CLAUDE.md`. `CLAUDE.md` imports it (`@…`) instead of keeping a copy. |
| Hooks | `plugin/hooks/` | `hooks.json`, a PreToolUse read-only auto-allow hook, a SessionStart platform guard, and a SessionStart update-notify hook. |
| Release workflow | `.github/workflows/release.yml` | Runs on every tag push (`tags: ['**']`), because the gate must see malformed tags. Steps: validate the tag, package (`build/Build-PluginArchive.ps1`), create a **draft** release, verify the draft, publish it. |
| PR build check | `.github/workflows/ci.yml` | Runs on pull requests to `main` and pushes to `main`. Same build, stage, assert and smoke-test steps as the release workflow. Publishes nothing. |
| Download stats | `.github/workflows/pages.yml` + `.github/stats/`, `.github/site/` | Takes a daily snapshot of release-asset download counts on a `stats` branch and deploys a GitHub Pages page. |
| Release scripts | `build/` (not shipped): `PluginIdentity.psm1`, `ReleaseAssets.psm1`, `Build-PluginArchive.ps1`, `Assert-ReleaseTag.ps1`, `Assert-PublishedRelease.ps1`, `Invoke-ScriptTests.ps1` | A shared module (asset names, plugin identity, manifest read and write, layout checks) plus thin entry points. They live outside `scripts/` because the csproj ships `scripts/**` next to the exe. Each one runs on a maintainer's machine too. |
| Version stamping | `Program.cs`, `TestStandMCP.csproj` | `--version` and the startup log read the assembly's informational version and `VERSION.txt` instead of the hard-coded `1.0.0`. The csproj default becomes `0.0.0-dev`, which marks a build that did not come from CI. |
| Docs | `README.md`, `CONTRIBUTING.md` (new) | README Quickstart = plugin install, update, auto-update. The manual and Claude Desktop routes move to a secondary section. CONTRIBUTING describes how to cut a release. |

### Archive layout (`teststand-mcp.zip`)

```
.claude-plugin/plugin.json      version stamped from the tag
.mcp.json
agents/                         plugin flavour (mcp__plugin_teststand-mcp_teststand__*)
hooks/
skills/teststand-rules/
VERSION.txt                     tag, version, commit, build time, run URL
bin/TestStandMCP.exe            self-contained FOLDER layout, win-x86: the exe, its runtimeconfig.json and the
                                runtime beside it (about 400 files; docs/validation/self-contained-x86.md)
bin/appsettings.json
bin/scripts/                    doc/presentation generators + template (AppContext.BaseDirectory\scripts)
bin/.claude/agents/             direct-registration flavour (mcp__teststand__*) + link-agents.bat
bin/CLAUDE.md                   the user rules of the skill, generated; no README in bin/
```

### Constraints

- **x86 only.** The TestStand engine COM server is 32-bit. Publish with `-r win-x86 --self-contained
  true` into a folder (the FOLDER layout; no `PublishSingleFile`). The `Microsoft.WindowsDesktop.App` and
  `Microsoft.AspNetCore.App` framework references stay.
- **The layout is the folder layout, measured on a station.** The engine loads .NET in-process and
  resolves the runtime from the host's `runtimeconfig.json`, so a layout without that file on disk is
  not self-contained against the engine: the single-file exe passed the four flows only because the
  engine fell back to the installed runtime. The folder layout ships the runtime and the runtimeconfig
  beside the exe, and the loaded `coreclr.dll` is the bundled one. No GitHub runner has TestStand, so CI
  cannot repeat that measurement; the record is
  [docs/validation/self-contained-x86.md](../validation/self-contained-x86.md), and `-Layout` of
  `build/Build-PluginArchive.ps1` still builds the other two layouts.
- **The isolated prototype worker re-launches `Environment.ProcessPath`.** In the folder layout that
  path is the apphost next to the runtime, and AC-2 flow 3 measured the relaunch (see
  `docs/validation/self-contained-x86.md`).
- **CI cannot run the integration tests.** All of `Test/TestExecution` needs a live engine. The
  pre-push hook (`.githooks/run-tests.ps1`) stays the test gate. CI gates on build, packaging
  assertions and executable smoke tests.
- **Releases come only from the workflow.** A hand-uploaded asset is rejected by the workflow's
  final verification step. There is no scheduled re-verification (the human chose this).
- **The fixed asset name is load-bearing.** Renaming `teststand-mcp.zip` breaks every install.
- **A plugin cannot ship `CLAUDE.md` or a permissions allow-list.** The rules ship as a skill, and
  the allow-list is a PreToolUse decision hook.
- **Encoding.** `plugin.json` contains "Zühlke". Every read and write is explicit UTF-8 without BOM,
  and the stamped file is checked byte for byte.
- **Minimum Claude Code version** is the one that supports the `archive` marketplace source
  (labview-mcp states ≥ 2.1.224; confirm during build).

### Out of scope

- Publishing to any marketplace other than this repository's own.
- Running the TestStand integration tests in CI.
- Deleting or renaming the existing mixed-case tags and hand-cut releases.
- Code signing the executable.
- Linux or macOS support (the platform guard only explains why there is none).

## Acceptance Criteria

### Release workflow

- **AC-1 Tag gate.** The release workflow triggers on **every** tag push, so the gate sees each
  mistake. GitHub's `v*` filter is case-sensitive, which means a `V1.9.0` tag would otherwise start
  nothing and give the maintainer no answer. A tag that is not exactly `v<major>.<minor>.<patch>`
  (lower-case `v`, three numeric parts) fails the workflow at its first step, before any build. The
  output names the problem and gives the retag commands in a safe order (delete the remote tag,
  delete the local tag, create the new tag, push). No asset is published.
  `build/Assert-ReleaseTag.ps1` gives the same verdict when run locally.
- **AC-2 Packaging layout spike (one-time, before the packaging is built).** On a TestStand station,
  three layouts are each driven through the four flows below:
  - self-contained single-file;
  - self-contained folder, with `runtimeconfig.json` on disk;
  - framework-dependent.

  The four flows:
  1. `connect_engine` succeeds.
  2. `open_sequence_file` succeeds on `DemoTestsequenz.seq`.
  3. `load_module_prototype` with `isolate:true` on a LabVIEW step whose VI does not exist returns
     `executionMode: "worker"` and `workerOutcome: "not-loaded"`, with the worker's own note (not
     `crashed`, `timeout` or a launch failure). `isolate` applies only to LabVIEW steps. This proves
     the worker relaunched from `Environment.ProcessPath`, started its own engine and reported back.
     `.lvlibp` panes are not covered.
  4. An execution started with `start_execution` reaches `Done`.

  For the self-contained layouts, the evidence must show that the .NET runtime was **not** loaded
  from `C:\Program Files (x86)\dotnet`: the loaded `coreclr.dll` path, taken from the process's
  module list. Cold-start time (first and second launch, with Defender real-time scanning on) is
  recorded too. The start budget is half of the MCP server start timeout recorded in
  `docs/validation/claude-code-plugin-facts.md`, and a layout whose first start exceeds it counts as
  failed. All three layouts are always run. The chosen layout is decided in this order:
  single-file if it passes, then folder if it passes, otherwise framework-dependent. The packaging
  script builds the chosen layout as its default. The result is recorded in
  `docs/validation/self-contained-x86.md`.
- **AC-3 Version stamping.** For tag `vX.Y.Z`:
  - the exe's `ProductVersion` starts with `X.Y.Z`;
  - `TestStandMCP.exe --version` prints `X.Y.Z`, the tag and the commit SHA to **stdout**, and never
    `-dev`;
  - the MCP `initialize` response's `serverInfo.version` is `X.Y.Z`;
  - the startup log line shows the same version. The runtime startup log is not observable under
    redirected stdio (with stdin redirected the exe logs to its own console window, not to stderr), so
    this item is checked statically: the startup `LogInformation` call prints `VersionInfo.Current.Version`,
    the value the runtime `serverInfo.version` check above proves;
  - the staged `plugin.json` has `"version": "X.Y.Z"` and is otherwise byte-identical to the source;
  - `VERSION.txt` at the archive root lists tag, version, commit, build time and run URL.

  The workflow fails if any of these does not hold. A local build without a tag reports `0.0.0-dev`.
- **AC-4 Archive layout.** The workflow fails unless the zip root contains
  `.claude-plugin/plugin.json`, `.mcp.json`, `VERSION.txt`, `agents/`, `hooks/` and
  `skills/teststand-rules/SKILL.md`, and `bin/` contains `TestStandMCP.exe` and `appsettings.json`.
  `bin/scripts/` contains **exactly** `generate_teststand_doc.py`,
  `generate_teststand_presentation.py` and `presentation_template.html`. No maintainer script (for
  example `Test-LoadPrototype.ps1`) is in it. `bin/` holds no `README.md`, no `build/` folder and no
  debug symbols (`*.pdb`), and nothing at its top level that `dotnet publish` did not produce, apart
  from `CLAUDE.md`, `scripts/` and `.claude/`. The staged `bin/CLAUDE.md` holds **the user rules and
  nothing else**: a header line, then the `teststand-rules` skill text and each of its reference files,
  inlined with their frontmatter removed, in the order of the import block of the repository's
  `CLAUDE.md`. None of the repository's developer sections is in it. The check is exact: the staged file
  must equal a fresh generation from the skill files, so a leftover import, an edit or an extra line
  fails the build. The `@` imports of the import block must be whole-line imports and must resolve to
  files below `plugin/skills`.
- **AC-5 Agent flavours.** The plugin-flavour agents are **generated while staging** from
  `.claude/agents/`. No second copy is committed. The workflow fails if either check below does not
  hold:
  - `agents/` holds exactly as many agents as `.claude/agents/`, and no plugin agent (frontmatter
    or body) contains `mcp__teststand__` or the string `CLAUDE.md` (case-insensitive), which a
    plugin install does not have;
  - `bin/.claude/agents/` holds the source agents unchanged, plus `link-agents.bat`.
- **AC-6 Smoke tests.** Run from the staged tree on the `windows-latest` runner, which has no
  TestStand installed, with stdin redirected:
  - `bin/TestStandMCP.exe --version` exits 0 with non-empty stdout;
  - `--list-tools --json` exits 0, and its stdout parses as a JSON array whose length equals the
    number of `Register("…")` calls in `src/Tools/TestStandToolRegistry.cs`.
- **AC-7 Published assets.** The workflow creates the release as a **draft**, verifies it (AC-8), and
  publishes it only when the verification passes. A failed verification therefore never moves
  `releases/latest`. The published release carries all of these assets, uploaded by
  `github-actions[bot]`:
  - `teststand-mcp.zip`;
  - `teststand-mcp-vX.Y.Z.zip` (same bytes);
  - `teststand-mcp.sha256` (`sha256sum -c` format);
  - `teststand-mcp.manifest.sha256` (path and hash of every file).
- **AC-8 Read-back verification.** Before publishing, the workflow downloads the draft's
  `teststand-mcp.zip` through the API. It fails, and leaves the release as a draft, if any of these
  holds:
  - the archive hash differs from the uploaded `.sha256`;
  - any entry differs from the manifest;
  - any required asset is missing;
  - the uploader is not `github-actions[bot]`.

  One retry, after a 30 s wait, is allowed only when the first attempt cannot download an asset
  (HTTP error or timeout). Amended: under Windows PowerShell 5.1 a body that stalls after the headers
  may not time out at all; the verify step's timeout then ends the run unpublished, with no retry. A hash, manifest, missing-asset or uploader failure is not retried.
  The one exception is the check after publishing: `releases/latest` can serve the previous release
  for a short while, so the comparison of the public latest download with the hash of the verified
  archive (`-Latest -ExpectedArchiveSha256`) is tried up to six times, 15 seconds apart. That
  propagation retry is the only retried hash comparison.
  CONTRIBUTING documents the rollback for a release that
  turns out bad after it was published: delete it, so `latest` falls back to the previous release,
  or cut `vX.Y.(Z+1)`. Deleting is safe only once an earlier workflow-built release exists, so the
  first one is fixed forward. A manual `dry_run` of the workflow rehearses the whole draft path and
  then deletes the draft.

  **What the verification does not prove.** It proves that the uploaded files are intact (digest,
  manifest, versioned copy) and were uploaded with this repository's workflow token. It does not
  prove that the build is genuine: a compromised build step would produce a consistent release that
  verifies. Build provenance attestation is a follow-up and is not part of this feature. To keep
  build code away from the write token, the workflow builds in a job with a read-only token and
  publishes from a second job that runs no build code. That job also compares the asset ids of the
  release with the verified ones immediately before and immediately after the publish call.

### PR build check

- **AC-9** A pull request to `main` runs all of the following. It publishes nothing.
  - the engine-free unit tests;
  - every script test (tag gate, release verifier, agent generation, hooks, stats). Every `.ps1`
    under `plugin/hooks/` and its tests run under both `powershell.exe` (5.1) and `pwsh` (7). Build
    scripts that declare `#Requires -Version 7` run under `pwsh` only;
  - AC-3 (with version `0.0.0-ci`), AC-4, AC-5 and AC-6 against a staged tree;
  - the AC-8 verifier against the staged output (local mode).

### Plugin install and update

- **AC-10 Install.** On Windows, with Claude Code at or above the minimum version recorded in
  `docs/validation/claude-code-plugin-facts.md` (checked with `claude --version`), run with an empty
  `CLAUDE_CONFIG_DIR` and no TestStand MCP registration present, these two commands install the
  plugin, and nothing is cloned, built or hand-edited:

  ```
  claude plugin marketplace add Zuehlke/teststand-mcp
  claude plugin install teststand-mcp@zuehlke-teststand
  ```

  After a restart:
  - `claude plugin list` shows `teststand-mcp` at the released version;
  - the TestStand tools appear as `mcp__plugin_teststand-mcp_teststand__*`;
  - `/agents` lists `teststand-sequence-builder`, `teststand-doc-generator` and
    `teststand-presentation-generator` under the plugin, and the skills listing (`/skills` or the
    session's available-skills list) shows `teststand-mcp:teststand-rules`.
- **AC-11 Fixed URL.** A new release is picked up with no change to `marketplace.json`. After
  release N+1, `claude plugin marketplace update zuehlke-teststand` followed by
  `claude plugin update teststand-mcp` installs N+1.
- **AC-12 Auto-update.** Marketplace auto-update is switched on by the procedure recorded in
  `docs/validation/claude-code-plugin-facts.md`, which is the same procedure the README documents.
  A session started after a new release then reports the new version with no manual command. If it
  has not updated after two session starts, the criterion fails.
- **AC-13 Update-notify hook.** At session **startup** only (not on resume or clear):
  - if the installed version (from `plugin.json`) is lower than the latest release, the hook prints
    one `systemMessage` in plain ASCII. The message names both versions and gives, as numbered
    steps in this order, `1. claude plugin marketplace update zuehlke-teststand` and
    `2. claude plugin update teststand-mcp`. It says to restart Claude Code afterwards, and that the
    message can be ignored when marketplace auto-update is on;
  - the same new version is announced at most once per 24 h;
  - when the versions are equal, when the installed version is the `0.0.0` development placeholder
    or is not a plain `X.Y.Z`, or when the check fails for any reason (offline, proxy, TLS, timeout,
    parse error), it prints nothing and exits 0. A pre-release tag (for example `v2.0.0-rc1`) is
    never announced: only plain `X.Y.Z` tags count;
  - a failed lookup is cached too, so an offline station pays the timeout at most once per 24 h;
  - the latest version comes from the redirect target of
    `https://github.com/Zuehlke/teststand-mcp/releases/latest`, which has no API rate limit. A
    leading `v` or `V` is stripped. Versions compare numerically (`1.10.0` > `1.9.0`);
  - the HTTP lookup has a 3 s timeout, and each hook entry in `hooks.json` (both SessionStart entries
    and the PreToolUse entry) has a timeout of 10 (seconds, slice 0 fact 5). The wall-clock total of a
    hanging lookup (the 3 s timeout plus the shell start, under 5 s) is checked locally on a Windows
    station (`TESTSTAND_MCP_TIMING_CHECK=1`) and is not a CI assertion;
  - the plugin name and the repository come from the installed `plugin.json`; a test-only override of
    the repository URL is honoured only for `https://github.com/` and loopback `http://` URLs;
  - setting `TESTSTAND_MCP_NO_UPDATE_CHECK=1` disables it;
  - it never blocks the session.
- **AC-14 Read-only auto-allow hook.** Tools on an explicit allow-list of **literal names** (no
  patterns) run without a permission prompt, and the decision carries the reason "no TestStand file
  is modified". Every other `mcp__plugin_teststand-mcp_teststand__*` tool still prompts.
  - The allow-list contains only tools that modify no file, engine, station or execution state and
    start no child process. An allow-listed tool may still connect the engine lazily on its first
    call (`EnsureConnected`): that starts the engine in the server process and can take seconds,
    but it changes no file or station state, so it does not count as a modification.
  - It excludes `evaluate_expression` and `evaluate_in_thread_context`, because an expression can
    assign.
  - It excludes the analyzer, `diff_sequence_files` and `compare_sequence_files`, because they spawn
    processes and write temp files.
  - It excludes `find_file`, because the engine can raise a modal prompt.
  - A tool that takes a file path also loads the file, and loading runs the file's `SequenceFileLoad`
    callback (measured, slice 0 facts, fact 18). So such a call is auto-allowed only when EVERY path
    argument the tool declares (`pathArguments` in `tool-classes.json`, derived from the tool schemas)
    is a `.seq` file (case-insensitive) on a local drive that lies inside the hook input's `cwd`, the
    session's project folder. A path must be a non-empty absolute `X:\...` path: no UNC, `\\?\`,
    `\\.\`, relative or drive-relative path, no wildcard, and no `..` that escapes the folder after
    `[IO.Path]::GetFullPath`. Both sides are normalised (full path, trailing separator, case-insensitive
    prefix on a segment boundary). Anything else, including a missing `cwd`, prints nothing, so the user
    gets the normal prompt. An absent argument does not block; a present non-string value prompts.
    Tools without a path argument are not affected. The one residual risk, stated in the README: a
    `.seq` inside the project still runs its `SequenceFileLoad` callback, so open only files you trust.
  - A test fails when a registered tool is in neither the allow-list nor an explicit prompt-list, so
    every new tool is classified on purpose, and when an allow-listed tool has a path-like parameter
    (a name containing `path` or `file`) that is declared neither as a path argument nor as "not a file".
  - Malformed hook input yields no decision, never an error.
- **AC-15 Platform guard.** At session **startup** only:
  - ~~on a non-Windows host, the hook prints one message explaining that the server needs Windows
    with TestStand~~ (struck: the server executable cannot start off Windows, and the README states
    that the plugin is Windows-only; a POSIX hook entry could also show a hook-error notice to a
    Windows user without Git Bash, slice 0 fact 10);
  - on Windows it prints one combined, plain-ASCII warning, and never blocks, when TestStand is not
    detected, or when Python (found through `py -3` first, then `python`), `python-docx`, Pillow or
    Edge/Chrome are missing. "TestStand detected" means the `TestStand` environment variable is set,
    or a `National Instruments\TestStand*` folder exists under `%ProgramFiles%` or
    `%ProgramFiles(x86)%`. The warning states that the doc and presentation agents are optional and
    gives the install command of the found interpreter (`py -m pip install python-docx pillow`, or
    `python -m ...` when only `python` was found);
  - a healthy station sees no warning;
  - the optional dependencies are probed at most once per 24 h and the outcome is cached with its
    time, so inside that window nothing is probed and the optional warning is not repeated; a
    changed missing set is noticed when the window ends. `TESTSTAND_MCP_NO_PLATFORM_WARNING=1`
    switches the optional part off. A missing TestStand is reported every time, and so is the
    rules pointer, even when an optional probe fails;
  - it also emits a one-line `additionalContext` that tells the model to load the `teststand-rules`
    skill before TestStand work, so the rules do not depend on the skill description being matched.
    This item applies only if the AC-2 spike confirms SessionStart supports `additionalContext`.
    Otherwise it is struck and the finding recorded.
- **AC-16 Rules skill.** The `teststand-rules` skill holds the user-facing rules. `SKILL.md`
  contains only the frontmatter, a never-do section (main thread for the builder, `NI_Flow_*`
  mandate, `.lvlibp` clone-not-load, diff plus `audit_type_consistency`) and one pointer per
  `references/*.md` file (rebuild, sequence design, agents, headless and tools, tool contracts).
  Each rule below must appear at least once in a `references/*.md` file; `SKILL.md` repeats only
  the never-do four:
  - export/import first;
  - `.lvlibp` clone-not-load;
  - verification with diff plus `audit_type_consistency`;
  - `NI_Flow_*` mandate and `Select`/`Case` structure;
  - `SequenceCall` default;
  - headless limitations;
  - the rule that the sequence builder runs in the main thread and the doc and presentation
    generators ask the language first.

  The text is edited for users, not copied: no test IDs, memory names, dated change history or
  "this file" references. Server internals are covered by the `Marshal`, `dynamic`, `taskkill` and
  `dotnet build` entries below. It contains none of the developer-only rules (C# COM
  interop, server rebuild, test-suite internals). This is checked by `build/Test-SkillSources.ps1`
  against this deny-list:
  - dates matching `\b20\d\d-\d\d-\d\d\b`;
  - test IDs matching `\bT\d{2}\b`;
  - the strings `memory`, `[[`, `.Demo_jcm`, `this file`, `Marshal`, `dynamic`, `taskkill` and
    `dotnet build`, and, as developer-only pointers, `CLAUDE.md`, `build/` and `src/`
    (all matched case-insensitively).
  `CLAUDE.md` imports the skill files, so each user-facing rule has exactly one copy in the
  repository.
- **AC-17 Environment configuration survives updates.** The README documents the plugin route for a
  `.tsenv`:
  - a copy-paste `setx TESTSTAND_MCP_TestStand__EnvironmentPath "<path>"`;
  - "restart Claude Code";
  - a check: ask Claude to call `get_engine_paths`, and `environmentActive` must be true;
  - the per-session alternative `connect_engine(tsenv_path=…)`.

  It states that edits to the plugin's own `bin/appsettings.json` are lost on update. The `userConfig`
  route is **unverified**: the documentation says `${user_config.KEY}` is substituted in the server's
  environment (`claude-code-plugin-facts.md`, fact 8), but no run has observed a filled-in value reach
  the server. The fork install (`docs/validation/fork-rehearsal.md`) only showed the prompt for the
  unset option ("1 userConfig option not yet set"). The README says so. The plugin's `tsenv_path`
  goes to its own variable `TestStand:PluginEnvironmentPath`, which wins over `TestStand:EnvironmentPath`
  only when it is non-blank and not an unexpanded `${…}` placeholder. A machine-wide setting therefore
  survives a plugin install with the field left empty, whichever way Claude Code treats the unset
  option. The README shows `userConfig` as the plugin route (marked "unverified") and keeps `setx` as
  the machine-wide alternative that has been observed to work. The `environmentActive` check settles
  it on a `.tsenv` station; until then "unverified" is an accepted outcome for this criterion.

### Stats

- **AC-18 Download stats.** A daily scheduled run (also runnable by hand) records the cumulative
  `download_count` of each release asset to the `stats` branch as CSV. It then deploys a Pages
  site that charts installs over time, with downloads by the repository's own workflows subtracted.
  A snapshot means the cumulative counts at the end of UTC day D. The run is scheduled just after
  midnight (`17 0 * * *`) and dates its snapshot the previous UTC day. A run started by hand
  during the day also records the previous day, with counts that already include part of today.
  The raw counts include the assets of draft releases, so a draft download shows up in the same
  day's delta as its own-download record. Installs on a day are the delta to the previous
  snapshot; when the snapshots are more than one day apart, the whole delta is booked on the
  later date and the table row and chart description say "since <previous date>". The chart
  shows two download series, the latest-archive asset `teststand-mcp.zip` and all versioned
  archives `teststand-mcp-vX.Y.Z.zip` summed, and covers at most the last 366 days.
  Every download a workflow makes is recorded per asset and per date in one run artifact: each
  verify attempt, the retry, and the final public fetch. Amended: a dry run records nothing, because
  its draft is deleted before a snapshot can count it (a draft left by a failed cleanup would count). The collector subtracts those counts per
  asset and per date, clamped at zero. A test on the shared fixture checks that the subtracted value
  equals the recorded count. The record is
  `{ "run_id", "counts": { "<YYYY-MM-DD>": { "<asset>": { "draft": n, "published": m } } } }`, so
  each download carries its own date and phase. The collector accepts such an artifact only from
  a run of `release.yml` of this repository (not a fork) started by a push or by hand, and only
  when the record's `run_id` names that run. Whether
  downloads of a *draft* raise `download_count` is measured on the first release, and the
  collector has a switch for both outcomes. If they count, the `draft` counts are subtracted with
  the `published` ones. If they do not, only the `published` counts are subtracted. The
  measured outcome sets the switch's committed default, and the fixture test covers both settings. Repository traffic is
  included only when the optional `STATS_TRAFFIC_TOKEN` secret exists, and the page says so in
  visible text when it does not. The chart has an accessible title and description, a visible data
  table, and series that are distinguishable without colour. A missing Pages setup fails only this
  workflow.

### Documentation

- **AC-19 README.** The README change lands in a **second PR**, merged only after the first
  workflow-built release is published and AC-10 has been checked on a clean profile. So the
  Quickstart on `main` never points at a release that does not exist. The first install section
  after the prerequisites is the plugin Quickstart, in this order:
  1. Install: the two commands and the minimum Claude Code version, with `claude --version` to
     check it.
  2. Check it worked: restart, `claude plugin list`, the expected tool prefix.
  3. Updates: how to switch on auto-update, with the manual commands as the fallback.
  4. Troubleshooting: an older Claude Code, a slow first start, AV/SmartScreen.

  The prerequisites table drops the .NET runtime row unless AC-2 chose the framework-dependent
  layout. The Claude Desktop and manual config route stays, under a secondary "Install another
  way" heading, and points at the release zip with the exact `bin\TestStandMCP.exe` path and the
  `bin\.claude\agents\link-agents.bat` step. It states that updates on that route are manual. A
  "Moving from a manual install" note says to remove the old `teststand` registration before
  installing the plugin. The README no longer links to `/tags` for downloads.
- **AC-20 CONTRIBUTING.** It describes how to cut a release:
  - push a `vX.Y.Z` tag, which the workflow turns into a draft, verifies and then publishes;
  - why a hand-uploaded asset is rejected;
  - the rollback;
  - how to package and test locally;
  - the one-time admin setup for Pages and the optional stats token.

  Each script failure message names what is wrong, why it matters, and the exact command that
  fixes it.

## Glossary

| Term | Definition | Status | Source |
|------|------------|--------|--------|
| Plugin archive | `teststand-mcp.zip`, the release asset that is the plugin's whole install payload | `verified` | labview-mcp `release.yml`; user request |
| Marketplace | The `zuehlke-teststand` catalogue in `.claude-plugin/marketplace.json` that Claude Code adds by repo name | `unverified` | named by analogy with `zuehlke-labview` |
| Hand-cut release | A release whose asset was uploaded by a person, not `github-actions[bot]` | `verified` | `gh api` on v1.8.6 (uploader: a maintainer account) |
| Plugin flavour / direct flavour | Agent files whose tool names use the plugin prefix (generated while staging) vs. the `mcp__teststand__` prefix (the committed source) | `unverified` | labview-mcp `Sync-PluginAgents.ps1`; plan review |
| Self-contained build | win-x86 exe in the FOLDER layout: the exe, its `runtimeconfig.json` and the .NET runtime with both required shared frameworks beside it | `verified` | station measurement, `docs/validation/self-contained-x86.md` |

## Ambiguity Log

| Decision | Classification | Resolved By | Rationale / Answer |
|----------|---------------|-------------|-------------------|
| Self-contained vs. framework-dependent exe | `requires-stakeholder-input` | human | Self-contained x86, with a one-time on-station validation and a framework-dependent fallback (AC-2). |
| Which workflows besides `release.yml` | `requires-stakeholder-input` | human | PR/push build check and Pages download stats. A daily verify-release was **declined**, so the only read-back check is the one inside `release.yml` (AC-8). Predictability check: a reader could expect the in-workflow check to go as well. Kept, because it is part of "the same release workflow" the user asked for. |
| Plugin contents | `requires-stakeholder-input` | human | All four: agents, read-only allow hook, rules skill, platform guard. |
| Meaning of "auto update" | `requires-stakeholder-input` | human | Claude Code marketplace auto-update **plus** a SessionStart notify hook (AC-12, AC-13). |
| Plugin, marketplace, server and asset names | `inferable` | inference | Mirrors labview-mcp (`labview-mcp` / `zuehlke-labview` / `labview` / `labview-mcp.zip`). The repo is `teststand-mcp` and the existing server key is `teststand`. Alternative `teststand` as plugin name: rejected, because it gives the confusing prefix `mcp__plugin_teststand_teststand__`. |
| Tag format and first new version | `inferable` | inference | `vX.Y.Z` lower-case (the latest tags are already `v1.8.x`). The next release is the maintainer's choice of number. Old `V*` tags are left alone (out of scope). |
| CI test gate | `inferable` | inference | Integration tests need a licensed TestStand engine, which runners lack. CI gates on build, packaging and smoke tests. The pre-push hook stays the test gate. |
| Read-only allow-list membership | `inferable` | inference | The server declares no `readOnlyHint`, so the list is explicit and curated. Rule: no file, engine, station or execution mutation. `evaluate_*` and the analyzer are excluded. The list is reviewed in `/plan`. |
| Single source for user rules | `inferable` | inference | A copied skill would drift from `CLAUDE.md`. `CLAUDE.md` supports `@path` imports, so the skill is canonical and `CLAUDE.md` imports it. |
| Notify-hook throttling and failure mode | `inferable` | inference | The unauthenticated GitHub API allows 60 requests/h, and a SessionStart hook must never block. So: 24 h cache, a 3 s HTTP cap inside a 10 s hook timeout, and silence on any failure. |
| Notify-hook shell | `inferable` | inference | Windows-only payoff, so PowerShell, started in exec form (`powershell.exe` with an `args` list: no shell, no Git Bash, no quoting). The platform guard is PowerShell too; the non-Windows message was dropped (AC-15), because the server cannot start off Windows. |
| `.tsenv` configuration on a plugin install | `inferable` | inference | The plugin cache is replaced on update, so `bin/appsettings.json` edits are lost. The existing `TESTSTAND_MCP_` environment variable route survives (AC-17). |
| Direct-registration agents in the zip | `inferable` | inference | The Claude Desktop/manual route still exists and needs the `mcp__teststand__` flavour plus `link-agents.bat`. labview-mcp ships both flavours for the same reason. |
| Hand-cut releases in existing history | `inferable` | inference | Not touched. `/releases/latest` resolves to the first workflow-built release once it is published. |
| CRUD sweep: release (create = tag push; read = install/verify; update = asset replacement → rejected by AC-8 at publish only; delete = out of scope) | `inferable` | inference | Replacement after publish goes undetected, because the daily verify was declined. The human accepted this when declining. |
| CRUD sweep: plugin install (create AC-10, read AC-10, update AC-11–13, delete = `claude plugin uninstall`, a Claude Code built-in, no criterion needed) | `inferable` | inference | Uninstall is not this repo's behaviour. |
| Auth sweep | `inferable` | inference | Workflows use `GITHUB_TOKEN` with least privilege (`contents: write` only in release, `pages`/`id-token` only in deploy). The stats token is optional and read-only. Users need no credentials, because the repo is public. |
| Build provenance (slice 7 review, 2026-10-24) | `requires-stakeholder-input` | human | The read-back (AC-8) proves integrity and the uploader, not that the build is genuine. Build provenance attestation is a follow-up and is deliberately not added now. The workflow limits the exposure instead: the build job has a read-only token, the job with `contents: write` runs no build, and the asset ids are re-checked around the publish call. |
| Error-handling sweep | `inferable` | inference | Every workflow step fails loudly with a diagnosis (AC-1 to AC-8). The hooks fail silent and non-blocking (AC-13 to AC-15). |
| LOW_VALUE: unit tests for `hooks.json` wiring | `inferable` | inference | Skipped. No branching logic, and the install smoke check (AC-10) covers it. |
| Build AC gate (2026-10-24) | `inferable` | spec-compliance-review + human | 12 criteria were tightened for verifiability, with no design change. The changes: AC-2's budget source, flow 3's definition and layout order; AC-4's exact `bin/scripts` set; AC-5's case-insensitive match; AC-6's outside-observable form; AC-8's retry trigger; AC-9's script set; AC-10's version and profile; AC-12's two-session bound; AC-13's timeouts; AC-15's detection rule and command; AC-16's structure and deny-list; AC-17's unverified outcome; AC-18's draft rule. The user chose "apply and build". |
| Pre-release build asset name (slice 5, 2026-10-24) | `inferable` | inference | A local or pull-request build uses version `0.0.0-ci`. The release tag format and the release verifier know only `X.Y.Z`, so the versioned copy of such a build is named after its numeric core: `teststand-mcp-v0.0.0.zip`. A real release (`vX.Y.Z`) is unaffected. |
| `bin/CLAUDE.md` content (slice 5 review, 2026-10-24) | `requires-stakeholder-input` | human | The direct route's `bin/CLAUDE.md` holds the user rules only (the skill and its reference files), not the repository's developer sections; `bin/README.md` is not shipped. |
| Plan-review revision (2026-10-24) | `inferable` | inference (plan reviewers) | Five plan reviewers found defects in the original ACs. **AC-1**: the trigger was case-sensitive, so the gate was unreachable. **AC-2**: the spike now runs first, tests three layouts, and needs evidence that no installed runtime was loaded. **AC-3/6**: the `serverInfo` version was missing, and `--list-tools` wrote to a reallocated console. **AC-4/5**: the `bin/CLAUDE.md` import dangled, and the generated agents are now staged rather than committed. **AC-7/8**: draft, then verify, then publish, plus a rollback. **AC-13**: failures are now cached, there is an opt-out, the run is startup-only, and the redirect replaces the API. **AC-14**: literal names only, a consistent no-child-process rule, and a classification test. **AC-15/16**: startup-only, plus `additionalContext`, and an edited skill split into references. **AC-18**: the download count contract and accessibility. **AC-19**: the README lands in a second PR after the first release. None of these changes a stakeholder decision. |

## Consistency Gate

- [x] Intent is unambiguous
- [x] Every behavior/goal maps to an acceptance criterion (install → AC-10; updates without user action → AC-11/12/13; single workflow from tag → AC-1–8; self-contained → AC-2/3/6; agents, hooks, skill → AC-5/14/15/16; README → AC-19; stats → AC-18)
- [x] Architecture constrains without over-engineering (no scheduled verify, no signing, no extra marketplaces)
- [x] Terminology consistent across artifacts (`teststand-mcp`, `zuehlke-teststand`, `teststand-mcp.zip`, plugin/direct flavour)
- [x] No contradictions between artifacts
- [x] Every gap/ambiguity finding is logged — inferable with rationale or resolved by human

**Verdict: PASS.** Two glossary terms remain `unverified` (naming by analogy). They are logged as
inferable and can be confirmed at plan review.
