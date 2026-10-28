# Contributing

This page is for maintainers. It shows how to cut a release, roll one back, rehearse the release path,
reproduce the checks on your machine and set the repository up. You do not need to read the build scripts
to do any of it.

The scripts run on Windows. They work under Windows PowerShell 5.1 (`powershell.exe`) and PowerShell 7
(`pwsh`).

## Cutting a release

Push a tag `vX.Y.Z` and the release workflow (`.github/workflows/release.yml`) takes it from there. You
never edit a version number: the tag is the version.

```powershell
git switch main
git pull
git tag v1.9.1
git push origin v1.9.1
```

Before you push, you can ask the tag gate for its verdict. It is the same script the workflow runs:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Assert-ReleaseTag.ps1 -Tag v1.9.1
```

Two things to know about the push itself:

- The `pre-push` hook runs on a tag push too. It stops every `TestStandMCP.exe` and `SeqEdit.exe` and runs
  the integration suite, so save open Sequence Editor work first. See [.githooks/README.md](.githooks/README.md).
  On the real repository, let the hook run.
- Every tag has its own run, and a running release is never cancelled. Two tags pushed close together may
  overlap. Before it publishes, the workflow compares the version with the current latest release, and a
  lower version is published with `make_latest=false`, so a tag pushed out of order does not take "latest"
  from a newer one. The check is not atomic: two runs that publish at the same moment can still end on the
  older one, so push release tags one at a time. The log of the publish step says which value it chose.

What the workflow does, in order:

1. Job `package` (read-only token): the tag gate, then `build\Build-PluginArchive.ps1` once per architecture
   (x86, then x64), each of which builds the exe, stages the plugin, smoke-tests it and zips it. Both plugins'
   assets are kept as one workflow artifact. If this job fails, nothing is created, not even a draft.
2. Job `release` (write token): checks that the artifact holds exactly each plugin's four assets, deletes every
   leftover DRAFT of the same tag (from an earlier failed run), and creates a DRAFT release with them
   (`build\New-ReleaseDraft.ps1`). A published release that already carries the tag stops the run.
3. Verifies the draft through the API, downloading the assets the way a user would
   (`build\Assert-PublishedRelease.ps1`).
4. Re-checks that the draft still holds exactly the verified asset ids (`build\Assert-ReleaseAssetSet.ps1`).
5. Publishes by the release id the verifier returned, never by tag name (`build\Publish-Release.ps1`), and marks
   it the latest release unless a higher version already is.
6. Re-checks the asset ids of the live release, then fetches `releases/latest` like a user and compares it
   with the verified archive. That last comparison is skipped for a release that was published with
   `make_latest=false` (a lower version than the latest release): `releases/latest` rightly serves another
   release then.

A failed verification leaves a draft. `releases/latest` never moves until the whole check has passed. To run it
again, use Re-run failed jobs on the run: the leftover draft of the tag is deleted first, so the second run
does not collide with the first. If a PUBLISHED release already carries the tag, the run stops with:

> A published release of <...> already carries the tag <...> (id <...>).

That tag is spent. Fix forward with the next patch version.

One tag releases both plugins: `teststand-mcp` for 32-bit TestStand and `teststand-mcp-x64` for 64-bit
TestStand (the names come from `build\PluginIdentity.psm1`). Each plugin has four assets, eight in all.
The x86 plugin's are `teststand-mcp.zip` (the file users get from `releases/latest/download/`),
`teststand-mcp-vX.Y.Z.zip` (the same bytes), `teststand-mcp.sha256` and `teststand-mcp.manifest.sha256`.
The x64 plugin's are the same names with `teststand-mcp-x64` in place of `teststand-mcp`. A missing or
damaged asset of either plugin stops the whole release.

### A tag is rejected

A tag must be exactly `vX.Y.Z`: a lower-case `v` and three numbers without leading zeros. `V1.9.0`, `v1.9`
and `v1.9.0-rc1` are rejected, because the release workflow, the asset name `teststand-mcp-vX.Y.Z.zip` and
the version stamped into the plugin all derive from the tag. The workflow starts on a tag of any name on
purpose, so a malformed tag fails at the gate with the commands to fix it. A filter such as `v*` would
ignore it silently. Nothing is built or published for a rejected tag.

The output has three parts, What, Why and Fix. The Fix retags in a safe order: delete the remote tag, delete
the local tag, create the new tag, push it. Its shape:

> The tag '<...>' cannot be released (<...>).

> Retag in this order (delete the remote tag, delete the local tag, create the new tag, push it). These are PowerShell commands:
>
> 1. git push origin --delete refs/tags/<...>    # only if the bad tag was pushed
> 2. git tag -d -- <...>
> 3. git tag <...> <...>
> 4. git push origin <...>

Run locally without `-Commit`, the gate prints one extra first step and writes `<commit>` where the SHA goes
in step 3. The extra step is `git rev-list -n 1 refs/tags/<tag>`, which prints the commit the bad tag points
at. In CI the SHA is filled in.

For `V1.9.0` it names `v1.9.0` itself. For a tag it cannot repair, such as `v1.9.0-rc1` or `v1.9`, it
does not guess a version:

> Choose the version you mean and write it as vX.Y.Z; this script does not guess one.

Then pick the version you mean and use the same four commands with it.

### A tag is not on main

Only a commit on `main` is released. The gate also checks that the tagged commit is an ancestor of
`refs/remotes/origin/main` and stops the run otherwise, so a tag on a branch that was never merged builds and publishes
nothing:

> The tag <...> points at <...>, which is not on <...>.

Merge the change into `main`, then move the tag to the merged commit with the same four commands. A dry run
skips this check: it may run from any branch and publishes nothing. To check locally before you push, add
`-MainRef refs/remotes/origin/main -Commit <sha>` to the gate command above (after a `git fetch`).

### A hand-uploaded asset fails verification

The verifier checks the uploader of EVERY asset on the release. Each one must have been uploaded by
`github-actions[bot]`, the workflow token. An asset you attach by hand, a replaced zip or an extra file
fails the check, because nothing proves what is in it. Never attach assets by hand. If you must change what
a release holds, cut a new release.

## Rolling back a bad release

If the verification failed, there is nothing to roll back: only a draft exists. A re-run deletes it itself
(Re-run failed jobs). To get rid of it without a re-run, delete the draft (Releases page, or
`gh release delete vX.Y.Z`).

A release carries both plugins, so a rollback always moves both: a bad x64 archive is rolled back
by the same delete or fix-forward as a bad x86 one.

If a bad release went live, there are two ways out. The scripts say the same:

> so releases/latest falls back to the previous release (safe only once an earlier workflow-built release exists), or fix forward by releasing the next patch version (see CONTRIBUTING).

- **Delete the release, only when an earlier release was built and verified by this workflow.**
  Otherwise `releases/latest` falls back to files nobody verified. With such a release, `releases/latest`
  falls back to the newest earlier one. Delete it on the Releases page or with `gh release delete vX.Y.Z`. The git
  tag stays unless you also delete it (`--cleanup-tag`). Do not reuse the tag. `v1.9.0` is the first
  workflow-built release in this repository, so it has no such release to fall back to.
- **Fix forward.** Fix the cause on `main` and push `vX.Y.(Z+1)`. This is the only way out for `v1.9.0`:
  fix it with `v1.9.1`. From the second workflow-built release on, deleting is available too.

## Rehearsing a release

A manual run of the release workflow is a DRY RUN. It packages the plugin, creates a draft under the
rehearsal tag `vX.Y.Z-rehearsal`, verifies it through the API, then deletes the draft it created (by its id,
so a draft of another run is never touched) and the rehearsal tag. It never publishes: a manual run with
`dry_run` unticked is refused before anything is built, and a ticked one deletes its draft.

```powershell
gh workflow run release.yml --ref main -f tag=v1.9.2-rehearsal -f dry_run=true -f inject_fault=none
```

This command works as written (measured in the fork rehearsal with `v1.9.0-rehearsal`).

To prove that a bad draft is never published, corrupt the uploaded zip. The run must FAIL at the
verification, publish nothing and still clean up:

```powershell
gh workflow run release.yml --ref main -f tag=v1.9.2-rehearsal -f dry_run=true -f inject_fault=corrupt-zip
```

In the rehearsal of 2026-10-25 this failed at the listing check (the versioned archive was 76 MB, the plugin
archive 15 bytes) and the draft was deleted.

A tag releases both plugins, the x86 `teststand-mcp` and the x64 `teststand-mcp-x64`. `inject_fault=corrupt-zip-x64`
corrupts the x64 zip instead, and must fail the same way.

### Full end-to-end test in a fork

A dry run cannot publish, so it cannot test the real release, the `releases/latest` check or an install.
For that, use a throw-away PUBLIC fork. Do not do it in the real repository: a bad release there is live.

1. Fork the repository and enable Actions in the fork.
2. Make a fork-only commit that is NEVER merged. It points three things at the fork: every
   `Zuehlke/teststand-mcp` URL in `.claude-plugin/marketplace.json` (the `source.url` is the one that
   matters: it is where the plugin is downloaded) and in `plugin/.claude-plugin/plugin.json`, and
   `$ReleaseRepoSlug` in `build\ReleaseAssets.psm1`. It also changes the tests that pin the slug; find them
   with `git grep -l Zuehlke/teststand-mcp -- build/tests`.
3. Keep the fork-only commit on its own branch and push it to the fork only, never to `origin`. A bare
   `git push --no-verify` goes to `origin`, which is the real repository in a normal clone, so name the
   fork remote:

   ```powershell
   git remote add fork https://github.com/<you>/teststand-mcp.git
   git remote -v
   git push --no-verify fork <branch>:main
   git push --no-verify fork refs/tags/vX.Y.Z
   ```

   Check that `git remote -v` shows `fork` pointing at your fork. The pre-push hook would stop
   `TestStandMCP.exe` and run the integration suite, which a fork push does not need. Use `--no-verify`
   ONLY for the fork, never for the real repository.
4. Run the dry runs above, then push the tags `v1.9.0` and `v1.9.1` to the fork the same way and check each
   run.
5. Install from the fork in an isolated profile (`CLAUDE_CONFIG_DIR` set to a fresh, SHORT folder, see
   Troubleshooting) so your own Claude Code setup stays untouched.

The full record of such a run, including the bug it found, is in
[docs/validation/fork-rehearsal.md](docs/validation/fork-rehearsal.md).

## Local work

Reproduce the pull-request check on your machine. Run from the repository root.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Invoke-ScriptTests.ps1
```

