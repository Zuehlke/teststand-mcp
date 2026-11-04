# Plan: release-please cuts the releases

**Created**: 2026-11-04
**Branch**: feature/release-please
**Status**: slices 1 to 6 built (2026-11-04), slice 0 step 2 and slice 7 open
**Builds on**: [plugin-install-and-release-workflow.md](plugin-install-and-release-workflow.md), [dual-bitness-plugins.md](dual-bitness-plugins.md)

## Goal

Nobody creates a tag or a release by hand anymore. Merging the release PR that release-please keeps open
is the release. `release.yml` still builds, verifies and publishes both plugins, so the zip downloads and
the marketplace URLs (`releases/latest/download/teststand-mcp.zip`, `...-x64.zip`) keep working unchanged.

**Why:** run 41810103378. A release `v1.9.2` was published by hand in the web UI with no assets. It took
`releases/latest`, so every marketplace install returned 404, and `release.yml` correctly refused to
overwrite a published release. Earlier tags `V1.7.x`/`V1.8.0` show the same manual step going wrong.

## Decision stances

- **release-please decides the version and writes the changelog; `release.yml` stays the only thing that
  publishes.** The draft, verify, publish chain and its guards are unchanged.
- **The version still comes from the tag only.** `TestStandMCP.csproj` (`0.0.0-dev`) and `plugin.json`
  (`0.0.0`) are not touched; release-please tracks the version in `.release-please-manifest.json`.
- **release-please creates a DRAFT with its tag** (`draft: true`, `force-tag-creation: true`, documented in
  `docs/manifest-releaser.md` of release-please). `releases/latest` never points at an empty release.
- **Chained, not triggered.** The release-please workflow calls `release.yml` through `workflow_call` after
  it has created the draft. A tag created with `GITHUB_TOKEN` does not start `on: push: tags`, so there is
  exactly one release run and no race between release-please's draft and ours.
- **The manual tag push stays** as a fallback path, unchanged.
- **Conventional PR titles** (`feat:`, `fix:`, `chore:`, ...). Squash merge only, so the PR title is the
  commit release-please reads.

## Slices

### 0. Repository settings (UI, maintainer)

See "UI instructions" below. Required before slice 1 is merged, so the first non-conventional squash
commit cannot land after release-please starts counting.

### 1. PR-title check

- Its own workflow `pr-title.yml` (as built: inside `ci.yml` a title edit would re-run the 30-minute
  Windows job), job `pr-title`, `if: github.event_name == 'pull_request'`, using
  `amannn/action-semantic-pull-request` pinned by SHA like every other action here.
- `types: [opened, edited, synchronize, reopened]` on `pull_request`, so a title edit re-runs it, plus
  `workflow_dispatch` for the release PR. `ci.yml` gets `workflow_dispatch` too.
- The job needs `pull-requests: read` only.
- A skipped job reports success, so a run without a PR (push, dispatch) does not block a required check.
- Update `build/tests/Workflows.Tests.ps1` / `WorkflowMutations.Tests.ps1` for the new job.

### 2. release-please config

- `release-please-config.json`: one package `"."`, `release-type: simple`, `include-component-in-tag: false`
  (tag `v1.9.3`, matches `build/Assert-ReleaseTag.ps1`), `draft: true`, `force-tag-creation: true`,
  `bootstrap-sha: 7fb2a2ec47ec42258ce574ebf08a28238187f1e3` (the `v1.9.2` commit).
- `.release-please-manifest.json`: `{ ".": "1.9.2" }`.
- `release-type: simple` also maintains `version.txt` and `CHANGELOG.md`. Nothing reads `version.txt`;
  accept it rather than adding a custom strategy.

### 3. release-please workflow

New `.github/workflows/release-please.yml` on `push: branches: [main]`:

- Job `release-please`: `googleapis/release-please-action` (pinned SHA), `permissions: contents: write,
  pull-requests: write`. Outputs `release_created` and `tag_name`.
- Job `release`: `needs: release-please`, `if: needs.release-please.outputs.release_created == 'true'`,
  `uses: ./.github/workflows/release.yml` with `tag: ${{ needs.release-please.outputs.tag_name }}`,
  `permissions: contents: write`.
