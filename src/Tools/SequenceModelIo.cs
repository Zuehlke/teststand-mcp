using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestStandMCP.Tools;

/// <summary>
/// The transport model for <c>export_sequence_file</c> / <c>import_sequence_file</c>: a complete,
/// round-trippable authoring description of a sequence file.
/// <para>
/// WHY THIS EXISTS. Rebuilding a real 30-sequence file with the granular tools took ~700 MCP calls,
/// and the dominant cost was not writing but READING: <c>get_steps</c> returns only name/type/group/
/// enabled/description, so every step needed extra calls for its adapter, precondition, expressions,
/// run mode, result recording and module configuration — and several of those (a step's Post
/// expression, the retained SequenceCall file path, a UInt64 representation) were not reachable
/// through any reader at all and only surfaced in the FileDiffer output. One export + one import
/// replaces that traffic.
/// </para>
/// The shape is deliberately symmetric: whatever <c>export</c> writes, <c>import</c> consumes.
/// </summary>
public class SequenceFileModel
{
    /// <summary>Schema version of this document, so an importer can reject an incompatible export.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Path the model was exported from (informational).</summary>
    public string? SourcePath { get; set; }
    /// <summary>File-level metadata.</summary>
    public FileMetaModel File { get; set; } = new();
    /// <summary>Custom data types in the file's TypeUsageList, with their attach state. Import copies
    /// them from <see cref="TypeDefsSourcePath"/> (types carry GUIDs and cannot be recreated
    /// field-by-field), preserving the per-type attach flag.</summary>
    public List<TypeDefModel> TypeDefs { get; init; } = new();
    /// <summary>Where import should copy the type definitions from — normally the exported file.</summary>
    public string? TypeDefsSourcePath { get; set; }
    /// <summary>File global variables, as property-node trees.</summary>
    public List<VarModel> FileGlobals { get; init; } = new();
    /// <summary>The sequences, in file order. Import creates them in exactly this order.</summary>
    public List<SequenceModel> Sequences { get; init; } = new();

    /// <summary>Shared JSON options: camelCase, nulls omitted, indented for human review.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented          = true,
    };
}

/// <summary>File-level metadata of a sequence file.</summary>
public class FileMetaModel
{
    /// <summary>The file comment.</summary>
    public string? Comment { get; set; }
    /// <summary>The file version string (e.g. "0.0.0.0").</summary>
    public string? Version { get; set; }
    /// <summary>
    /// The source file's on-disk SERIALIZATION — <c>binary</c> (the engine default: compressed, with the
    /// <c>TOF1</c> magic), <c>xml</c> or <c>ini</c>. Import reproduces it, so a rebuild of an XML project
    /// file comes back out as XML instead of silently becoming binary.
    /// </summary>
    /// <remarks>
    /// This is the one part of a rebuild that <c>diff_sequence_files</c> cannot see: it compares the
    /// property trees, so a binary rebuild of an XML original reports <c>identical</c> while differing in
    /// every byte on disk — measured at 25 KB vs 3.4 MB for the same 30-sequence file.
    /// </remarks>
    public string? FileFormat { get; set; }
}

/// <summary>A custom data type and whether the file embeds ("attaches") it.</summary>
public class TypeDefModel
{
    /// <summary>Type name.</summary>
    public string Name { get; set; } = "";
    /// <summary>True when the type is attached to (embedded in) the file.</summary>
    public bool Attached { get; set; }
}