| Script | Parameters | What it does |
|---|---|---|
| `build\Invoke-ScriptTests.ps1` | `-Only`, `-List`, `-PwshPath`, `-TimeoutSeconds`, `-TotalSeconds` | Runs every `build\tests\*.Tests.ps1` and `build\Test-SkillSources.ps1`, once per PowerShell host. `-Only` runs the named scripts, `-List` prints what would run, `-PwshPath` names the PowerShell 7 executable, `-TimeoutSeconds` limits one script under one host (default 1200). `-TotalSeconds` is a budget for the whole run (default 0, none): no script starts once less than `-TimeoutSeconds` of it is left, and the scripts left out count as failed. A script that prints no `PASS` line ran no case and fails too. |
| `build\Build-PluginArchive.ps1` | `-Version`, `-Tag`, `-Commit`, `-OutDir`, `-Layout`, `-Architecture` | Builds the plugin archive. `-Version` is required: `X.Y.Z`, or `0.0.0-ci` for a local build. `-Tag` defaults to `v<Version>`, `-Commit` to `git rev-parse HEAD`, `-OutDir` to `artifacts` (git ignores it). `-Layout` is `folder` (default), `single-file` or `framework-dependent`. `-Architecture` is `x86` (default, the `teststand-mcp` plugin) or `x64` (`teststand-mcp-x64`). |

