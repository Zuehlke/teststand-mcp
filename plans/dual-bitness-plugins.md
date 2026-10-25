# Plan: 32-bit and 64-bit TestStand, as two plugins

**Created**: 2026-10-25
**Branch**: claude/mcp-installation-workflow-88cdb4
**Status**: approved
**Gherkin persistence**: plan-file-only
**Spec**: [docs/specs/dual-bitness-plugins.md](../docs/specs/dual-bitness-plugins.md)
**Builds on**: [plugin-install-and-release-workflow.md](plugin-install-and-release-workflow.md) (slices 0–10 built, PR A not pushed)

## Goal

Ship the server as two plugins from one codebase and one tag: `teststand-mcp` (x86, 32-bit
TestStand) and `teststand-mcp-x64` (x64, 64-bit TestStand). Each drives only the TestStand of its
own bitness, including the helper tools it starts. The full integration suite passes on both
bitnesses on this station before PR A is pushed.

**Delivery.** This work joins PR A on the same branch; PR A is pushed only after slice 10 is green
on both bitnesses and slice 12's fork rehearsal passes. PR B (README) stays after the first real
release, now with two install commands.

**Already done (spike, 2026-10-25):** the unchanged code builds and runs for x64 (connect, Single
Pass `Passed`, diff, analyzer against 64-bit TestStand). After the analyzer template fix
(`cab876a`) the full integration suite is **660/660 on x86** (32-bit engine, 1 min 22 s) and
**660/660 on x64** (`dotnet test -p:Platform=x64 -p:PlatformTarget=x64`, 64-bit engine,
1 min 15 s), with no skips; each log names its engine's version string and root. So slice 10 is
expected to be a re-run after the code changes, not a hunt for failures.

**Decision stances:**

- **Scope:** exactly DB-1 to DB-15. Out of scope as in the spec: a dual-runtime plugin, a
  launcher, ARM64, TestStand in CI.
- **One identity table, no retyped names.** Architecture → plugin name, display name, asset
  prefix, runtime identifier, build platform lives in `build/PluginIdentity.psm1`. Every script
  reads it; nothing else spells `teststand-mcp-x64`. The two exceptions are committed files that
  are not generated, `marketplace.json` and the allow hook's prefix list, and a test pins each
  against the table.
- **x86 stays the default everywhere** (`dotnet build`, `Build-PluginArchive.ps1`, the dev
  restart command), so existing commands and the parent plan's tests keep their meaning.
- **Format fidelity:** as in the parent plan (UTF-8 without BOM through .NET file APIs).

## Acceptance Criteria

- [ ] DB-1 Both platforms build; default x86; PE machine field checked
- [ ] DB-2 `-Architecture x64` packaging; default output unchanged; unknown value fails early
- [ ] DB-3 Identity stamped into `plugin.json`, `hooks.json`, agents; no cross-prefix leaks
- [ ] DB-4 `arch:` in `--version` and `VERSION.txt`; smoke test checks it
- [ ] DB-5 Same-bitness install resolution, unit-tested; Sequence Editor launcher too
- [ ] DB-6 Engine flows plus diff and analyzer from each archive's exe on its own TestStand
- [ ] DB-7 Full integration suite green on x86 and on x64; skips recorded
- [ ] DB-8 Pre-push runs the suite per installed bitness
- [ ] DB-9 Two marketplace entries
- [ ] DB-10 One tag publishes both archives; every release check covers both
- [ ] DB-11 Platform guard per bitness, naming the other plugin when that one fits
- [ ] DB-12 Allow hook for both prefixes; update notice and state folder per plugin
- [ ] DB-13 Both plugins install and update side by side in the fork
- [ ] DB-14 Stats per plugin
- [ ] DB-15 Docs and the rules skill name both bitnesses

## Slices

### Slice 0: Build platform dimension

**Depends-on:** none
**Files:** `TestStandMCP.csproj`, `Test/TestExecution/TestStandMCP.IntegrationTests.csproj`, `TestStandMCP.sln`, `CLAUDE.md`

```gherkin
Feature: One codebase builds for both bitnesses

  Scenario: x64 build
    When the server is built with -p:Platform=x64
    Then TestStandMCP.exe has PE machine 0x8664 and the output is under bin\x64\

  Scenario: Default build
    When the server is built without a Platform
    Then TestStandMCP.exe has PE machine 0x14c, as today

  Scenario: Test project follows
    When the integration test project is built with -p:Platform=x64
    Then the test host and the referenced server are both 64-bit
```

