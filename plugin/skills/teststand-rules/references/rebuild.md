# Rebuilding, migrating and cloning sequence files

## Whole file: export, then import (the default path)

For a whole-file reproduction, a migration or a bulk edit, start with `export_sequence_file` and `import_sequence_file`. The granular tools (`insert_step`, `set_step_property`, ...) are for surgical single edits; rebuilding a file with them takes hundreds of calls and leaves hundreds of differences.

```
export_sequence_file(file_path)                          writes <file>.model.json, returns a summary
create_sequence_file(dest, overwrite=true)               closes and deletes an existing dest first
import_sequence_file(model_path, dest_file_path)         rebuilds everything, returns counts + warnings[]
diff_sequence_files(orig, dest)                          verify the CONTENT
audit_type_consistency(dest, reference_file_path=orig)   verify the TYPE REGISTRY
```

- The import removes the destination's leftover `MainSequence` itself when the model has none; there is no `delete_sequence` step.
- The model is written to disk, not returned inline (a 30-sequence model is about 350 KB). Pass its path straight to the import. Use `inline=true` only for a single sequence.
- The model carries the file comment and version, every type with its attach state, the FileGlobals, and per sequence its description, result recording, parameters, Locals (nested members, enum values, flags, numeric representation and format, comments) and all steps with their properties and complete module configuration.
- The import order is fixed: types, FileGlobals, ALL sequences with their interfaces, and only then the steps, so every callee's parameters exist before a caller's prototype is loaded.
- Read `warnings[]`. It names every item that could not be applied, and a non-empty list means a partial import. Never judge success from the counts alone.
- The outcome is also written to `<dest>.import.json`. A large import can outlive the MCP transport window; a `-32001` timeout does NOT abort it, the server finishes and saves. Read that file instead of re-running the import.
- Keep the defaults, they are the safe ones: `labview_panes='copy'`, `cross_file_prototypes='copy'` and `variables='copy'` clone from the model's source file. `'load'` exists only for a model with no source file and carries the process-death risk described below. `keep_unused_types=true` re-attaches types that a save would drop.
- Use `variables='model'` only for an EDITED model. A clone takes the source file's variable state and would silently discard your changes to the model's variables. Anything that cannot be cloned falls back to the model automatically and is named in `warnings`.
- Importing only a SUBSET of the sequences drops every type that only the omitted sequences kept alive. The import detects this after saving and reloading, re-attaches those types and names them in `warnings`. The rebuild then embeds more types than the original; the diff does not show it.

## File format is part of a rebuild

A `.seq` is stored as compressed binary (`TOF1` magic) or as XML (a UTF-8 BOM, then `<?xml`). The engine's default for a new file is binary.

- The export captures the source format (`file.fileFormat` in the model) and the import reproduces it by default; the outcome reports `fileFormat`. A 1:1 rebuild needs nothing extra.
- `get_file_properties` reports `fileFormat`. `create_sequence_file`, `save_sequence_file`, `set_file_properties` and `import_sequence_file` accept `file_format` = `binary` | `xml` | `ini`. The format is stored in the file, so an ordinary save never converts it back.
- The diff is blind to the format: two files can be content-identical yet differ in every byte and in size by a factor of about 130. Before reading a large size drop as data loss, check the first bytes (`TOF1` against `<?xml`).
- Prefer XML for anything that lives in git; binary `.seq` diffs are opaque.
- The format is the serialization, not the TestStand version target (that is the `version` field).

## `.lvlibp` panes: cloned, never loaded

A LabVIEW step that calls a VI inside a packed library (`.lvlibp`) gets its connector pane (the `ViCall` subtree: `Parms` with their bindings, namespace, VI description, connector-pane checksum) by CLONING it from a source `.seq`. Never load it, by any route.

- Why: the in-process load raises a native delay-load fault for the LabVIEW Run-Time even when LabVIEW is running. The fault escapes normal error handling, so the server process dies. The isolated worker variant (`isolate:true`) cannot bind the running LabVIEW and only times out. There is no working prototype-load route for a packed-library VI.
- The server enforces this: `load_module_prototype(isolate:false)` on a packed-library VI is REFUSED with an error that names the working route. Never pass `force_unsafe_inprocess:true`; it only reproduces the crash.
- `configure_labview_module` skips its automatic prototype load for a `.lvlibp` VI and says so in the result's `note`. The VI path is still written, so the step is configured.
- The route that works is `copy_step_module`, or the `import_sequence_file` default `labview_panes='copy'`. It takes about a second per step, needs no LabVIEW, and produces panes with zero differences.
- `labview_panes='load'` still exists for a plain `.vi`; on a packed-library step it becomes an import warning instead of a dead server.
- After a crash, `get_prototype_load_status` reports the job as unknown or expired, which looks like a genuine expiry. A job that vanishes right after a LabVIEW load means the server process died; check whether its process id changed.
- Connector-pane bindings written by hand need care: `set_module_parameter` always clears a control's `UseDefaultValues`, so binding every parameter through it flips that flag wherever the source keeps the VI's own default. Write `ViCall.Parms[i].ArgVal` and `UseDefaultValues` independently with `set_step_property`. The clone path carries both verbatim.
- A single server process can hold either LabVIEW panes or the cached `Prototype` of cross-file SequenceCalls that were produced by a load, never both: after one kind of load the other kind fails with `prototypeLoaded:false`. Cloning has no such interaction, so this only matters when you force `labview_panes='load'` or `cross_file_prototypes='load'`.

