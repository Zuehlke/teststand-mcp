# Tool contracts

How the write tools behave, where the tool descriptions leave room for a wrong guess. Read the matching section before the first call to a tool you have not used. The basics of `set_property_value` and `set_step_property` are in [headless-and-tools.md](headless-and-tools.md#properties-and-variables); the validator error codes are in [headless-and-tools.md](headless-and-tools.md#validation-before-and-after-building).

## Bulk writers and saving

- Almost every mutating tool saves the WHOLE file, and the save is about 90 percent of the cost of a call. `insert_steps_bulk`, `insert_sequences_bulk`, `insert_variables_bulk`, `set_property_nodes` and `set_module_parameters` do many writes in one call with one save, applied in array order. Prefer them.
- About 35 single-write tools accept `save:false`. Pass it until the last write, then save once.
- A tool that reads the file from disk (the isolated prototype load below) does not see unsaved edits.

## Enumerations

- Tools: `create_enum`, `get_enum_values`, `set_enum_values` (replaces the whole list), `add_enum_value`, `remove_enum_value`, `rename_enum_value`, `delete_enum`.
- An enum is a named numeric type stored in the sequence file. It is one of the file's type definitions, NOT a file-root subproperty, so it does not appear in `get_data_types`. Use `get_enum_values`.
- Values are `{name, value?}`. An omitted `value` is assigned C-style, previous value plus 1 starting at 0. `add_enum_value` uses the current maximum plus 1.
- To give a container member or a step subproperty an enum type, create it with `value_type='enum'`, `type_name` and `ordinal` (see Property nodes below), not with a plain number.

## Editing step properties

- `set_step_property` writes a property relative to the step (for example `VIModule.ViCall.VIPath`). The path must already exist; create a missing one with `create_step_property`. It peeks at the target type first, so writing "True" or "False" to a string expression property (`ActualArgs.<arg>.Expr`, any `*.Expr`) stays a string, and a string written to an enum property is retried as an enumerator label.
- Two paths are special: `Icon` writes the step icon file name (`TS.Icon`, for example `ni_blank.ico`), and `Flags` writes the step's flag bitfield (decimal or `0x` hex, for example `0x4000000` to blank a nameless Label's icon).
- `set_step_property` and `create_step_property` accept `unescape:true` to decode `\r`, `\n`, `\t`, `\\` and `\uXXXX`. It is the only way to write a bare control character, such as the carriage return in a VI description.
- `create_step_property` creates NEW subproperties by dotted path. `value_type` is `number`, `boolean`, `string`, `container`, `reference` (Object Reference), `enum` (with `type_name` and `ordinal`), `named_type` (with `type_name`, for example `SequenceArgument`, `ErrorDialogOptions` or `Error`) or `array_elements` (with `num_elements`). Elements of an array are created with the array's ELEMENT type. Named types resolve engine-wide. Requesting a named type on an existing node of a DIFFERENT type retypes that node in place; otherwise the call is idempotent.
- Array elements can be NAMED. The `ViCall.Parms` entries carry the connector-pane label as their element name, and the editor and the diff pair array elements BY that name. `create_step_property(array_elements)` creates unnamed elements, so name each one afterwards with `rename_step_property`, or the diff shows the whole array as same-named Delete/Insert pairs although the content is identical. `get_property_tree` reports element names in `elementName`.
- `set_step_property_flags` writes raw PropFlags on any step property, for example `0x4` PassByReference on a `Prototype` member or the `0x200000` marker on a module container. A plain write REPLACES the bits you pass: writing `0x4` and then `0x80` leaves `0x80`, not `0x84`. Separately, the engine keeps its own `0x400000` bit in every mode, so the READ-BACK includes it (`0x400080`); it is not a bit you wrote. `exact=true` gives the same result with a read-back check and a fallback to the instance-override layer for a bit that survives. The result is the read-back flags. Inspect current values first with `get_property_tree` (`flags` on the node).
- `rename_step_property` sets a property's name. `delete_step_property` removes a subproperty by dotted path, for example one surplus `TS.SData.ActualArgs.<name>` that a prototype load left behind.

## Property nodes in any scope

`set_property_node` and `delete_property_node` work on a dotted `lookup_string` relative to the root of a `scope`: `Parameters`, `Locals`, `FileGlobals`, `StationGlobals` or `SequenceFile`. `Parameters` and `Locals` need `sequence_name`; `StationGlobals` ignores the file path.