- Job `ci-on-release-pr`: when release-please opened or updated its PR, `gh workflow run ci.yml --ref
  <release branch>` (`actions: write`). A PR opened with `GITHUB_TOKEN` starts no `pull_request` run, so
  without this the release PR never gets its required checks. `workflow_dispatch` is the documented exception.

### 4. `release.yml` accepts the call

- Add `on: workflow_call: inputs: tag` next to `push: tags` and `workflow_dispatch`.
- `TAG` reads the call input first: `inputs.tag || github.ref_name` (both `workflow_dispatch` and
  `workflow_call` fill `inputs`). Under `workflow_call` `github.event_name` is the CALLER's (`push`), so
  `DRY_RUN` stays false and `EVENT_NAME` stays `push`: check that the gate and its tests accept that.
- `concurrency.group` keys on the tag, not `github.ref`, because the call runs with `refs/heads/main`.
  The group must stay per tag.
- The tag gate's "commit on main" check is unchanged: the tag points at the merge commit on `main`.
- Update `Workflows.Tests.ps1` / `WorkflowMutations.Tests.ps1` for the new trigger, `TAG` and concurrency.

### 5. Keep release-please's notes, recover from a hand-made empty release

In `build/ReleaseDraft.psm1`:

- `Clear-StaleDrafts` returns the body of the draft it deletes. `New-ReleaseDraft` uses it as the notes of
  its own draft, followed by the fixed `$ReleaseNotes` line; with no earlier draft, only the fixed line.
- A PUBLISHED release of the tag with ZERO assets is removed like a stale draft (and its body kept).
  A published release with any asset still stops the run with the current message. This is the run
  41810103378 case: it would have recovered in minutes.
- Tests in `build/tests/ReleaseDraft.Tests.ps1` for: body carried over, empty published release removed,
  published release with one asset still refused.

### 6. Docs

- `CONTRIBUTING.md` "Cutting a release": merge the release PR; the manual tag push is the fallback; never
  create a release in the web UI. Update `build/tests/Contributing.Tests.ps1` where it pins wording.
- The comment header of `release.yml` gains the `workflow_call` path.

### 7. Rehearsal and first release

1. PR with slices 1 to 6; CI green on Windows PowerShell 5.1 and 7.
2. Before merging: a dry run of `release.yml` from the branch with `v1.9.3-rehearsal`. It proves the
   changed `release.yml` still packages, verifies and cleans up. It does not exercise `workflow_call`.
3. Merge (squash, title `ci: release with release-please`). release-please opens `chore(main): release X`.
   Its version depends on the `feat:`/`fix:` commits since `bootstrap-sha`; a `ci:` commit alone bumps
   nothing, so the first release PR may only appear with the next `fix:`.
4. Merge the release PR. Watch: draft with tag created, `release.yml` called, assets verified, published as
   latest, `curl -IL .../releases/latest/download/teststand-mcp.zip` returns 200.
5. After the first good release PR, remove `bootstrap-sha` (it is ignored afterwards anyway).

## Local verification on macOS

- `actionlint` on all workflows.
- `pwsh -NoProfile -File build/tests/<name>.Tests.ps1` per touched suite. `build/Invoke-ScriptTests.ps1`
  itself does not run on macOS (`$env:SystemRoot` is unset), and several suites have macOS-only failures
  (Windows paths, `powershell.exe`); baseline on 2026-11-04: `Workflows`, `WorkflowMutations`,
  `Contributing`, `ReleaseVerifier`, `ReleaseAssets` all pass; `ReleaseDraft` has 2 macOS-only failures
  (the tests match asset paths with a literal backslash).
- Windows PowerShell 5.1 is only checked by `ci.yml`.

## Risks

| Risk | Mitigation |
|---|---|
| The release PR's CI started by `workflow_dispatch` does not satisfy the required check on the PR | Test on the first release PR. Fallback: a GitHub App token for release-please; then drop the `workflow_call` job and let the tag push trigger `release.yml` (one trigger only, never both). |
| `force-tag-creation` creates the tag but the draft creation fails | `release.yml` is not called (`release_created` false); re-run the release-please job. Nothing is published. |
| A non-conventional squash title slips in | It is only missing from the changelog and the version bump; the next `fix:` releases it. The PR-title check makes this rare. |
| Someone still publishes in the web UI | Slice 5 recovers an empty one; a published release WITH assets is refused as today. |
