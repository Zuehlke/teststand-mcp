# Git hooks

Versioned Git hooks for TestStandMCP.

## What it does

`pre-push` runs the integration test suite (`Test/TestExecution`) before every
push and **rejects the push unless all tests pass**. It runs the suite once per
installed TestStand bitness: as x86 when 32-bit TestStand is installed, then as
x64 (`-p:Platform=x64`) when 64-bit TestStand is. A missing bitness is skipped
with a note; a station with no TestStand at all cannot push. Both runs together
take about twice as long as one.

Before the integration tests it runs two script checks that would otherwise
fail only in CI (about a minute together): `plugin/hooks/hooks.json` must match
what `build/New-HooksJson.ps1` generates, and `build/tests/BuildPluginArchive.Tests.ps1`
must pass (it holds the drift test between the csproj output items and the
staging table in `build/PluginStaging.psm1`). The full script suite
(`build/Invoke-ScriptTests.ps1`, about 5 minutes per PowerShell host) stays in CI.

The script checks read the working tree, not the pushed commit. They run before
anything is stopped, so a failing check leaves your Sequence Editors open.

Because the integration tests need exclusive access to the TestStand COM engine,
the hook then **terminates any running `TestStandMCP.exe` / `SeqEdit.exe`**, so
save your work in open Sequence Editors before pushing.

## Activation (per clone)

This is **automatic**: on the first `dotnet build` / `dotnet test`,
`Directory.Build.targets` (repo root) runs `git config core.hooksPath .githooks`
once per clone (tracked by a marker in `.git/`). Since you always build before
pushing, the hook is active by the time it matters — no manual step needed.

Set it manually only if you want the hook active *before* the first build, or if
`git` wasn't on PATH during that build:

```powershell
git config core.hooksPath .githooks
```

Verify:

```powershell
git config core.hooksPath   # -> .githooks
```

## Files

| File            | Role                                                        |
| --------------- | ---------------------------------------------------------- |
| `pre-push`      | POSIX-sh entry point Git invokes; delegates to PowerShell. |
| `run-tests.ps1` | Runs the two script checks, stops engine processes, runs `dotnet test` per installed bitness, returns the result. `-DryRun` prints the runs it would make. |

The `pre-push` stub is forced to LF line endings via `.gitattributes`; without
that, `sh.exe` on Windows would fail with `bad interpreter: /bin/sh^M`.

## Bypassing (emergencies only)

```powershell
git push --no-verify
```
