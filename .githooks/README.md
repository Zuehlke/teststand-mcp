# Git hooks

Versioned Git hooks for TestStandMCP.

## What it does

`pre-push` runs the integration test suite (`Test/TestExecution`) before every
push and **rejects the push unless all tests pass**.

Because the tests need exclusive access to the TestStand COM engine, the hook
first **terminates any running `TestStandMCP.exe` / `SeqEdit.exe`** — so save
your work in open Sequence Editors before pushing.

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
| `run-tests.ps1` | Stops engine processes, runs `dotnet test`, returns the result. |

The `pre-push` stub is forced to LF line endings via `.gitattributes`; without
that, `sh.exe` on Windows would fail with `bad interpreter: /bin/sh^M`.

## Bypassing (emergencies only)

```powershell
git push --no-verify
```