- `set_property_node` creates or sets a node with the same `value_type` choices as `create_step_property`. Missing parent containers are created (`create_missing_parents`, default true).
- `value_type='named_type'` with `type_name` instantiates a FULL typed instance whose fields materialise, as in the editor. It is the only way a nested container member gets its named type instead of an anonymous `Container` (the cause of `NI_ExpressionEvaluationError "Expected <Type>, found Container"`). Recipe: `copy_typedefs` first so the type exists, then `named_type` for each typed member, then set only the fields that differ. Use it only where the node does NOT exist yet (see [rebuild.md](rebuild.md#never-replace-a-typed-propertyobject-with-a-clone)).
- `value_type='enum'` with `type_name` and `ordinal` (preferred over a name) stores an EXPLICITLY SET enumerator, which is what an editor-authored file holds. `array_elements` with `num_elements` sizes a typed array.
- `flags` writes raw PropFlags. A plain write replaces the bitfield, so `132` (`0x84`) means `0x04` PassByReference plus `0x80`. `clear_flags=true` takes the verified exact path. A value is written only when you supply one, so a flags-only call never clobbers the value. Do not write flags `0` on a type instance: it marks the instance as overridden and flips `{val}` to `[val]` on its members.
- `representation` (`float64`, `int64`, `uint64`) and `number_format` (for example `%#.4x`) set a numeric node's width and display format, and `0x` literals are accepted. The same two parameters exist on `insert_local_variable` and `insert_sequence_parameter`. The width is functional: a Float64 parameter fed a UInt64 argument is a hard `NI_ExpressionEvaluationError`.
- `delete_property_node` with `scope=Parameters` and a top-level name removes a whole parameter with its structure. A nested path such as `MDC_cmd.Request.Cmd` removes one submember. For Locals and FileGlobals, `delete_sub_property` is the narrower sibling.
- `insert_file_global` and `insert_local_variable` accept `reference` (Object Reference) and named custom types; there is no silent String fallback. `set_file_global` writes a boolean as a boolean and preserves an existing global's authored type. `set_file_global_comment` and `set_local_variable_comment` write comments, with a dotted path to reach container members.
- Enum leaves read back as `valueType:"Enum"` with `value:{ordinal, symbolicName}` from `get_file_globals`, `get_property_object` and `get_property_tree`. A value written by raw ordinal reads back with an empty `symbolicName` until the file is reloaded; the ordinal is always right.

## Reading the property tree

- `get_property_tree` addresses a sequence or step BY NAME (`sequence_name`, `step_group`, `step_name`). The raw path form is `Data.Seq[i].Main[j]`; there is no `Sequences` node.
- Scope a read with `lookup_string` (for example to a step's `...ViCall`). A dump of the whole `TS` node at low depth hits the node budget and is truncated.
- Each node reports `flags`; each enum leaf reports `isDefault` and `symbolicName`.

## Configuring step modules

`configure_labview_module`, `configure_dll_module`, `configure_dotnet_module`, `configure_python_module` and `configure_sequence_call_module` each set the target and then run the engine's "Load Prototype", so the step's parameter interface is populated in one call and returned in the result's `parameters`.

- Pass `load_prototype:false` to skip the load while the target is not reachable yet. An unresolvable target (an unlinked SequenceCall placeholder, a missing or not-yet-loaded file) skips silently, so the target must be loaded or on the search path for the interface to appear. For a `.lvlibp` VI the load is skipped by design; see [rebuild.md](rebuild.md#lvlibp-panes-cloned-never-loaded).
- `set_sequence_call_target` and `insert_steps_bulk` also load the SequenceCall prototype. `set_module_parameter` loads it when the named argument is missing.
- For a SequenceCall the load keeps `UseDef` and the argument expression consistent: an unbound argument has `UseDef=True`, a bound one `False`. An argument keeps its state in subproperties (`UseDef`, `Expr`, `ParamType`, `ParamRepresentation`, `Flags`). `UseDef` is independent of `Expr`: the editor keeps a remembered expression while still using the default. A load renames entries after the callee's CURRENT parameters, and argument order is compared positionally.
- `configure_sequence_call_module` also writes the retained stored file path (default: the current file's own name) after the load. Pass `stored_file_path` to reproduce a stale path, since real files carry paths from before a rename.
- `configure_python_module` sets everything in one call: `class_name`, `class_instance_location`, `operation_type` and `operation_scope`, `python_version`, `virtual_env_path`, `use_adapter_interpreter_settings` and the whole `parameters[]` list (`{name, type, value}`). The analyzer's "Python version cannot be empty" error is about the station's Python adapter configuration, not about this step property.
- `configure_labview_module` switches the step's adapter, so it refuses a utility step that has no adapter (such as `NI_LV_RunVIAsynchronously`). Use `set_step_property` there.
- An Action step that calls a subsequence: set the adapter to `Sequence` (with `change_step_adapter`, or `adapter='Sequence'` on `insert_step` or `insert_steps_bulk`), then call `configure_sequence_call_module`. It works on any Sequence-adapter step, and the step keeps its Action type.

## Binding parameters

`get_module_parameters` reads a step's interface: LabVIEW `ViCall.Parms` as label to binding, SequenceCall `ActualArgs`, Python arguments and .NET call parameters. `set_module_parameter` (bulk form `set_module_parameters`) writes a binding:

- LabVIEW: binds a connector-pane parameter by label (`'error out.status'` descends into clusters). It writes `ArgVal` and always clears `UseDefaultValues`; see [rebuild.md](rebuild.md#lvlibp-panes-cloned-never-loaded) for why that matters in a 1:1 rebuild.
- SequenceCall: binds `ActualArgs.<name>.Expr` and clears that argument's `UseDef`.
- .NET: see below.

## Re-syncing a prototype with `load_module_prototype`

It is the editor's "Load Prototype" button for any step (LabVIEW, DLL or CVI, .NET, ActiveX, SequenceCall). Use it after a `configure_*` call with `load_prototype:false` once the target is reachable, or to RE-SYNC a caller after the target's interface changed (a subsequence's Parameters were edited, a DLL, VI or ActiveX signature changed). It does not change the adapter, and it is non-destructive: existing bindings are matched by name. The result is `{stepName, adapter, prototypeLoaded, note, executionMode, workerOutcome, parameters[]}`; `prototypeLoaded:false` comes with a `note` when the target could not be resolved.

- It is not a route for a `.lvlibp` VI. Clone those panes instead; see [rebuild.md](rebuild.md#lvlibp-panes-cloned-never-loaded).
- A LabVIEW load attaches to or starts LabVIEW, which can outlast the MCP transport window. It therefore runs asynchronously by default: the call returns `{jobId, status:"running"}`, and you poll `get_prototype_load_status(job_id)` until `status:"completed"`. Parameters: `async` (default true for LabVIEW, false for other adapters), `isolate` (default true), `labview_server` (`deferred` default, `exec`, `rte`, `auto`) and `timeout_seconds` (default 120).
- `isolate:true` runs the load in a separate worker process, bounded by `timeout_seconds`. If it crashes or times out, the result says so (`workerOutcome` `crashed` or `timeout`) and the server survives. The worker reads the file from DISK, so unsaved `save:false` edits make a step look out of range.
- Adapters other than LabVIEW always run in-process and synchronously. The automatic load inside the `configure_*` tools is also in-process and synchronous.

## .NET steps

- `configure_dotnet_module` resolves the member in three tiers (the untouched lookup, then the Static bit, then the call-level signature) and reports `resolvedVia` and the `signature` the adapter really bound. Read both. A member that is not resolved leaves a step that runs as a silent no-op and still reports `Passed`, and a "success" flag alone is not evidence.
- A bare member name picks ONE overload silently. To select a specific one pass the full signature as `method_name`, for example `"Overloaded(Double, Double)"`, spelled exactly as the adapter's member list spells it (without the trailing ` (static)` display suffix), and read `signature` back.
- An INSTANCE member needs `create_object=true`; alone it is refused with a note that names the fix. The step is then built as a call chain: the first call is the constructor and the second invokes the member on that object. `constructor_signature` picks a non-default constructor, and `dispose_object` releases the object afterwards. Calling into an object that already exists elsewhere is not reachable.
- The parameters of a .NET step live in its calls, not in a flat list. With more than one call in the chain, the names are prefixed with the member (`Triple.a`, `InstanceOps.Return Value`), and `get_module_parameters` reports them that way.
- `set_module_parameter` binds a .NET argument by name, or by `<member>.<parameter>`, the only unambiguous form in a chain where every call has its own `Return Value`; unprefixed, the first match wins. The entry named `Return Value` takes a DESTINATION expression such as `Locals.Sum`, which receives the result. Direction is not a separate field: it sits in the flags (0 input, 6 return value, 10 out parameter).
- The writer touches only the binding expression. The "use default" asymmetry of LabVIEW panes does not exist for .NET.
- `load_module_prototype` runs the same three tiers for a .NET step, so it can complete a step that was configured while the assembly was missing, or re-sync one whose signature changed. An already valid interface survives the reload.

## Users and text encoding

- Pass `persist:false` for every test or experimental user. It edits only the loaded users data and never touches `users.ini` on disk.
- Comments and descriptions round-trip through Windows-1252. En and em dashes, ellipsis, bullet, euro sign, trademark sign and curly quotes survive. Characters outside it (arrows, check marks, emoji, CJK) become `?`, so keep punctuation plain ASCII where it matters.