#### Step 0.1: Platform-driven `PlatformTarget`

**Complexity**: standard
**IMPLEMENT**: `Platforms` = `AnyCPU;x86;x64`; `PlatformTarget` = `$(Platform)` when that is x86 or x64, else x86. The same in the test csproj; `ci.yml`'s `-p:PlatformTarget=AnyCPU` engine-free run keeps working. Add x64 solution configurations. `CLAUDE.md`'s restart line gains the x64 command (`-p:Platform=x64`, output `bin\x64\Debug\net8.0-windows\`).
**TEST**: Build both and read the PE machine field (a 5-line PowerShell check in the step evidence; the packaging smoke in slice 4 pins it permanently).
**REFACTOR**: Update the csproj comment that says the engine "is 32-bit".
**Commit**: `build: build the server and its tests for x86 or x64`

### Slice 1: Same-bitness install resolution

**Depends-on:** none
**Files:** `src/Services/TestStandInstallLocator.cs`, `src/Services/SequenceEditorService.cs`, `Test/TestExecution/Tests/T34_InstallLocatorTests.cs`

```gherkin
Feature: Helper tools come from the engine's own install

  Scenario: Connected engine lacks the tool
    Given the connected engine's Bin has no AnalyzerApp.exe
    And the other bitness's install has one
    When the analyzer is resolved
    Then the result is an error naming AnalyzerApp.exe, the process bitness and the probed paths
    And no path under the other bitness's root is returned

  Scenario: Not connected, 64-bit process
    Given an x64 process and TestStand in both Program Files roots
    When FileDiffer.exe is resolved without an engine
    Then only %ProgramW6432% and the 64-bit registry view are probed

  Scenario: Not connected, 32-bit process
    Given an x86 process
    Then only %ProgramFiles(x86)% and the 32-bit registry view are probed

  Scenario: Explicit override
    Given connect_engine was given engine_path pointing at the other bitness's Bin
    When the tool is resolved
    Then that Bin is used
```

#### Step 1.1: Resolve with one bitness

**Complexity**: complex
**IMPLEMENT**: `Resolve` takes the process bitness (`Environment.Is64BitProcess` at the call site, a parameter for tests). When `engineBinDir` is known, it is the only automatic candidate. Otherwise `%TESTSTANDBIN%` only if it lies under the same-bitness root, then the same-bitness registry view, then the same-bitness root scan. `GetProgramFilesRoots` becomes `GetProgramFilesRoot(bool is64)`; the `SequenceEditorService` scan uses it. Before editing, grep every caller of `Resolve`, `GetProgramFilesRoots` and `FindBinFromRegistry`. Check whether `T34` already injects the file system; if not, inject a `Func<string,bool> fileExists` and the roots rather than adding an abstraction layer.
**TEST**: `T34` cases for each scenario, `[Category("EngineFree")]`.
**REFACTOR**: Rewrite the class comment's "trap 2" for both bitnesses.
**Commit**: `fix: resolve TestStand helper tools from the engine's own bitness only`

### Slice 2: Architecture in the version identity

**Depends-on:** none
**Files:** `src/VersionInfo.cs`, `Test/TestExecution/Unit/VersionInfoTests.cs`, `build/PluginStaging.psm1`, `build/PluginSmoke.psm1`, `build/tests/BuildPluginArchive.Tests.ps1`

```gherkin
Feature: An install says which bitness it is

  Scenario: --version names the architecture
    Given an x64 build
    When the user runs the exe with --version
    Then stdout contains "arch: x64"

  Scenario: Smoke test catches a mismatch
    Given an archive staged for x64 whose exe is x86
    When the packaging smoke test runs
    Then it fails naming both architectures
```

#### Step 2.1: `arch` everywhere

**Complexity**: standard
**IMPLEMENT**: `VersionInfo` reports `arch` from `RuntimeInformation.ProcessArchitecture`; staging writes `arch:` into `VERSION.txt`; `PluginSmoke` asserts that `--version`, `VERSION.txt` and the requested architecture agree.
**TEST**: Unit test for the version text; script test for the mismatch.
**Commit**: `feat: report the build architecture in --version and VERSION.txt`

### Slice 3: Identity table and per-plugin stamping

**Depends-on:** none
**Files:** `build/PluginIdentity.psm1`, `build/PluginStaging.psm1`, `build/New-HooksJson.ps1`, `plugin/hooks/allow-readonly.ps1`, `plugin/hooks/hook-common.ps1`, `plugin/hooks/update-notify.ps1`, `build/tests/PluginIdentity.Tests.ps1`, `build/tests/AllowHook.Tests.ps1`, `build/tests/HooksJson.Tests.ps1`, `build/tests/UpdateNotify.Tests.ps1`

```gherkin
Feature: Each plugin carries its own name

  Scenario: x64 staging
    When the plugin is staged for x64
    Then plugin.json is named teststand-mcp-x64 with the 64-bit display name
    And every hooks.json matcher and agent tool reference uses mcp__plugin_teststand-mcp-x64_teststand__
    And no staged file contains mcp__plugin_teststand-mcp_teststand__

  Scenario: x86 staging is unchanged
    When the plugin is staged for x86
    Then the staged tree is identical to today's and contains no teststand-mcp-x64

  Scenario: Allow hook under either plugin
    Given a read-only tool call with either plugin's prefix on a .seq inside the project
    Then the allow hook allows it
    And a mutating tool under either prefix is not allowed

  Scenario: Separate state
    Given both plugins are installed
    Then the update notice of each names its own plugin
    And each keeps its cache in its own state folder
```

#### Step 3.1: Identity table

**Complexity**: standard
**IMPLEMENT**: `Get-PluginIdentity -Architecture` returns name, display name, description, asset prefix, RID, build platform. The committed `plugin.json` stays the x86 identity; staging stamps the x64 values with the parent plan's targeted replacement.
**TEST**: Table contents; an unknown architecture throws with the allowed values.
**Commit**: `build: one identity table for the x86 and x64 plugins`

#### Step 3.2: Stamped hooks, agents and hook scripts

**Complexity**: complex
**IMPLEMENT**: `New-HooksJson` takes the plugin name; staging generates `hooks.json` and the plugin-flavour agents per identity. The allow hook keeps a literal two-entry prefix list. The hooks' state folder derives from the installed `plugin.json` name (x86 keeps `teststand-mcp`, so existing caches stay valid).
**TEST**: The scenarios above, plus a test that the allow hook's prefix list equals the table's two prefixes.
**Commit**: `feat: stamp each plugin's own name into its hooks and agents`

### Slice 4: Packaging per architecture

**Depends-on:** 0, 2, 3
**Files:** `build/PluginArchive.psm1`, `build/Build-PluginArchive.ps1`, `build/PluginStaging.psm1`, `build/tests/BuildPluginArchive.Tests.ps1`

```gherkin
Feature: The packaging script builds either plugin

  Scenario: x64 archive
    When Build-PluginArchive.ps1 runs with -Architecture x64
    Then it writes teststand-mcp-x64.zip, its digest, manifest and teststand-mcp-x64-vX.Y.Z.zip
    And bin holds a win-x64 self-contained folder layout whose exe is 64-bit

  Scenario: Default is x86
    When the script runs without -Architecture
    Then the output names and staged tree are those of today

  Scenario: Unknown architecture
    When the script runs with -Architecture arm64
    Then it fails before any build, naming x86 and x64
```

#### Step 4.1: `-Architecture` through publish, stage, smoke and zip

**Complexity**: complex
**IMPLEMENT**: Replace the `$script:RuntimeIdentifier` / `$script:BuildPlatform` constants and the publish hint with the identity's values. Asset names come from the identity's prefix through `ReleaseAssets` (slice 6 makes that a parameter; this step passes it).
**TEST**: Script tests for both architectures with the publish faked; one real x64 build on the station with `Assert-PluginArchive`.
**Commit**: `build: package the plugin for x86 or x64`

### Slice 5: Platform guard per bitness

**Depends-on:** 3
**Files:** `plugin/hooks/platform-guard.ps1`, `build/tests/PlatformGuard.Tests.ps1`

```gherkin
Feature: The guard checks the TestStand this plugin can drive

  Scenario: Matching install present
    Given the x64 plugin and a 64-bit TestStand
    Then the guard reports no TestStand problem

  Scenario: Only the other bitness installed
    Given the x64 plugin and only a 32-bit TestStand
    Then the guard warns and names teststand-mcp as the plugin to install

  Scenario: No TestStand
    Given no TestStand at all
    Then the guard warns as today
```

#### Step 5.1: Bitness-aware detection

**Complexity**: standard
**IMPLEMENT**: The guard reads its plugin name, maps it to the bitness through the same two-entry table as the allow hook, and checks only that root (`%ProgramFiles(x86)%` or `%ProgramW6432%`). The `TestStand` / `TestStand64` environment variables count for their own bitness only.
**TEST**: The three scenarios with fake roots.
**Commit**: `feat: warn when the TestStand of the plugin's bitness is missing`

### Slice 6: Release assets for both architectures

**Depends-on:** 4
**Files:** `build/ReleaseAssets.psm1`, `build/ReleaseVerdict.psm1`, `build/Assert-PublishedRelease.ps1`, `build/Assert-ReleaseAssetSet.ps1`, `build/New-ReleaseDraft.ps1`, `build/Publish-Release.ps1`, `build/Remove-RehearsalRelease.ps1`, `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `build/tests/ReleaseAssets.Tests.ps1`, `build/tests/ReleaseVerifier.Tests.ps1`, `build/tests/WorkflowContract.ps1`, `build/tests/WorkflowMutations.Tests.ps1`

```gherkin
Feature: One release carries both plugins

  Scenario: A tag publishes both
    When a vX.Y.Z tag is pushed
    Then the package job builds and smoke-tests both archives
    And the draft holds the 8 assets, all uploaded by github-actions[bot]
    And publish happens only after both archives verify

  Scenario: One bad archive stops the release
    Given a dry run with inject_fault corrupt-zip-x64
    Then the verify fails on teststand-mcp-x64.zip and nothing is published

  Scenario: releases/latest serves both
    After publish
    Then latest/download/teststand-mcp.zip and latest/download/teststand-mcp-x64.zip each match their verified hash

  Scenario: PR check
    When ci.yml runs
    Then both architectures are packaged at 0.0.0-ci and verified locally
```

#### Step 6.1: Asset set as the union of both identities

**Complexity**: complex
**IMPLEMENT**: `ReleaseAssets` exposes the asset names per prefix and the full set over both. The verifier, `Assert-ReleaseAssetSet`, the latest check and the rehearsal cleanup iterate the set. Keep the single-release unrolling fix (`,@()`) intact.
**TEST**: Existing suites extended to 8 assets; a missing x64 asset and an x64 hash mismatch each fail with the asset named.
**Commit**: `build: verify and publish both plugins' assets as one release`

#### Step 6.2: Workflows

**Complexity**: standard
**IMPLEMENT**: The package job loops over both architectures into one `release-assets` artifact; `inject_fault` gains `corrupt-zip-x64`; the latest step compares both. `ci.yml` packages both.
**TEST**: `WorkflowContract` / `WorkflowMutations` updated; live proof is slice 12.
**Commit**: `ci: build and release the x86 and x64 plugins from one tag`

### Slice 7: Marketplace

**Depends-on:** 3
**Files:** `.claude-plugin/marketplace.json`, `build/tests/PluginIdentity.Tests.ps1`

```gherkin
Feature: Both plugins are listed

  Scenario: Marketplace entries
    Then marketplace.json lists teststand-mcp and teststand-mcp-x64
    And each source URL ends in releases/latest/download/<its prefix>.zip
    And each description names the TestStand bitness it needs
```

#### Step 7.1: Second entry

**Complexity**: trivial
**IMPLEMENT**: Add the x64 entry; reword both descriptions ("for 32-bit TestStand", "for 64-bit TestStand").
**TEST**: A test pins both entries against the identity table.
**Commit**: `feat: list the 64-bit plugin in the marketplace`

### Slice 8: Stats per plugin

**Depends-on:** 6
**Files:** `.github/stats/*.py`, `.github/stats/tests/*`, `.github/site/build.py`

```gherkin
Feature: Downloads per plugin

  Scenario: Both prefixes counted
    Given a release with both archives downloaded
    Then downloads.csv has rows for both prefixes' assets
    And the site shows one series per plugin
    And each release run's own downloads are subtracted per asset
```

#### Step 8.1: Prefix-aware collector and site

**Complexity**: standard
**IMPLEMENT**: Read the collector's asset filter first; count both prefixes, keyed by plugin. The site gets a per-plugin series with the existing accessible table.
**TEST**: Python tests with a two-prefix fixture.
**Commit**: `feat: chart downloads per plugin`

### Slice 9: Pre-push per bitness

**Depends-on:** 0
**Files:** `.githooks/run-tests.ps1`, `.githooks/README.md`

```gherkin
Feature: The push gate tests every installed bitness

  Scenario: Both installed
    Given 32-bit and 64-bit TestStand are installed
    When the pre-push hook runs
    Then the suite runs as x86, then as x64, each announced
    And the push continues only if both pass

  Scenario: One installed
    Given only 64-bit TestStand
    Then only the x64 run happens, and the hook says x86 was skipped because 32-bit TestStand is absent

  Scenario: None installed
    Then the push is blocked with the reason
```

#### Step 9.1: Loop over installed bitnesses

**Complexity**: standard
**IMPLEMENT**: Detect each root's `National Instruments\TestStand*\Bin\teapi.dll`; run `dotnet test ... -p:Platform=<arch>` per hit, stopping processes before each run as today.
**TEST**: Run on this station (both installed); the one-installed branch by temporarily pointing detection at a fake root through a parameter.
**Commit**: `test: run the pre-push suite once per installed TestStand bitness`

### Slice 10: Both bitnesses green on the station

**Depends-on:** 0, 1, 9
**Files:** `docs/validation/dual-bitness.md`, any test that needs a bitness-specific skip

```gherkin
Feature: Proven on both engines before merge

  Scenario: Full suite
    When the pre-push hook runs on this station
    Then both runs pass, and any skip names its bitness and reason

  Scenario: Installed archives
    Given the x86 and x64 archives built by slice 4
    When each exe runs the four engine flows plus diff and analyzer
    Then each passes against its own TestStand, and the diagnostic log shows helper tools from that install
```

#### Step 10.1: Run, fix, record

**Complexity**: complex
**IMPLEMENT**: Run the hook. A failure caused by the server gets a fix in its own commit; one caused by a missing 64-bit test asset gets a skip with a reason. Record results, timings and skips.
**TEST**: The runs themselves.
**Commit**: `docs: record the dual-bitness validation`

### Slice 11: Documentation

**Depends-on:** 6, 7
**Files:** `CONTRIBUTING.md`, `plugin/skills/teststand-rules/references/headless-and-tools.md`, `CLAUDE.md` (developer note), `README.md` (PR B only)

#### Step 11.1: Both bitnesses in the docs

**Complexity**: trivial
**IMPLEMENT**: CONTRIBUTING: both architectures in releasing, rollback, local packaging. Rules skill: one bullet that code modules must match the engine's bitness and that each plugin drives only its own. `CLAUDE.md` developer note: the spike evidence and the analyzer `buildversion` measurement. `Test-SkillSources.ps1` must stay green. The PR B README text gets two install commands and the "which one" rule.
**TEST**: `Invoke-ScriptTests.ps1` (the CONTRIBUTING command check).
**Commit**: `docs: describe the x86 and x64 plugins`

### Slice 12: Fork rehearsal, round 3

**Depends-on:** 6, 7, 8, 11
**Files:** `docs/validation/fork-rehearsal.md`

```gherkin
Feature: The two-plugin release works end to end before PR A

  Scenario: Release and install
    Given the branch merged into the fork branch with the slug switched
    When ci, a dry run, a corrupt-zip-x64 dry run and two tag releases run
    Then each behaves as DB-10 states
    And both plugins install side by side in an isolated profile and connect
    And each updates from the first release to the second
```

#### Step 12.1: Rehearse and record

**Complexity**: standard
**IMPLEMENT**: As rounds 1 and 2 (`--no-verify` for fork pushes only, the `gh` credential helper, a short `CLAUDE_CONFIG_DIR`).
**TEST**: The runs; record run ids.
**Commit**: `docs: record the two-plugin fork rehearsal`

## Parallelization

- Wave 1: slices 0, 1, 2, 3 (disjoint files)
- Wave 2: slices 4, 5, 7, 9
- Wave 3: slices 6, 10
- Wave 4: slices 8, 11
- Wave 5: slice 12

Run sequentially by default. Slices 1 and 10 need the station's engines, and the integration
suite never runs in a subagent.

## Pre-PR quality gate

- [x] Script suites, Python stats tests, engine-free tests: green (18 script runs under 5.1, 131 stats, 68 engine-free; PowerShell 7 is not installed on this station, so its runs are left to CI)
- [x] Pre-push hook: suite green on x86 and x64 (663/663 each)
- [x] `/code-review` backstop on the dual-bitness diff: 6 findings; two fixed (`fix: name the failing plugin and refuse one hash for two plugins`), the rest skipped as edge cases or cleanup, one recorded below
- [x] Fork rehearsal round 3 recorded
- [x] PR A body updated: two plugins, both bitnesses tested

## Follow-ups

- `launch_sequence_editor` resolves `SeqEdit.exe` without the connected engine's `Bin` or the `engine_path` override (`SequenceEditorService.FindSeqEditPath`). It predates this plan; with two installs of one bitness, the editor can differ from the engine.