/// <summary>
/// A variable (file global, sequence local or parameter) with everything needed to recreate it,
/// including its nested structure. <see cref="Members"/> holds container members / array elements so
/// a deeply nested authored payload round-trips.
/// </summary>
public class VarModel
{
    /// <summary>Variable name (or the member name inside a container).</summary>
    public string Name { get; set; } = "";
    /// <summary>Creation type: a builtin (string/number/boolean/container/reference), or a named
    /// custom/enum type; "[]" suffix for an array.</summary>
    public string? DataType { get; set; }
    /// <summary>TestStand's type display string, e.g. "RespStatusEnum (Enumeration)" (informational).</summary>
    public string? TypeDisplay { get; set; }
    /// <summary>Node kind: Number/Boolean/String/Enum/Container/Array/Empty.</summary>
    public string? ValueType { get; set; }
    /// <summary>Scalar value as text; for an enum the SYMBOLIC name when known, else the ordinal.</summary>
    public string? Value { get; set; }
    /// <summary>Enum ordinal, when the node is an enum.</summary>
    public int? Ordinal { get; set; }
    /// <summary>True when the value equals the type default (writing it would be redundant).</summary>
    public bool? IsDefault { get; set; }
    /// <summary>Numeric representation: Float64/Int64/UInt64.</summary>
    public string? Representation { get; set; }
    /// <summary>Display NumericFormat, e.g. "%#.4x".</summary>
    public string? NumberFormat { get; set; }
    /// <summary>Raw PropFlags bitfield.</summary>
    public int? Flags { get; set; }
    /// <summary>Comment/description.</summary>
    public string? Comment { get; set; }
    /// <summary>Element count for arrays.</summary>
    public int? NumElements { get; set; }
    /// <summary>Parameter only: Input/Output/InOut.</summary>
    public string? Direction { get; set; }
    /// <summary>Parameter only: passed by reference.</summary>
    public bool? PassByReference { get; set; }
    /// <summary>Container members / array elements.</summary>
    public List<VarModel>? Members { get; set; }
}

/// <summary>One sequence with its interface, variables and steps.</summary>
public class SequenceModel
{
    /// <summary>Sequence name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Sequence description/comment.</summary>
    public string? Description { get; set; }
    /// <summary>True when result recording is DISABLED for the sequence (FileDiffer "Record Results").</summary>
    public bool? DisableResults { get; set; }
    /// <summary>True when execution jumps to Cleanup on a step failure.</summary>
    public bool? GotoCleanupOnFailure { get; set; }
    /// <summary>Parameters, in order.</summary>
    public List<VarModel> Parameters { get; init; } = new();
    /// <summary>Locals, in order (ResultList is skipped — every sequence gets it automatically).</summary>
    public List<VarModel> Locals { get; init; } = new();
    /// <summary>Steps, in order, across all three groups.</summary>
    public List<StepModel> Steps { get; init; } = new();
}

/// <summary>
/// One step with the properties a rebuild has to reproduce. Every field is read on export and written
/// on import; the set is exactly the one a real 1:1 rebuild needed, including the ones no previous
/// reader surfaced (precondition, the non-Statement Post expression, IgnoreRTE,
/// StepFailureCausesSequenceFailure, the retained SequenceCall file path).
/// </summary>
public class StepModel
{
    /// <summary>Step group: Setup / Main / Cleanup.</summary>
    public string Group { get; set; } = "Main";
    /// <summary>Step name (may be empty for a spacer Label).</summary>
    public string Name { get; set; } = "";
    /// <summary>Step type name, e.g. "Statement", "NI_Flow_If", "SequenceCall".</summary>
    public string StepType { get; set; } = "";
    /// <summary>Adapter key/friendly name; null keeps the type's default adapter.</summary>
    public string? Adapter { get; set; }
    /// <summary>False for a skipped step.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Precondition expression.</summary>
    public string? Precondition { get; set; }
    /// <summary>Pre expression.</summary>
    public string? PreExpression { get; set; }
    /// <summary>Post expression (a Statement step's expression lives here).</summary>
    public string? PostExpression { get; set; }
    /// <summary>Status expression.</summary>
    public string? StatusExpression { get; set; }
    /// <summary>Flow-branch condition (If/ElseIf/While/DoWhile).</summary>
    public string? ConditionExpr { get; set; }
    /// <summary>Select/Case item expression.</summary>
    public string? ItemExpr { get; set; }
    /// <summary>NI_Flow_ForEach: the collection to iterate (ArrayExpr).</summary>
    public string? ArrayExpr { get; set; }
    /// <summary>NI_Flow_ForEach: the per-element variable (ArrayElementExpr).</summary>
    public string? ArrayElementExpr { get; set; }
    /// <summary>NI_Flow_For: the loop initialisation expression (InitializationExpr).</summary>
    public string? InitializationExpr { get; set; }
    /// <summary>NI_Flow_For: the loop increment expression (IncrementExpr).</summary>
    public string? IncrementExpr { get; set; }
    /// <summary>NI_Flow_Case: marks the default branch.</summary>
    public bool? IsDefaultCase { get; set; }
    /// <summary>Run mode: Normal / Skip / Force Pass / Force Fail.</summary>
    public string? RunMode { get; set; }
    /// <summary>Pass action.</summary>
    public string? PassAction { get; set; }
    /// <summary>Fail action.</summary>
    public string? FailAction { get; set; }
    /// <summary>Loop type.</summary>
    public string? LoopType { get; set; }
    /// <summary>Result recording option (ResultOption number).</summary>
    public int? ResultOption { get; set; }
    /// <summary>TS.IgnoreRTE — ignore run-time errors.</summary>
    public bool? IgnoreRuntimeErrors { get; set; }
    /// <summary>TS.StepFCSeqF — a step failure fails the sequence.</summary>
    public bool? StepFailureCausesSequenceFailure { get; set; }
    /// <summary>Module load option (TS.LoadOpt).</summary>
    public string? LoadOption { get; set; }
    /// <summary>Module unload option (TS.UnloadOpt).</summary>
    public string? UnloadOption { get; set; }
    /// <summary>Step comment/description when it was authored explicitly.</summary>
    public string? Comment { get; set; }
    /// <summary>The step's code-module configuration, when it has one.</summary>
    public StepModuleModel? Module { get; set; }
}