## Never replace a typed PropertyObject with a clone

Cloning a whole module subtree is right for `TS.SData`, `VIModule` and the authored config arrays. Doing it for a subtree whose node carries a NAMED TYPE registers a second, conflicting instance of that type in the destination: the file opens in the Sequence Editor with a type-conflict dialog ("... conflicts with the type already loaded") although it is functionally complete. For a typed node, write its leaf scalars by value on the existing node instead of swapping the object.

- `copy_step_module` and the import already do this. A shorter `copiedPaths` than you expect is that policy working.
- The trap is still live for `set_property_node value_type='named_type'`, which instantiates a type deliberately. Use it only when the node does NOT exist yet.
- The diff reports `identical: true` for a file that raises the dialog. `audit_type_consistency` is the check that sees it (next section).

## Verifying a rebuild

Two checks are required; they cover different failure classes:

1. `diff_sequence_files(orig, dest)` proves the CONTENT matches. `compare_sequence_files` in its default native mode is the same diff; its `mode='structural'` is a fast in-process compare whose zero differences do NOT prove identity.
2. `audit_type_consistency(dest, reference_file_path=orig)` proves the type registry is sane. Run it after every rebuild, next to the diff. It reads the registry raw, duplicates included, and reports `E_DUPLICATE_TYPE_NAME` (the same type registered twice, the dialog's actual cause) and `W_MODIFIED_TYPE`. With `reference_file_path` it also reports `E_TYPE_VERSION_MISMATCH`, `W_TYPE_STRUCTURE_MISMATCH` and the one-sided `W_TYPE_ONLY_IN_*`. `valid` keys off the errors only, so read the warnings too. Opening the file in the editor remains a belt-and-braces check.

A third, optional check covers the expressions: `audit_sequence_references(dest)` (see [headless-and-tools.md](headless-and-tools.md#validation-before-and-after-building)).

Reading a diff:

- The rows are capped at 150 by default and any truncation is stated in `truncated` and `note`. Every response carries `byCategory`, `byChangeType` and `bySequence` over ALL differences regardless of the cap. Drill in with `include_categories`, `exclude_categories`, `path_filter`, `change_types`, `max_results` (0 = unlimited) and `group_by='category'|'sequence'`. `summary_only=true` is an option, not a precaution.
- When you rebuild only SOME sequences, the omitted ones dominate the count as `other` Delete rows. Subtract them with `exclude_categories=['other']` before judging the rebuild.
- In diff values, `{val}` is the type default (not explicitly set) and `[val]` is explicitly set. A member that is at its type default in the original must be left unset: writing it flips `{val}` to `[val]` and creates a spurious difference. `get_property_tree` reports `isDefault` per enum leaf.
- A lone `File Properties > Attributes` delete (for example `NI.Analyzer.IgnoredMessages`) is irreducible: the engine API never loads those file attributes, so no tool can read or reproduce them. Treat an Attributes-only diff as a functional match; the rebuild may show a few extra analyzer warnings the original suppresses.
- After an import, expect zero differences inside the imported scope. A whole-file rebuild reaches `identical: true`. A named-type instance's enum member would read as explicitly set after a declarative rebuild; `variables='copy'` (the default) clones the variable and reproduces the state exactly.

## Cloning ONE sequence: `duplicate_sequence`

`duplicate_sequence` deep-clones one sequence (steps, modules, Locals, Parameters, comment, all settings), within a file or across files with `target_file_path`. Use it for a single sequence: a variant of an existing test, or one sequence lifted out of another file. For a whole FILE use export and import above: they also carry the file comment and version, the FileGlobals, the type attach state and the per-step authored configuration, which a sequence clone cannot.

To rebuild a file sequence by sequence on purpose:

1. `create_sequence_file` for the new file.
2. `copy_typedefs` for all types, so cloned sequences and FileGlobals resolve their types by GUID. It preserves the source's attach state by default (`attach='preserve'`; `'all'` and `'none'` exist).
3. `duplicate_sequence` source to target for EACH sequence, in source order, same name.
4. `delete_sequence` the default `MainSequence`.
5. `copy_file_globals`: FileGlobals belong to no sequence, so the clone misses them.
6. `copy_file_attributes` and `set_file_properties` for comment and version.
7. `save_sequence_file`, then verify with `diff_sequence_files` and `audit_type_consistency`.