A local build, the same way the pull-request check does it:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Build-PluginArchive.ps1 -Version 0.0.0-ci -OutDir artifacts\local
```

The x64 plugin, into its own folder (as the workflows do it, one folder per architecture):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Build-PluginArchive.ps1 -Version 0.0.0-ci -Architecture x64 -OutDir artifacts\local-x64
```

Use a clean `-OutDir`: assets of another version in it are not removed.

The pull-request check (`.github/workflows/ci.yml`) also runs the engine-free unit tests and the Python
tests of the download statistics:

```powershell
dotnet test Test/TestExecution/TestStandMCP.IntegrationTests.csproj --filter "Category=EngineFree" -p:PlatformTarget=AnyCPU --nologo
python -m unittest discover -s .github/stats
```

Use `py -m unittest ...` if `python` is not on your PATH.

The PowerShell 7 half needs `pwsh`; without it the local run SKIPs, and CI fails
(`TESTSTAND_MCP_REQUIRE_PWSH=1`).

The one real end-to-end packaging run per layout is off by default, because it publishes the real exe and is
slow. Switch it on with `TESTSTAND_MCP_PACKAGE_E2E=1`:

```powershell
$env:TESTSTAND_MCP_PACKAGE_E2E = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\BuildPluginArchive.Tests.ps1
```

## One-time setup

Done once by a repository admin:

1. **Enable Actions** for the repository (Settings, Actions, General). Nothing here runs without it.
2. **Set Pages to the "GitHub Actions" source** (Settings, Pages, Build and deployment, Source). The
   statistics workflow `.github/workflows/pages.yml` has a `deploy` job that publishes the page. Without
   this setting only that job fails. The download counts are already committed to the `stats` branch by
   then, and `ci.yml` and `release.yml` are not affected.