/// <summary>A step's code-module configuration, discriminated by <see cref="Kind"/>.</summary>
public class StepModuleModel
{
    /// <summary>"Python", "LabVIEW", "SequenceCall" or null when the step has no module.</summary>
    public string? Kind { get; set; }

    // ── LabVIEW ──
    /// <summary>VI path (LabVIEW adapter).</summary>
    public string? ViPath { get; set; }

    // ── Python ──
    /// <summary>Python module path (.py).</summary>
    public string? ModulePath { get; set; }
    /// <summary>Function / method / attribute name.</summary>
    public string? FunctionName { get; set; }
    /// <summary>Python class name.</summary>
    public string? ClassName { get; set; }
    /// <summary>Expression holding the class instance.</summary>
    public string? ClassInstanceLocation { get; set; }
    /// <summary>OperationType (0 = construct, 1 = call).</summary>
    public int? OperationType { get; set; }
    /// <summary>OperationScope (1 = class, 2 = instance).</summary>
    public int? OperationScope { get; set; }
    /// <summary>Interpreter version stored on the step.</summary>
    public string? PythonVersion { get; set; }
    /// <summary>Virtual-environment path stored on the step.</summary>
    public string? VirtualEnvPath { get; set; }
    /// <summary>UseAdapterSettingsForInterpreterSession.</summary>
    public bool? UseAdapterInterpreterSettings { get; set; }

    // ── SequenceCall ──
    /// <summary>Target sequence name.</summary>
    public string? TargetSequenceName { get; set; }
    /// <summary>Target sequence file, empty/null when the call targets the current file.</summary>
    public string? TargetSequenceFile { get; set; }
    /// <summary>The sequence-file path STRING retained on the step (SData.SFPath), including a stale one.</summary>
    public string? StoredFilePath { get; set; }
    /// <summary>True when the call targets the current file.</summary>
    public bool? UseCurrentFile { get; set; }

    /// <summary>Argument bindings. For Python these are the NI_PythonParameter entries (name/type/
    /// value); for a SequenceCall the ActualArgs (name/value = the bound expression, flags).</summary>
    public List<ModuleArgModel>? Arguments { get; set; }

    /// <summary>NI_Wait only: the wait time expression (seconds).</summary>
    public string? WaitTimeExpression { get; set; }

    /// <summary>EVERY scalar leaf under <c>TS.SData</c>, as a path/value/type triple — the adapter-agnostic
    /// remainder that the typed fields above do not cover. The typed fields describe a fraction of a real
    /// module (measured: 4 of a SequenceCall's 29 SData properties, and the Automation/ActiveX adapter has
    /// no typed branch at all), and enumerating the rest per adapter is endless, so the export simply walks
    /// the subtree. Containers, arrays and argument lists are left out — those have their own
    /// representation in <see cref="Arguments"/> or are reproduced by cloning.
    /// <para>Used when <c>modules='model'</c>; with the default <c>modules='copy'</c> the whole subtree is
    /// cloned from the source file and this list is informational. It is also what makes the exported JSON
    /// a complete record of a step's module for reading and hand-editing.</para></summary>
    public List<ModulePropModel>? Properties { get; set; }
}

