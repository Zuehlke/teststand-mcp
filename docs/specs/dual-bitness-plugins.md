<!-- spec-version: 14.0.1 -->
# Spec: 32-bit and 64-bit TestStand, as two plugins

**Format:** dev-team specs 14.0.1 · **Date:** 2026-10-25 · **Builds on:** [plugin-install-and-release-workflow.md](plugin-install-and-release-workflow.md) (unmerged, same branch)

## Intent Description

The server hosts the TestStand engine **in process**, and a COM in-process server only loads into
a process of the same bitness. Today the server is built x86 only, so it can only ever drive a
32-bit TestStand. A station with only 64-bit TestStand cannot use it at all, and a station with
both always gets the 32-bit one.

This change ships the same server twice from one codebase and one tag:

- **`teststand-mcp`**, an x86 build, for 32-bit TestStand (the plugin as it is today);
- **`teststand-mcp-x64`**, an x64 build, for 64-bit TestStand.

Both are entries of the one `zuehlke-teststand` marketplace. A user installs the one that matches
the TestStand they work with. Each plugin's server only ever uses the TestStand install of its own
bitness, including for the helper tools it starts (analyzer, FileDiffer, Sequence Editor). It never
borrows a tool from the other install, because a mixed run gives results from a different
configuration (process models, search directories, adapters) that look correct.

The release, verification, update and stats paths of the parent spec stay as they are. They gain
the architecture as one more dimension.

**Spike evidence (2026-10-25, this station, TestStand 2026 Q3 Patch 1 in both bitnesses).**
The unchanged codebase built with `-p:Platform=x64` connects to the 64-bit engine
(`versionString` ends in `64-bit`, `binDirectory` under `C:\Program Files`). Single Pass reaches
`Passed`, and the diff and the analyzer run. The x86 build does the same against the 32-bit
engine. The one 32-bit failure, the analyzer template rejected with -17100, was a header
attribute missing from the built-in analyzer project, unrelated to bitness; it is fixed in
`cab876a`.

## Architecture Specification

### Changed components

| Component | Change |
|---|---|
| `TestStandMCP.csproj`, `Test/TestExecution/TestStandMCP.IntegrationTests.csproj` | `Platforms` gains `x64`; `PlatformTarget` follows `Platform` (x86 or x64). Default stays x86, so every existing command keeps working. |
| `build/PluginArchive.psm1`, `build/Build-PluginArchive.ps1` | `-Architecture x86\|x64` (default x86) selects the runtime identifier (`win-x86`/`win-x64`), the build platform and the plugin identity. |
| `build/PluginIdentity.psm1`, `build/PluginStaging.psm1` | One identity table: architecture → plugin name, display name, description, asset prefix. Staging stamps the name into `plugin.json` and generates `hooks.json` and the agents with that name's tool prefix. |
| `build/ReleaseAssets.psm1`, `build/Assert-PublishedRelease.ps1`, release scripts | The release asset set is the union of both architectures' sets (8 assets). Every check that today covers one archive covers each. |
| `.claude-plugin/marketplace.json` | Two plugin entries, each pointing at its own fixed `releases/latest/download/<prefix>.zip`. |
| `plugin/hooks/*` | The allow hook accepts its own plugin's tool prefix, with the same read-only set in both plugins (DB-12). The platform guard checks for a TestStand of the plugin's own bitness. The state folder is per plugin. |
| `src/Services/TestStandInstallLocator.cs`, `SequenceEditorService.cs` | Same-bitness only: the process's own Program Files root and registry view; once connected, the engine's own `Bin` only. |
| `src/VersionInfo.cs`, `VERSION.txt` | `--version` and `VERSION.txt` name the architecture. |
| `.github/workflows/ci.yml`, `release.yml` | Package, smoke and verify both architectures in one package job. |
| `.githooks/run-tests.ps1` | Runs the integration suite once per installed TestStand bitness. |
| Docs | `CONTRIBUTING.md`, `CLAUDE.md` (dev rebuild command), the rules skill (bitness of code modules), the README (PR B). |

### Identity table

| Architecture | Plugin | Display name | Asset prefix | Runtime | TestStand |
|---|---|---|---|---|---|
| x86 | `teststand-mcp` | TestStand MCP (32-bit) | `teststand-mcp` | `win-x86` | 32-bit, `%ProgramFiles(x86)%\National Instruments\TestStand*` |
| x64 | `teststand-mcp-x64` | TestStand MCP (64-bit) | `teststand-mcp-x64` | `win-x64` | 64-bit, `%ProgramW6432%\National Instruments\TestStand*` |