3. **Optional: the `STATS_TRAFFIC_TOKEN` secret** (Settings, Secrets and variables, Actions). The workflow
   token cannot read repository traffic, so the views and clones need a token that can. Without the secret
   the statistics skip traffic, and the page says so. Make it a fine-grained personal access token for this
   ONE repository, with the single repository permission "Administration: Read-only" (that is what the
   traffic endpoints ask for) and an expiry date; renew it before it expires, or the traffic part stops
   until you do. A classic `repo` token is not wanted: it can write to every repository you can.

## Troubleshooting

Every failure of a build script has three parts: What went wrong, Why it matters, and the Fix. The Why also
says whether running it again can help:

> Running the verifier again will not help
>
> Running the verifier again may help

The messages below are quoted from the scripts. `<...>` stands for a part that changes from run to run.
`build\tests\Contributing.Tests.ps1` fails when a quoted message, script or parameter no longer exists, so
this list cannot drift from the code unnoticed.

### The release workflow

**It stopped at the tag gate.** See [A tag is rejected](#a-tag-is-rejected). A manual run says:

> A release is cut by pushing a tag vX.Y.Z. A manual run is only a dry run: tick dry_run.
>
> A dry run needs a rehearsal tag

A dry run needs the tag `vX.Y.Z-rehearsal`.

**The verification refused the draft.** Every verification failure starts with:

> <...> must not go live: <...>.

The part after it names the fault. The common ones:

> <...> is missing from the release
>
> <...> was uploaded by '<...>', not by <...>
>
> <...> is <...> bytes but the plugin archive is <...> bytes
>
> the plugin archive cannot be read as a zip file: <...>
>
> archive entry '<...>' differs from the manifest

A missing or hand-uploaded asset, a versioned archive of another size, a corrupt zip or an entry that
differs from the manifest. None of them heals by itself. Fix the cause, then run again: Re-run failed jobs
deletes the leftover draft of the tag first. If the cause is in the code, push the next patch version
instead.

**Two releases carry one tag.** A stale draft is still there:

> <...> releases of <...> carry the tag <...> (ids: <...>).
>
> No release tagged <...> was found in <...>.

Delete the stale draft so exactly one release carries the tag, but first confirm it is still a draft:
`gh release view vX.Y.Z --json isDraft`. A published release with the same tag is the live rollback case
(see [Rolling back a bad release](#rolling-back-a-bad-release)), not a stale draft. The second message
means the tag is spelled differently from the release, or the draft was deleted.

**A download failed.** This is a network or HTTP error, not a fault in the release:

> <...> could not be downloaded for verification after <...> attempts: could not download <...>: <...>

Re-run failed jobs.

**The draft changed after it was verified.** Someone added, replaced or removed an asset:

> <...> does not match what was verified (<...>): <...>.
>
> and start a new release run: the verified assets are no longer what the release holds.

If the message says the release is live, it is the rollback case: see
[Rolling back a bad release](#rolling-back-a-bad-release).

**The latest check failed after publishing.** `releases/latest` can serve the previous release for a short
while. The check tries up to six times, 15 seconds apart, and only then fails:

> the plugin archive users download from releases/latest (hash <...>) is not the archive of the release that was just verified (hash <...>)
>
> the plugin archive users download from releases/latest (hash <...>) does not match the published archive digest (<...>)

If it keeps failing, open the Releases page and check which release is marked Latest. A mismatch with the
published digest is the rollback case.

### The local scripts

> <...> script test run(s) failed: <...>.
>
> <...> was not found (the pull-request check requires it: <...>=1)

The first names the script and the host that failed; run that script by hand under that host. The second is
a missing PowerShell 7: install it, or pass `-PwshPath` to `build\Invoke-ScriptTests.ps1`.

> The version '<...>' is not X.Y.Z (or X.Y.Z-suffix).
>
> The commit '<...>' is not a full 40-character SHA.
>
> <...> is a junction or symbolic link inside <...>.

For the packaging script: pass `-Version 1.9.1` without a leading `v`, or `-Version 0.0.0-ci` for a local
build; pass the output of `git rev-parse HEAD` as `-Commit`; and delete a junction or link from the tree you
package, because it would pull in files nobody reviewed.

### Installing the plugin from a very long path

`claude plugin marketplace add` can fail with git's own message `fatal: '$GIT_DIR' too big` (it comes from
git, not from the scripts, so it is inline code and not one of the checked quotes above) when
`CLAUDE_CONFIG_DIR` (or the home folder) is a very long path. The same clone works from a short path. Point `CLAUDE_CONFIG_DIR` at a
short folder such as `C:\cc` when you test an install. Seen in the fork rehearsal.

## Design record

The layout decision (why the exe ships self-contained next to the plugin) and the measurements behind the
release path are in [docs/validation/](docs/validation/): `self-contained-x86.md`,
`claude-code-plugin-facts.md` and `fork-rehearsal.md`.