/// <summary>One scalar module property: its path relative to <c>TS.SData</c>, its value as text and the
/// value type needed to write it back.</summary>
public class ModulePropModel
{
    /// <summary>Dotted path relative to <c>TS.SData</c>, e.g. "ThreadOpt" or "Call.Member Name".</summary>
    public string Path { get; set; } = "";
    /// <summary>Value as text.</summary>
    public string? Value { get; set; }
    /// <summary>number / string / boolean.</summary>
    public string Type { get; set; } = "string";
}

/// <summary>One module argument / parameter binding.</summary>
public class ModuleArgModel
{
    /// <summary>Argument name.</summary>
    public string? Name { get; set; }
    /// <summary>Binding expression (Python ArgumentValue / SequenceCall Expr).</summary>
    public string? Value { get; set; }
    /// <summary>Python only: the entry's Type code.</summary>
    public string? Type { get; set; }
    /// <summary>SequenceCall only: true when the callee default is used. INDEPENDENT of
    /// <see cref="Value"/> — the editor keeps a remembered expression while still using the default,
    /// so both have to be reproduced verbatim.</summary>
    public bool? UseDefault { get; set; }
    /// <summary>Raw PropFlags on the argument entry (rarely non-zero).</summary>
    public int? Flags { get; set; }
    /// <summary>SequenceCall only: the SequenceArgument's own <c>Flags</c> NUMBER subproperty — the
    /// argument's pass mode (0x4 = by reference). This is what the FileDiffer shows as the argument's
    /// "Flags" row; it is NOT the entry's PropFlags. A prototype load copies it from the callee, so it
    /// must be written back explicitly when the caller in the original differs.</summary>
    public int? ArgFlags { get; set; }
    /// <summary>SequenceCall only: the argument's <c>ParamType</c>.</summary>
    public int? ParamType { get; set; }
    /// <summary>SequenceCall only: the argument's <c>ParamRepresentation</c> (1 = Float64, 3 = UInt64).</summary>
    public int? ParamRepresentation { get; set; }
}

/// <summary>One entry of a <c>set_property_nodes</c> batch — the same arguments the single
/// <c>set_property_node</c> takes. Entries are applied in list order because a nested member needs its
/// parent to exist first and container member ORDER is significant for the FileDiffer.</summary>
public class PropertyNodeSpec
{
    /// <summary>Scope root: Parameters / Locals / FileGlobals / StationGlobals / SequenceFile.</summary>
    public string Scope { get; set; } = "";
    /// <summary>Owning sequence, for Parameters/Locals.</summary>
    public string? SequenceName { get; set; }
    /// <summary>Dotted path relative to the scope root.</summary>
    public string LookupString { get; set; } = "";
    /// <summary>number/string/boolean/container/reference/named_type/enum/array_elements.</summary>
    public string ValueType { get; set; } = "";
    /// <summary>Named type for named_type/enum (or an array's element type).</summary>
    public string? TypeName { get; set; }
    /// <summary>Scalar value to assign.</summary>
    public string? Value { get; set; }
    /// <summary>Enum ordinal.</summary>
    public int? Ordinal { get; set; }
    /// <summary>Element count for array_elements.</summary>
    public int? NumElements { get; set; }
    /// <summary>PropFlags to apply.</summary>
    public int? Flags { get; set; }
    /// <summary>Assign <see cref="Flags"/> exactly (turning bits off) instead of OR-ing.</summary>
    public bool ClearFlags { get; set; }
    /// <summary>Numeric representation: float64/int64/uint64.</summary>
    public string? Representation { get; set; }
    /// <summary>Display NumericFormat.</summary>
    public string? NumberFormat { get; set; }
    /// <summary>Auto-create missing intermediate containers (default true).</summary>
    public bool CreateMissingParents { get; set; } = true;
}