The server key stays `teststand` in both, so the tool prefixes are
`mcp__plugin_teststand-mcp_teststand__` and `mcp__plugin_teststand-mcp-x64_teststand__`.

### Constraints

- **One codebase, no `#if` per bitness.** The difference is the build platform and the identity
  table. A runtime bitness question is answered with `Environment.Is64BitProcess`.
- **No cross-bitness fallback** anywhere a helper tool or install is resolved. The explicit
  `connect_engine(engine_path)` override stays the one escape hatch, because the operator chose it.
- **Code modules follow the engine's bitness.** A 64-bit TestStand loads 64-bit LabVIEW, Python
  and DLL modules. This is TestStand's own rule; the server only documents it.
- **The two plugins may be installed side by side.** Each starts its own server and engine; they
  do not share state files. The docs advise installing one.
- The x86 plugin keeps its name, so the parent spec's install commands, update path and stats
  history stay valid.

### Out of scope

- One plugin carrying both runtimes, or a launcher that picks one. Declined in favour of two plugins.
- ARM64.
- Running the integration suite in CI (no TestStand on the runners, unchanged).

## Acceptance Criteria

### Build

- **DB-1 Both platforms build.** `dotnet build TestStandMCP.csproj -p:Platform=x86` produces a
  32-bit `TestStandMCP.exe` and `-p:Platform=x64` a 64-bit one, read from the PE header machine
  field (`0x14c` / `0x8664`). A build without `Platform` is x86. The integration test project
  builds for both the same way.
- **DB-2 Packaging per architecture.** `Build-PluginArchive.ps1 -Architecture x64` produces
  `teststand-mcp-x64.zip`, its digest, manifest and versioned copy, with a `win-x64`
  self-contained `bin` in the folder layout. Without `-Architecture` the result is byte-for-byte
  the x86 output of today's script (same file names, same staged tree). An unknown architecture
  fails before any build, naming the allowed values.
- **DB-3 Identity is stamped, not edited.** In the x64 archive, `plugin.json` has name
  `teststand-mcp-x64` and the x64 display name. Every `hooks.json` matcher entry and every agent
  tool reference uses `mcp__plugin_teststand-mcp-x64_teststand__`, and no file in the staged tree
  contains `mcp__plugin_teststand-mcp_teststand__`. The x86 archive contains no
  `teststand-mcp-x64` string. A test asserts both directions.
- **DB-4 Architecture is visible.** `TestStandMCP.exe --version` and `VERSION.txt` carry
  `arch: x86` or `arch: x64`, matching the build. The packaging smoke test fails when they
  disagree with the requested architecture.

### Runtime

- **DB-5 Same-bitness resolution.** Unit-tested in `T34`, with a fake file system and registry:
  - with an engine `Bin` known, a helper tool missing there yields an error, never a path under the
    other bitness's root;
  - without an engine, the x64 process scans `%ProgramW6432%` and the 64-bit registry view only,
    and the x86 process `%ProgramFiles(x86)%` and the 32-bit view only;
  - an explicit `engine_path` still wins in both;
  - the error names the tool, the process bitness and the probed paths.

  The Sequence Editor launcher follows the same rule.
- **DB-6 Engine flows on each bitness.** The four parent-spec engine flows (connect, open
  `DemoTestsequenz.seq`, isolated worker `not-loaded`, an execution reaching `Passed`), plus
  `diff_sequence_files` and `analyze_sequence_file` on a one-step file, run from each installed
  archive's exe against its own TestStand. `get_engine_paths` reports a `binDirectory` under that
  bitness's root, and the analyzer and FileDiffer started come from it (verified from the server's
  diagnostic log).
- **DB-7 Integration suite on both.** The full integration suite passes under x86 against 32-bit
  TestStand and under x64 against 64-bit TestStand on this station, before PR A is pushed. A test
  that cannot pass on one bitness for a reason outside the server (a missing 64-bit test asset, for
  example) is skipped on that bitness with the reason in its skip message. A skip is not a pass:
  the list of skips per bitness goes into `docs/validation/dual-bitness.md`.