/// <summary>Per-item outcome of an import, so a partial success is reported honestly.</summary>
public class ImportOutcome
{
    /// <summary>Sequences created.</summary>
    public int SequencesCreated { get; set; }
    /// <summary>Steps inserted.</summary>
    public int StepsInserted { get; set; }
    /// <summary>Variables (locals + parameters + file globals) created.</summary>
    public int VariablesCreated { get; set; }
    /// <summary>Module configurations applied.</summary>
    public int ModulesConfigured { get; set; }
    /// <summary>LabVIEW VI connector panes actually loaded (editor "Load Prototype"). A step counted in
    /// <see cref="ModulesConfigured"/> but not here has its VI path set with an EMPTY connector pane —
    /// every load that did not succeed is also named in <see cref="Warnings"/>.</summary>
    public int PrototypesLoaded { get; set; }
    /// <summary>Cross-file SequenceCall prototype CACHES (<c>TS.SData.Prototype</c>) actually loaded.
    /// These need an isolated worker process: the LabVIEW pane loads and the cross-file SequenceCall
    /// loads poison each other within one process, so a single-process import can only have one of the
    /// two. A cross-file call missing here still works — only the editor's cached copy of the callee's
    /// parameter list stays empty — and it is named in <see cref="Warnings"/>.</summary>
    public int CrossFilePrototypesLoaded { get; set; }
    /// <summary>How many cross-file SequenceCall steps were FOUND to need a prototype cache — the
    /// denominator for <see cref="CrossFilePrototypesLoaded"/>. Reported separately so "none needed it"
    /// is distinguishable from "all of them failed".</summary>
    public int CrossFilePrototypeCandidates { get; set; }
    /// <summary>LabVIEW connector panes reproduced by CLONING the cached ViCall subtree out of the
    /// model's source file (<c>labview_panes='copy'</c>, the default) instead of loading the VI. This is
    /// the safe path: an in-process load of a packed-library VI can raise a process-fatal native fault
    /// (0xC06D007E), and the crash-isolated worker cannot bind the running LabVIEW at all.</summary>
    public int PanesCopied { get; set; }
    /// <summary>Cross-file SequenceCall prototype caches reproduced by CLONING
    /// (<c>cross_file_prototypes='copy'</c>, the default) rather than by a worker load.</summary>
    public int CrossFilePrototypesCopied { get; set; }
    /// <summary>Type definitions copied.</summary>
    public int TypeDefsCopied { get; set; }
    /// <summary>Types the save had dropped (neither attached nor referenced by the imported subset) and
    /// that were re-copied ATTACHED so they persist. Non-zero means the destination embeds more types
    /// than the original — a deviation the FileDiffer does not report.</summary>
    public int TypeDefsForceAttached { get; set; }
    /// <summary>Types from the model that are still absent from the destination after the rescue.</summary>
    public int TypeDefsMissing { get; set; }
    /// <summary>Variables (file globals + parameters + locals) reproduced by CLONING them from the
    /// model's source file (<c>variables='copy'</c>, the default). The declarative route cannot
    /// reproduce a type instance's member that has NO value of its own: instantiating the named type
    /// materialises the member with its default written out, which the FileDiffer reports as
    /// explicitly-set. A clone carries that state verbatim.</summary>
    public int VariablesCopied { get; set; }
    /// <summary>Which route reproduced the variables: copy / model.</summary>
    public string? VariableMode { get; set; }
    /// <summary>Step code MODULES reproduced by cloning the whole <c>TS.SData</c> subtree from the model's
    /// source file (<c>modules='copy'</c>, the default). The model describes only a fraction of a module —
    /// measured: 4 of a SequenceCall's 29 SData properties and nothing at all for the Automation/ActiveX
    /// adapter — so the clone is what makes a rebuild adapter-agnostic.</summary>
    public int ModulesCloned { get; set; }
    /// <summary>Which route reproduced the step modules: copy / model.</summary>
    public string? ModuleMode { get; set; }
    /// <summary>True when the destination's leftover default <c>MainSequence</c> was removed because the
    /// model contains no sequence of that name. Deleting it used to be a separate call the caller had to
    /// remember, and forgetting it left a stray empty sequence in every rebuild.</summary>
    public bool DefaultMainSequenceRemoved { get; set; }
    /// <summary>Which route reproduced the LabVIEW connector panes: copy / load / skip.</summary>
    public string? LabViewPaneMode { get; set; }
    /// <summary>Which route reproduced the cross-file prototype caches: copy / load / skip.</summary>
    public string? CrossFilePrototypeMode { get; set; }
    /// <summary>The on-disk serialization the destination was saved in (<c>binary</c>/<c>xml</c>/<c>ini</c>),
    /// whether that came from the model or from an explicit override. Null when the format was left
    /// untouched.</summary>
    public string? FileFormat { get; set; }
    /// <summary>Where this outcome was also written as JSON, so a caller whose RPC timed out can still
    /// read the warnings. Null when it could not be written.</summary>
    public string? OutcomePath { get; set; }
    /// <summary>Non-fatal problems, each naming the sequence/step it happened on.</summary>
    public List<string> Warnings { get; init; } = new();
}