- **DB-8 Pre-push per bitness.** `.githooks/run-tests.ps1` detects which TestStand bitnesses are
  installed by looking for each engine's `National Instruments\TestStand*\Bin\teapi.dll` (not
  the platform guard's detection, which accepts the TestStand environment variable or any `TestStand*`
  folder: the suite needs the engine DLL itself) and runs
  the suite once per installed bitness, printing which one runs. The push is blocked if any run
  fails, or if no TestStand is
  installed.

### Plugins and release

- **DB-9 Marketplace.** `marketplace.json` lists `teststand-mcp` and `teststand-mcp-x64`, each with
  an archive source at `https://github.com/Zuehlke/teststand-mcp/releases/latest/download/<prefix>.zip`
  and a description naming the TestStand bitness it needs.
- **DB-10 One tag, both archives.** A `vX.Y.Z` tag push builds, drafts, verifies and publishes one
  release that holds both architectures' four assets (8 in total). The verifier, the pre- and
  post-publish asset re-check and the `releases/latest` comparison each cover both archives. A
  fault in either archive (the dry run's `corrupt-zip` gains a `-x64` target) stops the publish of
  the whole release.
- **DB-11 Guard per bitness.** The platform guard of each plugin warns when no TestStand of the
  plugin's own bitness is installed, and names the other plugin when only the other bitness is
  installed ("64-bit TestStand found: install teststand-mcp-x64 instead").
- **DB-12 Hooks per plugin.** The allow hook allows the same read-only set for both prefixes, and
  each plugin's update notice names its own plugin in the update command. The two plugins keep
  their hook state in separate folders.
- **DB-13 Install and update in the fork.** In the throw-away fork, both plugins install side by
  side in an isolated `CLAUDE_CONFIG_DIR`, and `claude mcp list` shows both servers connected.
  Each one updates from one fork release to the next.
- **DB-14 Stats.** The stats collector counts both prefixes' assets, and the site shows downloads
  per plugin. The release runs' own downloads stay subtracted per asset.

### Documentation

- **DB-15** `CONTRIBUTING.md` names both architectures in the release, rollback and local-build
  sections. The repository `CLAUDE.md` gives the x64 dev rebuild command next to the x86 one. The
  rules skill states that code modules must match the engine's bitness. The README (PR B) has one
  install command per plugin and a one-line "which one do I need" rule.

## Glossary

| Term | Definition | Status | Source |
|------|------------|--------|--------|
| Architecture | `x86` or `x64`, the build platform of the server; it decides which TestStand bitness the plugin drives | `verified` | spike 2026-10-25 |
| Same-bitness install | The TestStand tree under the Program Files root and registry view of the server process's own bitness | `verified` | `TestStandInstallLocator`, registry on this station |

## Ambiguity Log

| Decision | Classification | Resolved By | Rationale / Answer |
|----------|---------------|-------------|-------------------|
| Distribution | `requires-stakeholder-input` | human | Two plugins in one marketplace (option A). One dual-runtime plugin and a launcher were declined. |
| Names | `inferable` | inference | The x86 plugin keeps `teststand-mcp` so the parent spec's names stay valid; the x64 one is `teststand-mcp-x64`, asset prefix the same. |
| Both bitnesses tested before merge | `requires-stakeholder-input` | human | "make the MCP work on 32 and 64 bit before merging back"; the 32-bit development system was installed for it. |
| Package job shape | `inferable` | inference | One package job builds both architectures in sequence. A matrix would need its artifacts merged again before the single release job; the extra build costs about 1.5 min. |
| Cross-bitness fallback | `inferable` | inference | Removed. It was the mechanism behind the earlier 32-bit analyzer failures, and a mixed run reports wrong results as clean. |
| Analyzer template | `inferable` | measurement | Fixed separately (`cab876a`): the header needs `buildversion`; measured through the engine API on both bitnesses, no dialog. |

## Consistency Gate

- [x] Intent is unambiguous
- [x] Every goal maps to a criterion (two builds → DB-1/2/4; two plugins → DB-3/9/11/12/13; same-bitness tools → DB-5/6; tested on both before merge → DB-7/8; release → DB-10; stats → DB-14; docs → DB-15)
- [x] Architecture constrains without over-engineering (no launcher, no `#if`, one package job)
- [x] Terminology consistent (`teststand-mcp`, `teststand-mcp-x64`, architecture `x86`/`x64`)
- [x] No contradictions with the parent spec (its x86 criteria become the `x86` row of the identity table)

**Verdict: PASS.**
