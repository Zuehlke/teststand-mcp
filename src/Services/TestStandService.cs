using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using TestStandMCP.Models;
using TestStandMCP.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NiSequenceFile      = NationalInstruments.TestStand.Interop.API.SequenceFile;
using NiSequence          = NationalInstruments.TestStand.Interop.API.Sequence;
using NiStep              = NationalInstruments.TestStand.Interop.API.Step;
using NiPropertyObject    = NationalInstruments.TestStand.Interop.API.PropertyObject;
using NiPropValueTypes    = NationalInstruments.TestStand.Interop.API.PropertyValueTypes;
using NiTypeCategories    = NationalInstruments.TestStand.Interop.API.TypeCategories;
using NiTypeUsageList     = NationalInstruments.TestStand.Interop.API.TypeUsageList;
using NiEngine            = NationalInstruments.TestStand.Interop.API.Engine;
using NiConflictHandler   = NationalInstruments.TestStand.Interop.API.TypeConflictHandlerTypes;
using NiExecution         = NationalInstruments.TestStand.Interop.API.Execution;
using NiExecRunStates     = NationalInstruments.TestStand.Interop.API.ExecutionRunStates;
using NiExecTermStates    = NationalInstruments.TestStand.Interop.API.ExecutionTerminationStates;
using NiThread            = NationalInstruments.TestStand.Interop.API.Thread;
using NiSequenceContext   = NationalInstruments.TestStand.Interop.API.SequenceContext;
using NiStepGroups        = NationalInstruments.TestStand.Interop.API.StepGroups;
using PropertyObjectFile  = NationalInstruments.TestStand.Interop.API.PropertyObjectFile;
using NiWriteFileFormat   = NationalInstruments.TestStand.Interop.API.WriteFileFormat;
using NiPropOptions       = NationalInstruments.TestStand.Interop.API.PropertyOptions;
using NiEvalOptions       = NationalInstruments.TestStand.Interop.API.EvaluationOptions;
using NiOutputSeverity    = NationalInstruments.TestStand.Interop.API.OutputMessageSeverityTypes;
using NiUIMessageCodes    = NationalInstruments.TestStand.Interop.API.UIMessageCodes;
using NiCsvOut            = NationalInstruments.TestStand.Interop.API.CsvFileOutputRecordStream;
using NiCsvIn             = NationalInstruments.TestStand.Interop.API.CsvFileInputRecordStream;
using NiFileOpenModes     = NationalInstruments.TestStand.Interop.API.FileOpenModes;
using NiOutRecordStream   = NationalInstruments.TestStand.Interop.API.OutputRecordStream;
using NiInRecordStream    = NationalInstruments.TestStand.Interop.API.InputRecordStream;
using NiUsersFile         = NationalInstruments.TestStand.Interop.API.UsersFile;
using NiUser              = NationalInstruments.TestStand.Interop.API.User;
using NiSearchOptions     = NationalInstruments.TestStand.Interop.API.SearchOptions;
using NiSearchElements    = NationalInstruments.TestStand.Interop.API.SearchElements;
using NiSearchFilter      = NationalInstruments.TestStand.Interop.API.SearchFilterOptions;
using NiFindFilePrompt    = NationalInstruments.TestStand.Interop.API.FindFilePromptOptions;
using NiFindFileSrchList  = NationalInstruments.TestStand.Interop.API.FindFileSearchListOptions;
using NiLabVIEWAdapter    = NationalInstruments.TestStand.Interop.AdapterAPI.LabVIEWAdapter;
using NiLabVIEWServerTypes = NationalInstruments.TestStand.Interop.AdapterAPI.LabVIEWServerTypes;
using NiPropObjType       = NationalInstruments.TestStand.Interop.API.PropertyObjectType;
using NiPropRepresentations = NationalInstruments.TestStand.Interop.API.PropertyRepresentations;

namespace TestStandMCP.Services;

// ── Interface ────────────────────────────────────────────────────────────────

/// <summary>Defines the contract for interacting with the NI TestStand engine.</summary>
public interface ITestStandService : IDisposable
{
    // Engine
    /// <summary>Returns station and engine information for the currently connected TestStand engine.</summary>
    Task<StationInfo> GetStationInfoAsync();
    /// <summary>Connects to (or creates) the TestStand engine, optionally using the specified engine path.</summary>
    Task<bool> ConnectAsync(string? enginePath = null);
    /// <summary>Disconnects from and shuts down the TestStand engine.</summary>
    Task DisconnectAsync();
    /// <summary>Gets a value indicating whether the service is currently connected to a TestStand engine.</summary>
    bool IsConnected { get; }

    // Sequence Files
    /// <summary>Opens the sequence file at the given path and returns its metadata.</summary>
    Task<SequenceFileInfo> OpenSequenceFileAsync(string filePath);
    /// <summary>Closes and releases the sequence file at the given path.</summary>
    Task CloseSequenceFileAsync(string filePath);
    /// <summary>Returns the list of all currently loaded sequence files.</summary>
    Task<List<SequenceFileInfo>> GetLoadedSequenceFilesAsync();
    /// <summary>Returns a summary list of all currently loaded sequence files.</summary>
    Task<List<SequenceFileSummary>> GetLoadedSequenceFilesSummaryAsync();
    /// <summary>Returns the details of the named sequence within the given sequence file.</summary>
    Task<SequenceInfo> GetSequenceAsync(string filePath, string sequenceName);
    /// <summary>Saves the sequence file at the given path to disk.</summary>
    Task SaveSequenceFileAsync(string filePath);
    /// <summary>Creates a new empty sequence file at the given path and returns the path.</summary>
    Task<string> CreateSequenceFileAsync(string filePath);
    /// <summary>Inserts a new sequence with the given name into the specified sequence file.</summary>
    Task InsertSequenceAsync(string filePath, string sequenceName);
    /// <summary>Inserts a single step into the specified sequence and step group at the given index.</summary>
    Task InsertStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepType, string stepName, int index = -1, string? adapterName = null);
    /// <summary>Bulk-inserts multiple steps into the specified sequence in a single operation.</summary>
    Task<BulkInsertResult> InsertStepsBulkAsync(string filePath, string sequenceName,
        string stepGroup, List<BulkStepSpec> steps, bool save = true);
    /// <summary>Inserts a new local variable into the specified sequence. <paramref name="representation"/>
    /// (float64/int64/uint64) and <paramref name="numberFormat"/> (e.g. <c>%#.4x</c>) set a NUMBER's
    /// width and display format — without them every number is a 64-bit float, which makes TestStand
    /// reject expressions that pass it to a UInt64 target.</summary>
    Task InsertLocalVariableAsync(string filePath, string sequenceName,
        string variableName, string dataType, string? defaultValue = null,
        string? representation = null, string? numberFormat = null);
    /// <summary>Sets the comment (description) on a local variable in the specified sequence. The
    /// name may be a dotted path to a nested container member (e.g. "MyCont.Field").</summary>
    Task SetLocalVariableCommentAsync(string filePath, string sequenceName,
        string variableName, string comment);
    /// <summary>Sets the comment (description) on a sequence parameter (or a nested member via a
    /// dotted path). There is no other tool that reaches a Parameter's comment.</summary>
    Task SetParameterCommentAsync(string filePath, string sequenceName,
        string parameterName, string comment);
    /// <summary>Sets the value of a local variable in the specified sequence.</summary>
    Task SetLocalVariableValueAsync(string filePath, string sequenceName,
        string variableName, string value);
    /// <summary>Returns all local variables defined in the specified sequence.</summary>
    Task<List<VariableInfo>> GetLocalVariablesAsync(string filePath, string sequenceName);
    /// <summary>Sets the expression on the specified step (e.g. the Statement expression or condition).</summary>
    Task SetStepExpressionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression, string expressionType = "Statement");

    /// <summary>Configures a SequenceCall step to invoke the named target sequence (optionally in another file).</summary>
    Task SetSequenceCallTargetAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string targetSequenceName, string targetSequenceFile = "");

    /// <summary>Sets the code-module path for the specified step.</summary>
    Task SetStepModulePathAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string modulePath);

    /// <summary>Sets ANY property on a step by a dotted path relative to the step's PropertyObject
    /// (e.g. "VIModule.ViCall.VIPath", "RemoteHost", "PortNumber", "Timeout"), then reads it back.
    /// This is the generic step-property writer — set_property_value/set_property only reach
    /// Globals/Locals, and configure_*_module only reach the adapter module. value_type is
    /// auto-detected (number / true|false / string) when null. The path must already exist.</summary>
    Task<StepPropertyValue> SetStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string value, string? valueType,
        bool save = true, bool unescape = false);

    /// <summary>Creates a NEW subproperty (or resizes an array) on a step by a dotted path —
    /// the creation counterpart to <see cref="SetStepPropertyAsync"/>, which requires the path
    /// to exist. value_type: number/boolean/string/container/reference, a NAMED type via
    /// type_name (e.g. "SequenceArgument", "ErrorDialogOptions"), or "array_elements" to
    /// SetNumElements on an existing array property (elements are created with the array's
    /// element type — the only way to author e.g. ViCall.Parms entries).</summary>
    Task<StepPropertyValue> CreateStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string valueType,
        string? typeName = null, int? numElements = null, string? value = null,
        bool unescape = false, bool save = true);

    /// <summary>Deletes a subproperty from a step by a dotted path — the counterpart to
    /// <see cref="CreateStepPropertyAsync"/>. Needed because a prototype load can leave an argument
    /// entry behind that the original does not have (e.g. a renamed callee parameter), and no other
    /// tool could remove a step subproperty.</summary>
    Task DeleteStepPropertyAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string propertyPath, bool save = true);

    /// <summary>Sets the raw PropFlags bitfield on a step property. By default this ORs the bits on
    /// (SetFlags); with <paramref name="exact"/> the whole bitfield is ASSIGNED, which is the only way
    /// to turn a bit OFF (e.g. clearing a 0x4 PassByReference a prototype load left behind). Returns
    /// the read-back flags.</summary>
    Task<StepPropertyValue> SetStepPropertyFlagsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, int flags, bool save = true,
        bool exact = false);

    /// <summary>Sets the NAME of a step property (PropertyObject.Name) — required for named
    /// ARRAY ELEMENTS such as ViCall.Parms entries, which carry the connector-pane label as
    /// their element name ("[0] error in (no error)"); SetNumElements creates them unnamed,
    /// and FileDiffer/the editor pair and display elements by this name.</summary>
    Task<StepPropertyValue> RenameStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string newName, bool save = true);

    // Executions
    /// <summary>Starts execution of an entry point in the given sequence file and returns execution info.</summary>
    Task<ExecutionInfo> StartExecutionAsync(string sequenceFilePath, string entryPoint,
        Dictionary<string, object>? parameters = null);
    /// <summary>Waits for the specified execution to complete within the given timeout and returns the result.</summary>
    Task<ExecutionResult> WaitForExecutionAsync(string executionId, int timeoutSeconds = 300);
    /// <summary>Returns the current status of the specified execution.</summary>
    Task<ExecutionInfo> GetExecutionStatusAsync(string executionId);
    /// <summary>Returns all currently active executions.</summary>
    Task<List<ExecutionInfo>> GetActiveExecutionsAsync();
    /// <summary>Terminates the specified execution.</summary>
    Task TerminateExecutionAsync(string executionId);
    /// <summary>Runs a sequence synchronously and returns the result.</summary>
    Task<ExecutionResult> RunSequenceAsync(string sequenceFilePath, string sequenceName,
        Dictionary<string, object>? parameters = null, int timeoutSeconds = 300);

    // Variables & Properties
    /// <summary>Gets the value of a TestStand property identified by a lookup string.</summary>
    Task<PropertyValue> GetPropertyAsync(string lookupString);
    /// <summary>Sets the value of a TestStand property identified by a lookup string.</summary>
    Task SetPropertyAsync(string lookupString, object value);
    /// <summary>Evaluates a TestStand expression and returns the result.</summary>
    Task<ExpressionResult> EvaluateExpressionAsync(string expression,
        string? sequenceFilePath = null);
    /// <summary>Returns the property object (and its sub-properties) at the specified path within a sequence file.</summary>
    Task<PropertyObjectInfo> GetPropertyObjectAsync(string filePath,
        string? sequenceName, string propertyName);
    /// <summary>Sets the typed value of a property within a sequence file or sequence. For
    /// valueType "named_type" a member is created as a full instance of the file-defined
    /// <paramref name="typeName"/> (container fields materialised). For "enum",
    /// <paramref name="typeName"/> is the enum data type and the value is <paramref name="ordinal"/>
    /// (preferred) or <paramref name="value"/> (ordinal number or symbolic name).</summary>
    Task SetPropertyValueAsync(string filePath, string? sequenceName,
        string propertyName, string valueType, string? value, string? typeName = null,
        int? ordinal = null);
    /// <summary>Deletes a sub-property from a file global or sequence local variable container.</summary>
    Task DeleteSubPropertyAsync(string filePath, string? sequenceName,
        string propertyName);
    /// <summary>Creates/sets a property-tree node (and optionally its PropFlags) under any scope
    /// root — Parameters / Locals / FileGlobals / StationGlobals / SequenceFile — addressed by a
    /// dotted <paramref name="lookupString"/> relative to that root. The scope-generic counterpart
    /// of <see cref="SetPropertyValueAsync"/> (Locals/FileGlobals only) and
    /// <see cref="SetStepPropertyFlagsAsync"/> (step only). Missing intermediate containers are
    /// created when <paramref name="createMissingParents"/> is true. Reuses the same creation
    /// switch (scalar / container / reference / named_type / enum / array_elements).
    /// <paramref name="representation"/>/<paramref name="numberFormat"/> set a NUMBER node's width
    /// (float64/int64/uint64) and display format; <paramref name="clearFlags"/> makes the
    /// <paramref name="flags"/> write EXACT (also turning bits off) instead of OR-only.</summary>
    Task<PropertyNodeInfo> SetPropertyNodeAsync(string filePath, string scope,
        string? sequenceName, string lookupString, string valueType, string? typeName,
        string? value, int? ordinal, int? numElements, int? flags,
        bool createMissingParents, bool save,
        string? representation = null, string? numberFormat = null, bool clearFlags = false);
    /// <summary>Deletes a property-tree node (a top-level Parameter/variable OR a nested submember)
    /// under any scope root — Parameters / Locals / FileGlobals / StationGlobals / SequenceFile.
    /// Subsumes the missing delete_sequence_parameter. The scope-generic counterpart of
    /// <see cref="DeleteSubPropertyAsync"/> (Locals/FileGlobals only).</summary>
    Task DeletePropertyNodeAsync(string filePath, string scope, string? sequenceName,
        string lookupString, bool save);
    /// <summary>Returns all file-global variables defined in the given sequence file.</summary>
    Task<List<VariableInfo>> GetFileGlobalsAsync(string sequenceFilePath);
    /// <summary>Returns all station global variables for the connected engine.</summary>
    Task<List<VariableInfo>> GetStationGlobalsAsync();
    /// <summary>Recursively walks a property object (StationGlobals or a sequence file's
    /// FileGlobals, optionally descending to a sub-path) into a <see cref="PropertyNode"/>
    /// tree. Hidden subproperties are included by default and annotated via
    /// <see cref="PropertyNode.IsHidden"/>; arrays and containers are expanded.</summary>
    /// <para>With root='SequenceFile', <paramref name="sequenceName"/> (and optionally
    /// <paramref name="stepGroup"/>/<paramref name="stepName"/>) address a sequence or step BY NAME.
    /// The engine's lookup has no "Sequences" node — the real path is <c>Data.Seq[i].Main[j]</c>, which
    /// is neither guessable nor stable when steps move — so these resolve the indices for you and
    /// <paramref name="lookupString"/> then applies relative to the resolved object.</para>
    Task<PropertyNode> GetPropertyTreeAsync(string root, string? filePath, string? lookupString,
        int maxDepth, bool includeHidden, int maxArrayElements,
        string? sequenceName = null, string? stepGroup = null, string? stepName = null);
    /// <summary>Sets the value of a file-global variable in the given sequence file.</summary>
    Task SetFileGlobalAsync(string sequenceFilePath, string variableName, object value);
    /// <summary>Sets the comment/description of a file-global variable (or a nested container member
    /// via a dotted path, e.g. "MyCont.Field"). The FileGlobals counterpart of
    /// <see cref="SetLocalVariableCommentAsync"/>.</summary>
    Task SetFileGlobalCommentAsync(string sequenceFilePath, string variableName, string comment);
    /// <summary>Sets the value of a station global variable.</summary>
    Task SetStationGlobalAsync(string variableName, object value);
    /// <summary>Delete a StationGlobal and commit the change to disk. The delete counterpart
    /// to <see cref="SetStationGlobalAsync"/> (no-op if the global does not exist).</summary>
    Task DeleteStationGlobalAsync(string variableName);
    /// <summary>Inserts a new file-global variable of the specified data type into the given sequence file.</summary>
    Task InsertFileGlobalAsync(string sequenceFilePath, string variableName, string dataType);

    // Steps
    /// <summary>Returns all steps in the specified sequence.</summary>
    Task<List<StepInfo>> GetStepsAsync(string sequenceFilePath, string sequenceName);
    /// <summary>Returns the details of a single named step in the specified sequence.</summary>
    Task<StepInfo> GetStepAsync(string sequenceFilePath, string sequenceName, string stepName);
    /// <summary>Enables or disables the specified step.</summary>
    Task EnableStepAsync(string sequenceFilePath, string sequenceName, string stepName, bool enabled);
    /// <summary>Returns a dictionary of all properties for the specified step.</summary>
    Task<Dictionary<string, object>> GetStepPropertiesAsync(string sequenceFilePath,
        string sequenceName, string stepName);

    // Bulk writers
    /// <summary>Inserts many sequences in ONE call (and one save), in list order so the file's
    /// sequence indices match the input.</summary>
    Task<Dictionary<string, object>> InsertSequencesBulkAsync(string filePath,
        IReadOnlyList<(string Name, string? Description)> sequences, bool save = true);
    /// <summary>Inserts many variables into one scope (Locals / Parameters / FileGlobals) in ONE
    /// call and one save.</summary>
    Task<Dictionary<string, object>> InsertVariablesBulkAsync(string filePath, string scope,
        string? sequenceName, IReadOnlyList<VarModel> variables, bool save = true);
    /// <summary>Applies many property-node writes in ONE call and one save, strictly in list order.</summary>
    Task<Dictionary<string, object>> SetPropertyNodesBulkAsync(string filePath,
        IReadOnlyList<PropertyNodeSpec> nodes, bool save = true);
    /// <summary>Binds many module arguments on ONE step in a single call and save.</summary>
    Task<Dictionary<string, object>> SetModuleParametersBulkAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        IReadOnlyList<(string Name, string Value)> parameters, bool save = true);

    // Whole-file export / import
    /// <summary>Exports a sequence file as one complete, round-trippable authoring model — file
    /// metadata, type definitions with their attach state, file globals, and per sequence its
    /// description/result-recording, parameters, locals and steps INCLUDING every step property and
    /// module configuration a rebuild needs. Replaces the per-step reader traffic (a real 30-sequence
    /// rebuild spent the bulk of ~700 calls on reconnaissance alone).</summary>
    Task<SequenceFileModel> ExportSequenceFileAsync(string filePath, bool includeTypeDefs = true,
        string? sequenceName = null, IReadOnlyList<string>? sequenceNames = null);
    /// <summary>Rebuilds a sequence file from a model produced by <see cref="ExportSequenceFileAsync"/>.
    /// Order is fixed so cross-references resolve: types → file metadata/globals → all sequences with
    /// their interfaces → all steps (so every callee's parameters exist before a caller is
    /// configured). Returns per-item counts plus a warning for anything that could not be applied —
    /// a partial import is reported, never silently swallowed.</summary>
    Task<ImportOutcome> ImportSequenceFileAsync(SequenceFileModel model, string destFilePath,
        bool copyTypeDefs = true, bool save = true, string labViewPanes = "copy",
        int prototypeTimeoutSeconds = 120, string crossFilePrototypes = "copy",
        bool keepUnusedTypes = true, string variables = "copy");

    // Sequence Analyzer
    /// <summary>Runs the TestStand Sequence Analyzer on the given file and returns any messages.</summary>
    /// <summary>Runs the TestStand Sequence Analyzer on the given file and returns any messages.
    /// <paramref name="timeoutSeconds"/> bounds the AnalyzerApp.exe child: a COLD analysis that loads
    /// LabVIEW .lvlibp or Python code modules legitimately takes many minutes (~8.5 min measured on a
    /// 30-sequence file), so the default is generous; the call throws rather than hanging forever.</summary>
    Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath,
        int timeoutSeconds = TestStandService.DefaultAnalyzerTimeoutSeconds);

    // Reports
    /// <summary>Generates a report for the specified execution and writes it to the output path.</summary>
    Task<ReportInfo> GenerateReportAsync(string executionId, string outputPath,
        string format = "HTML");
    /// <summary>Returns the report text for the specified execution.</summary>
    Task<string> GetReportTextAsync(string executionId);

    // UUT / Batch
    /// <summary>Returns UUT (Unit Under Test) information for the specified execution.</summary>
    Task<UutInfo> GetUutInfoAsync(string executionId);
    /// <summary>Sets the UUT serial number for the specified execution.</summary>
    Task SetUutSerialNumberAsync(string executionId, string serialNumber);
    /// <summary>Sets the UUT part number for the specified execution.</summary>
    Task SetUutPartNumberAsync(string executionId, string partNumber);

    // Adapters
    /// <summary>Returns a list of all currently loaded adapters.</summary>
    Task<List<AdapterInfo>> GetLoadedAdaptersAsync();
    /// <summary>Loads the specified adapter into the engine.</summary>
    Task LoadAdapterAsync(string adapterName);
    /// <summary>Unloads the specified adapter from the engine.</summary>
    Task UnloadAdapterAsync(string adapterName);

    // Logging
    /// <summary>Returns the execution log entries for the specified execution.</summary>
    Task<List<LogEntry>> GetExecutionLogAsync(string executionId, int maxEntries = 100);
    /// <summary>Clears the execution log for the specified execution.</summary>
    Task ClearLogAsync(string executionId);

    // Process Model
    /// <summary>Returns the path to the current process model sequence file.</summary>
    Task<string> GetProcessModelAsync();
    /// <summary>Sets the active process model to the specified sequence file path.</summary>
    Task SetProcessModelAsync(string processModelPath);

    // Database / Result Schema
    /// <summary>Returns the names of all available result schemas.</summary>
    Task<List<string>> GetResultSchemasAsync();
    /// <summary>Exports execution results using the specified schema to the output path.</summary>
    Task<string> ExportResultsAsync(string executionId, string schemaName, string outputPath);

    // Type Palettes
    /// <summary>Returns all loaded type palettes.</summary>
    Task<List<TypePaletteInfo>> GetTypePalettesAsync();
    /// <summary>Loads the type palette file at the specified path.</summary>
    Task LoadTypePaletteAsync(string palettePath);
    /// <summary>Unloads the type palette file at the specified path.</summary>
    Task UnloadTypePaletteAsync(string palettePath);
    /// <summary>Returns all step types available, optionally filtered by palette file.</summary>
    Task<List<StepTypeInfo>> GetStepTypesAsync(string? paletteFile = null);
    /// <summary>Returns detailed information about the named step type.</summary>
    Task<StepTypeInfo> GetStepTypeAsync(string stepTypeName);
    /// <summary>Returns all data types defined, optionally scoped to a sequence file.</summary>
    Task<List<DataTypeInfo>> GetDataTypesAsync(string? sequenceFilePath = null);
    /// <summary>Lists every custom data TYPE embedded in a file's TypeUsageList — including the
    /// LabVIEW-cluster container typedefs that <see cref="GetDataTypesAsync"/> cannot see (those live
    /// in the TypeUsageList, not as file-root subproperties). Each entry carries the type name,
    /// whether it is attached to the file, and a coarse kind.</summary>
    Task<List<DataTypeInfo>> GetFileTypeDefsAsync(string filePath);
    /// <summary>Copies custom data type definitions from one sequence file into another. This is the
    /// ONLY way to reproduce LabVIEW-cluster typedefs in a rebuilt file — they carry GUIDs/structure
    /// that cannot be recreated field-by-field. Pass explicit <paramref name="typeNames"/> (reliable)
    /// or null to copy every embedded type. Types already present in the destination are left
    /// untouched. <paramref name="attach"/> controls the destination's IsTypeAttachedToFile state:
    /// <c>preserve</c> (default) mirrors the SOURCE's per-type attach flag — required for a 1:1
    /// rebuild, because attaching a type the original does NOT embed adds it to the destination's
    /// embedded-type set and shows up as a FileDiffer difference; <c>all</c> attaches every copied
    /// type (the pre-2026-07-29 behaviour); <c>none</c> attaches nothing. A type is inserted into the
    /// TypeUsageList either way, so GUID-based resolution of cloned sequences works in all modes.
    /// Returns the names actually copied.</summary>
    Task<List<string>> CopyTypeDefsAsync(string sourceFilePath, string destFilePath,
        IReadOnlyList<string>? typeNames = null, bool save = true, string attach = "preserve");

    /// <summary>Copies the file-level name/value ATTRIBUTES (a separate namespace from subproperties,
    /// reached via the file-root <c>PropertyObjectFile.Attributes</c>) from a SOURCE sequence file onto
    /// a DESTINATION file. Copies whatever the ENGINE exposes on the loaded file; each subtree is
    /// flag-preservingly cloned before it is attached. Pass explicit <paramref name="attributeNames"/>
    /// (top-level names) or null to copy all. LIMITATION: the Sequence Analyzer's ignored-message list
    /// (<c>NI.Analyzer.IgnoredMessages</c>) is NOT loaded into the in-memory object by the TestStand
    /// engine API — only FileDiffer's raw disk reader sees it — so it CANNOT be read or reproduced here;
    /// a rebuild retains that one cosmetic 'Attributes' diff. Returns the attribute names copied.</summary>
    Task<Dictionary<string, object>> CopyFileAttributesAsync(string sourceFilePath, string destFilePath,
        IReadOnlyList<string>? attributeNames = null, bool save = true);

    /// <summary>Copies the FILE GLOBAL variables (the <c>FileGlobalDefaults</c> container — every file
    /// global with its exact type, value, comment, PropFlags and nested container/enum members) from a
    /// SOURCE sequence file onto a DESTINATION file via a flag-preserving deep clone. File globals are
    /// NOT part of any sequence, so duplicate_sequence / copy_step_module do not carry them — this is the
    /// reliable way to reproduce them in a 1:1 rebuild (including enum ordinals, Object References and
    /// typed container members, with the FileDiffer's [val]/{val} explicit-vs-default distinction
    /// preserved). Referenced data types must already exist in the destination (run copy_typedefs first).
    /// Pass explicit <paramref name="globalNames"/> to copy only those top-level globals, or null to
    /// replace the destination's entire file-globals set with the source's. Returns the names copied.</summary>
    Task<Dictionary<string, object>> CopyFileGlobalsAsync(string sourceFilePath, string destFilePath,
        IReadOnlyList<string>? globalNames = null, bool save = true);

    // Engine Info & Control
    /// <summary>Returns the filesystem paths used by the TestStand engine.</summary>
    Task<EnginePaths> GetEnginePathsAsync();
    /// <summary>Checks whether the given expression is syntactically valid.</summary>
    Task<ExpressionCheckResult> CheckExpressionAsync(string expression, string? sequenceFilePath = null);
    /// <summary>Expands TestStand path macros (e.g. &lt;TESTSTANDDIR&gt;) in the given path string.</summary>
    Task<string> ExpandPathMacrosAsync(string path);
    /// <summary>Searches the TestStand search directories for the given filename and returns the full path.</summary>
    Task<string> FindFileAsync(string filename);
    /// <summary>Breaks (pauses) all active executions.</summary>
    Task BreakAllAsync();
    /// <summary>Aborts all active executions.</summary>
    Task AbortAllAsync();
    /// <summary>Terminates all active executions.</summary>
    Task TerminateAllAsync();
    /// <summary>Returns the current station options from the engine.</summary>
    Task<StationOptionsInfo> GetStationOptionsAsync();
    /// <summary>Applies the given station options to the engine.</summary>
    Task SetStationOptionsAsync(StationOptionsInfo options);

    // Execution Debug Control
    /// <summary>Breaks (pauses) the specified execution.</summary>
    Task BreakExecutionAsync(string executionId);
    /// <summary>Resumes the specified paused execution.</summary>
    Task ResumeExecutionAsync(string executionId);
    /// <summary>Aborts the specified execution.</summary>
    Task AbortExecutionAsync(string executionId);
    /// <summary>Restarts the specified execution from the beginning.</summary>
    Task RestartExecutionAsync(string executionId);
    /// <summary>Executes one step in the current execution and stops before the next step.</summary>
    Task StepOverAsync(string executionId);
    /// <summary>Steps into the current step (entering a subsequence if applicable).</summary>
    Task StepIntoAsync(string executionId);
    /// <summary>Steps out of the current subsequence back to the caller.</summary>
    Task StepOutAsync(string executionId);

    // Sequence File Operations
    /// <summary>Deletes the named sequence from the specified sequence file.</summary>
    Task DeleteSequenceAsync(string filePath, string sequenceName);
    /// <summary>Returns whether a sequence with the given name exists in the file.</summary>
    Task<bool> SequenceNameExistsAsync(string filePath, string sequenceName);
    /// <summary>Renames a sequence within the specified sequence file.</summary>
    Task RenameSequenceAsync(string filePath, string oldName, string newName);

    // Sequence Operations
    /// <summary>Deletes the named step from the specified sequence and step group.</summary>
    Task DeleteStepAsync(string filePath, string sequenceName, string stepGroup, string stepName);
    /// <summary>Moves the named step to a new position within the step group.</summary>
    Task MoveStepAsync(string filePath, string sequenceName, string stepGroup, string stepName, int newIndex);
    /// <summary>Returns whether a step with the given name exists in the specified sequence.</summary>
    Task<bool> StepNameExistsAsync(string filePath, string sequenceName, string stepName);
    /// <summary>Returns all parameters defined for the specified sequence.</summary>
    Task<List<ParameterInfo>> GetSequenceParametersAsync(string filePath, string sequenceName);
    /// <summary>Inserts a new parameter into the specified sequence. When <paramref name="passByReference"/>
    /// is supplied it decides BY VALUE (false) vs BY REFERENCE (true); when null the legacy
    /// <paramref name="direction"/> mapping is used (InOut/byref → by reference, else by value).</summary>
    /// <para><paramref name="representation"/> (float64/int64/uint64) and <paramref name="numberFormat"/>
    /// (e.g. <c>%#.4x</c>) set a NUMBER parameter's width and display format.</para>
    Task InsertSequenceParameterAsync(string filePath, string sequenceName, string paramName,
        string dataType, string direction = "Input", string? defaultValue = null,
        bool? passByReference = null, string? representation = null, string? numberFormat = null);
    /// <summary>Deletes the specified local variable from the given sequence.</summary>
    Task DeleteLocalVariableAsync(string filePath, string sequenceName, string variableName);
    /// <summary>Reads the expressions and declared scopes needed to audit Locals./Parameters./
    /// FileGlobals. references in a built sequence. When <paramref name="sequenceName"/> is null
    /// or empty, every sequence in the file is read. Pure COM read — the auditing itself is done
    /// by the engine-free <c>TestStandMCP.Tools.ReferenceAuditor</c>.</summary>
    Task<ReferenceAuditData> ReadReferenceAuditDataAsync(string filePath, string? sequenceName = null);
    /// <summary>Returns all step templates available in the specified sequence file.</summary>
    Task<List<StepTemplateInfo>> GetStepTemplatesAsync(string filePath);
    /// <summary>Inserts a step based on the named template into the specified sequence.</summary>
    Task InsertStepFromTemplateAsync(string filePath, string sequenceName, string stepGroup,
        string templateName, string newStepName, int index = -1);
    /// <summary>Returns sequence-level properties (e.g. run-mode, description) for the given sequence.</summary>
    Task<SequenceProperties> GetSequencePropertiesAsync(string filePath, string sequenceName);
    /// <summary>Applies sequence-level property changes to the specified sequence.</summary>
    Task SetSequencePropertiesAsync(string filePath, string sequenceName, SequenceProperties props);

    // Step Property Operations
    /// <summary>Renames the specified step within a sequence.</summary>
    Task RenameStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string newName);
    /// <summary>Sets the description comment on the specified step.</summary>
    Task<string> SetStepCommentAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string comment);
    /// <summary>Sets the run mode (Normal, Skip, etc.) for the specified step.</summary>
    Task SetStepRunModeAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string runMode);
    /// <summary>Sets the precondition expression for the specified step.</summary>
    Task SetStepPreconditionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string precondition);
    /// <summary>Sets the pass action (e.g. Continue, Goto) for the specified step.</summary>
    Task SetStepPassActionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string passAction, string? target = null);
    /// <summary>Sets the fail action (e.g. Continue, Goto) for the specified step.</summary>
    Task SetStepFailActionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string failAction, string? target = null);
    /// <summary>Configures the loop settings for the specified step. For a 'Custom' loop pass any of
    /// <paramref name="initExpr"/> / <paramref name="whileExpr"/> / <paramref name="incExpr"/> /
    /// <paramref name="statusExpr"/>.</summary>
    Task SetStepLoopAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string loopType, string? initExpr = null,
        string? whileExpr = null, string? incExpr = null, string? statusExpr = null);
    /// <summary>Sets the flow-control CONDITION on a branch step — the dedicated property the
    /// engine evaluates to branch (NOT Pre/Post/Status). Writes <c>ConditionExpr</c> for
    /// NI_Flow_If/ElseIf/While/DoWhile and <c>ItemExpr</c> for NI_Flow_Select (switch) /
    /// NI_Flow_Case (case value(s)). Optionally marks a Case as the default branch.</summary>
    Task SetFlowConditionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string condition, bool? isDefault = null);
    /// <summary>Configures a counted NI_Flow_For loop by writing its InitializationExpr /
    /// ConditionExpr / IncrementExpr. Either supply <paramref name="count"/> (+ optional
    /// <paramref name="indexVar"/>) to generate the standard 0..count-1 counted loop, or pass any of
    /// the explicit expressions (which take precedence). Returns the three effective expressions.</summary>
    Task<ForLoopConfigResult> ConfigureForLoopAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, int? count = null, string? indexVar = null,
        string? initExpr = null, string? conditionExpr = null, string? incrementExpr = null,
        bool save = true);
    /// <summary>Configures an NI_Flow_ForEach loop by writing its ArrayExpr (the collection to
    /// iterate) and ArrayElementExpr (the per-element variable), plus optional Offset/Subscript.
    /// Rejects a non-ForEach step. Returns the effective expressions.</summary>
    Task<ForEachLoopConfigResult> ConfigureForEachLoopAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string? arrayExpr = null, string? elementExpr = null,
        string? offsetExpr = null, string? subscriptExpr = null, bool save = true);
    /// <summary>Sets the result-recording option for the specified step.</summary>
    Task SetStepRecordResultAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string recordingOption);
    /// <summary>Sets the evaluate-precondition option for the specified step.</summary>
    Task SetStepEvalPrecondAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    /// <summary>Sets the module load option for the specified step.</summary>
    Task SetStepModuleLoadOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    /// <summary>Sets the module unload option for the specified step.</summary>
    Task SetStepModuleUnloadOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    /// <summary>Sets the batch synchronization option for the specified step.</summary>
    Task SetStepBatchSyncOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    /// <summary>Changes the adapter associated with the specified step.</summary>
    Task ChangeStepAdapterAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string newAdapter);
    /// <summary>Returns the unique ID assigned to the specified step.</summary>
    Task<string> GetStepUniqueIdAsync(string filePath, string sequenceName, string stepGroup,
        string stepName);

    // Report Operations
    /// <summary>Saves the execution report to the specified output path.</summary>
    Task SaveReportAsync(string executionId, string outputPath, string format = "HTML");
    /// <summary>Launches the report viewer for the specified execution.</summary>
    Task LaunchReportViewerAsync(string executionId);
    /// <summary>Returns the full report content as a string for the specified execution.</summary>
    Task<string> GetFullReportAsync(string executionId);

    // Undo/Redo
    /// <summary>Returns information about the current undo stack for the given file.</summary>
    Task<UndoStackInfo> GetUndoStackAsync(string? filePath = null);
    /// <summary>Undoes the last operation for the given file and returns whether the undo succeeded.</summary>
    Task<bool> UndoAsync(string? filePath = null);
    /// <summary>Redoes the last undone operation for the given file and returns whether it succeeded.</summary>
    Task<bool> RedoAsync(string? filePath = null);
    /// <summary>Begins a named undo group so that subsequent edits can be undone as a single unit.</summary>
    Task BeginUndoGroupAsync(string groupName, string? filePath = null);
    /// <summary>Ends the current undo group, committing it to the undo stack.</summary>
    Task EndUndoGroupAsync(string? filePath = null);
    /// <summary>Cancels the current undo group, discarding all edits made since it was started.</summary>
    Task CancelUndoGroupAsync(string? filePath = null);

    // Sequence File Comparison
    /// <summary>Compares two sequence files and returns the structural differences between them.</summary>
    Task<SequenceFileDiff> CompareSequenceFilesAsync(string filePath1, string filePath2);

    /// <summary>Runs the native TestStand FileDiffer and returns its detailed, classified diff report.</summary>
    Task<FileDifferReport> DiffSequenceFilesAsync(string filePath1, string filePath2);

    // Sync Manager
    /// <summary>Returns all synchronization objects registered with the SyncManager.</summary>
    Task<List<SyncObjectInfo>> GetSyncObjectsAsync();
    /// <summary>Creates a new synchronization object of the specified type.</summary>
    Task CreateSyncObjectAsync(string name, string type, int initialValue = 1, int maxValue = 1);
    /// <summary>Deletes the synchronization object with the specified name.</summary>
    Task DeleteSyncObjectAsync(string name);
    /// <summary>Waits to acquire the specified semaphore synchronization object.</summary>
    Task SyncSemaphoreWaitAsync(string name, double timeoutSeconds = 30);
    /// <summary>Releases the specified semaphore synchronization object.</summary>
    Task SyncSemaphoreReleaseAsync(string name);
    /// <summary>Acquires the specified mutex synchronization object.</summary>
    Task SyncMutexLockAsync(string name, double timeoutSeconds = 30);
    /// <summary>Releases the specified mutex synchronization object.</summary>
    Task SyncMutexUnlockAsync(string name);
    /// <summary>Enqueues a value into the specified queue synchronization object.</summary>
    Task SyncQueueEnqueueAsync(string name, string value);
    /// <summary>Dequeues and returns the next value from the specified queue synchronization object.</summary>
    Task<string> SyncQueueDequeueAsync(string name, double timeoutSeconds = 30);
    /// <summary>Flushes all pending values from the specified queue synchronization object.</summary>
    Task SyncQueueFlushAsync(string name);
    /// <summary>Sets the specified notification synchronization object to the signaled state.</summary>
    Task SyncNotificationSetAsync(string name, string value = "");
    /// <summary>Resets the specified notification synchronization object to the non-signaled state.</summary>
    Task SyncNotificationResetAsync(string name);
    /// <summary>Waits for the specified notification synchronization object to become signaled.</summary>
    Task<string> SyncNotificationWaitAsync(string name, double timeoutSeconds = 30);

    // Advanced Adapter Introspection
    /// <summary>Returns detailed information about the specified adapter.</summary>
    Task<AdapterDetailInfo> GetAdapterDetailsAsync(string adapterName);
    /// <summary>Returns code-module information for the specified step.</summary>
    Task<StepModuleInfo> GetStepModuleInfoAsync(string filePath, string sequenceName,
        string stepGroup, string stepName);

    // Search
    /// <summary>Searches for steps matching the given pattern in the specified sequence file.</summary>
    Task<SearchResult> SearchStepsAsync(string filePath, string pattern,
        string searchIn = "all", bool caseSensitive = false);

    // Thread-Level Execution Control
    /// <summary>Returns all threads currently active in the specified execution.</summary>
    Task<List<ThreadInfo>> GetExecutionThreadsAsync(string executionId);
    /// <summary>Returns the current status of the specified thread within an execution.</summary>
    Task<ThreadInfo> GetThreadStatusAsync(string executionId, string threadId);
    /// <summary>Breaks (pauses) the specified thread within an execution.</summary>
    Task BreakThreadAsync(string executionId, string threadId);
    /// <summary>Resumes the specified paused thread within an execution.</summary>
    Task ResumeThreadAsync(string executionId, string threadId);
    /// <summary>Steps over the current step in the specified thread.</summary>
    Task StepOverThreadAsync(string executionId, string threadId);
    /// <summary>Steps into the current step in the specified thread.</summary>
    Task StepIntoThreadAsync(string executionId, string threadId);
    /// <summary>Steps out of the current subsequence in the specified thread.</summary>
    Task StepOutThreadAsync(string executionId, string threadId);
    /// <summary>Returns the call stack for the specified thread within an execution.</summary>
    Task<List<CallStackFrame>> GetThreadCallStackAsync(string executionId, string threadId);

    // Live Thread-Context Inspection (runtime debugging)
    /// <summary>Dumps the live SequenceContext/RunState tree of a thread's call-stack frame as a
    /// nested <see cref="PropertyNode"/>. <paramref name="scope"/> picks the sub-tree
    /// (full/ThisContext/RunState/Locals/Parameters/Step/Sequence); <paramref name="lookupString"/>
    /// descends further.</summary>
    Task<PropertyNode> InspectThreadContextAsync(string executionId, string? threadId,
        int callStackIndex, string scope, string? lookupString, int maxDepth,
        bool includeHidden, int maxArrayElements);
    /// <summary>Evaluates an expression in a live thread frame's context — the scope where
    /// <c>Locals.</c>/<c>Parameters.</c>/<c>RunState.</c> resolve (which evaluate_expression cannot reach).</summary>
    Task<ExpressionResult> EvaluateInThreadContextAsync(string executionId, string? threadId,
        int callStackIndex, string expression);
    /// <summary>Reads a single variable/property by path (relative to ThisContext) in a live thread frame.</summary>
    Task<RuntimeVariableInfo> GetRuntimeVariableAsync(string executionId, string? threadId,
        int callStackIndex, string propertyPath);
    /// <summary>Writes a single variable/property by path in a live thread frame (e.g. set
    /// <c>RunState.NextStepIndex</c> — the "Set Next Step" debugger action). Reads the value back.</summary>
    Task<RuntimeVariableInfo> SetRuntimeVariableAsync(string executionId, string? threadId,
        int callStackIndex, string propertyPath, string value, string? valueType);
    /// <summary>Returns a curated flat snapshot of the most-used RunState fields for a live thread frame.</summary>
    Task<RunStateSummary> GetRunStateSummaryAsync(string executionId, string? threadId,
        int callStackIndex);

    // Numeric/String Limit Configuration
    /// <summary>Sets the numeric limits (low, high, units, comparison) for a NumericLimitTest step.</summary>
    Task SetNumericLimitsAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, double? lowLimit, double? highLimit, string? units,
        string comparisonType = "GELE");
    /// <summary>Returns the numeric limits configured on the specified NumericLimitTest step.</summary>
    Task<Dictionary<string, object?>> GetNumericLimitsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName);
    /// <summary>Sets the measurement expression on the specified test step.</summary>
    Task SetStepMeasurementAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression);
    /// <summary>Configures an NI_Wait step to wait a fixed time (seconds) — sets the wait mode to
    /// "time interval" and the time expression. <paramref name="timeExpression"/> may be a literal
    /// number ("2.5") or any TestStand expression that evaluates to seconds.</summary>
    Task SetWaitTimeAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string timeExpression);
    /// <summary>Configures an NI_Wait step's wait TARGET: 'time' (seconds), 'thread' (a thread
    /// reference expression, e.g. FileGlobals.X) or 'execution'. Sets WaitForTarget + the matching
    /// expression, optional timeout, and clears the "specify by sequence call" flags a fresh NI_Wait
    /// otherwise carries. Broader than SetWaitTimeAsync (which only does the time mode).</summary>
    Task ConfigureWaitAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string waitMode, string? expression = null,
        string? timeoutExpr = null, bool? timeoutEnabled = null, bool? errorOnTimeout = null);
    /// <summary>Configures an <c>NI_LV_RunVIAsynchronously</c> ("Run VI Asynchronously") step in one
    /// call: builds the Sequence-adapter <c>SeqCallStepAdditions</c> launch module (which the step does
    /// NOT get from a plain insert — it comes up as <c>NoneStepAdditions</c>, and a plain adapter switch
    /// corrupts the step), sets the async-launch defaults (SFPathExpr/SeqNameExpr/SpecifyByExpr/
    /// UsePrototype/ThreadOpt/AutoWaitAsync/CustomThreadAffinity), stores the VI in the step-own
    /// <c>VIModule.ViCall</c> (VIPath/Namespace) and sets the module marker flag. Returns the applied settings.</summary>
    Task<Dictionary<string, object>> ConfigureRunViAsyncAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string viPath, string? viNamespace = null,
        int threadOption = 1, string? threadRefExpr = null, bool autoWait = true,
        string sequenceNameExpr = "\"MainSequence\"",
        string sequenceFileExpr = "Evaluate(Step.SequenceFileExpr)", bool save = true);
    /// <summary>Configures a StringValueTest step with the expression, expected value, and comparison type.</summary>
    Task ConfigureStringValueTestAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression, string expectedValue,
        string comparisonType = "CaseSensitive");

    // Breakpoints
    /// <summary>Enables or disables a breakpoint on the specified step.</summary>
    Task SetStepBreakpointAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, bool enabled, string breakpointType = "Before");
    /// <summary>Returns all breakpoints defined in the specified sequence file.</summary>
    Task<List<Dictionary<string, string>>> GetBreakpointsAsync(string filePath);

    // Execution Results
    /// <summary>Returns the result of the specified step within a completed execution.</summary>
    Task<Dictionary<string, object?>> GetStepResultAsync(string executionId,
        string sequenceName, string stepName);
    /// <summary>Returns all result data for the specified execution.</summary>
    Task<Dictionary<string, object?>> GetExecutionResultsAsync(string executionId);
    /// <summary>Returns the total elapsed time in seconds for the specified execution.</summary>
    Task<double> GetExecutionTimeAsync(string executionId);

    // Workspace
    /// <summary>Opens the workspace file at the given path and returns its metadata.</summary>
    Task<WorkspaceInfo> OpenWorkspaceAsync(string workspacePath);
    /// <summary>Returns information about the currently open workspace.</summary>
    Task<WorkspaceInfo> GetWorkspaceAsync();

    // Watch Expressions
    /// <summary>Adds a watch expression and returns its assigned index.</summary>
    Task<int> AddWatchExpressionAsync(string expression, string? label = null);
    /// <summary>Returns all currently registered watch expressions.</summary>
    Task<List<WatchExpressionInfo>> GetWatchExpressionsAsync();
    /// <summary>Removes the watch expression at the specified index.</summary>
    Task RemoveWatchExpressionAsync(int index);

    // Callbacks
    /// <summary>Returns all callbacks defined in the specified sequence file.</summary>
    Task<List<CallbackInfo>> GetCallbacksAsync(string filePath);
    /// <summary>Adds an override of a model/engine callback (e.g. "PreUUT", "PostUUT") to the
    /// sequence file — same as the editor's "Sequence File Callbacks → Add". When
    /// <paramref name="copyDefaultSteps"/> is true the model's default steps are copied in (so e.g.
    /// the "Call DoPreUUT" dialog step can then be set to Skip). Returns the override sequence name.</summary>
    Task<string> AddCallbackOverrideAsync(string filePath, string callbackName, bool copyDefaultSteps = true);

    // File Properties
    /// <summary>Returns the file-level properties (comment, version, etc.) for the given sequence file.</summary>
    Task<FilePropertiesInfo> GetFilePropertiesAsync(string filePath);
    /// <summary>Sets file-level properties (comment, version) on the given sequence file.</summary>
    Task SetFilePropertiesAsync(string filePath, string? comment = null, string? version = null);

    // Duplicate Sequence
    /// <summary>Duplicates the named sequence into a new sequence (optionally in a different file) and returns the new sequence name.</summary>
    Task<string> DuplicateSequenceAsync(string sourceFilePath, string sourceSequenceName,
        string newSequenceName, string? targetFilePath = null);

    // Array Variable Operations
    /// <summary>Returns the elements of an array variable from a sequence file or sequence.</summary>
    Task<List<ArrayElementInfo>> GetArrayVariableAsync(string filePath,
        string? sequenceName, string variableName, int maxElements = 100);
    /// <summary>Sets the value of a single element in an array variable.</summary>
    Task SetArrayElementAsync(string filePath, string? sequenceName,
        string variableName, int index, string value);
    /// <summary>Resizes an array variable to the specified number of elements.</summary>
    Task ResizeArrayVariableAsync(string filePath, string? sequenceName,
        string variableName, int newSize);

    // Data Type Operations
    /// <summary>Creates a new custom data type in the specified sequence file.</summary>
    Task<DataTypeInfo> CreateDataTypeAsync(string filePath, string typeName,
        string baseType = "Object");
    /// <summary>Deletes the named custom data type from the specified sequence file.</summary>
    Task DeleteDataTypeAsync(string filePath, string typeName);

    // ── Enumeration Data Types ───────────────────────────────────────────────
    /// <summary>Creates a new enumeration data type with the given name→value constants.</summary>
    Task<EnumInfo> CreateEnumAsync(string filePath, string enumName,
        IReadOnlyList<EnumValueInfo> values, bool save = true);
    /// <summary>Returns the named enum's constants (name → numeric value) in definition order.</summary>
    Task<EnumInfo> GetEnumValuesAsync(string filePath, string enumName);
    /// <summary>Replaces the entire enumerator list of the named enum.</summary>
    Task<EnumInfo> SetEnumValuesAsync(string filePath, string enumName,
        IReadOnlyList<EnumValueInfo> values, bool save = true);
    /// <summary>Appends a single enumerator to the named enum (auto-value = max+1 when omitted).</summary>
    Task<EnumInfo> AddEnumValueAsync(string filePath, string enumName,
        string valueName, double? value = null, bool save = true);
    /// <summary>Removes a single enumerator (by name) from the named enum.</summary>
    Task<EnumInfo> RemoveEnumValueAsync(string filePath, string enumName,
        string valueName, bool save = true);
    /// <summary>Renames an enumerator (via OldEnumeratorName), optionally changing its value.</summary>
    Task<EnumInfo> RenameEnumValueAsync(string filePath, string enumName,
        string oldName, string newName, double? value = null, bool save = true);
    /// <summary>Deletes the named enum data type from the specified sequence file.</summary>
    Task DeleteEnumAsync(string filePath, string enumName, bool save = true);

    // Module Parameter Operations
    /// <summary>Returns the code-module parameters for the specified step.</summary>
    Task<List<ModuleParameterInfo>> GetModuleParametersAsync(string filePath,
        string sequenceName, string stepGroup, string stepName);
    /// <summary>Sets the value of a code-module parameter on the specified step.</summary>
    Task SetModuleParameterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string parameterName, string value,
        bool useExpression = true);

    // Step Configuration
    /// <summary>Configures a MessagePopup step with the given message, title, buttons, and timeout.</summary>
    Task ConfigureMessagePopupAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string message,
        string? title = null, string buttons = "OK", double timeout = -1);
    /// <summary>Configures a PropertyLoader step with the specified file path expression and mode.</summary>
    Task ConfigurePropertyLoaderAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string filePathExpr, string mode = "Read");

    // ── User & Privilege Management (Engine.UsersFile / User) ────────────────
    /// <summary>Returns all users defined in the TestStand users file.</summary>
    Task<List<UserInfo>> GetUsersAsync();
    /// <summary>Returns information about the currently logged-in user, or null if no user is logged in.</summary>
    Task<UserInfo?> GetCurrentUserAsync();
    /// <summary>Returns whether a user with the given login name exists.</summary>
    Task<bool> UserNameExistsAsync(string loginName);
    /// <summary>Creates a new TestStand user with the specified credentials and optional profile.</summary>
    Task CreateUserAsync(string loginName, string fullName, string password,
        string? profileName = null, bool persist = true);
    /// <summary>Deletes the user with the specified login name.</summary>
    Task DeleteUserAsync(string loginName, bool persist = true);
    /// <summary>Changes the password for the specified user.</summary>
    Task SetUserPasswordAsync(string loginName, string password, bool persist = true);
    /// <summary>Returns the list of privilege names assigned to the specified user.</summary>
    Task<List<string>> GetUserPrivilegesAsync(string loginName);
    /// <summary>Checks whether the specified user has the given privilege.</summary>
    Task<bool> CheckUserPrivilegeAsync(string loginName, string privilege);
    /// <summary>Returns the names of all available user profiles.</summary>
    Task<List<string>> GetUserProfilesAsync();

    // ── Native Find / Replace (PropertyObject.Search / SearchMatch) ──────────
    /// <summary>Searches the sequence file for occurrences of the given pattern.</summary>
    Task<FindReplaceResult> FindInFileAsync(string filePath, string pattern,
        bool matchCase = false, bool wholeWord = false, bool regex = false,
        string elements = "all", int maxResults = 500);
    /// <summary>Searches and replaces occurrences of a pattern throughout the sequence file.</summary>
    Task<FindReplaceResult> ReplaceInFileAsync(string filePath, string pattern,
        string replacement, bool matchCase = false, bool wholeWord = false,
        bool regex = false, string elements = "all", bool save = true);

    // ── Typed Adapter / Code-Module Configuration ───────────────────────────
    /// <summary>Configures a step to call a .NET method by specifying the assembly, class, and method.
    /// When <paramref name="loadPrototype"/> is true (default) the method prototype is loaded afterwards
    /// so the step's parameter interface is populated (editor "Load Prototype").</summary>
    Task<ModuleConfigResult> ConfigureDotNetModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string assemblyPath,
        string className, string methodName, bool save = true, bool loadPrototype = true);
    /// <summary>Configures a step to call a native DLL function. When <paramref name="loadPrototype"/>
    /// is true (default) the function prototype is loaded afterwards to populate the parameters.</summary>
    Task<ModuleConfigResult> ConfigureDllModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string dllPath,
        string functionName, bool save = true, bool loadPrototype = true);
    /// <summary>Configures a step to call a LabVIEW VI. When <paramref name="loadPrototype"/> is true
    /// (default) the VI's connector pane is loaded afterwards to populate the parameter interface —
    /// this requires the VI to be loadable (LabVIEW available / not an unloadable .lvlibp).</summary>
    Task<ModuleConfigResult> ConfigureLabViewModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string viPath,
        bool save = true, bool loadPrototype = true);
    /// <summary>Configures a step to call a Python function. When <paramref name="loadPrototype"/> is
    /// true (default) the prototype is loaded afterwards to populate the parameters.</summary>
    /// <para>Beyond module path + function name this also writes the settings the Python adapter keeps
    /// in the STEP's own tree (<c>TS.SData.PythonCall.*</c>): <paramref name="className"/>,
    /// <paramref name="classInstanceLocation"/>, <paramref name="operationType"/> /
    /// <paramref name="operationScope"/> (module function vs. constructor vs. method on an instance),
    /// the interpreter session settings (<paramref name="pythonVersion"/>,
    /// <paramref name="virtualEnvPath"/>, <paramref name="useAdapterInterpreterSettings"/>) and the
    /// explicit argument list <paramref name="parameters"/>. Without these an object-oriented Python
    /// step cannot be reproduced at all — the prototype cannot be loaded headlessly for an arbitrary
    /// module, so the argument list has to be authored.</para>
    Task<ModuleConfigResult> ConfigurePythonModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string modulePath,
        string functionName, bool save = true, bool loadPrototype = true,
        string? className = null, string? classInstanceLocation = null,
        int? operationType = null, int? operationScope = null,
        string? pythonVersion = null, string? virtualEnvPath = null,
        bool? useAdapterInterpreterSettings = null,
        IReadOnlyList<PythonParamSpec>? parameters = null);
    /// <summary>Configures a SequenceCall step to call the specified target sequence. When
    /// <paramref name="loadPrototype"/> is true (default) the callee's parameter list is loaded into
    /// TS.SData.ActualArgs afterwards (editor "Load Prototype"). Optionally sets the threading/async
    /// options: <paramref name="executionMode"/> ('UseCurrentThread' / 'NewThread' /
    /// 'NewExecution' → SData.ThreadOpt), <paramref name="threadRefExpr"/> (SData.AsyncThreadExpr — an
    /// expression to store the new thread/execution reference, e.g. FileGlobals.X) and
    /// <paramref name="autoWait"/> (SData.AutoWaitAsync — wait for the async subsequence at end of the
    /// current sequence).</summary>
    /// <para>When <paramref name="targetSequenceFile"/> is empty the call targets the CURRENT file
    /// (UseCurFile). TestStand still stores a sequence-file path on the step in that case, so it is
    /// defaulted to this file's own name; <paramref name="storedFilePath"/> overrides it verbatim,
    /// which is the only way to reproduce an original that retained a stale path.</para>
    Task<ModuleConfigResult> ConfigureSequenceCallModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        string targetSequenceName, string targetSequenceFile = "", bool save = true,
        string? executionMode = null, string? threadRefExpr = null, bool? autoWait = null,
        bool loadPrototype = true, string? storedFilePath = null);
    /// <summary>Loads (refreshes) a step's code-module prototype — the programmatic equivalent of the
    /// Sequence Editor's "Load Prototype" action — so the step's parameter interface reflects the
    /// current target. Adapter-agnostic: works for LabVIEW VIs, DLL/CVI functions, .NET/ActiveX calls
    /// and SequenceCalls. Use it after the target's own interface changed (e.g. a subsequence's
    /// Parameters were edited) to re-sync the caller. Does NOT change the step's adapter.
    /// <para>IMPORTANT when the isolated worker runs (the default for LabVIEW): the worker is a
    /// separate PROCESS with its own engine and reads the file from DISK, so the step has to be SAVED
    /// first. Called after a run of <c>save:false</c> edits it reports the step as out of range —
    /// which reads like an unloadable VI but is really an unsaved file.</para></summary>
    /// <para><paramref name="calleeFiles"/> are extra sequence files the ISOLATED WORKER opens before
    /// the load — required for a cross-file SequenceCall, whose prototype cache is only filled when the
    /// callee file is loaded in the same engine.</para>
    Task<LoadPrototypeResult> LoadModulePrototypeAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, bool save = true,
        bool? isolate = null, int timeoutSeconds = 120, bool? async = null,
        string? labviewServer = null, IReadOnlyList<string>? calleeFiles = null);
    /// <summary>Returns the current state of an ASYNC prototype-load job started by
    /// <see cref="LoadModulePrototypeAsync"/> (async mode). While running, Status="running"; once done,
    /// Status="completed" and the full result fields are final (or "error" if the job itself faulted).
    /// Unknown/expired id → throws <see cref="KeyNotFoundException"/>.</summary>
    Task<LoadPrototypeResult> GetPrototypeLoadStatusAsync(string jobId);
    /// <summary>The in-process core of the prototype load (the actual native
    /// <c>Module.LoadPrototype</c> call). Used directly for non-LabVIEW adapters, for the in-process
    /// LabVIEW path (which attaches to the SAME running LabVIEW the editor uses), and BY the isolated
    /// worker process. See <see cref="LoadModulePrototypeAsync"/>.</summary>
    Task<LoadPrototypeResult> LoadPrototypeInProcessAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, bool save = true,
        string? labviewServer = null);
    /// <summary>Deep-copies a step's whole code-module subtree (TS.SData — incl. a SequenceCall's
    /// ActualArgs / a RunVIAsync's SeqCallStepAdditions — plus the step-own VIModule with its
    /// ViCall metadata: Namespace, VI Description, Connector-Pane Checksum and Parms) from a SOURCE
    /// step onto a TARGET step, and aligns the target's adapter. This is the reliable way to
    /// reproduce LabVIEW module metadata for VIs in a packed library (.lvlibp) that cannot be loaded
    /// headless (Load Prototype fails), so the connector pane cannot be regenerated — the cached
    /// metadata is copied verbatim instead. The module types must exist in the target file (use
    /// copy_typedefs first). Does NOT load LabVIEW.
    /// <para><paramref name="paths"/> restricts which subtrees are copied (null = all of them, which is
    /// what the MCP tool does). Passing a subset also SKIPS the adapter alignment, so an internal caller
    /// can add authored step config to a step whose module is already configured.</para></summary>
    Task<Dictionary<string, object>> CopyStepModuleAsync(
        string sourceFilePath, string sourceSequenceName, string sourceStepGroup, string sourceStepName,
        string targetFilePath, string targetSequenceName, string targetStepGroup, string targetStepName,
        bool save = true, IReadOnlyList<string>? paths = null);

    // ── Sequence Analyzer (detailed) ─────────────────────────────────────────
    /// <summary>Runs the Sequence Analyzer and returns a detailed result filtered by minimum
    /// severity and optionally grouped (by "severity", "rule", or "none" for a flat list). When
    /// <paramref name="async"/> is true the analysis is started on a background job and the call
    /// returns IMMEDIATELY with a running handle (JobId + Status="running"); poll the result with
    /// <see cref="GetAnalysisStatusAsync"/>. Async is the fix for the ~60s MCP transport timeout
    /// (-32001) that a cold analysis of LabVIEW <c>.lvlibp</c> steps otherwise trips.</summary>
    /// <para><paramref name="timeoutSeconds"/> bounds the AnalyzerApp.exe child (default 900 s — a cold
    /// analysis that loads LabVIEW/Python code modules takes minutes).</para>
    Task<AnalyzerResult> RunSequenceAnalyzerDetailedAsync(string filePath,
        string minSeverity = "Information", string groupBy = "severity", bool async = false,
        int timeoutSeconds = TestStandService.DefaultAnalyzerTimeoutSeconds);

    /// <summary>Polls an ASYNC analysis job started by <see cref="RunSequenceAnalyzerDetailedAsync"/>
    /// (async=true). Returns the full <see cref="AnalyzerResult"/> plus a Status of "running",
    /// "completed" or "error". Throws <see cref="KeyNotFoundException"/> for an unknown/expired
    /// job id. Finished jobs are retained ~10 minutes.</summary>
    Task<AnalyzerResult> GetAnalysisStatusAsync(string jobId);

    // ── Output & UI Messages ─────────────────────────────────────────────────
    /// <summary>Posts a message to the engine output window.</summary>
    Task<OutputMessageInfo> PostOutputMessageAsync(string message,
        string category = "", string severity = "Information");
    /// <summary>Returns recent messages from the engine output window.</summary>
    Task<List<OutputMessageInfo>> GetOutputMessagesAsync(int maxMessages = 200);
    /// <summary>Clears all messages from the engine output window.</summary>
    Task ClearOutputMessagesAsync();
    /// <summary>Posts a UI message to the specified execution (requires a live execution).</summary>
    Task PostUiMessageAsync(string executionId, string messageCode,
        double numericData = 0, string stringData = "");

    // ── Search Directories ───────────────────────────────────────────────────
    /// <summary>Returns the list of file-search directories configured in the engine.</summary>
    Task<List<SearchDirectoryInfo>> GetSearchDirectoriesAsync();
    /// <summary>Adds a directory to the engine's file-search path.</summary>
    Task AddSearchDirectoryAsync(string path, int index = -1,
        bool searchSubdirectories = true);
    /// <summary>Removes the specified directory from the engine's file-search path.</summary>
    Task RemoveSearchDirectoryAsync(string path);

    // ── Data-Type Field Editing ──────────────────────────────────────────────
    /// <summary>Adds a field of the given type to the named custom data type.</summary>
    Task AddDataTypeFieldAsync(string filePath, string typeName, string fieldName,
        string fieldType, bool save = true);
    /// <summary>Returns all fields defined on the named custom data type.</summary>
    Task<List<TypeFieldInfo>> GetDataTypeFieldsAsync(string filePath, string typeName);
    /// <summary>Removes the named field from the specified custom data type.</summary>
    Task RemoveDataTypeFieldAsync(string filePath, string typeName, string fieldName,
        bool save = true);

    // ── CSV Record Streams ───────────────────────────────────────────────────
    /// <summary>Writes lines to a CSV file using the TestStand CSV stream API.</summary>
    Task WriteCsvLinesAsync(string filePath, List<string> lines);
    /// <summary>Reads lines from a CSV file using the TestStand CSV stream API.</summary>
    Task<CsvReadResult> ReadCsvLinesAsync(string filePath, int maxLines = 1000);

    // ── Result Logging (smoke) ───────────────────────────────────────────────
    /// <summary>Creates a result log file in the specified format and returns its path.</summary>
    Task<string> CreateResultLogAsync(string filePath, string format = "ASCII");

    // ── Batch Synchronization (best-effort) ──────────────────────────────────
    /// <summary>Creates a batch synchronization object with the given name.</summary>
    Task CreateBatchSyncObjectAsync(string name);

    // ── Interactive Execution (smoke) ────────────────────────────────────────
    /// <summary>Runs the specified steps interactively (as if from the Sequence Editor) and returns the execution ID.</summary>
    Task<string> RunStepsInteractivelyAsync(string filePath, string sequenceName,
        string stepGroup, List<string> stepNames, int timeoutSeconds = 60);

    // ── Report Sections (smoke) ──────────────────────────────────────────────
    /// <summary>Adds a titled section with body text to the report for the specified execution.</summary>
    Task<string> AddReportSectionAsync(string executionId, string title, string body);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>Default implementation of <see cref="ITestStandService"/> that communicates with the NI TestStand engine via COM.</summary>
public sealed class TestStandService : ITestStandService
{
    private readonly ILogger<TestStandService> _logger;
    private NiEngine? _engine;        // NationalInstruments.TestStand.Interop.API.Engine (coclass)
    private bool _disposed;

    // Number of live TestStand engine instances across the whole process. Engine.ShutDown
    // takes a `final` flag that, when true, also shuts down NI licensing (the NILM helper
    // process) — which is what lets a headless host actually exit. That global shutdown is
    // only safe for the LAST engine: doing it while another instance is still connected
    // poisons the shared engine and hangs teardown. We therefore pass final:true only when
    // this is the last engine being released, and final:false otherwise.
    private static int _liveEngineCount;
    private readonly Dictionary<string, DateTime> _executionStartTimes = new();
    private readonly Dictionary<string, List<LogEntry>> _executionLogs = new();
    private readonly Dictionary<string, dynamic> _syncObjects = new();

    // In-memory tracking (Engine API has no SequenceFiles/Executions collection)
    private readonly Dictionary<string, NiSequenceFile> _loadedSequenceFiles = new();
    private readonly Dictionary<string, NiExecution> _activeExecutions = new();

    // ── Dedicated engine thread ────────────────────────────────────────────────
    // The engine is created and owned by a single persistent thread that runs a continuous Windows
    // message pump. TestStand posts execution-progress messages to a hidden window owned by the
    // engine's creation thread, and an execution only advances while THAT thread pumps — so the
    // engine must live on a stable, pumping thread, not a transient Task.Run thread. (Full
    // analysis: memory teststand-execution-needs-waitforendex-pump.)
    private Thread? _engineThread;
    private volatile bool _enginePumpRunning;
    private readonly ManualResetEventSlim _engineReady = new(false);
    private volatile bool _engineConnected;
    private Exception? _engineConnectError;
    // Operator-supplied TestStand Bin directory (connect_engine's engine_path), normalised. Takes
    // precedence over every automatic candidate when resolving the NI tools — see ResolveTestStandBin.
    private string? _explicitBinDir;
    // Serializes lazy reconnects from EnsureConnected so two concurrent tool calls never start two
    // engine threads (a second live engine hangs teardown — see teststand-testhost-teardown-hang).
    private readonly object _connectLock = new();

    // Wait efficiently (no busy-spin) until a window message arrives or the timeout elapses; the
    // PeekMessage/TranslateMessage/DispatchMessage P/Invokes + the PumpMessages() helper already
    // exist further down in this file and are reused by the engine-thread pump loop.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr[]? pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);
    private const uint QS_ALLINPUT = 0x04FF;


    // Watch expressions are an editor/GUI concept not available in the engine API;
    // we keep them in memory so Claude can manage them across calls.
    private readonly List<WatchExpressionInfo> _watchExpressions = new();

    /// <inheritdoc/>
    public bool IsConnected => _engine != null;

    private static readonly System.Reflection.BindingFlags _comFlags =
        System.Reflection.BindingFlags.InvokeMethod |
        System.Reflection.BindingFlags.GetProperty |
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public;

    private static readonly System.Reflection.BindingFlags _comInvokeFlags =
        System.Reflection.BindingFlags.InvokeMethod |
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public;

    // Cached, compiled regexes for analyzer-project XML rewriting/parsing.
    // Hoisted out of the methods so the patterns compile once, not on every call.
    private const RegexOptions XmlRx = RegexOptions.Singleline | RegexOptions.Compiled;
    private static readonly Regex _filesBlockRx       = new(@"<Files classname='Strs'>.*?</Files>", XmlRx);
    private static readonly Regex _messagesBlockRx    = new(@"<Messages classname='Objs'>.*?</Messages>", XmlRx);
    private static readonly Regex _pathAtLastWriteRx  = new(@"<PathAtLastWrite classname='Str'>.*?</PathAtLastWrite>", XmlRx);
    private static readonly Regex _messagesCaptureRx  = new(@"<Messages classname='Objs'>(.*?)</Messages>", XmlRx);
    private static readonly Regex _firstValueRx       = new(@"<Messages classname='Objs'>\s*<value\b([^>]*)>?", XmlRx);
    private static readonly Regex _selfClosingValueRx = new(@"<Messages classname='Objs'>\s*<value\b[^>]*/\s*>", XmlRx);
    // Analyzer location paths embed the sequence + step as bracketed, double-quoted names,
    // e.g.  Data.Seq["MainSequence"].Main["Label_Disabled"].TS.Mode
    private static readonly Regex _analyzerSeqNameRx  = new(@"\bSeq\[""([^""]+)""\]", RegexOptions.Compiled);
    private static readonly Regex _analyzerStepNameRx = new(@"\b(?:Setup|Main|Cleanup)\[""([^""]+)""\]", RegexOptions.Compiled);

    /// <summary>Creates the service with the given logger.</summary>
    public TestStandService(ILogger<TestStandService> logger)
    {
        _logger = logger;
    }

    // ── Engine ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(string? enginePath = null)
    {
        // An explicit engine_path pins the TestStand INSTALL whose NI tools (FileDiffer.exe,
        // AnalyzerApp.exe) get launched — the manual escape hatch for a station where the automatic
        // search picks the wrong one. It is applied even when already connected, so it can be set
        // after the fact, and a bad path now FAILS LOUDLY instead of being silently dropped.
        //
        // It deliberately does NOT choose which engine COM activates: TestStand registers exactly one
        // active Engine coclass and activation goes through its ProgID (see EngineThreadProc).
        // Switching the active version is NI's version-selector's job, not ours.
        if (!string.IsNullOrWhiteSpace(enginePath))
        {
            var binDir = TestStandInstallLocator.NormalizeBinDirectory(enginePath)
                ?? throw new DirectoryNotFoundException(
                    $"engine_path '{enginePath}' does not exist. Pass the TestStand engine DLL " +
                    @"(…\Bin\teapi.dll), the Bin directory, or the install root — or omit it to use " +
                    "the registered installation.");
            _explicitBinDir = binDir;
            _logger.LogInformation("engine_path override: NI tools will be resolved from '{Bin}'.", binDir);
        }

        if (_engine != null) return true;

        _logger.LogInformation("Connecting to TestStand engine...");

        // Create and OWN the engine on a single dedicated, persistent thread that runs a continuous
        // message pump for the engine's whole lifetime. This is required for executions to run: the
        // engine posts execution-progress messages to a hidden window owned by its creation thread,
        // and a sequence only advances while that thread pumps. (A transient Task.Run thread, the
        // old approach, is gone the instant ConnectAsync returns — so executions were created but
        // never ran.) The engine is created MTA, so every other (non-execution) tool keeps calling
        // it directly from its own Task.Run thread with no marshaling or serialization change.
        _engineReady.Reset();
        _enginePumpRunning = true;
        _engineConnected = false;
        _engineConnectError = null;

        _engineThread = new Thread(EngineThreadProc)
        {
            IsBackground = true,
            Name = "TestStand-Engine",
        };
        _engineThread.SetApartmentState(ApartmentState.MTA);
        _engineThread.Start();

        await Task.Run(() => _engineReady.Wait());

        if (!_engineConnected)
        {
            _logger.LogError(_engineConnectError, "Failed to connect to TestStand engine");
            _enginePumpRunning = false;
            _engineThread = null;
            return false;
        }
        _logger.LogInformation("Successfully connected to TestStand engine.");
        return true;
    }

    /// <summary>
    /// Body of the dedicated engine thread: create the engine (so its hidden message window is
    /// owned by THIS thread), then run a continuous Windows message pump — that pump is what
    /// advances executions — and finally tear the engine down on the same thread. Pumping has to
    /// happen on the engine's creation thread; that is the only thread to which TestStand delivers
    /// execution-progress messages. See memory teststand-execution-needs-waitforendex-pump.
    /// <para>
    /// Activation always goes through the <c>TestStand.Engine</c> ProgID, i.e. the ONE engine
    /// TestStand has registered as active — there is no supported way to activate a different
    /// installation's engine in-process, so this takes no path argument. <c>connect_engine</c>'s
    /// <c>engine_path</c> instead pins which install's NI TOOLS are launched (see ConnectAsync).
    /// </para>
    /// </summary>
    private void EngineThreadProc()
    {
        try
        {
            var engineType = Type.GetTypeFromProgID("TestStand.Engine")
                ?? throw new InvalidOperationException(
                    "TestStand Engine COM server not found. Ensure NI TestStand is installed.");

            _engine = (NiEngine)(Activator.CreateInstance(engineType)
                ?? throw new InvalidOperationException("Failed to create TestStand Engine instance."));
            System.Threading.Interlocked.Increment(ref _liveEngineCount);

            // Load type palette files so step types (Label, Action, etc.) are available
            try { _engine.LoadTypePaletteFiles(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not load type palette files"); }

            _engineConnected = true;
        }
        catch (Exception ex)
        {
            _engineConnectError = ex;
            _engineConnected = false;
            _engineReady.Set();
            return;
        }
        _engineReady.Set();

        // Continuous message pump — THIS drives executions. MsgWaitForMultipleObjectsEx blocks
        // (no busy-spin) until a window message arrives or the 100 ms backstop elapses, which also
        // bounds how long disconnect waits for the loop to observe _enginePumpRunning == false.
        while (_enginePumpRunning)
        {
            PumpMessages();   // drain all pending window messages (advances executions)
            MsgWaitForMultipleObjectsEx(0, null, 100, QS_ALLINPUT, 0);
        }

        ShutDownEngineCore();
    }

    /// <summary>Tears the engine down ON the engine thread (the thread that created it).</summary>
    private void ShutDownEngineCore()
    {
        // Release all loaded sequence file COM objects before shutting down the engine.
        // Abandoning RCWs causes GC finalizer crashes when the engine is already gone.
        foreach (var sf in _loadedSequenceFiles.Values)
        {
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sf); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to release COM object for sequence file during disconnect."); }
        }
        _loadedSequenceFiles.Clear();
        _activeExecutions.Clear();
        _undoGroups.Clear();

        if (_engine != null)
        {
            // Engine.ShutDown stops the engine's background threads. Pass final:true ONLY
            // when this is the last live engine — that also shuts down NI licensing (the
            // NILM helper) which otherwise keeps the host process alive and hangs exit.
            // Passing final:true while another instance is still connected would break the
            // shared engine, so transient instances use final:false.
            bool isLast = System.Threading.Interlocked.Decrement(ref _liveEngineCount) <= 0;
            try { _engine.ShutDown(isLast); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to shut down TestStand engine."); }

            if (isLast)
            {
                // Releasing the LAST engine's RCW synchronously triggers the NI License-Manager
                // (NILM) teardown over COM/RPC to the out-of-process NilmCompatibilityServer, which
                // can block for a long time (threads park in EventPairLow LPC waits) and stalls
                // process shutdown. The engine has already been ShutDown and the process is ending,
                // so we intentionally do NOT release the RCW and suppress its finalization.
                try { GC.SuppressFinalize((object)_engine!); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to suppress finalizer on engine COM object."); }
            }
            else
            {
                // Transient second engine: NILM stays alive (the primary engine holds it),
                // so a full release is cheap and keeps the instance from leaking.
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_engine); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to release engine COM object RCW."); }
            }
            _engine = null;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        var thread = _engineThread;
        if (thread == null) return;

        // Signal the pump loop to stop; the engine thread then tears the engine down on its own
        // thread (where it was created) and exits. Bounded join so a stuck NILM teardown can't hang
        // us indefinitely — the process hard-terminates on exit anyway.
        _enginePumpRunning = false;
        await Task.Run(() => thread.Join(TimeSpan.FromSeconds(30)));
        _engineThread = null;
    }

    /// <inheritdoc/>
    public async Task<StationInfo> GetStationInfoAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                var info = new StationInfo
                {
                    StationName      = Environment.MachineName,
                    TestStandVersion = GetEngineProperty<string>("VersionString") ?? "Unknown",
                    OperatingSystem  = Environment.OSVersion.ToString(),
                    Username         = Environment.UserName,
                    IsLicensed       = true
                };

                // Loaded sequence files — tracked in memory (Engine has no SequenceFiles collection)
                foreach (var path in _loadedSequenceFiles.Keys)
                    info.LoadedSequenceFiles.Add(path);

                // Active executions — tracked in memory
                foreach (var exec in _activeExecutions.Values)
                {
                    try { info.ActiveExecutions.Add(MapExecutionInfo(exec)); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to map active execution info entry."); }
                }

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get station info");
                throw;
            }
        });
    }

    // ── Sequence Files ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SequenceFileInfo> OpenSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Opening sequence file: {Path}", filePath);
                // GetSequenceFileEx(path, getSeqFileFlags=0, conflictHandler=UseGlobalType=4)
                var sf = _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);
                _loadedSequenceFiles[filePath] = sf;
                return MapSequenceFileInfo(sf, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open sequence file: {Path}", filePath);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task CloseSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            try
            {
                if (_loadedSequenceFiles.TryGetValue(filePath, out var sf))
                {
                    // ReleaseSequenceFileEx(sequenceFileObj, options=0)
                    _engine!.ReleaseSequenceFileEx(sf, 0);
                    _loadedSequenceFiles.Remove(filePath);
                    // Explicitly release the RCW so the GC finalizer doesn't try to touch
                    // an already-released COM object after engine shutdown (crash prevention).
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sf); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to release COM object for closed sequence file."); }
                }
                _logger.LogInformation("Closed sequence file: {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close sequence file: {Path}", filePath);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<List<SequenceFileInfo>> GetLoadedSequenceFilesAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<SequenceFileInfo>();
            foreach (var kvp in _loadedSequenceFiles)
            {
                try { result.Add(MapSequenceFileInfo(kvp.Value, kvp.Key)); }
                catch { result.Add(new SequenceFileInfo { FilePath = kvp.Key, FileName = Path.GetFileName(kvp.Key) }); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<List<SequenceFileSummary>> GetLoadedSequenceFilesSummaryAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<SequenceFileSummary>();
            foreach (var kvp in _loadedSequenceFiles)
            {
                var summary = new SequenceFileSummary
                {
                    FilePath = kvp.Key,
                    FileName = Path.GetFileName(kvp.Key)
                };
                try
                {
                    dynamic sf = kvp.Value;
                    int numSeqs = 0;
                    try { numSeqs = Convert.ToInt32((object)sf.NumSequences); }
                    catch
                    {
                        // Probe fallback (cap kept low — only counting names).
                        for (int probe = 0; probe < 1000; probe++)
                        {
                            try { object _ = sf.GetSequence(probe); numSeqs = probe + 1; }
                            catch { break; }
                        }
                    }

                    for (int i = 0; i < numSeqs; i++)
                    {
                        try
                        {
                            dynamic seq = sf.GetSequence(i);
                            string name;
                            try { name = (string)seq.Name; }
                            catch { name = "Unknown"; }
                            summary.Sequences.Add(name);
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence entry while summarizing sequence file."); }
                    }
                    summary.SequenceCount = summary.Sequences.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to summarize sequence file {Path}", kvp.Key);
                }
                result.Add(summary);
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<SequenceInfo> GetSequenceAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapSequenceInfo(seq);
        });
    }

    /// <inheritdoc/>
    public async Task SaveSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<string> CreateSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _engine!.NewSequenceFile();
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
            return filePath;
        });
    }

    /// <inheritdoc/>
    public async Task InsertSequenceAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq  = _engine!.NewSequence();
            seq.Name = sequenceName;
            sf.InsertSequence(seq);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);

            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task InsertStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepType, string stepName, int index = -1, string? adapterName = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = ParseStepGroup(stepGroup);

            var (adapterKey, internalType) = ResolveStepTypeAndAdapter(stepType, adapterName);

            var step = _engine!.NewStep(adapterKey, internalType);
            step.Name = stepName;

            int insertAt = index < 0 ? (int)seq.GetNumSteps((NiStepGroups)sgValue) : index;
            seq.InsertStep(step, insertAt, (NiStepGroups)sgValue);

            InitStepDescriptionField(step);
            ApplyBlankLabelIconIfNameless(step, stepName, internalType);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // Map adapter display names to internal key names + resolve the internal step
    // type. Shared by InsertStepAsync and InsertStepsBulkAsync so both behave identically.
    private static (string adapterKey, string internalType) ResolveStepTypeAndAdapter(
        string stepType, string? adapterName)
    {
        string ResolveAdapter(string name) => ResolveAdapterKeyName(name);

        string adapterKey, internalType;
        switch (stepType.ToLowerInvariant())
        {
            case "sequence call":
            case "sequencecall":
                adapterKey = "Sequence Adapter"; internalType = "SequenceCall"; break;
            case "call executable":
            case "callexecutable":
                adapterKey = "None Adapter"; internalType = "CallExecutable"; break;
            case "numericlimittest":
            case "numeric limit test":
                adapterKey = "None Adapter"; internalType = "NumericLimitTest"; break;
            case "stringvaluetest":
            case "string value test":
                adapterKey = "None Adapter"; internalType = "StringValueTest"; break;
            case "passfail":
            case "passfailtest":
            case "pass/fail":
            case "pass/fail test":
                adapterKey = "None Adapter"; internalType = "PassFailTest"; break;
            case "messagepopup":
            case "message popup":
                adapterKey = "None Adapter"; internalType = "MessagePopup"; break;
            case "statement":
                adapterKey = "None Adapter"; internalType = "Statement"; break;
            case "goto":
                adapterKey = "None Adapter"; internalType = "Goto"; break;
            default:
                adapterKey = "None Adapter"; internalType = stepType; break;
        }

        if (!string.IsNullOrWhiteSpace(adapterName))
            adapterKey = ResolveAdapter(adapterName);

        return (adapterKey, internalType);
    }

    // Initialize TS.Description to a non-empty placeholder so the binary file
    // serializes this field. Empty strings are omitted by the TOF1 binary format,
    // so we use a space to force the field to exist after save/load.
    // set_step_comment / a bulk comment will overwrite this with the real description.
    private static void InitStepDescriptionField(dynamic step)
    {
        bool tsDescInit = false;
        try { step.SetValString("TS.Description", 0, " "); tsDescInit = true; } catch (Exception) { /* best-effort: initialize TS.Description placeholder field — intentionally ignored */ }
        if (!tsDescInit)
            try { step.SetValString("TS.Description", 0x8, " "); } catch (Exception) { /* best-effort: initialize TS.Description via alternate flags — intentionally ignored */ }
    }

    // A Label step with an EMPTY name is a blank spacer line in the Sequence Editor. TestStand
    // blanks its icon there: TS.Icon = "ni_blank.ico" (instead of the default "label.ico") plus a
    // step flag (0x4000000). A tools-inserted nameless label otherwise keeps "label.ico" and shows
    // a visible icon. Mirror the editor so a nameless label looks like an editor-made spacer.
    private const int StepFlag_BlankLabelIcon = 0x4000000;
    private void ApplyBlankLabelIconIfNameless(dynamic step, string? stepName, string internalType)
    {
        if (!string.Equals(internalType, "Label", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.IsNullOrWhiteSpace(stepName)) return; // named label keeps its normal icon
        try
        {
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
            try { stepPo.SetValString("TS.Icon", 0, "ni_blank.ico"); }
            catch (Exception ex) { _logger.LogDebug(ex, "Blank-label: could not set TS.Icon."); }
            try { stepPo.SetFlags("", 0, stepPo.GetFlags("", 0) | StepFlag_BlankLabelIcon); }
            catch (Exception ex) { _logger.LogDebug(ex, "Blank-label: could not set the blank-icon step flag."); }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Blank-label icon convention skipped."); }
    }

    /// <inheritdoc/>
    public async Task<BulkInsertResult> InsertStepsBulkAsync(string filePath,
        string sequenceName, string stepGroup, List<BulkStepSpec> steps, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new BulkInsertResult
            {
                SequenceName = sequenceName,
                StepGroup    = stepGroup
            };
            if (steps == null || steps.Count == 0)
                return result;

            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = ParseStepGroup(stepGroup);

            foreach (var spec in steps)
            {
                // A Label MAY be nameless (a blank spacer line) — allow an empty name for it; every
                // other step type still requires a name. A missing step type is always a skip.
                bool isLabelType = string.Equals(spec.StepType, "Label", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(spec.StepType) ||
                    (string.IsNullOrWhiteSpace(spec.Name) && !isLabelType))
                {
                    result.Warnings.Add($"Skipped step with empty name or type ('{spec.Name}').");
                    continue;
                }

                var (adapterKey, internalType) =
                    ResolveStepTypeAndAdapter(spec.StepType, spec.Adapter);

                var step  = _engine!.NewStep(adapterKey, internalType);
                step.Name = spec.Name;

                // Always append in list order (bulk builds a sequence top-to-bottom).
                int insertAt = (int)seq.GetNumSteps((NiStepGroups)sgValue);
                seq.InsertStep(step, insertAt, (NiStepGroups)sgValue);
                InitStepDescriptionField(step);
                ApplyBlankLabelIconIfNameless(step, spec.Name, internalType);

                // Optional comment
                if (!string.IsNullOrEmpty(spec.Comment))
                {
                    bool ok = false;
                    try { ((dynamic)step).Comment = spec.Comment; ok = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set step comment via Comment property."); }
                    if (!ok) try { ((NiStep)(object)step).AsPropertyObject().Comment = spec.Comment; ok = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set step comment via AsPropertyObject().Comment."); }
                    if (ok) result.CommentsSet++;
                    else    result.Warnings.Add($"Comment not set on '{spec.Name}'.");
                    var encWarn = InputGuards.DescribeLatin1Loss(spec.Comment, $"comment on '{spec.Name}'");
                    if (encWarn != null) result.Warnings.Add(encWarn);
                }

                // Optional expression
                if (!string.IsNullOrEmpty(spec.Expression))
                {
                    try
                    {
                        var exprType     = (spec.ExpressionType ?? "Statement").ToLowerInvariant();
                        bool explicitSlot = exprType is "pre" or "post" or "status";
                        string? flowCondProp = InputGuards.FlowConditionProperty(spec.StepType);

                        if (flowCondProp != null && !explicitSlot)
                        {
                            // For a flow BRANCH step the default 'expression' IS the branch condition.
                            // Writing it to the Post Expression (the historical default) would
                            // evaluate-and-discard it WITHOUT branching. Route it to the dedicated
                            // condition property (ConditionExpr / ItemExpr) so it actually branches,
                            // mirroring set_flow_condition. An explicit Pre/Post/Status is respected.
                            ((NiStep)(object)step).AsPropertyObject()
                                .SetValString(flowCondProp, 0, spec.Expression);
                            result.Warnings.Add(
                                $"'{spec.Name}' ({spec.StepType}): expression routed to {flowCondProp} " +
                                "(the branch condition), not the Post Expression, so it actually branches.");
                        }
                        else
                        {
                            switch (exprType)
                            {
                                case "pre":    step.PreExpression    = spec.Expression; break;
                                case "post":   step.PostExpression   = spec.Expression; break;
                                case "status": step.StatusExpression = spec.Expression; break;
                                default:
                                    // Statement steps: the primary expression home is the Post Expression.
                                    step.PostExpression = spec.Expression;
                                    break;
                            }
                        }
                        result.ExpressionsSet++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Expression not set on '{spec.Name}': {ex.Message}");
                    }
                }

                // Optional For-loop init / increment expressions. A counted NI_Flow_For keeps its
                // three parts in dedicated step properties (InitializationExpr / ConditionExpr /
                // IncrementExpr). 'expression' above already routed the loop-continue test to
                // ConditionExpr (For is in InputGuards.ConditionExprSteps); here we fill the other two
                // so a whole For loop can be declared in ONE bulk step, e.g.
                //   { step_type:"NI_Flow_For", init_expr:"Locals.i = 0",
                //     expression:"Locals.i < 10", increment_expr:"Locals.i += 1" }
                if (InputGuards.IsCountedForLoop(spec.StepType) &&
                    (!string.IsNullOrEmpty(spec.InitExpr) || !string.IsNullOrEmpty(spec.IncrementExpr)))
                {
                    try
                    {
                        var forPo = ((NiStep)(object)step).AsPropertyObject();
                        if (!string.IsNullOrEmpty(spec.InitExpr))
                        {
                            forPo.SetValString("InitializationExpr", 0, spec.InitExpr);
                            result.ExpressionsSet++;
                        }
                        if (!string.IsNullOrEmpty(spec.IncrementExpr))
                        {
                            forPo.SetValString("IncrementExpr", 0, spec.IncrementExpr);
                            result.ExpressionsSet++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"For-loop init/increment not set on '{spec.Name}': {ex.Message}");
                    }
                }
                else if ((!string.IsNullOrEmpty(spec.InitExpr) || !string.IsNullOrEmpty(spec.IncrementExpr)))
                {
                    result.Warnings.Add(
                        $"'{spec.Name}' ({spec.StepType}): init_expr/increment_expr are only applied to " +
                        "an NI_Flow_For step and were ignored.");
                }

                // Optional NI_Flow_ForEach config: the collection lives in ArrayExpr, the per-element
                // variable in ArrayElementExpr. A ForEach with an empty ArrayExpr never iterates, so
                // this is the ForEach equivalent of a For loop's condition.
                if (InputGuards.IsForEachLoop(spec.StepType) &&
                    (!string.IsNullOrEmpty(spec.ArrayExpr) || !string.IsNullOrEmpty(spec.ElementExpr)))
                {
                    try
                    {
                        var fePo = ((NiStep)(object)step).AsPropertyObject();
                        if (!string.IsNullOrEmpty(spec.ArrayExpr))
                        {
                            fePo.SetValString("ArrayExpr", 0, spec.ArrayExpr);
                            result.ExpressionsSet++;
                        }
                        if (!string.IsNullOrEmpty(spec.ElementExpr))
                        {
                            fePo.SetValString("ArrayElementExpr", 0, spec.ElementExpr);
                            result.ExpressionsSet++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"ForEach array/element not set on '{spec.Name}': {ex.Message}");
                    }
                }
                else if ((!string.IsNullOrEmpty(spec.ArrayExpr) || !string.IsNullOrEmpty(spec.ElementExpr)))
                {
                    result.Warnings.Add(
                        $"'{spec.Name}' ({spec.StepType}): array_expr/element_expr are only applied to " +
                        "an NI_Flow_ForEach step and were ignored.");
                }

                // Optional NI_Flow_Case default flag: mark this case as the default branch. The case
                // value(s) are set through the 'expression' routing above (ItemExpr); a default case
                // typically has no value expression.
                if (spec.IsDefault == true)
                {
                    if (InputGuards.IsCaseStep(spec.StepType))
                    {
                        try { ((NiStep)(object)step).AsPropertyObject().SetValBoolean("IsDefault", 0, true); }
                        catch (Exception ex) { result.Warnings.Add($"IsDefault not set on '{spec.Name}': {ex.Message}"); }
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"'{spec.Name}' ({spec.StepType}): is_default is only applied to an NI_Flow_Case step and was ignored.");
                    }
                }

                // A freshly inserted NI_Wait has an EMPTY TimeExpr and never actually waits until a
                // wait time is configured — and bulk has no time field. Flag it so it is not silently
                // a no-op.
                if (InputGuards.IsWaitStep(spec.StepType))
                    result.Warnings.Add(
                        $"NI_Wait step '{spec.Name}' has no wait time yet — it will not wait until " +
                        "you call set_wait_time for it.");

                // Optional SequenceCall target
                if (!string.IsNullOrEmpty(spec.TargetSequenceName))
                {
                    try
                    {
                        dynamic seqCallModule = step.Module;
                        seqCallModule.SequenceName   = spec.TargetSequenceName;
                        seqCallModule.UseCurrentFile = string.IsNullOrEmpty(spec.TargetSequenceFile);
                        if (!string.IsNullOrEmpty(spec.TargetSequenceFile))
                        {
                            string relTarget = MakeRelativePath(
                                Path.GetDirectoryName(filePath) ?? "", spec.TargetSequenceFile);
                            seqCallModule.SequenceFilePath = relTarget;
                            foreach (var propName in new[] { "UseAbsolutePath", "AbsolutePath", "IsAbsolutePath" })
                            {
                                try { ((object)seqCallModule).GetType().InvokeMember(
                                    propName, System.Reflection.BindingFlags.SetProperty,
                                    null, seqCallModule, new object[] { false }); }
                                catch (Exception ex) { _logger.LogDebug(ex, "Failed to clear absolute path flag '{PropName}' on SequenceCall module.", propName); }
                            }
                        }
                        // Load the callee prototype so ActualArgs/Prototype are populated
                        // exactly as the editor would (correct arg types + UseDef defaults).
                        TryLoadModulePrototype(seqCallModule, spec.Name);
                        result.TargetsSet++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"SequenceCall target not set on '{spec.Name}': {ex.Message}");
                    }
                }

                result.InsertedCount++;
                result.InsertedSteps.Add(spec.Name);
            }

            // Save ONCE for the whole batch — this is the key efficiency win over
            // calling insert_step (which saves per step).
            if (save)
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            return result;
        });
    }

    // ── Executions ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ExecutionInfo> StartExecutionAsync(string sequenceFilePath,
        string entryPoint, Dictionary<string, object>? parameters = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Starting execution: {File} / {EntryPoint}",
                    sequenceFilePath, entryPoint);

                var sf = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                    ? cached
                    : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);

                // Resolve process-model entry points (e.g. "Single Pass" / "Test UUTs", spaces/casing
                // optional). A name that is NOT a sequence in the client file but IS a station-model
                // entry point runs the client THROUGH the process model — which is what populates
                // step results and generates the report. A normal client sequence name (e.g.
                // "MainSequence") runs directly with no model. (NOTE: "Test UUTs" pauses headless,
                // waiting for the UUT serial-number dialog that has no UI to answer it.)
                var (model, modelEntry) = TryResolveModelEntryPoint(sf, entryPoint);
                var effectiveEntry = modelEntry ?? entryPoint;

                // NewExecution via typed IEngine interface to avoid COM argument-conversion issues.
                // execTypeMask=0 = ExecTypeMask_Normal.
                var typedEngine = (NiEngine)_engine!;
                dynamic exec = typedEngine.NewExecution(
                    sf,                            // client sequence file
                    effectiveEntry,                // client sequence name, or model entry-point sequence
                    model,                         // process model file (null = run the sequence directly)
                    false,                         // breakAtFirstStep
                    0,                             // executionTypeMask = Normal
                    null,                          // sequenceArgs
                    null,                          // editArgs
                    null);                         // interactiveArgs

                // Set parameters if provided
                if (parameters != null)
                {
                    foreach (var kv in parameters)
                    {
                        try
                        {
                            exec.Locals.SetValString($"Parameters.{kv.Key}", 1,
                                kv.Value?.ToString() ?? "");
                        }
                        catch { /* ignore parameter set errors */ }
                    }
                }

                // Force a concrete string: TryGetString(dynamic,...) is dynamically dispatched, so
                // `var` would infer `dynamic` and the pump's _logger.LogDebug(execId) below would
                // fail to bind (extension methods cannot be dynamically dispatched).
                string execId = TryGetString(exec, "Id");
                if (string.IsNullOrEmpty(execId))
                    execId = ((object)exec).GetHashCode().ToString();
                _executionStartTimes[execId] = DateTime.UtcNow;
                _executionLogs[execId] = new List<LogEntry>();
                _activeExecutions[execId] = exec;

                // NOTE: the execution advances on its own from here because the dedicated engine
                // thread (see ConnectAsync / EngineThreadProc) continuously pumps the Windows
                // message queue of the thread that CREATED the engine — which is where TestStand
                // posts execution-progress messages. Without that pump Engine.NewExecution creates
                // an execution that never leaves the "Running" state and no step ever runs (the
                // original bug). See memory teststand-execution-needs-waitforendex-pump.
                return MapExecutionInfo(exec);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start execution");
                throw;
            }
        });
    }

    /// <summary>
    /// Resolves a process-model entry point. If <paramref name="entryPoint"/> is NOT a sequence in
    /// the client file but IS a station-process-model entry-point sequence (e.g. "Single Pass" /
    /// "Test UUTs" — spaces and casing optional), returns the model file plus the exact model entry
    /// sequence name, so the client runs THROUGH the model (which is what populates step results and
    /// the report). Returns (null, null) for a direct client-sequence run (the common case, e.g.
    /// "MainSequence"). Direct runs pay only a GetSequenceIndex lookup — the model is not loaded.
    /// </summary>
    private (NiSequenceFile? model, string? entrySeq) TryResolveModelEntryPoint(
        NiSequenceFile clientFile, string entryPoint)
    {
        // A name that exists in the client file is always a direct run — never the model.
        try { if (clientFile.GetSequenceIndex(entryPoint) >= 0) return (null, null); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetSequenceIndex failed for '{Ep}'.", entryPoint); }

        NiSequenceFile model;
        try { model = ((NiEngine)_engine!).GetStationModelSequenceFile(out _); }
        catch (Exception ex) { _logger.LogDebug(ex, "No station process model available."); return (null, null); }
        if (model == null) return (null, null);

        static string Norm(string s) =>
            new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        string want = Norm(entryPoint);

        string? exact = null, fuzzy = null;
        int n = 0; try { n = model.NumSequences; } catch { /* best-effort */ }
        for (int i = 0; i < n; i++)
        {
            NiSequence s;
            try { s = model.GetSequence(i); } catch { continue; }
            // Only real entry-point sequences carry an EntryPointNameExpression.
            try { if (string.IsNullOrEmpty(s.EntryPointNameExpression)) continue; } catch { continue; }
            string name; try { name = s.Name; } catch { continue; }
            if (string.Equals(name, entryPoint, StringComparison.OrdinalIgnoreCase)) { exact = name; break; }
            if (fuzzy == null && Norm(name) == want) fuzzy = name;
        }
        string? match = exact ?? fuzzy;
        return match != null ? (model, match) : (null, null);
    }

    /// <inheritdoc/>
    public async Task<ExecutionResult> WaitForExecutionAsync(string executionId,
        int timeoutSeconds = 300)
    {
        EnsureConnected();
        return await Task.Run(async () =>
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                dynamic? exec = FindExecution(executionId);

                while (exec != null && DateTime.UtcNow < deadline)
                {
                    // ExecRunState: 1=Running, 2=Paused, 3=Stopped
                    // exec is guaranteed non-null by the enclosing `while (exec != null ...)`.
                    int runState = GetExecutionRunState((object)exec!);
                    if (runState == 3) break; // Stopped = done
                    await Task.Delay(200);
                    exec = FindExecution(executionId);
                }

                // NOTE: the execution is intentionally NOT removed from _activeExecutions here.
                // The background pump (see StartExecutionAsync) drives it to completion; this loop
                // just observes the now-accurate run state. Keeping the completed execution around
                // lets get_execution_results / get_execution_status work AFTER run_sequence returns.
                if (exec == null)
                {
                    // Execution was removed externally (e.g. terminate/abort) during the wait.
                    return new ExecutionResult
                    {
                        ExecutionId = executionId,
                        Status      = "Completed",
                        Result      = "Unknown",
                        ElapsedSeconds = _executionStartTimes.TryGetValue(executionId, out var st)
                            ? (DateTime.UtcNow - st).TotalSeconds : 0
                    };
                }

                return BuildExecutionResult(exec, executionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for execution {Id}", executionId);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<ExecutionInfo> GetExecutionStatusAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            return MapExecutionInfo(exec);
        });
    }

    /// <inheritdoc/>
    public async Task<List<ExecutionInfo>> GetActiveExecutionsAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<ExecutionInfo>();
            foreach (var exec in _activeExecutions.Values)
            {
                try
                {
                    // Completed executions are kept in _activeExecutions so their results stay
                    // queryable, but "active" means Running(1)/Paused(2) only — skip Stopped(3).
                    if (GetExecutionRunState((object)exec) == 3) continue;
                    result.Add(MapExecutionInfo(exec));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to map execution info entry in active executions list."); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task TerminateExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.Terminate();
            _activeExecutions.Remove(executionId);
        });
    }

    /// <inheritdoc/>
    public async Task<ExecutionResult> RunSequenceAsync(string sequenceFilePath,
        string sequenceName, Dictionary<string, object>? parameters = null,
        int timeoutSeconds = 300)
    {
        var execInfo = await StartExecutionAsync(sequenceFilePath, sequenceName, parameters);
        return await WaitForExecutionAsync(execInfo.ExecutionId, timeoutSeconds);
    }

    // ── Variables & Properties ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PropertyValue> GetPropertyAsync(string lookupString)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                var root = ResolvePropertyRoot(lookupString, out string subPath);
                var prop = root.GetPropertyObject(subPath, (object)0);
                return new PropertyValue
                {
                    Name         = lookupString,
                    // Cast to object first: 'prop' is a dynamic COM PropertyObject whose own
                    // GetType(lookupString, options) method would otherwise shadow
                    // object.GetType() and throw "Error while invoking GetType." (same idiom
                    // used by TryGetString/InvokeMember helpers below).
                    DataType     = ((object)prop).GetType().Name,
                    Value        = TryGetValue(prop),
                    LookupString = lookupString
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get property: {Lookup}", lookupString);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task SetPropertyAsync(string lookupString, object value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            try
            {
                var root    = ResolvePropertyRoot(lookupString, out string subPath);
                var strVal  = value?.ToString() ?? "";
                if (double.TryParse(strVal, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                    root.SetValNumber(subPath, (object)0, d);
                else if (strVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                    root.SetValBoolean(subPath, (object)0, true);
                else if (strVal.Equals("false", StringComparison.OrdinalIgnoreCase))
                    root.SetValBoolean(subPath, (object)0, false);
                else
                    root.SetValString(subPath, (object)0, strVal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set property: {Lookup}", lookupString);
                throw;
            }
        });
    }

    // Resolves "StationGlobals.X" → engine.Globals, subPath="X"
    //          "FileGlobals.X"    → first loaded seq file FileGlobals, subPath="X"
    //          anything else      → engine.Globals, subPath=full string
    private dynamic ResolvePropertyRoot(string lookupString, out string subPath)
    {
        const string sgPrefix = "StationGlobals.";
        const string fgPrefix = "FileGlobals.";

        if (lookupString.StartsWith(sgPrefix, StringComparison.OrdinalIgnoreCase))
        {
            subPath = lookupString.Substring(sgPrefix.Length);
            return _engine!.Globals;
        }
        if (lookupString.StartsWith(fgPrefix, StringComparison.OrdinalIgnoreCase))
        {
            subPath = lookupString.Substring(fgPrefix.Length);
            var sf = _loadedSequenceFiles.Values.FirstOrDefault()
                ?? throw new InvalidOperationException("No sequence file loaded for FileGlobals lookup.");
            return GetFileGlobals(sf);
        }
        // Fallback: treat as a station global variable name
        subPath = lookupString;
        return GetStationGlobals();
    }

    // ── Expression evaluation & structured property access ─────────────────────

    /// <inheritdoc/>
    public async Task<ExpressionResult> EvaluateExpressionAsync(string expression,
        string? sequenceFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new ExpressionResult { Expression = expression };
            try
            {
                // Evaluation context: a sequence file's FileGlobals when a path is given,
                // otherwise the engine's StationGlobals. The expression can reference the
                // subproperties of that context by name (e.g. a station-global variable),
                // plus literals, operators and built-in expression functions.
                NiPropertyObject context = sequenceFilePath is { Length: > 0 }
                    ? GetFileGlobals(GetOrLoadSeqFile(sequenceFilePath))
                    : GetStationGlobals();

                // EvaluateEx returns the result as a PropertyObject of any type.
                NiPropertyObject resultPo =
                    context.EvaluateEx(expression, (int)NiEvalOptions.EvalOption_NoOptions);

                if (resultPo == null)
                {
                    result.ValueType = "Empty";
                    result.Value     = null;
                }
                else
                {
                    result.ValueType = InferValueKind(resultPo, out _, out _);
                    result.Value     = TryGetValue(resultPo);
                }
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.IsValid      = false;
                result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<PropertyObjectInfo> GetPropertyObjectAsync(string filePath,
        string? sequenceName, string propertyName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf        = GetOrLoadSeqFile(filePath);
            var container = ResolveValueContainer(sf, sequenceName);
            NiPropertyObject prop =
                (NiPropertyObject)(object)container.GetPropertyObject(propertyName, 0);

            var info = new PropertyObjectInfo { Name = propertyName };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElements);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElements;

            // Named-type name, if this property is an instance of a custom type.
            try { info.TypeName = NullIfEmpty((string)((dynamic)prop).Type.Name); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read named type name from property object."); }

            if (info.ValueType == "Container")
            {
                int numSub = 0;
                try { numSub = (int)prop.GetNumSubProperties(""); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get sub-property count on container property object."); }
                for (int i = 0; i < numSub; i++)
                {
                    try
                    {
                        string subName = (string)prop.GetNthSubPropertyName("", i, 0);
                        NiPropertyObject sub =
                            (NiPropertyObject)(object)prop.GetPropertyObject(subName, 0);
                        info.SubProperties.Add(new PropertySubInfo
                        {
                            Name      = subName,
                            ValueType = InferValueKind(sub, out _, out _),
                            Value     = TryGetValue(sub)
                        });
                    }
                    catch { /* skip unreadable subproperty */ }
                }
            }
            else if (!isArray)
            {
                info.Value = TryGetValue(prop);
            }
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task SetPropertyValueAsync(string filePath, string? sequenceName,
        string propertyName, string valueType, string? value, string? typeName = null,
        int? ordinal = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var root = ResolveValueContainer(sf, sequenceName);
            string vtl = valueType.Trim().ToLowerInvariant();
            bool isEnum      = vtl == "enum";
            bool isNamedType = vtl is "named_type" or "namedtype" or "type";

            // Create the property if it does not exist yet. NewSubProperty takes a simple
            // name, so split a dotted path into its parent container and the leaf name.
            if (!PropertyExists(root, propertyName))
            {
                int last = propertyName.LastIndexOf('.');
                string parentPath = last >= 0 ? propertyName.Substring(0, last) : "";
                string leaf       = last >= 0 ? propertyName.Substring(last + 1) : propertyName;
                NiPropertyObject parent = string.IsNullOrEmpty(parentPath)
                    ? root
                    : (NiPropertyObject)(object)root.GetPropertyObject(parentPath, 0);

                if (isEnum || isNamedType)
                {
                    // A member of a file-defined type (an enum, or a named container/leaf such as
                    // TFW_DB_TestCasesLimits / VisionSensorSbsi_Reply_Payload / Error): instantiate it
                    // as a FULL typed instance (materialising the type's fields, like the editor), so
                    // it carries the right type instead of an anonymous 'Container', and afterwards only
                    // the non-default field values need setting. type_name is required.
                    if (string.IsNullOrWhiteSpace(typeName))
                        throw new ArgumentException(
                            $"value_type='{valueType}' requires type_name (a data type defined in the file).");
                    InstantiateNamedTypeMember(parent, leaf, typeName!.Trim());
                }
                else
                {
                    parent.NewSubProperty(leaf, (NiPropValueTypes)MapPropValueType(valueType),
                        false, "", 0);
                }
            }

            switch (vtl)
            {
                case "named_type": case "namedtype": case "type":
                    break; // typed container/leaf — fields materialised on creation, no scalar to set here
                case "container":
                case "reference": case "object reference":
                case "objectreference": case "objref":
                    break; // structural only — no scalar value to assign
                case "boolean":
                case "bool":
                    root.SetValBoolean(propertyName, 0,
                        value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                          || value == "1"));
                    break;
                case "number":
                case "double":
                case "float":
                case "int":
                case "integer":
                    var numVal = double.Parse(value ?? "0",
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture);
                    // Plain set fails on an Enumeration-typed target ("Expected type X. Found
                    // type Number"); retry coercing for-this-operation (preserves the enum type,
                    // mirrors the enum READ path which uses PropOption_CoerceToNumber).
                    try { root.SetValNumber(propertyName, 0, numVal); }
                    catch { root.SetValNumber(propertyName,
                        (int)NiPropOptions.PropOption_CoerceToEnum, numVal); }
                    break;
                case "enum":
                    // Always land an EXPLICITLY-SET value (FileDiffer "[val]"): see
                    // WriteEnumLeafExplicit — only the by-NAME write marks the value explicit, so the
                    // ordinal is resolved to its enumerator name (file TUL → engine-wide → read-back
                    // off the property itself) before it is written.
                    WriteEnumLeafExplicit(root, propertyName, ordinal, value, typeName, sf, filePath);
                    break;
                default: // string (or enum-by-label)
                    try { root.SetValString(propertyName, 0, value ?? ""); }
                    catch { root.SetValString(propertyName,
                        (int)NiPropOptions.PropOption_CoerceToEnum, value ?? ""); }
                    break;
            }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task DeleteSubPropertyAsync(string filePath, string? sequenceName,
        string propertyName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var root = ResolveValueContainer(sf, sequenceName);
            root.DeleteSubProperty(propertyName, 0);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task<PropertyNodeInfo> SetPropertyNodeAsync(string filePath, string scope,
        string? sequenceName, string lookupString, string valueType, string? typeName,
        string? value, int? ordinal, int? numElements, int? flags,
        bool createMissingParents, bool save,
        string? representation = null, string? numberFormat = null, bool clearFlags = false)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            bool     isStation = IsStationGlobalsScope(scope);
            dynamic? sf        = isStation ? null : GetOrLoadSeqFile(filePath);
            NiPropertyObject root = ResolveScopeRoot(sf, scope, sequenceName);

            string vtl         = (valueType ?? "").Trim().ToLowerInvariant();
            bool   isEnum      = vtl == "enum";
            bool   isNamedType = vtl is "named_type" or "namedtype" or "type";
            bool   isArrayElems= vtl is "array_elements" or "arrayelements";

            // Split the dotted lookup into its parent container path and the leaf name —
            // NewSubProperty takes a simple name, while SetVal*/SetFlags/GetPropertyObject
            // take a lookup path (so operating on parent+leaf works for both).
            int    last       = lookupString.LastIndexOf('.');
            string parentPath = last >= 0 ? lookupString.Substring(0, last) : "";
            string leaf       = last >= 0 ? lookupString.Substring(last + 1) : lookupString;

            NiPropertyObject parent = ResolveOrCreateParent(root, parentPath, createMissingParents);
            bool leafExists = PropertyExists(parent, leaf);

            if (isArrayElems)
            {
                if (numElements is null or < 0)
                    throw new ArgumentException(
                        "value_type='array_elements' requires num_elements >= 0.");
                NiPropertyObject arr;
                if (leafExists)
                {
                    arr = (NiPropertyObject)(object)parent.GetPropertyObject(leaf, 0);
                }
                else
                {
                    (int elemPvt, string elemTn) = MapArrayElementType(typeName);
                    try
                    {
                        parent.NewSubProperty(leaf, (NiPropValueTypes)elemPvt, true, elemTn, 0);
                    }
                    catch when (elemPvt == (int)NiPropValueTypes.PropValType_NamedType)
                    {
                        NiPropertyObject typedArr = (NiPropertyObject)(object)
                            _engine!.NewPropertyObject((NiPropValueTypes)elemPvt, true, elemTn, 0);
                        parent.SetPropertyObject(leaf, PropOption_InsertIfMissing, typedArr);
                    }
                    arr = (NiPropertyObject)(object)parent.GetPropertyObject(leaf, 0);
                }
                arr.SetNumElements(numElements.Value, 0);
            }
            else
            {
                // A named_type/enum request on an EXISTING node of a DIFFERENT type replaces it
                // with a fresh typed instance (mirrors CreateStepPropertyAsync's retype path).
                string existingTypeDisp = "";
                if (leafExists)
                    try { existingTypeDisp = ((NiPropertyObject)(object)
                            parent.GetPropertyObject(leaf, 0)).GetTypeDisplayString("", 0); }
                    catch { }

                bool retype = leafExists && (isNamedType || isEnum)
                    && !string.IsNullOrWhiteSpace(typeName)
                    && existingTypeDisp != typeName
                    && !existingTypeDisp.StartsWith(typeName + " ", StringComparison.Ordinal);

                if (retype)
                {
                    NiPropertyObject typedNew = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                        NiPropValueTypes.PropValType_NamedType, false, typeName!.Trim(), 0);
                    parent.SetPropertyObject(leaf, 0, typedNew);
                }
                else if (!leafExists)
                {
                    if (isEnum || isNamedType)
                    {
                        // A member of a file-defined type (enum, or a named container/leaf):
                        // instantiate as a FULL typed instance so its fields materialise and it
                        // carries its real type instead of an anonymous 'Container'.
                        if (string.IsNullOrWhiteSpace(typeName))
                            throw new ArgumentException(
                                $"value_type='{valueType}' requires type_name (a data type defined in the file).");
                        InstantiateNamedTypeMember(parent, leaf, typeName!.Trim());
                    }
                    else
                    {
                        parent.NewSubProperty(leaf,
                            (NiPropValueTypes)MapPropValueType(vtl), false, "", 0);
                    }
                }

                // A numeric REPRESENTATION must be applied BEFORE the value (a wide property rejects
                // SetValNumber); ApplyNumericRepresentation writes the value with the matching
                // width-specific setter, so skip the generic write in that case.
                bool repWroteValue = false;
                if (!string.IsNullOrWhiteSpace(representation) || numberFormat != null)
                {
                    var target = (NiPropertyObject)(object)parent.GetPropertyObject(leaf, 0);
                    ApplyNumericRepresentation(target, representation, numberFormat, value);
                    repWroteValue = !string.IsNullOrWhiteSpace(representation) && value != null;
                }
                if (!repWroteValue)
                    SetLeafValue(parent, leaf, vtl, value, ordinal, typeName, sf, filePath);
            }

            // Apply PropFlags LAST. SetFlags has OR semantics (it turns bits ON and can never turn
            // one off), so an exact-flags write — needed when the original has 0x0 where the engine's
            // own LoadPrototype left 0x4 (PassByReference) behind — goes through the property's Flags
            // *setter* instead, which assigns the whole bitfield.
            if (flags.HasValue)
            {
                if (clearFlags) SetExactFlags(parent, leaf, flags.Value);
                else            parent.SetFlags(leaf, 0, flags.Value);
            }

            PersistScope(isStation, sf, filePath, save);

            // Read the node back.
            NiPropertyObject prop = (NiPropertyObject)(object)parent.GetPropertyObject(leaf, 0);
            var info = new PropertyNodeInfo
            {
                Scope = scope, SequenceName = sequenceName, LookupString = lookupString
            };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElem);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElem;
            if (info.ValueType is "Number" or "Boolean" or "String" or "Enum")
                info.Value = TryGetValue(prop);
            try { info.Flags    = prop.GetFlags("", 0); } catch (Exception ex) { _logger.LogDebug(ex, "GetFlags read-back failed for '{Path}'.", lookupString); }
            try { info.TypeName = NullIfEmpty(prop.GetTypeDisplayString("", 0)); } catch (Exception ex) { _logger.LogDebug(ex, "GetTypeDisplayString read-back failed for '{Path}'.", lookupString); }
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task DeletePropertyNodeAsync(string filePath, string scope,
        string? sequenceName, string lookupString, bool save)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            bool     isStation = IsStationGlobalsScope(scope);
            dynamic? sf        = isStation ? null : GetOrLoadSeqFile(filePath);
            NiPropertyObject root = ResolveScopeRoot(sf, scope, sequenceName);
            // DeleteSubProperty accepts a dotted lookup path, so a top-level Parameter and a
            // nested submember are removed the same way (the parent of a top-level Parameter is
            // the 'Parameters' container itself → the whole parameter + its structure go).
            root.DeleteSubProperty(lookupString, 0);
            PersistScope(isStation, sf, filePath, save);
        });
    }

    /// <summary>
    /// Assigns a property's PropFlags bitfield EXACTLY, i.e. also turning bits OFF. Needed because
    /// <c>SetFlags</c> could only ever add bits in practice, so a rebuild could not reproduce an
    /// original that has 0x0 where the engine's own LoadPrototype left 0x4 (PassByReference) behind
    /// on a caller's argument. Strategy: SetFlags → verify; if bits survive, drive the INSTANCE
    /// OVERRIDE flags (the per-instance layer on top of the type's flags) → verify again. Returns the
    /// flags actually in effect so callers can report a residual honestly.
    /// </summary>
    private int SetExactFlags(NiPropertyObject container, string leafPath, int wanted)
    {
        int Read()
        {
            try { return container.GetFlags(leafPath, 0); } catch { return wanted; }
        }

        try { container.SetFlags(leafPath, 0, wanted); }
        catch (Exception ex) { _logger.LogDebug(ex, "SetFlags failed for '{Path}'.", leafPath); }
        int now = Read();
        if (now == wanted) return now;

        try
        {
            container.SetInstanceOverrideFlags(leafPath, 0, wanted);
            now = Read();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SetInstanceOverrideFlags failed for '{Path}'.", leafPath); }

        if (now != wanted)
            _logger.LogDebug("Exact flag write on '{Path}' wanted 0x{Wanted:X} but effective flags are 0x{Now:X}.",
                leafPath, wanted, now);
        return now;
    }

    private static bool IsStationGlobalsScope(string scope)
        => string.Equals(scope?.Trim(), "StationGlobals", StringComparison.OrdinalIgnoreCase);

    // Resolves the base PropertyObject for a scope: a sequence's Parameters or Locals (both need
    // sequence_name), the file's FileGlobals, the engine's StationGlobals, or the whole sequence
    // file as a property tree (AsPropertyObject). Same roots used by set_property_value /
    // set_parameter_comment / get_property_tree — unified so the node tools reach ALL of them.
    private NiPropertyObject ResolveScopeRoot(dynamic? sf, string scope, string? sequenceName)
    {
        switch ((scope ?? "").Trim().ToLowerInvariant())
        {
            case "parameters":
                if (string.IsNullOrWhiteSpace(sequenceName))
                    throw new ArgumentException("scope='Parameters' requires sequence_name.");
                return (NiPropertyObject)(object)sf!.GetSequenceByName(sequenceName).Parameters;
            case "locals":
                if (string.IsNullOrWhiteSpace(sequenceName))
                    throw new ArgumentException("scope='Locals' requires sequence_name.");
                return (NiPropertyObject)(object)sf!.GetSequenceByName(sequenceName).Locals;
            case "fileglobals":
                return GetFileGlobals(sf!);
            case "stationglobals":
                return GetStationGlobals();
            case "sequencefile":
                return (NiPropertyObject)(object)((NiSequenceFile)(object)sf!).AsPropertyObject();
            default:
                throw new ArgumentException(
                    $"Unknown scope '{scope}'. Use Parameters/Locals/FileGlobals/StationGlobals/SequenceFile.");
        }
    }

    // Navigates to the container that will hold the leaf, creating each missing intermediate
    // segment as an anonymous Container when createMissing is true (so a deep path like
    // "MDC_cmd.Request.Cmd" materialises Request+Cmd on the way down).
    private NiPropertyObject ResolveOrCreateParent(NiPropertyObject root, string parentPath,
        bool createMissing)
    {
        if (string.IsNullOrEmpty(parentPath)) return root;
        if (PropertyExists(root, parentPath))
            return (NiPropertyObject)(object)root.GetPropertyObject(parentPath, 0);
        if (!createMissing)
            throw new ArgumentException(
                $"Intermediate node '{parentPath}' does not exist (set create_missing_parents=true to build it).");

        NiPropertyObject cur = root;
        string acc = "";
        foreach (var seg in parentPath.Split('.'))
        {
            acc = acc.Length == 0 ? seg : acc + "." + seg;
            if (!PropertyExists(root, acc))
                cur.NewSubProperty(seg, NiPropValueTypes.PropValType_Container, false, "", 0);
            cur = (NiPropertyObject)(object)root.GetPropertyObject(acc, 0);
        }
        return cur;
    }

    // The element PropValType + type name for a new array, from an optional element type name
    // (mirrors CreateStepPropertyAsync's array_elements element-type mapping).
    private static (int pvt, string typeName) MapArrayElementType(string? typeName) =>
        (typeName ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "container"
                => ((int)NiPropValueTypes.PropValType_Container, ""),
            "number" or "double" or "float" or "int" or "integer"
                => ((int)NiPropValueTypes.PropValType_Number, ""),
            "boolean" or "bool"
                => ((int)NiPropValueTypes.PropValType_Boolean, ""),
            "string" or "expression"
                => ((int)NiPropValueTypes.PropValType_String, ""),
            "reference" or "object reference" or "objectreference" or "objref"
                => ((int)NiPropValueTypes.PropValType_Reference, ""),
            _   => ((int)NiPropValueTypes.PropValType_NamedType, typeName!.Trim()),
        };

    // Sets a leaf's scalar/enum value (reusing set_property_value's coercion rules). Only writes
    // when a value/ordinal is actually supplied, so a flags-only call never clobbers the value.
    private void SetLeafValue(NiPropertyObject container, string leafPath, string vtl,
        string? value, int? ordinal, string? typeName, dynamic? sf, string filePath)
    {
        switch (vtl)
        {
            case "named_type": case "namedtype": case "type":
            case "container":
            case "reference": case "object reference":
            case "objectreference": case "objref":
                break; // structural — no scalar to assign
            case "boolean": case "bool":
                if (value != null)
                    container.SetValBoolean(leafPath, 0,
                        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
                break;
            case "number": case "double": case "float": case "int": case "integer":
                if (value != null)
                {
                    // Accept a hex literal ("0x374e") — the form the editor shows for a %#.4x UInt64.
                    string lit = value.Trim();
                    bool   hex = lit.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                    double numVal = hex
                        ? Convert.ToUInt64(lit.Substring(2), 16)
                        : double.Parse(lit, System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture);
                    try { container.SetValNumber(leafPath, 0, numVal); }
                    catch
                    {
                        // A UInt64/Int64-REPRESENTED target rejects SetValNumber ("representations
                        // must match exactly"); an enum-typed one needs the coerce. Try both before
                        // giving up so a wide-integer member/array element is actually writable.
                        bool ok = false;
                        try { container.SetValUnsignedInteger64(leafPath, 0, (ulong)numVal); ok = true; }
                        catch (Exception ex) { _logger.LogDebug(ex, "SetValUnsignedInteger64 failed for '{Path}'.", leafPath); }
                        if (!ok)
                            try { container.SetValInteger64(leafPath, 0, (long)numVal); ok = true; }
                            catch (Exception ex) { _logger.LogDebug(ex, "SetValInteger64 failed for '{Path}'.", leafPath); }
                        if (!ok)
                        {
                            container.SetValNumber(leafPath,
                                (int)NiPropOptions.PropOption_CoerceToEnum, numVal);
                            PromoteEnumLeafToExplicit(container, leafPath);
                        }
                    }
                }
                break;
            case "enum":
                // Always land an EXPLICITLY-SET value (FileDiffer "[val]") — see WriteEnumLeafExplicit.
                WriteEnumLeafExplicit(container, leafPath, ordinal, value, typeName, sf, filePath);
                break;
            default: // string (or enum-by-label)
                if (value != null)
                {
                    try { container.SetValString(leafPath, 0, value); }
                    catch { container.SetValString(leafPath,
                        (int)NiPropOptions.PropOption_CoerceToEnum, value); }
                }
                break;
        }
    }

    // Persists a scope edit: StationGlobals commit to the station .ini via the engine; every other
    // scope lives in the sequence file, so save it (with the standard retry) and refresh the cache.
    private void PersistScope(bool isStation, dynamic? sf, string filePath, bool save)
    {
        if (isStation)
        {
            ((NiEngine)(object)_engine!).CommitGlobalsToDisk();
            return;
        }
        if (save)
        {
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf!, filePath);
            _loadedSequenceFiles[filePath] = sf!;
        }
    }

    /// <summary>
    /// Creates <paramref name="leaf"/> under <paramref name="parent"/> as a FULL instance of the
    /// file/engine-defined type <paramref name="typeName"/> — the same as the editor's "insert a
    /// variable of type X": <c>Engine.NewPropertyObject(PropValType_NamedType, typeName)</c>
    /// materialises the type's whole structure (all container fields, an enum's type binding), then
    /// <c>SetPropertyObject(PropOption_InsertIfMissing)</c> hangs it at the target. This is why a
    /// named container member (TFW_DB_TestCasesLimits, VisionSensorSbsi_Reply_Payload, Error) comes
    /// out correctly TYPED with its fields present, instead of an anonymous 'Container'. Falls back
    /// to NewSubProperty if the standalone instance cannot be built.
    /// </summary>
    private void InstantiateNamedTypeMember(NiPropertyObject parent, string leaf, string typeName)
    {
        try
        {
            NiPropertyObject typed = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                NiPropValueTypes.PropValType_NamedType, false, typeName, 0);
            parent.SetPropertyObject(leaf, 0x1 /* PropOption_InsertIfMissing */, typed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NewPropertyObject('{Type}') failed; falling back to NewSubProperty.", typeName);
            parent.NewSubProperty(leaf, NiPropValueTypes.PropValType_NamedType, false, typeName, 0);
        }
    }

    // Resolves the value container for the structured-property tools: a sequence's Locals
    // (when sequenceName is given) or the file's FileGlobals (when it is omitted).
    private NiPropertyObject ResolveValueContainer(dynamic sf, string? sequenceName)
    {
        if (string.IsNullOrEmpty(sequenceName))
            return GetFileGlobals(sf);
        var seq = sf.GetSequenceByName(sequenceName);
        return (NiPropertyObject)(object)seq.Locals;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static int MapPropValueType(string valueType) => valueType.ToLowerInvariant() switch
    {
        "boolean" or "bool"                                   => (int)NiPropValueTypes.PropValType_Boolean,
        "number" or "double" or "float" or "int" or "integer" => (int)NiPropValueTypes.PropValType_Number,
        "container"                                           => (int)NiPropValueTypes.PropValType_Container,
        "reference" or "object reference" or
        "objectreference" or "objref"                         => (int)NiPropValueTypes.PropValType_Reference,
        _                                                     => (int)NiPropValueTypes.PropValType_String,
    };

    // Best-effort classification of a PropertyObject's value kind, mirroring the probing
    // style used elsewhere for COM PropertyObjects (avoids the obsolete GetType overload).
    private static string InferValueKind(dynamic prop, out bool isArray, out int numElements)
    {
        isArray = false; numElements = 0;
        if (prop == null) return "Empty";
        try { numElements = Convert.ToInt32((object)prop.GetNumElements()); isArray = true; return "Array"; } catch (Exception) { /* best-effort: probe for array kind — intentionally ignored */ }
        int numSub = 0;
        try { numSub = Convert.ToInt32((object)prop.GetNumSubProperties("")); } catch (Exception) { /* best-effort: probe for container kind — intentionally ignored */ }
        if (numSub > 0) return "Container";
        try { _ = (double)prop.GetValNumber("", 0);  return "Number";  } catch (Exception) { /* best-effort: probe for number kind — intentionally ignored */ }
        try { _ = (bool)  prop.GetValBoolean("", 0); return "Boolean"; } catch (Exception) { /* best-effort: probe for boolean kind — intentionally ignored */ }
        try { _ = (string)prop.GetValString("", 0);  return "String";  } catch (Exception) { /* best-effort: probe for string kind — intentionally ignored */ }
        // Enum leaf: plain reads all throw; a coerced read succeeds (mirrors TryReadEnumValue).
        try { _ = (double)prop.GetValNumber("", PropOption_CoerceToNumber);
              _ = (string)prop.GetValString("", PropOption_CoerceToString); return "Enum"; }
        catch (Exception) { /* not an enum */ }
        return "Unknown";
    }

    /// <inheritdoc/>
    public async Task<List<VariableInfo>> GetFileGlobalsAsync(string sequenceFilePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            try { return MapVariables(GetFileGlobals(sf)); }
            catch { return new List<VariableInfo>(); }
        });
    }

    /// <inheritdoc/>
    public async Task<List<VariableInfo>> GetStationGlobalsAsync()
    {
        EnsureConnected();
        return await Task.Run(() => { try { return MapVariables(GetStationGlobals()); } catch { return new List<VariableInfo>(); } });
    }

    // PropFlags bits we annotate (NationalInstruments.TestStand.Interop.API.PropertyFlags).
    private const int PropFlags_Hidden         = 0x00000008;
    private const int PropFlags_HiddenInTypes  = 0x00000010;

    /// <inheritdoc/>
    public async Task<PropertyNode> GetPropertyTreeAsync(string root, string? filePath,
        string? lookupString, int maxDepth, bool includeHidden, int maxArrayElements,
        string? sequenceName = null, string? stepGroup = null, string? stepName = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            // Resolve the starting property object for the requested root.
            NiPropertyObject start;
            string rootLabel;
            if (string.Equals(root, "FileGlobals", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("root='FileGlobals' requires 'file_path'.");
                start     = GetFileGlobals(GetOrLoadSeqFile(filePath));
                rootLabel = "FileGlobals";
            }
            else if (string.Equals(root, "SequenceFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("root='SequenceFile' requires 'file_path'.");
                // AsPropertyObject() exposes the ENTIRE sequence file as a property tree:
                // every sequence, step, parameter and (often hidden) engine property.
                start     = (NiPropertyObject)(object)
                            ((NiSequenceFile)(object)GetOrLoadSeqFile(filePath)).AsPropertyObject();
                rootLabel = System.IO.Path.GetFileName(filePath);
            }
            else
            {
                start     = GetStationGlobals();
                rootLabel = "StationGlobals";
            }

            // Convenience addressing for root='SequenceFile': a sequence (and optionally a step) by
            // NAME. The engine's own lookup has no "Sequences" node — the real path is
            // Data.Seq[i].Main[j], which is neither guessable nor stable when steps move — so resolve
            // the indices here and let lookup_string stay relative to the resolved object.
            if (!string.IsNullOrWhiteSpace(sequenceName))
            {
                if (!string.Equals(root, "SequenceFile", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        "sequence_name/step_name are only meaningful with root='SequenceFile'.");
                dynamic sfDyn = GetOrLoadSeqFile(filePath!);
                dynamic seq   = sfDyn.GetSequenceByName(sequenceName);
                if (!string.IsNullOrWhiteSpace(stepName))
                {
                    int sg = ParseStepGroup(string.IsNullOrWhiteSpace(stepGroup) ? "Main" : stepGroup!);
                    dynamic step = ResolveStepInGroup(seq, sg, stepName!);
                    start     = (NiPropertyObject)(object)((NiStep)(object)step).AsPropertyObject();
                    rootLabel = $"{sequenceName}/{stepGroup ?? "Main"}/{stepName}";
                }
                else
                {
                    start     = (NiPropertyObject)(object)((NiSequence)(object)seq).AsPropertyObject();
                    rootLabel = sequenceName!;
                }
            }

            // Optionally descend to a sub-path before walking.
            if (!string.IsNullOrWhiteSpace(lookupString))
            {
                start     = (NiPropertyObject)(object)start.GetPropertyObject(lookupString, 0);
                rootLabel = string.IsNullOrWhiteSpace(sequenceName)
                            ? lookupString!
                            : rootLabel + "." + lookupString;
            }

            // Hard cap on total nodes so a pathological (or cyclic) tree cannot run away,
            // in addition to the per-branch depth and per-array element limits.
            int budget = 200_000;
            return BuildPropertyNode(start, rootLabel, 0, maxDepth, includeHidden,
                Math.Max(0, maxArrayElements), ref budget);
        });
    }

    // Recursively converts a PropertyObject into a PropertyNode. Enumeration via
    // GetNumSubProperties/GetNthSubProperty returns ALL members regardless of the Hidden
    // flag (that flag is purely a Sequence-Editor display concern), so hidden properties
    // are included by default and only filtered out when includeHidden is false.
    private PropertyNode BuildPropertyNode(NiPropertyObject po, string name, int depth,
        int maxDepth, bool includeHidden, int maxArrayElements, ref int budget)
    {
        var node = new PropertyNode { Name = name };

        try
        {
            int flags = po.GetFlags("", 0);
            node.Flags           = flags;
            node.IsHidden        = (flags & PropFlags_Hidden)        != 0;
            node.IsHiddenInTypes = (flags & PropFlags_HiddenInTypes) != 0;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "GetFlags failed for '{Name}'.", name); }

        try { node.Type = po.GetTypeDisplayString("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetTypeDisplayString failed for '{Name}'.", name); }

        int numSub = 0;
        try { numSub = po.GetNumSubProperties(""); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetNumSubProperties failed for '{Name}'.", name); }
        node.SubPropertyCount = numSub;

        // Named members → container.
        if (numSub > 0)
        {
            node.ValueType = "Container";
            if (depth >= maxDepth) { node.Truncated = true; return node; }

            var children = new List<PropertyNode>();
            for (int i = 0; i < numSub; i++)
            {
                if (budget <= 0) { node.Truncated = true; break; }
                try
                {
                    NiPropertyObject child = po.GetNthSubProperty("", i, 0);
                    if (!includeHidden)
                    {
                        int cf = 0;
                        try { cf = child.GetFlags("", 0); } catch { /* default: keep */ }
                        if ((cf & PropFlags_Hidden) != 0) continue;
                    }
                    budget--;
                    children.Add(BuildPropertyNode(child, SafeName(child, i), depth + 1,
                        maxDepth, includeHidden, maxArrayElements, ref budget));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Sub-property {Index} of '{Name}' failed.", i, name); }
            }
            node.Children = children;
            return node;
        }

        // No named members: it may be an array (indexed elements) or a scalar leaf.
        int numElem = 0;
        try { numElem = po.GetNumElements(); } catch { /* not an array */ }

        if (numElem > 0)
        {
            node.ValueType = "Array";
            node.IsArray   = true;
            node.ArraySize = numElem;
            if (depth >= maxDepth) { node.Truncated = true; return node; }

            var children = new List<PropertyNode>();
            int cap = maxArrayElements == 0 ? numElem : Math.Min(numElem, maxArrayElements);
            for (int i = 0; i < cap; i++)
            {
                if (budget <= 0) { node.Truncated = true; break; }
                try
                {
                    NiPropertyObject elem = po.GetPropertyObjectByOffset(i, 0);
                    if (elem == null) break;
                    budget--;
                    var childNode = BuildPropertyNode(elem, $"[{i}]", depth + 1, maxDepth,
                        includeHidden, maxArrayElements, ref budget);
                    // Array elements may carry their OWN PropertyObject.Name (ViCall.Parms
                    // entries are named after the connector-pane label; the editor and
                    // FileDiffer display "[i] Name" and pair elements by it). Surface it.
                    try
                    {
                        string en = elem.Name;
                        if (!string.IsNullOrEmpty(en) && en != $"[{i}]")
                            childNode.ElementName = en;
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Element name read failed at {Index} of '{Name}'.", i, name); }
                    children.Add(childNode);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Array element {Index} of '{Name}' failed.", i, name); }
            }
            if (cap < numElem) node.Truncated = true;
            node.Children = children;
            return node;
        }

        // Scalar leaf — read the value and infer its kind (mirrors TryGetValue's order).
        try { node.Value = po.GetValNumber("", 0);  node.ValueType = "Number";  AnnotateNumber(node, po); return node; } catch { }
        // A UInt64/Int64-REPRESENTED number rejects GetValNumber ("representations must match
        // exactly"); the wide readers are the only way to see it. Without this such a leaf (a
        // UInt64 VID/PID, a UInt64 array element) came back as "Empty" with no value at all.
        var wide = TryReadWideInteger(po);
        if (wide != null) { node.Value = wide; node.ValueType = "Number"; AnnotateNumber(node, po); return node; }
        try { node.Value = po.GetValBoolean("", 0); node.ValueType = "Boolean"; return node; } catch { }
        try { node.Value = po.GetValString("", 0);  node.ValueType = "String";  return node; } catch { }
        // Enum leaf: plain reads all throw; read {ordinal, symbolicName} via coercion so a Locals /
        // FileGlobals enum default is not reported as Empty/0.
        var enumVal = TryReadEnumValue(po);
        if (enumVal != null)
        {
            node.Value     = enumVal;
            node.ValueType = "Enum";
            // Same rule the exporter uses: an enum still at its TYPE DEFAULT reports an EMPTY symbolic
            // name, an explicitly-set one reports its enumerator. That maps onto the FileDiffer's
            // {val}/[val], so it answers "does this value have to be written?".
            node.IsDefault = string.IsNullOrEmpty(enumVal.SymbolicName);
            return node;
        }
        node.ValueType = "Empty";
        return node;
    }

    // Numeric leaves carry a REPRESENTATION and a display format that a 1:1 rebuild must reproduce
    // (a UInt64 %#.4x parameter written as the default Float64 makes TestStand's own analyzer reject
    // an expression that passes it: "Expected Number {64-bit Floating Point}").
    private void AnnotateNumber(PropertyNode node, NiPropertyObject po)
    {
        node.Representation = TryReadRepresentation(po);
        node.NumericFormat  = TryReadNumericFormat(po);
    }

    private static string SafeName(NiPropertyObject po, int index)
    {
        try { var n = po.Name; if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return $"#{index}";
    }

    /// <inheritdoc/>
    public async Task SetFileGlobalAsync(string sequenceFilePath, string variableName,
        object value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            dynamic sf;
            try
            {
                sf = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                    ? cached
                    : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            }
            catch (Exception ex) { throw new InvalidOperationException("GetSeqFile failed: " + ex.Message, ex); }

            NiPropertyObject fg;
            try { fg = GetFileGlobals(sf); }
            catch (Exception ex) { throw new InvalidOperationException("FileGlobals access failed: " + ex.Message, ex); }

            string valStr = value?.ToString() ?? "";

            // Determine the target property type. If the FileGlobal ALREADY exists, PRESERVE its
            // authored type — the value arrives from the tool as a string, and the old logic then
            // mis-typed "true"/"false" as a String, so a Boolean default was silently written as a
            // string and never persisted as a real Boolean (this is the reported bug). For a NEW
            // global, infer from the literal, treating "true"/"false" as Boolean. Writes to
            // FileGlobalsDefaultValues (see GetFileGlobals), same container set_property_value uses.
            int propType;
            if (PropertyExists(fg, variableName))
            {
                NiPropertyObject existing =
                    (NiPropertyObject)(object)fg.GetPropertyObject(variableName, 0);
                propType = InferValueKind(existing, out _, out _) switch
                {
                    "Boolean" => 2,
                    "Number"  => 3,
                    _         => 1,   // String / Enum / Container → string path (coerces enums)
                };
            }
            else
            {
                propType = value switch
                {
                    bool   => 2,
                    double => 3,
                    float  => 3,
                    int    => 3,
                    long   => 3,
                    _      => (valStr.Equals("true",  StringComparison.OrdinalIgnoreCase) ||
                               valStr.Equals("false", StringComparison.OrdinalIgnoreCase)) ? 2
                            : double.TryParse(valStr, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture, out _) ? 3 : 1
                };
                fg.NewSubProperty(variableName, (NiPropValueTypes)propType, false, "", 0);
            }

            SetPropertyValueByType(fg, variableName, valStr, propType);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, sequenceFilePath);
        });
    }

    private static NiPropertyObject GetFileGlobals(dynamic sf)
        => ((NiSequenceFile)(object)sf).FileGlobalsDefaultValues;

    private static bool PropertyExists(NiPropertyObject container, string name)
    {
        try { container.GetPropertyObject(name, 0); return true; }
        catch { return false; }
    }

    private NiPropertyObject GetStationGlobals()
        => ((NiEngine)(object)_engine!).Globals;

    /// <inheritdoc/>
    public async Task SetStationGlobalAsync(string variableName, object value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sg = GetStationGlobals();
            int propType = value switch
            {
                bool   => 2,
                double => 3,
                float  => 3,
                int    => 3,
                long   => 3,
                _      => double.TryParse(value?.ToString(),
                              System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture, out _) ? 3 : 1
            };
            if (!PropertyExists(sg, variableName))
                sg.NewSubProperty(variableName, (NiPropValueTypes)propType, false, "", 0);
            SetPropertyValueByType(sg, variableName, value?.ToString() ?? "", propType);
            ((NiEngine)(object)_engine!).CommitGlobalsToDisk();
        });
    }

    /// <inheritdoc/>
    public async Task DeleteStationGlobalAsync(string variableName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sg = GetStationGlobals();
            if (PropertyExists(sg, variableName))
                sg.DeleteSubProperty(variableName, 0);
            // Persist the removal so the StationGlobals.ini on disk matches in-memory state.
            ((NiEngine)(object)_engine!).CommitGlobalsToDisk();
        });
    }

    /// <inheritdoc/>
    public async Task InsertFileGlobalAsync(string sequenceFilePath, string variableName,
        string dataType)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(sequenceFilePath);
            // Detect an array suffix ("number[]", "array:string", …) — same convention as
            // InsertLocalVariableAsync — so array file globals can be created for the
            // get/set/resize_array_variable tools to operate on.
            // Keep the ORIGINAL casing for named types; lower-case only for builtin matching.
            string rawType = dataType.Trim();
            bool   isArray = rawType.EndsWith("[]") ||
                             rawType.StartsWith("array:", StringComparison.OrdinalIgnoreCase);
            string baseDataType = rawType;
            if (baseDataType.EndsWith("[]")) baseDataType = baseDataType[..^2].Trim();
            if (baseDataType.StartsWith("array:", StringComparison.OrdinalIgnoreCase))
                baseDataType = baseDataType.Substring("array:".Length).Trim();
            // Builtins map to their PropValType; anything else is a NAMED type (same contract
            // as insert_local_variable). No silent string fallback — an unknown name that is
            // not a defined type surfaces as an engine error instead of a wrong-typed global.
            int propType; string typeNameParam = "";
            switch (baseDataType.ToLowerInvariant())
            {
                case "string":                                          propType = 1; break;
                case "boolean": case "bool":                            propType = 2; break;
                case "number": case "double": case "float":
                case "int":    case "integer":                          propType = 3; break;
                case "reference": case "object reference":
                case "objectreference": case "objref":
                    propType = (int)NiPropValueTypes.PropValType_Reference; break;
                case "container":
                    propType = (int)NiPropValueTypes.PropValType_Container; break;
                default:        propType = 4; typeNameParam = baseDataType; break;
            }
            var fg2 = GetFileGlobals(sf);
            fg2.NewSubProperty(variableName, (NiPropValueTypes)propType, isArray, typeNameParam, 0);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, sequenceFilePath);
        });
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<StepInfo>> GetStepsAsync(string sequenceFilePath,
        string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            var seq = sf.GetSequenceByName(sequenceName);
            // Collect steps from all three groups
            var all = new List<StepInfo>();
            string[] groupNames = { "Setup", "Main", "Cleanup" };
            for (int g = 0; g <= 2; g++)
            {
                try
                {
                    int count = Convert.ToInt32((object)seq.GetNumSteps((NiStepGroups)g));
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var step = MapStepInfo(seq.GetStep(i, (NiStepGroups)g));
                            // Omit the default "Main" group (g==1) to save tokens; absent = Main.
                            step.StepGroup = g == 1 ? null : groupNames[g];
                            all.Add(step);
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to map step info at index {Index} in group {Group}.", i, g); }
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate steps for group {Group}.", g); }
            }
            return all;
        });
    }

    /// <inheritdoc/>
    public async Task<StepInfo> GetStepAsync(string sequenceFilePath, string sequenceName,
        string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapStepInfo(FindStepInAllGroups(seq, stepName));
        });
    }

    /// <inheritdoc/>
    public async Task EnableStepAsync(string sequenceFilePath, string sequenceName,
        string stepName, bool enabled)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            var seq = sf.GetSequenceByName(sequenceName);
            var step = FindStepInAllGroups(seq, stepName);
            // Step has no `StepEnabled` property. A step is "disabled" by setting its
            // RunMode to "Skip" (and re-enabled with "Normal") — this is exactly the
            // representation GetStepsAsync reads back (RunMode == "Skip" → Enabled=false).
            step.SetRunModeEx(enabled ? "Normal" : "Skip", System.Type.Missing);
        });
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> GetStepPropertiesAsync(
        string sequenceFilePath, string sequenceName, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);
            var seq = sf.GetSequenceByName(sequenceName);
            var step = FindStepInAllGroups(seq, stepName);
            // Resolve the step's PropertyObject ONCE via a typed (vtable) call and reuse it for
            // every GetValString read below. The parameterless dynamic AsPropertyObject() is the
            // DLR call most prone to intermittent TargetParameterCountException / RuntimeBinder
            // failures under cumulative load in the shared-engine test harness; binding it
            // statically removes that flakiness (e.g. ComparisonType reading back null).
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            var props = new Dictionary<string, object>();
            try { props["Name"]            = (string)step.Name; }            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step Name property."); }
            try { props["StepType"]        = (string)step.StepType.Name; }   catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step StepType.Name property."); }
            try { props["Enabled"]         = (string)step.RunMode != "Skip"; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step RunMode property."); }
            try { props["PreExpression"]   = (string)step.PreExpression; }   catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step PreExpression property."); }
            try { props["PostExpression"]  = (string)step.PostExpression; }  catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step PostExpression property."); }
            try { props["StatusExpression"]= (string)step.StatusExpression;} catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step StatusExpression property."); }
            // Read the user-set description first (stored in property bag as TS.Description).
            // For steps without stored description, step.Description returns the auto-generated
            // type-name (e.g. "Action"), which masks any stored value — so try stored first.
            string? desc = null;
            try
            {
                var storedDesc = (string)stepPo.GetValString("TS.Description", 0);
                if (!string.IsNullOrWhiteSpace(storedDesc)) desc = storedDesc;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read stored TS.Description from step property bag."); }
            if (desc == null) try { desc = (string)step.Description; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step Description property."); }
            if (string.IsNullOrEmpty(desc))
                try { desc = (string)stepPo.GetValString("Description", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Description from step property bag."); }
            if (desc != null) props["Description"] = desc;
            // Also read the PropertyObject.Comment attribute (separate from Description)
            try
            {
                var poComment = (string)stepPo.Comment;
                if (!string.IsNullOrEmpty(poComment)) props["Comment"] = poComment;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step PropertyObject.Comment."); }
            try
            {
                var expr = (string)stepPo.GetValString("Module.Expression", 0);
                props["ModuleExpression"] = expr;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Module.Expression from step property bag."); }
            // ── Run-time enum properties (exposed for readback / round-trip verification) ──
            // Universal typed Step enum properties (string-valued: e.g. RunMode is
            // "Normal"/"Skip"/"Pass"/"Fail", Pass/FailAction is "Next"/"Break"/"Terminate"/
            // "Goto"/"Cback", LoopType is "NoLooping"/"FixedNumLoops"/"PassFailCount"/"Custom").
            // Each is guarded so a step type lacking one simply omits that key.
            try { props["RunMode"]    = (string)step.RunMode;    } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step RunMode."); }
            try { props["PassAction"] = (string)step.PassAction; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step PassAction."); }
            try { props["FailAction"] = (string)step.FailAction; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step FailAction."); }
            try { props["LoopType"]   = (string)step.LoopType;   } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step LoopType."); }
            // NumericLimitTest / StringValueTest comparison operator, stored as the STRING
            // property "Comp" (e.g. "GELE"/"GT"/"EQ" for numeric, "CaseSensitive"/"IgnoreCase"
            // for string). Present only on step types that have it; absent otherwise.
            try { props["ComparisonType"] = (string)stepPo.GetValString("Comp", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step comparison type (Comp)."); }
            return props;
        });
    }

    // ── Reports ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ReportInfo> GenerateReportAsync(string executionId,
        string outputPath, string format = "HTML")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                var reportGen = ((dynamic)_engine!).ReportGenerator;
                // Use the engine's built-in report generator
                _logger.LogInformation("Generating {Format} report for execution {Id}",
                    format, executionId);

                return new ReportInfo
                {
                    ExecutionId   = executionId,
                    ReportPath    = outputPath,
                    Format        = format,
                    GeneratedAt   = DateTime.UtcNow,
                    OverallResult = "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate report");
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<string> GetReportTextAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId);
            if (exec == null) return $"Execution {executionId} not found or already completed.";

            try
            {
                // Get the UUT result from the execution context
                var report = exec.ReportText ?? "No report available.";
                return (string)report;
            }
            catch
            {
                return $"Report not available for execution {executionId}.";
            }
        });
    }

    // ── UUT / Batch ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<UutInfo> GetUutInfoAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            try
            {
                var uut = exec.UUT;
                return new UutInfo
                {
                    SerialNumber      = TryGetString(uut, "SerialNumber"),
                    PartNumber        = TryGetString(uut, "PartNumber"),
                    BatchSerialNumber = TryGetString(uut, "BatchSerialNumber"),
                    Result            = TryGetString(uut, "Result"),
                    StartTime         = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get full UUT info");
                return new UutInfo { SerialNumber = "Unknown" };
            }
        });
    }

    /// <inheritdoc/>
    public async Task SetUutSerialNumberAsync(string executionId, string serialNumber)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.UUT.SerialNumber = serialNumber;
        });
    }

    /// <inheritdoc/>
    public async Task SetUutPartNumberAsync(string executionId, string partNumber)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.UUT.PartNumber = partNumber;
        });
    }

    // ── Adapters ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<AdapterInfo>> GetLoadedAdaptersAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<AdapterInfo>();
            // IEngine exposes adapters via NumAdapters + GetAdapter(index) — there is
            // no `Adapters` collection. Adapter has KeyName/DisplayName (no Name/Type).
            int count = (int)_engine!.NumAdapters;
            for (int i = 0; i < count; i++)
            {
                dynamic adapter = _engine!.GetAdapter(i);
                result.Add(new AdapterInfo
                {
                    Name     = TryGetString(adapter, "DisplayName"),
                    Type     = TryGetString(adapter, "KeyName"),
                    Version  = "",
                    IsLoaded = true
                });
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task LoadAdapterAsync(string adapterName)
    {
        EnsureConnected();
        await Task.Run(() => ((dynamic)_engine!).Adapters.LoadAdapter(adapterName));
    }

    /// <inheritdoc/>
    public async Task UnloadAdapterAsync(string adapterName)
    {
        EnsureConnected();
        await Task.Run(() => ((dynamic)_engine!).Adapters.UnloadAdapter(adapterName));
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<LogEntry>> GetExecutionLogAsync(string executionId,
        int maxEntries = 100)
    {
        return await Task.Run(() =>
        {
            if (_executionLogs.TryGetValue(executionId, out var entries))
                return entries.Skip(Math.Max(0, entries.Count - maxEntries)).ToList();
            return new List<LogEntry>();
        });
    }

    /// <inheritdoc/>
    public async Task ClearLogAsync(string executionId)
    {
        await Task.Run(() =>
        {
            if (_executionLogs.TryGetValue(executionId, out var log))
                log.Clear();
        });
    }

    // ── Process Model ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> GetProcessModelAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try { return (string)_engine!.StationModelSequenceFilePath; }
            catch { return "Unknown"; }
        });
    }

    /// <inheritdoc/>
    public async Task SetProcessModelAsync(string processModelPath)
    {
        EnsureConnected();
        await Task.Run(() => _engine!.StationModelSequenceFilePath = processModelPath);
    }

    // ── Result Schemas ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<string>> GetResultSchemasAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var schemas = new List<string>();
            try
            {
                var db = ((dynamic)_engine!).DatabaseLogger;
                var schemaList = db.ResultSchemas;
                for (int i = 0; i < (int)schemaList.Count; i++)
                    schemas.Add((string)schemaList[(object)i].Name);
            }
            catch { /* DB logger may not be configured */ }
            return schemas;
        });
    }

    /// <inheritdoc/>
    public async Task<string> ExportResultsAsync(string executionId, string schemaName,
        string outputPath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Exporting results for {Id} using schema {Schema}",
                    executionId, schemaName);
                // Export logic depends on the configured result schema
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export results");
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task InsertLocalVariableAsync(string filePath, string sequenceName,
        string variableName, string dataType, string? defaultValue = null,
        string? representation = null, string? numberFormat = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);

            // Detect array suffix: "number[]", "string[]", "array:number", etc. Keep the original
            // case of the base type — named types (e.g. an enum) are looked up case-sensitively.
            string rawType = dataType.Trim();
            bool   isArray = rawType.EndsWith("[]") ||
                             rawType.StartsWith("array:", StringComparison.OrdinalIgnoreCase);
            string baseDataType = rawType;
            if (baseDataType.EndsWith("[]")) baseDataType = baseDataType[..^2].Trim();
            if (baseDataType.StartsWith("array:", StringComparison.OrdinalIgnoreCase))
                baseDataType = baseDataType.Substring("array:".Length).Trim();

            // Builtins map to their PropertyValueType; anything else is treated as a NAMED type
            // (PropValType_NamedType=4) — e.g. an enum or custom data type defined in the file.
            int propType; string typeNameParam = "";
            switch (baseDataType.ToLowerInvariant())
            {
                case "string":                                          propType = 1; break;
                case "boolean": case "bool":                            propType = 2; break;
                case "number": case "double": case "float":
                case "int":    case "integer":                          propType = 3; break;
                case "reference": case "object reference":
                case "objectreference": case "objref":
                    propType = (int)NiPropValueTypes.PropValType_Reference; break;
                case "container":
                    propType = (int)NiPropValueTypes.PropValType_Container; break;
                default:        propType = 4; typeNameParam = baseDataType; break;
            }

            // NewSubProperty(lookupString, valueType, asArray, typeName, options)
            seq.Locals.NewSubProperty(variableName, (NiPropValueTypes)propType, isArray, typeNameParam, 0);

            // A numeric REPRESENTATION (UInt64/Int64) must be applied BEFORE the value — a wide
            // property rejects the plain SetValNumber below — so ApplyNumericRepresentation writes
            // the value itself through the matching width-specific setter (0x… literals accepted).
            bool repApplied = false;
            if (!string.IsNullOrWhiteSpace(representation) || numberFormat != null)
            {
                var target = (NiPropertyObject)(object)seq.Locals.GetPropertyObject(variableName, 0);
                ApplyNumericRepresentation(target, representation, numberFormat, isArray ? null : defaultValue);
                repApplied = !isArray && !string.IsNullOrWhiteSpace(representation) && defaultValue != null;
            }

            if (defaultValue != null && !repApplied)
            {
                try
                {
                    if (propType == 3)
                        seq.Locals.SetValNumber(variableName, 0, double.Parse(defaultValue));
                    else if (propType == 2)
                        seq.Locals.SetValBoolean(variableName, 0, bool.Parse(defaultValue));
                    else
                        seq.Locals.SetValString(variableName, 0, defaultValue);
                }
                catch { /* ignore default value errors */ }
            }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetLocalVariableCommentAsync(string filePath, string sequenceName,
        string variableName, string comment)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            // GetPropertyObject resolves a dotted lookup path, so nested container members
            // (e.g. "MyCont.Field") get their comment set too.
            var prop = seq.Locals.GetPropertyObject(variableName, 0);
            prop.Comment = comment;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetFileGlobalCommentAsync(string sequenceFilePath, string variableName,
        string comment)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, (NiConflictHandler)4);

            // GetFileGlobals targets FileGlobalsDefaultValues (the authored-defaults container, same
            // one set_file_global writes). GetPropertyObject resolves a dotted lookup path, so a
            // nested container member (e.g. "MyCont.Field") gets its comment set too.
            NiPropertyObject fg = GetFileGlobals(sf);
            var prop = (NiPropertyObject)(object)fg.GetPropertyObject(variableName, 0);
            prop.Comment = comment;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, sequenceFilePath);
            _loadedSequenceFiles[sequenceFilePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetParameterCommentAsync(string filePath, string sequenceName,
        string parameterName, string comment)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            // A sequence's parameters live on Parameters (a PropertyObject); GetPropertyObject
            // resolves a dotted path so nested members work too. This is the only tool that
            // reaches a Parameter's comment (set_local_variable_comment only touches Locals).
            var prop = seq.Parameters.GetPropertyObject(parameterName, 0);
            prop.Comment = comment;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetLocalVariableValueAsync(string filePath, string sequenceName,
        string variableName, string value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);

            // Auto-detect type and set accordingly
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                SetPropertyValue(seq.Locals, variableName, numVal);
            else if (bool.TryParse(value, out var boolVal))
                SetPropertyValue(seq.Locals, variableName, boolVal);
            else
                SetPropertyValue(seq.Locals, variableName, value);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetStepExpressionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression, string expressionType = "Statement")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            dynamic step = ResolveStepInGroup(seq, sgValue, stepName);

            switch (expressionType.ToLowerInvariant())
            {
                case "pre":
                    step.PreExpression = expression;
                    break;
                case "post":
                    step.PostExpression = expression;
                    break;
                case "status":
                    step.StatusExpression = expression;
                    break;
                default:
                    // Statement steps (and the unspecified-type default): the primary home for
                    // the expression is the Post Expression — it is evaluated after the step's
                    // (empty) action. 'Pre'/'Post'/'Status' remain available as explicit targets.
                    step.PostExpression = expression;
                    break;
            }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetSequenceCallTargetAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string targetSequenceName, string targetSequenceFile = "")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            dynamic step    = ResolveStepInGroup(seq, sgValue, stepName);

            // Use SequenceCallModule properties via dynamic COM dispatch:
            // SequenceCallModule.SequenceName, .UseCurrentFile, .SequenceFilePath
            dynamic seqCallModule = step.Module;
            seqCallModule.SequenceName   = targetSequenceName;
            seqCallModule.UseCurrentFile = string.IsNullOrEmpty(targetSequenceFile);
            if (!string.IsNullOrEmpty(targetSequenceFile))
            {
                // Always store the SequenceCall target as a *relative* path
                // (relative to the source sequence file's directory). Users
                // explicitly do not want absolute paths persisted in the
                // sequence file.
                string relTarget = MakeRelativePath(
                    Path.GetDirectoryName(filePath) ?? "",
                    targetSequenceFile);

                seqCallModule.SequenceFilePath = relTarget;

                // Defensive: clear the "use absolute path" flag if the COM
                // object exposes it. Older / different engine builds expose
                // it under varying names — try the common ones and ignore
                // the rest.
                foreach (var propName in new[] {
                    "UseAbsolutePath", "AbsolutePath", "IsAbsolutePath" })
                {
                    try { ((object)seqCallModule).GetType().InvokeMember(
                        propName,
                        System.Reflection.BindingFlags.SetProperty,
                        null, seqCallModule, new object[] { false });
                    }
                    catch { /* property not present on this build */ }
                }
            }

            // Materialise the callee prototype into ActualArgs/Prototype (editor "Load
            // Prototype") so every parameter becomes a correctly-typed SequenceArgument.
            TryLoadModulePrototype(seqCallModule, stepName);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task SetStepModulePathAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string modulePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            dynamic step = ResolveStepInGroup(seq, sgValue, stepName);

            // Access Module via dynamic COM dispatch so VIPath persists.
            dynamic lvModule = step.Module;
            lvModule.VIPath = modulePath;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task DeleteStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, bool save = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            // DeleteSubProperty takes a dotted lookup path, so both a top-level step subproperty and a
            // nested one (e.g. "TS.SData.ActualArgs.vis") are removed with the same call. This is the
            // counterpart create_step_property was missing: a rebuild that regenerates a caller's
            // argument list via LoadPrototype can end up with an argument the ORIGINAL does not have
            // (a renamed callee parameter leaves the old name behind in real files), and there was no
            // way to remove it.
            stepPo.DeleteSubProperty(propertyPath, 0);

            if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task<StepPropertyValue> SetStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string value, string? valueType,
        bool save = true, bool unescape = false)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            // Resolve the step's PropertyObject via the typed (vtable) call — the parameterless
            // dynamic AsPropertyObject() is the DLR call most prone to TargetParameterCountException
            // under load. This is the ThisContext-less design-time twin of set_runtime_variable:
            // SetVal* takes a dotted lookup path relative to the step, reaching nested props like
            // VIModule.ViCall.VIPath that no other writer can address.
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            // MCP string parameters cannot carry bare control characters (a client cannot type a
            // lone CR); unescape=true turns \r \n \t \\ \uXXXX sequences into their characters so
            // values like VI descriptions with embedded CRs are reproducible byte-exact.
            if (unescape) value = UnescapeValue(value);

            // Convenience resolver for two step attributes not addressable by their bare name:
            //  • 'Icon'  → the step's icon file, which lives at 'TS.Icon' (e.g. 'ni_blank.ico').
            //  • 'Flags' → the step's flag BITFIELD, set via SetFlags on the step root (NOT a named
            //    property; a bare 'Flags' path otherwise errors "Unknown variable or property name").
            //    Accepts a decimal or 0x-hex value (e.g. '0x4000000' to blank a nameless Label's icon).
            if (string.Equals(propertyPath, "Icon", StringComparison.OrdinalIgnoreCase))
            {
                propertyPath = "TS.Icon";
            }
            else if (string.Equals(propertyPath, "Flags", StringComparison.OrdinalIgnoreCase))
            {
                string fv = value.Trim();
                long raw = fv.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt64(fv.Substring(2), 16)
                    : long.Parse(fv, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture);
                stepPo.SetFlags("", 0, unchecked((int)raw));
                if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
                var fi = new StepPropertyValue { StepName = stepName, PropertyPath = "Flags", ValueType = "Flags" };
                try { fi.Value = stepPo.GetFlags("", 0); } catch (Exception ex) { _logger.LogDebug(ex, "GetFlags read-back failed."); }
                return fi;
            }

            // Explicit value_type wins; otherwise auto-detect. We FIRST honour the TARGET
            // property's existing kind so a literal is never coerced to the wrong type — e.g.
            // writing "False"/"True" into a String expression property (TS.SData.ActualArgs.<arg>.Expr,
            // any *.Expr / *Expression slot) must stay a String, not become a Boolean (which throws
            // "Expected Boolean, found String"). Only when the target's type can't be read do we fall
            // back to the literal heuristic (number / true|false / string).
            string kind    = (valueType ?? "").Trim().ToLowerInvariant();
            bool asNumber  = kind is "number" or "double" or "float" or "int" or "integer";
            bool asBoolean = kind is "boolean" or "bool";
            bool asString  = kind is "string";
            if (!asNumber && !asBoolean && !asString)
            {
                string existingKind = "";
                try
                {
                    NiPropertyObject existing =
                        (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
                    existingKind = InferValueKind(existing, out _, out _);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Auto-detect: target property '{Path}' not readable yet.", propertyPath); }

                if (existingKind == "String" || existingKind == "Enum") asString = true;
                else if (existingKind == "Boolean") asBoolean = true;
                else if (existingKind == "Number") asNumber = true;
                else if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) asNumber = true;
                else if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                      || value.Equals("false", StringComparison.OrdinalIgnoreCase)) asBoolean = true;
                else asString = true;
            }

            int toEnum = (int)NiPropOptions.PropOption_CoerceToEnum;
            if (asNumber)
            {
                var numVal = double.Parse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture);
                // A numeric ordinal targeting an enum-typed prop needs the coerce (preserves the enum
                // type). The coerced path then gets promoted to an explicit by-name write, otherwise
                // TestStand keeps the value type-default-flagged and the FileDiffer shows "{val}".
                try { stepPo.SetValNumber(propertyPath, 0, numVal); }
                catch
                {
                    stepPo.SetValNumber(propertyPath, toEnum, numVal);
                    PromoteEnumLeafToExplicit(stepPo, propertyPath);
                }
            }
            else if (asBoolean)
                stepPo.SetValBoolean(propertyPath, 0,
                    value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
            else
            {
                // Plain string set; retry coercing so an enum-by-label ("MyEnum.ValueName" or a bare
                // enumerator) can be written without retyping the target away from its enum type.
                try { stepPo.SetValString(propertyPath, 0, value); }
                catch { stepPo.SetValString(propertyPath, toEnum, value); }
            }

            if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            // Read the value back so the caller sees the applied result + resolved type.
            NiPropertyObject prop = (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
            var info = new StepPropertyValue { StepName = stepName, PropertyPath = propertyPath };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElem);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElem;
            if (info.ValueType is "Number" or "Boolean" or "String")
                info.Value = TryGetValue(prop);
            return info;
        });
    }

    /// <summary>
    /// Decodes the escape sequences \r \n \t \" \\ and \uXXXX in a tool-supplied value.
    /// Only used when the caller opts in (unescape=true) — a literal backslash the caller
    /// wants preserved must then be doubled.
    /// </summary>
    internal static string UnescapeValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0) return value;
        var sb = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || i + 1 >= value.Length) { sb.Append(c); continue; }
            char n = value[++i];
            switch (n)
            {
                case 'r':  sb.Append('\r'); break;
                case 'n':  sb.Append('\n'); break;
                case 't':  sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '"':  sb.Append('"');  break;
                case 'u' when i + 4 < value.Length &&
                              int.TryParse(value.Substring(i + 1, 4),
                                  System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture, out int cp):
                    sb.Append((char)cp); i += 4; break;
                default:   sb.Append('\\').Append(n); break; // unknown escape → keep verbatim
            }
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public async Task<StepPropertyValue> CreateStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string valueType,
        string? typeName = null, int? numElements = null, string? value = null,
        bool unescape = false, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            string vt = (valueType ?? "").Trim().ToLowerInvariant();

            if (vt is "array_elements" or "arrayelements")
            {
                // Resize an array property (e.g. TS.SData.ViCall.Parms,
                // TS.AdditionalResultsHints). New elements are instantiated with the array's
                // ELEMENT TYPE — the only way to author typed entries like VIParameter.
                // A MISSING array is created first; type_name selects its element type
                // ('' / 'container' → containers, a builtin scalar name, or a named type
                // like 'VIParameter' — e.g. the per-parameter ArrayClusterEls cluster array).
                if (numElements is null or < 0)
                    throw new ArgumentException(
                        "value_type='array_elements' requires num_elements >= 0.");
                NiPropertyObject arr;
                try
                {
                    arr = (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
                }
                catch
                {
                    int    last       = propertyPath.LastIndexOf('.');
                    string parentPath = last >= 0 ? propertyPath[..last] : "";
                    string leaf       = last >= 0 ? propertyPath[(last + 1)..] : propertyPath;
                    NiPropertyObject parent = string.IsNullOrEmpty(parentPath)
                        ? stepPo
                        : (NiPropertyObject)(object)stepPo.GetPropertyObject(parentPath, 0);
                    (int elemPvt, string elemTn) = (typeName ?? "").Trim().ToLowerInvariant() switch
                    {
                        "" or "container"
                            => ((int)NiPropValueTypes.PropValType_Container, ""),
                        "number" or "double" or "float" or "int" or "integer"
                            => ((int)NiPropValueTypes.PropValType_Number, ""),
                        "boolean" or "bool"
                            => ((int)NiPropValueTypes.PropValType_Boolean, ""),
                        "string" or "expression"
                            => ((int)NiPropValueTypes.PropValType_String, ""),
                        "reference" or "object reference" or "objectreference" or "objref"
                            => ((int)NiPropValueTypes.PropValType_Reference, ""),
                        _   => ((int)NiPropValueTypes.PropValType_NamedType, typeName!.Trim()),
                    };
                    try
                    {
                        parent.NewSubProperty(leaf, (NiPropValueTypes)elemPvt, true, elemTn, 0);
                    }
                    catch when (elemPvt == (int)NiPropValueTypes.PropValType_NamedType)
                    {
                        // Same engine-level type search fallback as the scalar named-type path.
                        NiPropertyObject typedArr = (NiPropertyObject)(object)
                            _engine!.NewPropertyObject((NiPropValueTypes)elemPvt, true, elemTn, 0);
                        parent.SetPropertyObject(leaf, 0x1 /* PropOption_InsertIfMissing */, typedArr);
                    }
                    arr = (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
                }
                arr.SetNumElements(numElements.Value, 0);
            }
            else
            {
                // Create the subproperty when missing (idempotent when it already exists —
                // the optional value below is applied either way). A named_type request on an
                // EXISTING node of a DIFFERENT type replaces it with a fresh typed instance
                // (needed to retype e.g. a plain-container array element to VIParameterElement).
                bool exists = true;
                string existingTypeDisp = "";
                try
                {
                    NiPropertyObject existing =
                        (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
                    try { existingTypeDisp = existing.GetTypeDisplayString("", 0); } catch { }
                }
                catch { exists = false; }

                bool retype = exists && vt is "namedtype" or "named_type" or "type" or "enum"
                    && !string.IsNullOrWhiteSpace(typeName)
                    && existingTypeDisp != typeName
                    && !existingTypeDisp.StartsWith(typeName + " ", StringComparison.Ordinal);
                if (retype)
                {
                    NiPropertyObject typedNew = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                        NiPropValueTypes.PropValType_NamedType, false, typeName!.Trim(), 0);
                    stepPo.SetPropertyObject(propertyPath, 0, typedNew);
                }
                else if (!exists)
                {
                    int    last       = propertyPath.LastIndexOf('.');
                    string parentPath = last >= 0 ? propertyPath[..last] : "";
                    string leaf       = last >= 0 ? propertyPath[(last + 1)..] : propertyPath;
                    NiPropertyObject parent = string.IsNullOrEmpty(parentPath)
                        ? stepPo
                        : (NiPropertyObject)(object)stepPo.GetPropertyObject(parentPath, 0);

                    (int pvt, string tn) = vt switch
                    {
                        "number" or "double" or "float" or "int" or "integer"
                            => ((int)NiPropValueTypes.PropValType_Number, ""),
                        "boolean" or "bool"
                            => ((int)NiPropValueTypes.PropValType_Boolean, ""),
                        "string" or "expression"
                            => ((int)NiPropValueTypes.PropValType_String, ""),
                        "container"
                            => ((int)NiPropValueTypes.PropValType_Container, ""),
                        "reference" or "object reference" or "objectreference" or "objref"
                            => ((int)NiPropValueTypes.PropValType_Reference, ""),
                        // An enum instance IS a named-type instance whose type is the enum typedef;
                        // create it via the named enum type, then set its ordinal/name below.
                        "namedtype" or "named_type" or "type" or "enum" or ""
                            when !string.IsNullOrWhiteSpace(typeName)
                            => ((int)NiPropValueTypes.PropValType_NamedType, typeName!),
                        "enum" => throw new ArgumentException(
                            "value_type='enum' requires type_name (the enum data type, e.g. 'Color')."),
                        _ => throw new ArgumentException(
                            $"Unsupported value_type '{valueType}'. Use number/boolean/string/" +
                            "container/reference, 'named_type' or 'enum' with type_name, or 'array_elements'.")
                    };
                    try
                    {
                        parent.NewSubProperty(leaf, (NiPropValueTypes)pvt, false, tn, 0);
                    }
                    catch when (pvt == (int)NiPropValueTypes.PropValType_NamedType)
                    {
                        // NewSubProperty resolves named types only against the FILE's type
                        // usage list. Step-type-owned types (e.g. 'ErrorDialogOptions') need
                        // the ENGINE-level search — same as the canonical edit-time expression
                        // Engine.NewPropertyObject(PropValType_NamedType,...) + SetPropertyObject
                        // with PropOption_InsertIfMissing (0x1).
                        NiPropertyObject typed = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                            (NiPropValueTypes)pvt, false, tn, 0);
                        parent.SetPropertyObject(leaf, 0x1 /* PropOption_InsertIfMissing */, typed);
                    }
                }

                if (value != null)
                {
                    string v = unescape ? UnescapeValue(value) : value;
                    switch (vt)
                    {
                        case "number" or "double" or "float" or "int" or "integer":
                            stepPo.SetValNumber(propertyPath, 0, double.Parse(v,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture));
                            break;
                        case "boolean" or "bool":
                            stepPo.SetValBoolean(propertyPath, 0,
                                v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
                            break;
                        case "string" or "expression":
                            stepPo.SetValString(propertyPath, 0, v);
                            break;
                        case "enum":
                            // value may be the numeric ordinal OR the symbolic enumerator name. Route
                            // through WriteEnumLeafExplicit so an ordinal is resolved to its
                            // enumerator NAME first — only the by-name write stores the value as
                            // explicitly-set ("[val]") instead of type-default-flagged ("{val}").
                            WriteEnumLeafExplicit(stepPo, propertyPath, null, v, typeName, sf, filePath);
                            break;
                        // container/reference/named types have no scalar value to assign here.
                    }
                }
            }

            if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            NiPropertyObject prop = (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
            var info = new StepPropertyValue { StepName = stepName, PropertyPath = propertyPath };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElem);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElem;
            if (info.ValueType is "Number" or "Boolean" or "String" or "Enum")
                info.Value = TryGetValue(prop);
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task<StepPropertyValue> SetStepPropertyFlagsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, int flags, bool save = true,
        bool exact = false)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            // SetFlags only ever adds bits; 'exact' assigns the whole bitfield so a bit can be turned
            // OFF (see SetExactFlags) — the case a rebuild needs when the original has 0x0 where a
            // prototype load left 0x4 (PassByReference) on a caller's argument.
            if (exact) SetExactFlags(stepPo, propertyPath, flags);
            else       stepPo.SetFlags(propertyPath, 0, flags);

            if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            var info = new StepPropertyValue { StepName = stepName, PropertyPath = propertyPath };
            try { info.Value = stepPo.GetFlags(propertyPath, 0); info.ValueType = "Flags"; }
            catch (Exception ex) { _logger.LogDebug(ex, "GetFlags read-back failed for '{Path}'.", propertyPath); }
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task<StepPropertyValue> RenameStepPropertyAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string propertyPath, string newName, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            NiPropertyObject prop =
                (NiPropertyObject)(object)stepPo.GetPropertyObject(propertyPath, 0);
            prop.Name = newName;

            if (save) SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            var info = new StepPropertyValue { StepName = stepName, PropertyPath = propertyPath };
            try { info.Value = prop.Name; info.ValueType = "Name"; }
            catch (Exception ex) { _logger.LogDebug(ex, "Name read-back failed for '{Path}'.", propertyPath); }
            return info;
        });
    }

    /// <summary>
    /// Default bound for the AnalyzerApp.exe child. Deliberately generous: the analyzer's
    /// "module is loadable" rule LOADS every step's code module, so a cold analysis of a file with
    /// LabVIEW <c>.lvlibp</c> or Python steps takes minutes — measured ~511 s on a 30-sequence file
    /// once Python and LabVIEW were actually installed, versus seconds when neither could be loaded.
    /// A tight bound would kill legitimate work; the point of the bound is that a genuinely stuck
    /// child fails loudly instead of hanging forever.
    /// </summary>
    public const int DefaultAnalyzerTimeoutSeconds = 900;

    /// <inheritdoc/>
    public async Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath,
        int timeoutSeconds = DefaultAnalyzerTimeoutSeconds)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var diag = new System.Text.StringBuilder();
            string diagPath = Path.Combine(Path.GetTempPath(), "ts_analyzer_diag.txt");
            void Log(string msg) { diag.AppendLine(msg); }
            void Flush() { try { System.IO.File.WriteAllText(diagPath, diag.ToString()); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to write analyzer diagnostics to temp file."); } }

            // Ensure the file is saved to disk before analysis
            if (_loadedSequenceFiles.TryGetValue(filePath, out var cachedSf))
            {
                try { SaveSequenceFileWithRetry(cachedSf, filePath); Log("File saved to disk OK"); }
                catch (Exception ex) { Log($"File save warning: {ex.Message}"); }
            }

            // Resolve Bin/Public dirs + version for the *connected* engine — no hard-coded release.
            var (binDir, publicDir, productVersion, probed) = ResolveAnalyzerLocations();
            return RunAnalysisViaApp(filePath, binDir, publicDir, productVersion, probed, Log, Flush,
                timeoutSeconds);
        });
    }

    private static List<AnalyzerMessage> RunAnalysisViaApp(
        string filePath,
        string binDir,
        string publicDir,
        string productVersion,
        string probed,
        Action<string> Log,
        Action Flush,
        int timeoutSeconds)
    {
        // AnalyzerApp.exe ships in the connected engine's Bin directory — never hard-code a release.
        string analyzerExe = !string.IsNullOrEmpty(binDir)
            ? Path.Combine(binDir, "AnalyzerApp.exe")
            : "AnalyzerApp.exe";
        // The user's saved analyzer project (with its configured rules) lives in the TestStand
        // Public directory of the running version — empty when that directory is unknown.
        string savedProject = !string.IsNullOrEmpty(publicDir)
            ? Path.Combine(publicDir, "MyAnalyzerProject.tsaproj")
            : "";
        Log($"Resolved AnalyzerApp.exe: {analyzerExe}");
        Log($"Resolved saved project:   {(string.IsNullOrEmpty(savedProject) ? "(public dir unknown)" : savedProject)}");
        string tempProject = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ts_mcp_analysis_" + System.IO.Path.GetFileNameWithoutExtension(filePath) + ".tsaproj");
        // The saved project's per-message records carry NO severity — only a RuleId. Effective
        // severity (what the editor's Analysis-Results pane shows) is per-RULE and only materialises
        // in the analyzer REPORT, so we also ask AnalyzerApp to emit an XML report and read the
        // resolved RuleId→Severity map from its rule catalog. (See ParseRuleSeverities.)
        string reportPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ts_mcp_report_" + System.IO.Path.GetFileNameWithoutExtension(filePath) + ".xml");
        try { if (System.IO.File.Exists(reportPath)) System.IO.File.Delete(reportPath); }
        catch (Exception) { /* best-effort: clear stale report before the run */ }

        // ── 1. Build temp project XML ─────────────────────────────────────────
        // Start from the user's saved project (rules configured) or a minimal template.
        string projectXml;
        if (System.IO.File.Exists(savedProject))
        {
            projectXml = System.IO.File.ReadAllText(savedProject, System.Text.Encoding.UTF8);
            Log($"Base project loaded: {savedProject}");
        }
        else
        {
            Log("Saved project not found — using minimal template");
            // productversion is cosmetic file metadata; compatibleversion + the namespace govern
            // parsing. Stamp the connected engine's version when known, else the compatible baseline.
            string headerVersion = string.IsNullOrEmpty(productVersion) ? "23.0.0.0" : productVersion;
            projectXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<teststandfileheader type='SequenceAnalyzerProjectFile' fileversion='1022' productname='TestStand' productversion='{headerVersion}' compatibleversion='23.0.0.0' xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://www.ni.com/TestStand/23.0.0/PropertyObjectFile"">
 <typelist/>
 <Data classname='Obj'><subprops><data classname='Obj'><subprops>
  <Files classname='Strs'><value lbound='[0]' ubound='[]'/></Files>
  <Messages classname='Objs'><value lbound='[0]' ubound='[]'/></Messages>
 </subprops></data></subprops></Data>
</teststandfileheader>";
        }

        // ── 2. Inject the target file into <Files> ────────────────────────────
        // The format is:  <Files classname='Strs'><value .../></Files>
        // Replace the inner <value> element entirely with one containing our file.
        string escapedPath = filePath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        // TestStand XML requires arrayindex='[N]' on each element inside a sized array
        string newFilesBlock =
            $"<Files classname='Strs'><value lbound='[0]' ubound='[0]'><value arrayindex='[0]'>{escapedPath}</value></value></Files>";

        // Use a simple regex to replace the Files element
        projectXml = _filesBlockRx.Replace(projectXml, newFilesBlock);
        Log($"Injected file into project XML: {filePath}");

        // Clear old messages so only the new run's results remain
        string clearMessages = "<Messages classname='Objs'><value lbound='[0]' ubound='[]'/></Messages>";
        projectXml = _messagesBlockRx.Replace(projectXml, clearMessages);

        // Update PathAtLastWrite to match our temp file so AnalyzerApp /save works
        string escapedTempProject = tempProject.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        projectXml = _pathAtLastWriteRx.Replace(projectXml,
            $"<PathAtLastWrite classname='Str'><value>{escapedTempProject}</value></PathAtLastWrite>");

        System.IO.File.WriteAllText(tempProject, projectXml, System.Text.Encoding.UTF8);
        Log($"Temp project written: {tempProject}");
        Flush();

        // ── 3. Run AnalyzerApp.exe ────────────────────────────────────────────
        if (!System.IO.File.Exists(analyzerExe))
            throw new InvalidOperationException(
                $"AnalyzerApp.exe not found at: {analyzerExe}." +
                (string.IsNullOrEmpty(probed) ? "" : $" Probed: {probed}"));

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = analyzerExe,
            Arguments              = $"\"{tempProject}\" /analyze /report \"{reportPath}\" /save /quit",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        // ── Normalize the child environment ───────────────────────────────────
        // AnalyzerApp.exe is 32-bit and, when it analyzes LabVIEW code modules, loads the 32-bit
        // LabVIEW Run-Time Engine (lvrt.dll). lvrt builds paths from %ProgramFiles(x86)% and crashes
        // hard with 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN → exit code -1073740791, empty stdout/
        // stderr) when that variable is absent. The MCP host can inherit a heavily reduced environment
        // (observed: ~15 vars, no ProgramFiles(x86)) when its launcher does not pass the full user
        // environment, so we must guarantee the child has the system variables the analyzer + lvrt
        // depend on — independent of how TestStandMCP.exe itself was started.
        //
        // psi.Environment is pre-seeded with this process's environment. ApplyTestStandToolChildEnv
        // supplies every variable that is MISSING from it, derived from the OS (NOT from
        // GetEnvironmentVariable, which returns null when a variable is absent in *this* process).
        // ProgramFiles(x86) is mandatory; the rest harden common lvrt/Windows lookups.
        // (UseShellExecute=false above is required for psi.Environment to apply.)
        ApplyTestStandToolChildEnv(psi);

        Log($"Child env normalized — ProgramFiles(x86)=" +
            (psi.Environment.TryGetValue("ProgramFiles(x86)", out var pf86) && !string.IsNullOrEmpty(pf86)
                ? pf86 : "(MISSING!)"));

        Log($"Launching: {analyzerExe} {psi.Arguments}");
        Flush();

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start AnalyzerApp.exe process.");

        // The output must be drained ASYNCHRONOUSLY and the wait must be the thing that enforces the
        // timeout. The previous code did `ReadToEnd(); ReadToEnd(); WaitForExit(120_000)` — but
        // ReadToEnd blocks until the child closes the stream, i.e. until it EXITS, so by the time
        // WaitForExit ran the process was always gone and the "2 minute timeout" could never fire.
        // A genuinely hung AnalyzerApp therefore hung the call forever. Reading the two pipes
        // serially could also deadlock: a child that fills the stderr buffer while we are still
        // draining stdout blocks, and we never get to stderr.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        int timeoutMs  = Math.Max(1, timeoutSeconds) * 1000;

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch (Exception) { /* best-effort */ }
            Log($"AnalyzerApp.exe timed out after {timeoutSeconds}s — killed.");
            Flush();
            throw new InvalidOperationException(
                $"AnalyzerApp.exe timed out after {timeoutSeconds} seconds. A cold analysis that " +
                "loads LabVIEW .lvlibp or Python code modules can legitimately take many minutes " +
                "(measured: ~8.5 min on a 30-sequence file) — raise timeout_seconds, or run the " +
                "analysis with async=true and poll get_analysis_status.");
        }

        // The child has exited, so both reads are complete or about to be; bounded so a stuck pipe
        // cannot hang us after a successful exit.
        string stdout = "", stderr = "";
        try
        {
            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 15_000);
            if (stdoutTask.IsCompletedSuccessfully) stdout = stdoutTask.Result;
            if (stderrTask.IsCompletedSuccessfully) stderr = stderrTask.Result;
        }
        catch (Exception ex) { Log($"Draining AnalyzerApp output failed: {ex.Message}"); }

        int exitCode = proc.ExitCode;
        Log($"AnalyzerApp exit code: {exitCode}");
        if (!string.IsNullOrWhiteSpace(stdout)) Log($"stdout: {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr)) Log($"stderr: {stderr.Trim()}");
        // exit 0 = clean, 1 = errors, 2 = warnings, <0 = bad args/paths
        if (exitCode < 0)
        {
            // A negative exit (notably -1073740791 / 0xC0000409 from lvrt.dll) means the child
            // crashed before producing output. Dump the critical child env vars so a regression —
            // e.g. ProgramFiles(x86) going missing again — is immediately visible in the diag file.
            Log("AnalyzerApp.exe exited with a negative code — dumping critical child env vars:");
            foreach (var key in new[]
            {
                "ProgramFiles(x86)", "ProgramFiles", "ProgramData", "ALLUSERSPROFILE",
                "CommonProgramFiles", "CommonProgramFiles(x86)", "ComSpec",
                "TMP", "TEMP", "NUMBER_OF_PROCESSORS", "SystemRoot", "PATH",
            })
            {
                Log($"  {key} = {(psi.Environment.TryGetValue(key, out var v) ? v : "(absent)")}");
            }
            Flush();
            throw new InvalidOperationException(
                $"AnalyzerApp.exe returned error code {exitCode}. stdout: {stdout.Trim()} stderr: {stderr.Trim()}");
        }
        Flush();

        // ── 4. Parse the saved project XML for messages ───────────────────────
        if (!System.IO.File.Exists(tempProject))
            throw new InvalidOperationException("AnalyzerApp.exe did not save the project file.");

        string savedXml = System.IO.File.ReadAllText(tempProject, System.Text.Encoding.UTF8);

        // Build the RuleId→Severity map from the report's rule catalog (resolves Default→effective
        // severity). Best-effort: if the report is missing/unparseable, ParseAnalyzerMessages falls
        // back to the legacy per-message Severity value.
        var ruleSeverity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (System.IO.File.Exists(reportPath))
            {
                string reportXml = System.IO.File.ReadAllText(reportPath, System.Text.Encoding.UTF8);
                ruleSeverity = ParseRuleSeverities(reportXml, Log);
            }
            else
            {
                Log($"Analyzer report not found at {reportPath} — severities fall back to per-message parse.");
            }
        }
        catch (Exception ex) { Log($"Failed to read analyzer report: {ex.Message}"); }

        var result = ParseAnalyzerMessages(savedXml, Log, ruleSeverity);

        // Clean up temp files
        try { System.IO.File.Delete(tempProject); } catch (Exception) { /* best-effort: delete temp analyzer project file — intentionally ignored */ }
        try { System.IO.File.Delete(reportPath); } catch (Exception) { /* best-effort: delete temp analyzer report file — intentionally ignored */ }

        Log($"Total messages collected: {result.Count}");
        Flush();

        int SevOrder(string s) => s switch { "Error" => 0, "Warning" => 1, "Information" => 2, _ => 3 };
        result.Sort((a, b) => SevOrder(a.Severity).CompareTo(SevOrder(b.Severity)));
        return result;
    }

    /// <summary>
    /// Resolves the TestStand <c>Public</c> directory and the product-version string for the
    /// *currently connected* engine, plus the <c>Bin</c> directory holding AnalyzerApp.exe, so the
    /// Sequence Analyzer always runs the build matching the running TestStand — never a hard-coded
    /// release. <c>Probed</c> lists the candidates that were tried when the Bin lookup failed.
    /// </summary>
    private (string BinDir, string PublicDir, string ProductVersion, string Probed) ResolveAnalyzerLocations()
    {
        string publicDir = "";
        string productVersion = "";

        // Ask the connected engine — this is the exact running version.
        if (_engine != null)
        {
            productVersion = GetEngineProperty<string>("VersionString") ?? "";
            try { publicDir = (string)((dynamic)_engine!).GetTestStandPath((object)4); } // 4 = TestStandPublic
            catch (Exception ex) { _logger.LogDebug(ex, "Engine GetTestStandPath(TestStandPublic) failed."); }
        }

        // Environment variable exported by the TestStand installer.
        if (string.IsNullOrEmpty(publicDir))
            publicDir = Environment.GetEnvironmentVariable("TESTSTANDPUBLIC") ?? "";

        var (binDir, probed) = ResolveTestStandBin("AnalyzerApp.exe");
        return (binDir, publicDir, productVersion, probed);
    }

    /// <summary>
    /// Resolves the TestStand <c>Bin</c> directory that actually CONTAINS <paramref name="requiredExe"/>,
    /// preferring the connected engine's own Bin. See <see cref="TestStandInstallLocator"/> for the
    /// full candidate order and the WOW64/registry-view traps it works around.
    /// </summary>
    private (string BinDir, string Probed) ResolveTestStandBin(string requiredExe)
    {
        string? engineBin = _engine != null ? GetEngineProperty<string>("BinDirectory") : null;
        return TestStandInstallLocator.Resolve(requiredExe, engineBin, _explicitBinDir);
    }

    internal static List<AnalyzerMessage> ParseAnalyzerMessages(string projectXml, Action<string> Log,
        IReadOnlyDictionary<string, int>? ruleSeverity = null)
    {
        var result = new List<AnalyzerMessage>();

        // Extract the <Messages classname='Objs'>...</Messages> block
        var msgBlockMatch = _messagesCaptureRx.Match(projectXml);

        if (!msgBlockMatch.Success)
        {
            Log("Messages block not found in saved XML");
            return result;
        }

        string msgBlock = msgBlockMatch.Value;
        Log($"Messages block length: {msgBlock.Length} chars");

        // Quick check: if the direct array child has ubound='[]' (self-closing), it's empty.
        // Match only the FIRST <value ...> tag directly inside <Messages> — not nested ones.
        var firstValueMatch = _firstValueRx.Match(msgBlock);
        if (firstValueMatch.Success)
        {
            string attrs = firstValueMatch.Groups[1].Value;
            bool isSelfClosing = attrs.EndsWith("/") || _selfClosingValueRx.IsMatch(msgBlock);
            bool emptyBound = attrs.Contains("ubound='[]'");
            if (isSelfClosing && emptyBound)
            {
                Log("Messages array is empty (ubound='[]' self-closing) — no messages");
                return result;
            }
            Log($"Messages array attrs: {attrs.Trim()}");
        }

        // Parse using XmlDocument so we handle single-quoted attributes correctly
        var doc = new System.Xml.XmlDocument();
        try { doc.LoadXml(msgBlock); }
        catch (Exception ex)
        {
            Log($"XML parse error on Messages block: {ex.Message}");
            return result;
        }

        // Each message is an <Obj> element inside the <value lbound...> container
        var objNodes = doc.SelectNodes("//*[local-name()='Obj']");
        if (objNodes == null || objNodes.Count == 0)
        {
            Log("No <Obj> nodes found in Messages block");
            return result;
        }

        Log($"Found {objNodes.Count} <Obj> nodes in Messages block");

        foreach (System.Xml.XmlNode obj in objNodes)
        {
            string? GetSubProp(string name)
            {
                var node = obj.SelectSingleNode($"subprops/{name}/value");
                return node?.InnerText?.Trim();
            }

            string sevStr  = GetSubProp("Severity") ?? "";
            string ruleId  = GetSubProp("RuleId")   ?? "";
            string text    = GetSubProp("Text")      ?? "";

            if (string.IsNullOrEmpty(ruleId) && string.IsNullOrEmpty(text))
                continue; // skip empty/sentinel objects

            // The finding's location is NOT a flat property — it is the first element of a
            // Locations[] array of Objs, each exposing PropertyPath (step-ID form),
            // PropertyPathWithNames (friendly step name, when the location is a step) and FilePath.
            // Prefer the friendly path; derive the sequence + step names from its bracketed tokens.
            string loc = "", seqName = "", stepName = "";
            var locSub = obj.SelectSingleNode("subprops/Locations/value/value/Obj/subprops");
            if (locSub != null)
            {
                string pathNamed = locSub.SelectSingleNode("PropertyPathWithNames/value")?.InnerText?.Trim() ?? "";
                string pathRaw   = locSub.SelectSingleNode("PropertyPath/value")?.InnerText?.Trim() ?? "";
                loc = pathNamed.Length > 0 ? pathNamed : pathRaw;

                var seqMatch  = _analyzerSeqNameRx.Match(loc);
                if (seqMatch.Success)  seqName  = seqMatch.Groups[1].Value;
                var stepMatch = _analyzerStepNameRx.Match(loc);
                if (stepMatch.Success) stepName = stepMatch.Groups[1].Value;
            }

            // Effective severity comes from the report's per-rule catalog (ruleSeverity). The saved
            // project messages carry no Severity, so fall back to the (legacy) per-message value only
            // when the rule is absent from the map — e.g. the report could not be generated.
            int sevInt;
            if (!string.IsNullOrEmpty(ruleId) && ruleSeverity != null
                && ruleSeverity.TryGetValue(ruleId, out int ruleSev))
                sevInt = ruleSev;
            else
                int.TryParse(sevStr, out sevInt);

            string sevLabel = sevInt switch
            {
                0 => "Error",
                1 => "Warning",
                2 => "Information",
                _ => "Information"   // 3 = Default/Disabled (rules at 3 do not produce messages)
            };

            result.Add(new AnalyzerMessage
            {
                Severity     = sevLabel,
                RuleId       = ruleId,
                Text         = text,
                Location     = loc,
                SequenceName = seqName,
                StepName     = stepName
            });
        }

        return result;
    }

    /// <summary>
    /// Builds a RuleId → severity map (0=Error, 1=Warning, 2=Information, 3=Default/Disabled) from
    /// the analyzer REPORT XML. The saved project's messages carry no severity — only a RuleId — so
    /// the effective severity (what the editor's Analysis-Results pane shows) is taken from the
    /// report's rule catalog, where each rule <c>&lt;Obj&gt;</c> exposes its resolved <c>Id</c> +
    /// <c>Severity</c>. Result-message Objs (keyed <c>RuleId</c>, not <c>Id</c>) are ignored here.
    /// The report carries the default TestStand namespace, so all matching is by local-name.
    /// </summary>
    internal static Dictionary<string, int> ParseRuleSeverities(string reportXml, Action<string> Log)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(reportXml)) return map;

        var doc = new System.Xml.XmlDocument();
        try { doc.LoadXml(reportXml); }
        catch (Exception ex) { Log($"Report XML parse error: {ex.Message}"); return map; }

        var objNodes = doc.SelectNodes("//*[local-name()='Obj']");
        if (objNodes == null) return map;

        static string? LeafChildVal(System.Xml.XmlNode subprops, string name)
        {
            foreach (System.Xml.XmlNode c in subprops.ChildNodes)
            {
                if (!string.Equals(c.LocalName, name, StringComparison.Ordinal)) continue;
                foreach (System.Xml.XmlNode v in c.ChildNodes)
                    if (string.Equals(v.LocalName, "value", StringComparison.Ordinal))
                        return v.InnerText?.Trim();
            }
            return null;
        }

        foreach (System.Xml.XmlNode obj in objNodes)
        {
            System.Xml.XmlNode? sp = null;
            foreach (System.Xml.XmlNode c in obj.ChildNodes)
                if (string.Equals(c.LocalName, "subprops", StringComparison.Ordinal)) { sp = c; break; }
            if (sp == null) continue;

            // Rule-catalog entries expose Id + Severity; everything else (messages, options) is skipped.
            string? id  = LeafChildVal(sp, "Id");
            string? sev = LeafChildVal(sp, "Severity");
            if (!string.IsNullOrEmpty(id) && int.TryParse(sev, out int si))
                map[id!] = si;
        }

        Log($"Rule-severity map built from report: {map.Count} rules");
        return map;
    }

    // ── Win32 message pump (used by the dedicated engine thread to drive executions) ──
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMsg msg, IntPtr hWnd,
        uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMsg msg);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMsg msg);
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr hwnd; public uint message;
        public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }
    private static void PumpMessages()
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static string TryGetRuleId(dynamic m)
    {
        // AnalysisMessage.RuleId is the correct property per the interop XML docs
        try { return (string)m.RuleId;  } catch (Exception) { /* best-effort: probe RuleId property on analysis message — intentionally ignored */ }
        try { return (string)m.RuleID;  } catch (Exception) { /* best-effort: probe RuleID property on analysis message — intentionally ignored */ }
        try { return (string)m.Rule.Id; } catch (Exception) { /* best-effort: probe Rule.Id property on analysis message — intentionally ignored */ }
        return "";
    }

    // ── Workspace ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<WorkspaceInfo> OpenWorkspaceAsync(string workspacePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                _engine!.GetType().InvokeMember("OpenWorkspace",
                    _comFlags, null, _engine, new object[] { workspacePath, 0 });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenWorkspace via reflection failed, trying dynamic");
                try { ((dynamic)_engine!).OpenWorkspace(workspacePath, 0); }
                catch (Exception ex2)
                {
                    _logger.LogWarning(ex2, "OpenWorkspace dynamic also failed");
                }
            }
            return BuildWorkspaceInfo();
        });
    }

    /// <inheritdoc/>
    public async Task<WorkspaceInfo> GetWorkspaceAsync()
    {
        EnsureConnected();
        return await Task.Run(() => BuildWorkspaceInfo());
    }

    private WorkspaceInfo BuildWorkspaceInfo()
    {
        var info = new WorkspaceInfo();
        try
        {
            dynamic ws = ((dynamic)_engine!).Workspace;
            try { info.WorkspacePath = (string)ws.Path; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read workspace path."); }
            try
            {
                dynamic files = ws.Files;
                int count = Convert.ToInt32((object)files.Count);
                for (int i = 0; i < count; i++)
                {
                    try { info.SequenceFiles.Add((string)files[(object)i].Path); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence file path from workspace at index {Index}.", i); }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate workspace sequence files."); }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to access workspace from engine."); }
        return info;
    }

    // ── Watch Expressions ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<int> AddWatchExpressionAsync(string expression, string? label = null)
    {
        // WatchExpressions are a Sequence Editor GUI concept; the engine has no such API.
        // We maintain them in memory so they can be listed and removed later.
        return await Task.Run(() =>
        {
            lock (_watchExpressions)
            {
                int idx = _watchExpressions.Count;
                _watchExpressions.Add(new WatchExpressionInfo
                {
                    Index      = idx,
                    Expression = expression,
                    Label      = label ?? expression,
                    Value      = null,
                    Type       = null
                });
                return idx;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<List<WatchExpressionInfo>> GetWatchExpressionsAsync()
    {
        return await Task.Run(() =>
        {
            lock (_watchExpressions)
            {
                // Re-index so Index always matches position in the list
                for (int i = 0; i < _watchExpressions.Count; i++)
                    _watchExpressions[i].Index = i;
                return _watchExpressions.ToList();
            }
        });
    }

    /// <inheritdoc/>
    public async Task RemoveWatchExpressionAsync(int index)
    {
        await Task.Run(() =>
        {
            lock (_watchExpressions)
            {
                if (index < 0 || index >= _watchExpressions.Count)
                    throw new ArgumentOutOfRangeException(nameof(index),
                        $"Watch expression index {index} is out of range (count={_watchExpressions.Count}).");
                _watchExpressions.RemoveAt(index);
            }
        });
    }

    // ── Callbacks ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<CallbackInfo>> GetCallbacksAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var result = new List<CallbackInfo>();
            try
            {
                dynamic callbacks = ((dynamic)sf).Callbacks;
                int count = Convert.ToInt32((object)callbacks.Count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic cb = callbacks[(object)i];
                        result.Add(new CallbackInfo
                        {
                            Name             = TryGetString(cb, "Name"),
                            AssignedSequence = TryGetString(cb, "SequenceName")
                        });
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read callback entry at index {Index}.", i); }
                }
            }
            catch
            {
                // Fallback: iterate sequences with common callback names
                string[] knownCallbacks = {
                    "PreUUT", "PostUUT", "TestReport", "CleanupSequence",
                    "SetupSequence", "MainSequence", "SequentialModel",
                    "ParallelModel", "BatchModel"
                };
                int numSeqs = 0;
                try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get sequence count in callback fallback enumeration."); }
                for (int i = 0; i < numSeqs; i++)
                {
                    try
                    {
                        dynamic seq = sf.GetSequence(i);
                        string name = TryGetString(seq, "Name");
                        if (Array.Exists(knownCallbacks, cbName =>
                            string.Equals(cbName, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(new CallbackInfo
                            {
                                Name             = name,
                                AssignedSequence = name
                            });
                        }
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence entry in callback fallback at index {Index}.", i); }
                }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<string> AddCallbackOverrideAsync(string filePath, string callbackName,
        bool copyDefaultSteps = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            // Same as the editor's "Sequence File Callbacks → Add": creates an override sequence for
            // the named model/engine callback in this file, optionally copying the model's default
            // steps (so e.g. a "Call DoPreUUT" dialog step exists and can be set to Skip).
            NiSequence cb = sf.CreateCallbackOverrideSequence(callbackName, copyDefaultSteps);
            bool inFile = false;
            try { inFile = sf.GetSequenceByName(callbackName) != null; }
            catch (Exception ex) { _logger.LogDebug(ex, "Callback '{Cb}' not yet in file before insert.", callbackName); }
            if (!inFile) sf.InsertSequence(cb);
            SaveSequenceFileWithRetry(sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
            return cb.Name;
        });
    }

    // ── File Properties ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<FilePropertiesInfo> GetFilePropertiesAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            var info = new FilePropertiesInfo { FilePath = filePath };
            // Use PropertyObjectFile typed interface for all file-level metadata
            try
            {
                var pof = (PropertyObjectFile)(object)sf.AsPropertyObjectFile();
                info.Version    = pof.Version;
                info.IsModified = pof.IsModified;
                info.Comment    = string.IsNullOrEmpty(pof.Comment) ? null : pof.Comment;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read file properties via PropertyObjectFile interface."); }
            // NumSequences is on SequenceFile interface directly
            try { info.NumSequences = Convert.ToInt32((object)sf.NumSequences); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read NumSequences from sequence file."); }
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task SetFilePropertiesAsync(string filePath, string? comment = null,
        string? version = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);

            // PropertyObjectFile has Comment and Version as direct typed properties
            var pof = (PropertyObjectFile)(object)sf.AsPropertyObjectFile();
            if (comment != null) pof.Comment = comment;
            if (version != null) pof.Version = version;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Duplicate Sequence ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> DuplicateSequenceAsync(string sourceFilePath,
        string sourceSequenceName, string newSequenceName, string? targetFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var srcSf = (NiSequenceFile)(object)GetOrLoadSeqFile(sourceFilePath);

            string destPath = targetFilePath ?? sourceFilePath;
            var dstSf = string.Equals(destPath, sourceFilePath, StringComparison.OrdinalIgnoreCase)
                ? srcSf
                : (NiSequenceFile)(object)GetOrLoadSeqFile(destPath);

            NiSequence srcSeq = srcSf.GetSequenceByName(sourceSequenceName);

            // Deep-clone the ENTIRE sequence — every step, local, parameter, the sequence Comment
            // and its settings (RecordResults, failure/cleanup options, …). The SequenceFile API
            // has NO CopySequence method in TS2026, so the previous `srcSf.CopySequence(srcSeq)`
            // call ALWAYS threw and was silently swallowed, falling back to an EMPTY NewSequence():
            // the "duplicate" came out as a name-only shell (no steps/locals, comment dropped,
            // RecordResults reset to the default True). The reliable copy primitive is a
            // flag-preserving PropertyObject clone (PropOption_CopyAllFlags = 0x20000000), the same
            // mechanism copy_step_module / copy_file_attributes use. For a cross-file duplicate the
            // referenced data types must already exist in the destination (run copy_typedefs first);
            // the clone carries type references by GUID, which resolve against the destination's
            // TypeUsageList.
            NiSequence newSeq = CloneSequenceDeep(srcSf, srcSeq, sourceSequenceName);

            newSeq.Name = newSequenceName;
            dstSf.InsertSequence(newSeq);

            SaveSequenceFileWithRetry(dstSf, destPath);
            _loadedSequenceFiles[destPath] = dstSf;

            return newSequenceName;
        });
    }

    /// <summary>
    /// Deep-copies a whole sequence into a new, detached <see cref="NiSequence"/> using a
    /// flag-preserving PropertyObject clone. Tries the sequence's own property object first
    /// (Clone("") = copy the object itself); if that is rejected, falls back to cloning the
    /// file-level <c>Data.Seq[idx]</c> array element (which is the same underlying Sequence COM
    /// object). Throws if neither yields a usable Sequence — we never silently return an empty
    /// sequence, so a copy failure surfaces instead of producing a name-only shell.
    /// </summary>
    private NiSequence CloneSequenceDeep(NiSequenceFile srcSf, NiSequence srcSeq, string srcSeqName)
    {
        const int CopyAllFlags = 0x20000000; // PropOption_CopyAllFlags

        // Primary: clone the sequence's own property object.
        try
        {
            NiPropertyObject srcSeqPo = srcSeq.AsPropertyObject();
            var clone = (NiPropertyObject)(object)srcSeqPo.Clone("", CopyAllFlags);
            var asSeq = clone as NiSequence ?? (NiSequence)(object)clone;
            // Sanity: a real clone carries the source's step count.
            if (asSeq != null) return asSeq;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Clone(\"\") of sequence '{Seq}' failed; trying file-array clone.", srcSeqName); }

        // Fallback: locate the sequence's index in the file's Seq array and clone that element.
        try
        {
            NiPropertyObject srcFilePo = srcSf.AsPropertyObject();
            int n = Convert.ToInt32((object)srcSf.NumSequences);
            for (int i = 0; i < n; i++)
            {
                string nm;
                try { nm = (string)srcSf.GetSequence(i).Name; } catch { continue; }
                if (!string.Equals(nm, srcSeqName, StringComparison.Ordinal)) continue;
                var clone = (NiPropertyObject)(object)srcFilePo.Clone($"Data.Seq[{i}]", CopyAllFlags);
                return (NiSequence)(object)clone;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "File-array clone of sequence '{Seq}' failed.", srcSeqName); }

        throw new InvalidOperationException(
            $"Could not deep-clone sequence '{srcSeqName}'. Neither PropertyObject.Clone(\"\") nor " +
            "the Data.Seq[idx] array clone produced a usable Sequence object.");
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (_engine != null) return;

        // The MCP host can restart this server process mid-session (or the engine can be torn
        // down), leaving _engine null and every tool failing with "Not connected". Attempt a
        // one-shot lazy reconnect with the default engine path instead of forcing the caller to
        // re-run connect_engine. Serialized so two concurrent calls never spin up two engines.
        lock (_connectLock)
        {
            if (_engine != null) return;
            try
            {
                _logger.LogWarning("Engine not connected — attempting one-shot lazy reconnect.");
                if (ConnectAsync().GetAwaiter().GetResult() && _engine != null)
                {
                    _logger.LogInformation("Lazy reconnect succeeded.");
                    return;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Lazy reconnect failed."); }
        }

        throw new InvalidOperationException(
            "Not connected to TestStand engine. Call connect_engine first.");
    }

    private dynamic? FindExecution(string executionId)
    {
        if (_activeExecutions.TryGetValue(executionId, out var exec))
            return exec;
        return null;
    }

    private static string TryGetString(Func<string> getter)
    {
        try { return getter() ?? ""; }
        catch { return ""; }
    }

    /// <summary>
    /// Read the execution's run state via the TYPED Execution.GetStates(out,out) call.
    /// runState: 1=Running, 2=Paused, 3=Stopped.
    /// (Reflection InvokeMember with out-param ParameterModifiers does NOT marshal on the
    /// TestStand COM RCW — it always threw and the old code then reported "Stopped" for every
    /// execution. The typed cast works cleanly; see memory teststand-getstates-reflection-fails.)
    /// </summary>
    private static int GetExecutionRunState(object execObj)
    {
        try
        {
            var exec = (NiExecution)execObj;
            exec.GetStates(out NiExecRunStates runState, out NiExecTermStates _);
            return (int)runState;
        }
        catch { return 3; } // assume Stopped on error
    }

    private ExecutionInfo MapExecutionInfo(dynamic exec)
    {
        var id = TryGetString(exec, "Id");
        if (string.IsNullOrEmpty(id))
            id = ((object)exec).GetHashCode().ToString();
        int runState = GetExecutionRunState((object)exec);
        return new ExecutionInfo
        {
            ExecutionId      = id,
            Status           = MapExecutionState(runState),
            StartTime        = _executionStartTimes.TryGetValue(id, out DateTime st) ? st : DateTime.UtcNow,
            SequenceFilePath = TryGetString(exec, "SequenceFilePath"),
            EntryPoint       = TryGetString(exec, "DisplayName")
        };
    }

    // ExecRunState: 1=Running, 2=Paused, 3=Stopped
    private static string MapExecutionState(int runState) => runState switch
    {
        1 => "Running",
        2 => "Paused",
        3 => "Stopped",
        _ => "Unknown"
    };

    /// <summary>Typed read of the execution's termination state (1=Normal, 2/3=Terminating,
    /// 4=Aborting, 5=KillingThreads). Defaults to Normal(1) on error.</summary>
    private static int GetExecutionTermState(object execObj)
    {
        try
        {
            var exec = (NiExecution)execObj;
            exec.GetStates(out NiExecRunStates _, out NiExecTermStates termState);
            return (int)termState;
        }
        catch { return 1; } // ExecTermState_Normal
    }

    /// <summary>Derive a human-readable result for runs where Execution.ResultStatus is empty
    /// (e.g. a direct sequence run with no process model), based on run + termination state.</summary>
    private static string DeriveResult(int runState, int termState) => termState switch
    {
        2 or 3 => "Terminated",
        4 or 5 => "Aborted",
        _      => runState switch          // ExecTermState_Normal
        {
            1 => "Running",
            2 => "Paused",
            3 => "Done",                   // finished normally
            _ => "Unknown"
        }
    };

    private ExecutionResult BuildExecutionResult(dynamic exec, string executionId)
    {
        var elapsed = _executionStartTimes.TryGetValue(executionId, out var st)
            ? (DateTime.UtcNow - st).TotalSeconds : 0;

        int runState  = GetExecutionRunState((object)exec);
        int termState = GetExecutionTermState((object)exec);

        // ResultStatus is populated for process-model / UUT runs; for a direct sequence run it is
        // typically empty, so fall back to a status derived from the run + termination state
        // (e.g. a normally-finished direct run reports "Done" rather than "Unknown").
        string result;
        try { result = ((NiExecution)(object)exec).ResultStatus ?? ""; }
        catch { result = ""; }
        if (string.IsNullOrEmpty(result)) result = DeriveResult(runState, termState);

        return new ExecutionResult
        {
            ExecutionId    = executionId,
            Status         = MapExecutionState(runState),
            Result         = result,
            ElapsedSeconds = elapsed
        };
    }

    private SequenceFileInfo MapSequenceFileInfo(dynamic sf, string filePath)
    {
        var info = new SequenceFileInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath)
        };

        try
        {
            int numSeqs = 0;

            // Use dynamic dispatch directly — reflection on __ComObject does not resolve
            // IDispatch COM members and always fails silently.
            try
            {
                numSeqs = Convert.ToInt32((object)sf.NumSequences);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NumSequences via dynamic dispatch failed for {Path}, falling back to probe", filePath);

                // Probe-based fallback: call GetSequence(index) until it throws
                for (int probe = 0; probe < 1000; probe++)
                {
                    try
                    {
                        object _ = sf.GetSequence(probe);
                        numSeqs = probe + 1;
                    }
                    catch { break; }
                }
                _logger.LogInformation("Probe-based sequence count for {Path}: {Count}", filePath, numSeqs);
            }

            _logger.LogInformation("Enumerating {Count} sequences in {Path}", numSeqs, filePath);

            for (int i = 0; i < numSeqs; i++)
            {
                try
                {
                    dynamic seq = sf.GetSequence(i);
                    info.Sequences.Add(MapSequenceInfo(seq));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to map sequence at index {Index} in {Path}", i, filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate sequences in {Path}", filePath);
        }

        try { info.FileGlobals.AddRange((List<VariableInfo>)MapVariables(GetFileGlobals(sf))); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate file globals for sequence file info."); }
        try { info.StationGlobals.AddRange((List<VariableInfo>)MapVariables(GetStationGlobals())); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate station globals for sequence file info."); }

        return info;
    }

    private SequenceInfo MapSequenceInfo(dynamic seq)
    {
        var info = new SequenceInfo();

        // Protect Name access — if this throws unguarded the whole sequence is lost
        try { info.Name = (string)seq.Name; }
        catch
        {
            try
            {
                var seqObj = (object)seq;
                info.Name = seqObj.GetType().InvokeMember("Name",
                    _comFlags, null, seqObj, null)?.ToString() ?? "Unknown";
            }
            catch { info.Name = "Unknown"; }
        }

        // TestStand stores sequence comments as "Comment", not "Description"
        string? seqDesc = null;
        try { seqDesc = (string)seq.Comment; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence Comment property."); }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)((NiSequence)(object)seq).AsPropertyObject().GetValString("TS.Comment", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read TS.Comment from sequence property bag."); }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)((NiSequence)(object)seq).AsPropertyObject().GetValString("Comment", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Comment from sequence property bag."); }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)seq.Description; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence Description property."); }
        if (!string.IsNullOrEmpty(seqDesc)) info.Description = seqDesc;
        string[] groupNames = { "Setup", "Main", "Cleanup" };
        for (int g = 0; g <= 2; g++)
        {
            try
            {
                int count = Convert.ToInt32((object)seq.GetNumSteps((NiStepGroups)g));
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var step = MapStepInfo(seq.GetStep(i, (NiStepGroups)g));
                        // Omit the default "Main" group (g==1) to save tokens; absent = Main.
                        step.StepGroup = g == 1 ? null : groupNames[g];
                        info.Steps.Add(step);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to map step at index {Index} in group {Group} in MapSequenceInfo.", i, g); }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate steps for group {Group} in MapSequenceInfo.", g); }
        }
        try { info.Locals.AddRange((List<VariableInfo>)MapVariables(seq.Locals)); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to map local variables for sequence info."); }
        try { info.Parameters.AddRange((List<ParameterInfo>)MapParameters(seq.Parameters)); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to map parameters for sequence info."); }
        return info;
    }

    private List<StepInfo> MapSteps(dynamic stepGroup)
    {
        var steps = new List<StepInfo>();
        try
        {
            int n = (int)stepGroup.Count;
            for (int i = 0; i < n; i++)
                steps.Add(MapStepInfo(stepGroup[(object)i]));
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to map step group collection."); }
        return steps;
    }

    private StepInfo MapStepInfo(dynamic step)
    {
        var info = new StepInfo
        {
            Name     = (string)step.Name,
            StepType = TryGetString(step.StepType, "Name"),
        };
        // RunMode is a string property: "Normal", "Skip", "Fail", "Pass".
        // Only emit Enabled when the step is skipped; enabled steps leave it
        // null so the serializer omits it (absence = enabled — token saver).
        try { if ((string)step.RunMode == "Skip") info.Enabled = false; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step RunMode for enabled flag."); }
        // step.Comment holds the user-set comment (written by SetStepCommentAsync).
        // step.Description returns the auto-generated type description (e.g. "Action"),
        // so prefer Comment, and only fall back to Description when Comment is empty.
        try
        {
            var c = (string)step.Comment;
            if (!string.IsNullOrEmpty(c)) info.Description = c;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step Comment property."); }
        if (string.IsNullOrEmpty(info.Description))
            try { info.Description = (string)step.Description; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step Description property."); }
        try
        {
            if ((int)step.SubSteps.Count > 0)
                info.SubSteps = MapSteps(step.SubSteps);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate step sub-steps."); }
        return info;
    }

    private List<VariableInfo> MapVariables(dynamic propBlock)
    {
        var vars = new List<VariableInfo>();
        try
        {
            var propObj  = (object)propBlock;
            var propType = propObj.GetType();
            int count = Convert.ToInt32(propType.InvokeMember("GetNumSubProperties",
                _comFlags, null, propObj, new object[] { "" }));

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic prop = propType.InvokeMember("GetNthSubProperty",
                        _comFlags, null, propObj, new object[] { "", i, 0 })!;

                    // PropertyObject has no `TypeName` property — the human-readable type
                    // name comes from GetTypeDisplayString(lookupString, options).
                    string dataType = "";
                    try { dataType = (string)prop.GetTypeDisplayString("", (object)0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get type display string for variable."); }
                    if (string.IsNullOrEmpty(dataType)) dataType = TryGetString(prop, "TypeName");

                    vars.Add(new VariableInfo
                    {
                        Name        = (string)prop.Name,
                        DataType    = dataType,
                        Value       = TryGetValue(prop),
                        Description = TryGetStringOrNull(prop, "Comment")
                    });
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read variable entry at index {Index} in MapVariables.", i); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate sub-properties in MapVariables."); }
        return vars;
    }

    private void SetPropertyValue(dynamic propBlock, string name, object value)
    {
        // A plain set (option 0) throws on an Enumeration-typed target ("Expected type X. Found
        // type Number/String"); retry coercing TO the enum type. The coerce is for-this-operation
        // only — it sets the value and preserves the target's enum type (does not retype to Number).
        // Mirrors the enum READ path (PropOption_CoerceToNumber) and set_property_value.
        int toEnum = (int)NiPropOptions.PropOption_CoerceToEnum;
        if (value is double d)
        {
            try { propBlock.SetValNumber(name, 0, d); }
            catch { propBlock.SetValNumber(name, toEnum, d); }
        }
        else if (value is bool b)
            propBlock.SetValBoolean(name, 0, b);
        else
        {
            try { propBlock.SetValString(name, 0, value?.ToString() ?? ""); }
            catch { propBlock.SetValString(name, toEnum, value?.ToString() ?? ""); }
        }
    }

    private object? TryGetValue(dynamic prop)
    {
        try { return (double)prop.GetValNumber("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as number."); }
        // A number whose REPRESENTATION is UInt64/Int64 rejects GetValNumber ("Numeric
        // representations must match exactly") — it needs the width-specific reader. Without this the
        // value used to come back as Empty/null, so e.g. a UInt64 VID/PID default read as "no value".
        var wide = TryReadWideInteger(prop);
        if (wide != null) return wide;
        try { return (bool)prop.GetValBoolean("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as boolean."); }
        try { return (string)prop.GetValString("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as string."); }
        // Enum leaf: the three plain reads above all throw ("Expected type X. Found type <Enum>");
        // read it via coercion → {ordinal, symbolicName} so an authored enum value is not lost.
        return TryReadEnumValue(prop);
    }

    /// <summary>
    /// Reads a number whose <c>PropertyRepresentations</c> is UInt64 or Int64 — TestStand
    /// rejects <c>GetValNumber</c> on those ("Numeric representations must match exactly"), so the
    /// 64-bit-wide accessors are the only way to see the value. Returns the boxed
    /// <see cref="ulong"/>/<see cref="long"/>, or null when the property is not a wide integer.
    /// </summary>
    private object? TryReadWideInteger(dynamic prop)
    {
        try { return (ulong)prop.GetValUnsignedInteger64("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetValUnsignedInteger64 failed."); }
        try { return (long)prop.GetValInteger64("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetValInteger64 failed."); }
        return null;
    }

    /// <summary>
    /// The numeric REPRESENTATION of a property ("Float64" / "Int64" / "UInt64" / "None"), read off
    /// its <c>PropertyObjectType</c>. Null when the property has no type object (non-numeric nodes).
    /// </summary>
    private string? TryReadRepresentation(NiPropertyObject po)
    {
        try
        {
            var rep = ((NiPropObjType)(object)po.Type).Representation;
            return rep switch
            {
                NiPropRepresentations.PropertyRepresentation_Float64 => "Float64",
                NiPropRepresentations.PropertyRepresentation_Int64   => "Int64",
                NiPropRepresentations.PropertyRepresentation_UInt64  => "UInt64",
                _                                                       => null,
            };
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Representation read failed."); return null; }
    }

    /// <summary>The property's display NumericFormat (e.g. <c>%#.4x</c>), or null when unset.</summary>
    private string? TryReadNumericFormat(NiPropertyObject po)
    {
        try { var f = po.NumericFormat; return string.IsNullOrEmpty(f) ? null : f; }
        catch (Exception ex) { _logger.LogDebug(ex, "NumericFormat read failed."); return null; }
    }

    /// <summary>
    /// Applies a numeric REPRESENTATION and/or display NumericFormat to a property, then (re)writes
    /// the value with the matching width-specific setter when one was supplied. TestStand keeps the
    /// representation on the property's TYPE object, and once a property is UInt64/Int64 the plain
    /// <c>SetValNumber</c> is rejected — so representation must be applied FIRST and the value then
    /// written through <c>SetValUnsignedInteger64</c>/<c>SetValInteger64</c>.
    /// <paramref name="representation"/> accepts float64/double, int64, uint64 (case-insensitive).
    /// </summary>
    private void ApplyNumericRepresentation(NiPropertyObject po, string? representation,
        string? numberFormat, string? valueLiteral)
    {
        string rep = (representation ?? "").Trim().ToLowerInvariant();
        if (rep.Length > 0)
        {
            NiPropRepresentations target = rep switch
            {
                "float64" or "double" or "number" => NiPropRepresentations.PropertyRepresentation_Float64,
                "int64" or "i64" or "signed"      => NiPropRepresentations.PropertyRepresentation_Int64,
                "uint64" or "ui64" or "unsigned"  => NiPropRepresentations.PropertyRepresentation_UInt64,
                _ => throw new ArgumentException(
                        $"Unknown representation '{representation}'. Use float64/int64/uint64."),
            };
            ((NiPropObjType)(object)po.Type).Representation = target;

            if (!string.IsNullOrWhiteSpace(valueLiteral))
            {
                // Hex literals ("0x374e", the form the editor shows for a %#.4x UInt64) are accepted
                // alongside plain decimals.
                string lit = valueLiteral!.Trim();
                bool hex = lit.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                string digits = hex ? lit.Substring(2) : lit;
                switch (target)
                {
                    case NiPropRepresentations.PropertyRepresentation_UInt64:
                        po.SetValUnsignedInteger64("", 0, hex
                            ? Convert.ToUInt64(digits, 16)
                            : ulong.Parse(digits, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case NiPropRepresentations.PropertyRepresentation_Int64:
                        po.SetValInteger64("", 0, hex
                            ? Convert.ToInt64(digits, 16)
                            : long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    default:
                        po.SetValNumber("", 0, hex
                            ? Convert.ToUInt64(digits, 16)
                            : double.Parse(digits, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }
            }
        }

        if (numberFormat != null)
        {
            try { po.NumericFormat = numberFormat; }
            catch (Exception ex) { _logger.LogDebug(ex, "NumericFormat write failed."); }
        }
    }

    /// <summary>
    /// Reads an ENUM-typed leaf's current value via coercion (a plain GetVal* read throws on enums).
    /// Returns <see cref="EnumLeafValue"/> {ordinal, symbolicName}, or null when the property is not
    /// an enum (or is genuinely empty). Only meaningful AFTER the plain number/boolean/string reads
    /// failed — a plain Number/String leaf would be caught by those first.
    /// </summary>
    private EnumLeafValue? TryReadEnumValue(dynamic prop)
    {
        try
        {
            double ord = (double)prop.GetValNumber("", PropOption_CoerceToNumber);
            string sym = (string)prop.GetValString("", PropOption_CoerceToString);
            return new EnumLeafValue { Ordinal = (int)Math.Round(ord), SymbolicName = sym };
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Not an enum leaf (coerced read failed)."); return null; }
    }

    private T? GetEngineProperty<T>(string propName)
    {
        try
        {
            return (T)((object)_engine!).GetType().InvokeMember(
                propName, _comFlags, null, _engine, null)!;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read engine property '{PropName}'.", propName); }
        return default;
    }

    private static string TryGetString(dynamic obj, string propName = "")
    {
        try
        {
            if (string.IsNullOrEmpty(propName)) return (string)obj;
            return ((object)obj).GetType().InvokeMember(
                propName, _comFlags, null, obj, null)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static string? TryGetStringOrNull(dynamic obj, string propName)
    {
        try
        {
            var val = ((object)obj).GetType().InvokeMember(
                propName, _comFlags, null, obj, null)?.ToString();
            return string.IsNullOrEmpty(val) ? null : val;
        }
        catch { return null; }
    }

    private static bool TryGetBool(dynamic obj, string propName)
    {
        try
        {
            var val = ((object)obj).GetType().InvokeMember(
                propName, _comFlags, null, obj, null);
            return val is bool b ? b : Convert.ToBoolean(val);
        }
        catch { return false; }
    }

    private static object TryGetStepComment(dynamic step)
    {
        try { return (string)step.Comment; }
        catch { return ""; }
    }

    private static string TryGetStepExpression(dynamic step, string type)
    {
        try
        {
            return type switch
            {
                "Pre"    => (string)step.PreExpression,
                "Post"   => (string)step.PostExpression,
                "Status" => (string)step.StatusExpression,
                _        => ""
            };
        }
        catch { return ""; }
    }

    private static dynamic FindStepInAllGroups(dynamic seq, string stepName)
    {
        for (int g = 0; g <= 2; g++)
        {
            try
            {
                int count = (int)seq.GetNumSteps((object)g);
                for (int i = 0; i < count; i++)
                {
                    var s = seq.GetStep(i, (object)g);
                    if ((string)s.Name == stepName) return s;
                }
            }
            catch (Exception) { /* best-effort: search step group — intentionally ignored */ }
        }
        throw new KeyNotFoundException($"Step '{stepName}' not found in any step group.");
    }

    /// <summary>
    /// Resolves a step within a single step group by name — the shared entry point for every
    /// by-name step tool. Supports a "<c>Name#N</c>" suffix to target the N-th (1-based) occurrence
    /// when a group holds several steps with the SAME name (e.g. repeated "Call Log" / "End" /
    /// "If"): the native <c>GetStepByName</c> always returns the FIRST match, so without this the
    /// 2nd+ duplicate could only be reached via the rename-configure-rename-back workaround. A plain
    /// name (no '#') delegates to <c>GetStepByName</c>, preserving existing behaviour exactly.
    /// </summary>
    private static dynamic ResolveStepInGroup(dynamic seq, int group, string stepName)
    {
        // Positional selector '@idx:N' — the 0-based step index within the group. Lets callers
        // address a specific step among duplicate-named ones (e.g. three "Call Log" steps) without
        // renaming. Shared by every by-name step tool (set_step_*, set_module_parameter, ...).
        if (stepName.StartsWith("@idx:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(stepName.Substring(5), out int atIdx))
        {
            int total = (int)seq.GetNumSteps((object)group);
            if (atIdx < 0 || atIdx >= total)
                throw new KeyNotFoundException(
                    $"@idx:{atIdx} is out of range — the group has {total} step(s) (valid 0..{total - 1}).");
            return seq.GetStep(atIdx, (object)group);
        }

        int hash = stepName.LastIndexOf('#');
        if (hash > 0 && int.TryParse(stepName.Substring(hash + 1), out int occurrence) && occurrence >= 1)
        {
            string baseName = stepName.Substring(0, hash);
            int count = (int)seq.GetNumSteps((object)group);
            int seen = 0;
            for (int i = 0; i < count; i++)
            {
                var s = seq.GetStep(i, (object)group);
                if ((string)s.Name == baseName && ++seen == occurrence) return s;
            }
            throw new KeyNotFoundException(
                $"Step '{baseName}' occurrence #{occurrence} not found in the group (found {seen} with that name).");
        }
        return seq.GetStepByName(stepName, (object)group);
    }

    // ── Type Palettes ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<TypePaletteInfo>> GetTypePalettesAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<TypePaletteInfo>();

            // Get palette paths
            var paletteArray = (object[])_engine!.GetTypePaletteFileList();
            var palettePaths = new List<(string Path, string Name)>();
            foreach (var item in paletteArray)
            {
                string path = "";
                try
                {
                    dynamic d = item;
                    path = (string)d.GetPropertyObject("Path", (object)0).GetValString("", (object)0);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read palette path from palette file list entry."); }
                palettePaths.Add((path, System.IO.Path.GetFileNameWithoutExtension(path)));
            }

            // Get all loaded step types
            var allTypeNames = (string[])_engine!.GetTypeNames();
            var stepTypesByPalette = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, name) in palettePaths)
                stepTypesByPalette[name] = new List<string>();

            // Map each step type to its palette using TypeVersion + naming conventions.
            // SetTypePaletteFileList cannot be reliably called via late-bound COM dispatch,
            // so palette membership is determined by the documented NI type naming scheme.
            foreach (var typeName in allTypeNames)
            {
                try
                {
                    dynamic td = _engine!.GetTypeDefinition(typeName);
                    if ((int)td.TypeCategory != 1) continue; // step types only

                    string ver = "";
                    try { ver = (string)td.TypeVersion; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read TypeVersion for type '{TypeName}'.", typeName); }

                    string palette = ResolvePaletteName(typeName, ver, stepTypesByPalette.Keys);
                    if (!string.IsNullOrEmpty(palette) && stepTypesByPalette.ContainsKey(palette))
                        stepTypesByPalette[palette].Add(typeName);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to process type definition for '{TypeName}' in palette enumeration.", typeName); }
            }

            // Build result
            foreach (var (path, name) in palettePaths)
            {
                var types = stepTypesByPalette.TryGetValue(name, out var t) ? t : new List<string>();
                result.Add(new TypePaletteInfo
                {
                    Path = path,
                    Name = name,
                    StepTypeNames = types,
                    StepTypeCount = types.Count
                });
            }

            return result;
        });
    }

    /// <summary>
    /// Resolves which palette a step type belongs to based on TypeVersion and naming conventions.
    /// The TestStand COM API does not expose palette membership directly via late-bound dispatch.
    /// </summary>
    internal static string ResolvePaletteName(string typeName, string typeVersion, IEnumerable<string> availablePalettes)
    {
        // Version-unique palettes
        if (typeVersion == "23.0.0.2" || typeVersion == "23.0.0.4")
            return "NI_FlowControl";
        if (typeVersion == "23.0.0.3")
            return "NI_PropertyLoader";
        if (typeVersion == "23.0.0.49152")
            return "NI_SubstepTypes";

        // NI_Flow_* with any version → NI_FlowControl
        if (typeName.StartsWith("NI_Flow_", StringComparison.OrdinalIgnoreCase))
            return "NI_FlowControl";

        // Property Loader types
        if (typeName == "NI_PropertyLoader" || typeName == "NI_FTPFiles" ||
            typeName.StartsWith("NI_NewCsvFile", StringComparison.OrdinalIgnoreCase))
            return "NI_PropertyLoader";

        // Database types
        if (typeName == "NI_OpenDatabase" || typeName == "NI_CloseDatabase" ||
            typeName == "NI_OpenSQLStatement" || typeName == "NI_CloseSQLStatement" ||
            typeName == "NI_DataOperation" || typeName == "NI_WriteRecord" ||
            typeName.StartsWith("NI_Legacy", StringComparison.OrdinalIgnoreCase))
            return "NI_DatabaseTypes";

        // Synchronization types
        if (typeName == "NI_Lock" || typeName == "NI_Queue" || typeName == "NI_Notification" ||
            typeName == "NI_Rendezvous" || typeName == "NI_Semaphore" || typeName == "NI_Wait" ||
            typeName == "NI_BatchSpec" || typeName == "NI_BatchSync" ||
            typeName == "NI_AutoSchedule" || typeName == "NI_CPUAffinity" ||
            typeName == "NI_ThreadPriority" || typeName == "NI_UseResource" ||
            typeName.StartsWith("NI_Resource", StringComparison.OrdinalIgnoreCase))
            return "NI_SyncTypes";

        // Hardware configuration types
        if (typeName == "NI_ApplyIOConfig" || typeName == "NI_CloseIOSession" ||
            typeName.StartsWith("NI_CreateIOSession", StringComparison.OrdinalIgnoreCase))
            return "NI_HardwareConfiguration";

        // All other types (built-in and LabVIEW integration) belong to NI_Types
        if (availablePalettes.Contains("NI_Types"))
            return "NI_Types";

        return "";
    }

    /// <inheritdoc/>
    public async Task LoadTypePaletteAsync(string palettePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var currentArray = (object[])_engine!.GetTypePaletteFileList();
            // Check if already loaded
            foreach (var item in currentArray)
            {
                try
                {
                    dynamic d = item;
                    var pp = d.GetPropertyObject("Path", (object)0);
                    var p = (string)pp.GetValString("", (object)0);
                    if (p.Equals(palettePath, StringComparison.OrdinalIgnoreCase)) return;
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read path from palette file list entry during load check."); }
            }
            // Clone first entry as template for the new entry, then adjust path
            if (currentArray.Length == 0)
                throw new InvalidOperationException(
                    "Cannot load palette: no existing palette to use as template.");

            dynamic template = ((dynamic)currentArray[0]).Clone();
            var pathProp = template.GetPropertyObject("Path", (object)0);
            pathProp.SetValString("", (object)0, (object)palettePath);

            var newArray = new object[currentArray.Length + 1];
            Array.Copy(currentArray, newArray, currentArray.Length);
            newArray[newArray.Length - 1] = template;

            ((dynamic)_engine!).SetTypePaletteFileList(newArray);
            _engine!.LoadTypePaletteFiles();
        });
    }

    /// <inheritdoc/>
    public async Task UnloadTypePaletteAsync(string palettePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var currentArray = (object[])_engine!.GetTypePaletteFileList();
            var filtered = new System.Collections.Generic.List<object>();
            foreach (var item in currentArray)
            {
                try
                {
                    dynamic d = item;
                    var pp = d.GetPropertyObject("Path", (object)0);
                    var p = (string)pp.GetValString("", (object)0);
                    if (!p.Equals(palettePath, StringComparison.OrdinalIgnoreCase))
                        filtered.Add(item);
                }
                catch { filtered.Add(item); }
            }
            ((dynamic)_engine!).SetTypePaletteFileList(filtered.ToArray());
            _engine!.LoadTypePaletteFiles();
        });
    }

    /// <inheritdoc/>
    public async Task<List<StepTypeInfo>> GetStepTypesAsync(string? paletteFile = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<StepTypeInfo>();
            try
            {
                var names = (string[])_engine!.GetTypeNames();
                string? palFilter = string.IsNullOrEmpty(paletteFile) ? null
                    : System.IO.Path.GetFileNameWithoutExtension(paletteFile);

                foreach (var name in names)
                {
                    try
                    {
                        dynamic td = _engine.GetTypeDefinition(name);
                        if ((int)td.TypeCategory != 1) continue;

                        var loc = TryGetTypeLocation(td);
                        if (palFilter != null &&
                            loc.IndexOf(palFilter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        result.Add(new StepTypeInfo
                        {
                            Name        = name,
                            Description = TryGetStringOrNull(td, "Comment"),
                            PaletteFile = loc,
                            AdapterName = null
                        });
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to process step type definition for '{Name}' in GetStepTypesAsync.", name); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enumerate step types");
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<StepTypeInfo> GetStepTypeAsync(string stepTypeName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                dynamic td = _engine!.GetTypeDefinition(stepTypeName);
                if (td == null)
                    throw new KeyNotFoundException($"Step type '{stepTypeName}' not found.");

                var info = new StepTypeInfo
                {
                    Name        = stepTypeName,
                    Description = TryGetStringOrNull(td, "Comment"),
                    PaletteFile = TryGetTypeLocation(td),
                    AdapterName = null
                };

                // Collect sub-property names as property metadata
                try
                {
                    int n = (int)td.GetNumSubProperties((object)"");
                    for (int i = 0; i < n; i++)
                    {
                        var sub = td.GetNthSubProperty((object)"", (object)i, (object)0);
                        string subName = (string)sub.Name;
                        info.Properties[subName] = TryGetString(sub, "TypeVersion");
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate sub-properties of step type '{Name}'.", stepTypeName); }

                return info;
            }
            catch (KeyNotFoundException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get step type '{Name}'", stepTypeName);
                throw new KeyNotFoundException($"Step type '{stepTypeName}' not found.");
            }
        });
    }

    // The PropertyObjectFile root carries these system entries alongside any user-created
    // data types; they must be filtered out when listing a file's custom data types.
    private static readonly HashSet<string> _fileRootSystemProps =
        new(StringComparer.OrdinalIgnoreCase) { "ChangeCount", "LastSavedChangeCount", "Path", "Data" };

    /// <inheritdoc/>
    public async Task<List<DataTypeInfo>> GetDataTypesAsync(string? sequenceFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<DataTypeInfo>();
            try
            {
                // File context: a file's custom data types are stored as named subproperties on
                // the file's root PropertyObject — the same store CreateDataTypeAsync writes to and
                // DeleteDataTypeAsync removes from. Enumerate those (minus the file's own system
                // entries) so create → list → delete is coherent.
                if (!string.IsNullOrEmpty(sequenceFilePath))
                {
                    var sf = GetOrLoadSeqFile(sequenceFilePath);
                    dynamic sfPo = sf.AsPropertyObjectFile();
                    int cnt = Convert.ToInt32((object)sfPo.GetNumSubProperties((object)""));
                    for (int i = 0; i < cnt; i++)
                    {
                        string name;
                        try { name = (string)sfPo.GetNthSubPropertyName((object)"", (object)i, (object)0); }
                        catch { continue; }
                        if (_fileRootSystemProps.Contains(name)) continue;

                        string kind = "Container";
                        bool isArr = false;
                        int  numEl = 0;
                        try
                        {
                            dynamic prop = sfPo.GetPropertyObject((object)name, (object)0);
                            kind = InferValueKind(prop, out isArr, out numEl);
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to infer value kind for data type '{Name}'.", name); }
                        result.Add(new DataTypeInfo { Name = name, BaseType = kind, IsArray = isArr });
                    }
                    return result;
                }

                // No file context: enumerate the engine-level type list.
                var names = (string[])_engine!.GetTypeNames();
                foreach (var name in names)
                {
                    try
                    {
                        dynamic td = _engine.GetTypeDefinition(name);
                        // TypeCategory 1 = step type → skip; everything else is a data type
                        if ((int)td.TypeCategory == 1) continue;

                        result.Add(new DataTypeInfo
                        {
                            Name        = name,
                            Description = TryGetStringOrNull(td, "Comment"),
                            BaseType    = TryGetString(td, "TypeVersion"),
                            IsArray     = false
                        });
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to process data type definition for '{Name}'.", name); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enumerate data types");
            }
            return result;
        });
    }

    /// <summary>
    /// Walks a file's TypeUsageList and returns every embedded type's name. The index space is not
    /// reliably bounded by GetNumTypes(category) via late-bound COM, so it walks GetTypeDefinition(i)
    /// (proven to work) until it runs off the end, reading each definition's Name. Robust for both
    /// listing and copy-all.
    /// </summary>
    private List<(string Name, bool Attached, string Kind)> EnumerateFileTypeDefs(NiTypeUsageList tul)
    {
        var outp = new List<(string, bool, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // MUST use TYPED interop here — the C# dynamic-COM binder throws TargetParameterCountException
        // on TypeUsageList.GetTypeDefinition (the earlier dynamic version silently caught that and
        // returned nothing → empty list). Walk indices with the typed 1-arg GetTypeDefinition(i)
        // (the same call CreateEnumAsync uses successfully) until it runs off the end.
        for (int i = 0; i < 4000; i++)
        {
            NiPropertyObject def;
            try { def = tul.GetTypeDefinition(i); }
            catch { break; }                       // out-of-range index → end of list
            if (def == null) break;

            string name;
            try { name = def.Name; } catch { continue; }
            if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

            bool attached = false;
            try { attached = tul.GetIsTypeAttachedToFile(i); }
            catch (Exception ex) { _logger.LogDebug(ex, "GetIsTypeAttachedToFile failed for type '{Type}'.", name); }

            string kind = "Container";
            try { kind = InferValueKind(def, out _, out _); }
            catch (Exception ex) { _logger.LogDebug(ex, "InferValueKind failed for type '{Type}'.", name); }

            outp.Add((name, attached, kind));
        }
        return outp;
    }

    /// <inheritdoc/>
    public async Task<List<DataTypeInfo>> GetFileTypeDefsAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            var result = new List<DataTypeInfo>();
            foreach (var (name, attached, kind) in EnumerateFileTypeDefs(tul))
                result.Add(new DataTypeInfo
                {
                    Name        = name,
                    BaseType    = kind,
                    IsArray     = false,
                    Description = attached ? "attached-to-file" : "not-attached",
                });
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<List<string>> CopyTypeDefsAsync(string sourceFilePath, string destFilePath,
        IReadOnlyList<string>? typeNames = null, bool save = true, string attach = "preserve")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var src = GetOrLoadSeqFile(sourceFilePath);
            var dst = GetOrLoadSeqFile(destFilePath);
            NiTypeUsageList srcTul = GetTypeUsageList(src);
            NiTypeUsageList dstTul = GetTypeUsageList(dst);

            string attachMode = (attach ?? "preserve").Trim().ToLowerInvariant();
            if (attachMode is not ("preserve" or "all" or "none"))
                throw new ArgumentException($"Unknown attach mode '{attach}'. Use preserve/all/none.");

            // The SOURCE's per-type attach flag. A 1:1 rebuild must mirror it: blanket-attaching every
            // copied type embeds types the original does not, which the FileDiffer reports as a
            // difference (observed: 59 attached instead of 7 on TFW_MDC_com_Python.seq).
            var srcAttached = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var (n, att, _) in EnumerateFileTypeDefs(srcTul)) srcAttached[n] = att;

            // Explicit names are the reliable path (GetTypeIndex(name) resolves directly). With no
            // names, copy every embedded type in the source.
            List<string> names = (typeNames != null && typeNames.Count > 0)
                ? typeNames.ToList()
                : EnumerateFileTypeDefs(srcTul).Select(t => t.Name).ToList();

            var copied = new List<string>();
            foreach (var name in names)
            {
                int sidx = -1;
                try { sidx = srcTul.GetTypeIndex(name); }
                catch (Exception ex) { _logger.LogDebug(ex, "Source GetTypeIndex failed for '{Type}'.", name); }
                if (sidx < 0) continue;                       // not in source — skip

                NiPropertyObject def;
                try { def = srcTul.GetTypeDefinition(sidx); }
                catch (Exception ex) { _logger.LogDebug(ex, "Source GetTypeDefinition failed for '{Type}'.", name); continue; }

                // Skip if the destination already has a type with this name (don't clobber, e.g.
                // standard Error/Result already present in a fresh file).
                int didx = -1;
                try { didx = dstTul.GetTypeIndex(name); } catch { didx = -1; }
                if (didx < 0)
                {
                    try
                    {
                        dstTul.InsertType(def, 0, NiTypeCategories.TypeCategory_CustomDataTypes);
                        didx = dstTul.GetTypeIndex(name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to copy type '{Type}' into '{Dest}'.", name, Path.GetFileName(destFilePath));
                        continue;
                    }
                }
                if (didx >= 0)
                {
                    bool wantAttached = attachMode switch
                    {
                        "all"  => true,
                        "none" => false,
                        _      => srcAttached.TryGetValue(name, out var a) && a,   // preserve
                    };
                    try { dstTul.SetIsTypeAttachedToFile(didx, wantAttached); }
                    catch (Exception ex) { _logger.LogDebug(ex, "SetIsTypeAttachedToFile failed for '{Type}'.", name); }
                }
                copied.Add(name);
            }

            try { ((PropertyObjectFile)(object)dst.AsPropertyObjectFile()).IncChangeCount(); }
            catch (Exception ex) { _logger.LogDebug(ex, "IncChangeCount failed on destination file."); }

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)dst, destFilePath);
                _loadedSequenceFiles[destFilePath] = dst;
            }
            return copied;
        });
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> CopyFileAttributesAsync(string sourceFilePath,
        string destFilePath, IReadOnlyList<string>? attributeNames = null, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var src = (NiSequenceFile)(object)GetOrLoadSeqFile(sourceFilePath);
            var dst = (NiSequenceFile)(object)GetOrLoadSeqFile(destFilePath);

            // The file's name/value attributes (e.g. NI.Analyzer.IgnoredMessages) hang off the file's
            // ROOT object — which is the PropertyObjectFile (AsPropertyObjectFile), NOT the content
            // PropertyObject returned by AsPropertyObject(). PropertyObjectFile derives from
            // PropertyObject, so it also exposes .Attributes; the analyzer stores its ignored-message
            // list there. Using AsPropertyObject() reaches a DIFFERENT object whose attribute container
            // is empty — which is why this tool used to find nothing to copy. Cast the file-root object
            // to PropertyObject (the underlying COM object implements both interfaces).
            NiPropertyObject srcFileObj = (NiPropertyObject)(object)src.AsPropertyObjectFile();
            NiPropertyObject dstFileObj = (NiPropertyObject)(object)dst.AsPropertyObjectFile();

            var copied   = new List<string>();
            var warnings = new List<string>();
            var result   = new Dictionary<string, object>();

            // NOTE: do NOT gate on PropertyObject.HasAttributes. On TS2026 it returns a FALSE NEGATIVE
            // for a file that genuinely carries attributes (e.g. NI.Analyzer.IgnoredMessages) — the same
            // typed-COM binding quirk that makes Execution.GetStates misbehave — so the tool used to skip
            // real attributes and report "no attributes to copy". Instead, reach the attribute-root
            // container directly and decide by its actual subproperty count.
            NiPropertyObject srcAttrs;
            try { srcAttrs = srcFileObj.Attributes; }   // attribute-root container
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Accessing source file Attributes failed.");
                result["copiedCount"] = 0;
                result["copied"]      = copied;
                result["warnings"]    = new List<string> { "Source file exposes no Attributes container." };
                return result;
            }

            int srcAttrCount = 0;
            try { srcAttrCount = srcAttrs.GetNumSubProperties(""); }
            catch (Exception ex) { _logger.LogDebug(ex, "GetNumSubProperties failed on source attributes."); }

            if (srcAttrCount == 0 && (attributeNames == null || attributeNames.Count == 0))
            {
                result["copiedCount"] = 0;
                result["copied"]      = copied;
                result["warnings"]    = new List<string> { "Source file has no name/value attributes to copy." };
                return result;
            }

            NiPropertyObject dstAttrs = dstFileObj.Attributes;   // created on demand if absent

            // Which top-level attribute names to copy: the explicit list, or every attribute present
            // on the source when none is given.
            List<string> names;
            if (attributeNames != null && attributeNames.Count > 0)
            {
                names = attributeNames.ToList();
            }
            else
            {
                names = new List<string>();
                int n = 0;
                try { n = srcAttrs.GetNumSubProperties(""); }
                catch (Exception ex) { _logger.LogDebug(ex, "GetNumSubProperties failed on source attributes."); }
                for (int i = 0; i < n; i++)
                {
                    try { names.Add(((NiPropertyObject)(object)srcAttrs.GetPropertyObjectByOffset(i, 0)).Name); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read source attribute name at offset {Idx}.", i); }
                }
            }

            foreach (var name in names)
            {
                // Existence probe on the source attribute container.
                try { _ = (NiPropertyObject)(object)srcAttrs.GetPropertyObject(name, 0); }
                catch { warnings.Add($"Attribute '{name}' not present on the source file — skipped."); continue; }
                try
                {
                    // The object returned by GetPropertyObject still belongs to the source tree, so
                    // SetPropertyObject would reject it ("already has a parent object"). Clone first
                    // (PropOption_CopyAllFlags = flag-preserving detached deep copy), then attach.
                    NiPropertyObject clone = (NiPropertyObject)(object)srcAttrs.Clone(
                        name, 0x20000000 /* PropOption_CopyAllFlags */);
                    dstAttrs.SetPropertyObject(name, 0x1 /* PropOption_InsertIfMissing */, clone);
                    copied.Add(name);
                }
                catch (Exception ex) { warnings.Add($"Could not copy attribute '{name}': {ex.Message}"); }
            }

            try { ((PropertyObjectFile)(object)dst.AsPropertyObjectFile()).IncChangeCount(); }
            catch (Exception ex) { _logger.LogDebug(ex, "IncChangeCount failed on destination file."); }

            if (save)
            {
                SaveSequenceFileWithRetry(dst, destFilePath);
                _loadedSequenceFiles[destFilePath] = dst;
            }

            result["copiedCount"] = copied.Count;
            result["copied"]      = copied;
            result["warnings"]    = warnings;
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> CopyFileGlobalsAsync(string sourceFilePath,
        string destFilePath, IReadOnlyList<string>? globalNames = null, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var src = (NiSequenceFile)(object)GetOrLoadSeqFile(sourceFilePath);
            var dst = (NiSequenceFile)(object)GetOrLoadSeqFile(destFilePath);

            // The file globals live in the file's FileGlobalsDefaultValues container — the SAME
            // accessor get_file_globals (GetFileGlobals) and set_file_global use. Reaching them via
            // the file-root property path "Data.FileGlobalDefaults" resolves a named member (so the
            // explicit-names path worked) but reports 0 direct sub-properties, which silently emptied
            // the copy-all enumeration below. Use the typed accessor so both paths agree.
            NiPropertyObject srcFg = GetFileGlobals(src);
            NiPropertyObject dstFg = GetFileGlobals(dst);

            var copied   = new List<string>();
            var warnings = new List<string>();
            var result   = new Dictionary<string, object>();

            // Which globals to copy: the explicit list, or every global present on the source.
            List<string> names;
            if (globalNames != null && globalNames.Count > 0)
            {
                names = globalNames.ToList();
            }
            else
            {
                // Enumerate via MapVariables — the SAME reflection-based reader get_file_globals uses.
                // An early-bound srcFg.GetNumSubProperties("") returns 0 on the FileGlobalsDefaultValues
                // container here (a COM early-binding quirk), which is what silently emptied this branch;
                // MapVariables reads the members reliably through late binding.
                names = MapVariables(srcFg).Select(v => v.Name).ToList();
            }

            foreach (var name in names)
            {
                // Existence probe on the source globals container.
                try { _ = (NiPropertyObject)(object)srcFg.GetPropertyObject(name, 0); }
                catch { warnings.Add($"File global '{name}' not present on the source file — skipped."); continue; }
                try
                {
                    // The object from GetPropertyObject still belongs to the source tree, so
                    // SetPropertyObject would reject it ("already has a parent object"). Clone first
                    // (PropOption_CopyAllFlags = flag-preserving detached deep copy — carries the type,
                    // value, comment, PropFlags and nested members), then attach to the destination.
                    NiPropertyObject clone = (NiPropertyObject)(object)srcFg.Clone(
                        name, 0x20000000 /* PropOption_CopyAllFlags */);
                    dstFg.SetPropertyObject(name, 0x1 /* PropOption_InsertIfMissing */, clone);
                    copied.Add(name);
                }
                catch (Exception ex) { warnings.Add($"Could not copy file global '{name}': {ex.Message}"); }
            }

            try { ((PropertyObjectFile)(object)dst.AsPropertyObjectFile()).IncChangeCount(); }
            catch (Exception ex) { _logger.LogDebug(ex, "IncChangeCount failed on destination file."); }

            if (save)
            {
                SaveSequenceFileWithRetry(dst, destFilePath);
                _loadedSequenceFiles[destFilePath] = dst;
            }

            result["copiedCount"] = copied.Count;
            result["copied"]      = copied;
            result["warnings"]    = warnings;
            return result;
        });
    }

    /// <summary>
    /// Tries to determine the palette file location of a type definition
    /// by examining the GetLocation property object.
    /// </summary>
    private static string TryGetTypeLocation(dynamic typeDefinition)
    {
        try
        {
            // GetLocation is a PropertyObject method - use reflection to avoid
            // C# dynamic dispatch ambiguity with the "Get" prefix naming.
            var loc = ((object)typeDefinition).GetType().InvokeMember(
                "GetLocation", _comInvokeFlags, null, typeDefinition, new object[] { "" });
            return loc?.ToString() ?? "";
        }
        catch { return ""; }
    }

    // ── Engine Info & Control ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<EnginePaths> GetEnginePathsAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            return new EnginePaths
            {
                BinDirectory      = GetEngineProperty<string>("BinDirectory") ?? "",
                ConfigDirectory   = GetEngineProperty<string>("ConfigDirectory") ?? "",
                TestStandDirectory= GetEngineProperty<string>("TestStandDirectory") ?? "",
                VersionString     = GetEngineProperty<string>("VersionString") ?? "",
                MajorVersion      = GetEngineProperty<int>("MajorVersion"),
                MinorVersion      = GetEngineProperty<int>("MinorVersion"),
                StationId         = GetEngineProperty<string>("StationID") ?? "",
                ComputerName      = GetEngineProperty<string>("ComputerName") ?? Environment.MachineName,
                McpServerDirectory= AppContext.BaseDirectory,
                // scripts\ is shipped next to the exe (see the csproj <None Include="scripts\**"/>);
                // expose the absolute path so the doc/presentation agents don't guess "<repo>\scripts".
                ScriptsDirectory  = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "scripts"))
                    ? Path.Combine(AppContext.BaseDirectory, "scripts")
                    : ""
            };
        });
    }

    /// <inheritdoc/>
    public async Task<ExpressionCheckResult> CheckExpressionAsync(string expression,
        string? sequenceFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                // sequenceFilePath is accepted for API compatibility but not needed —
                // TestStand 2026 IEngine.CheckExprSyntax does not take a context arg.
                _ = sequenceFilePath;

                string errMsg = "";
                try
                {
                    // TestStand 2026 IEngine.CheckExprSyntax signature (from interop assembly):
                    //   CheckExprSyntax(string expressionStr,
                    //                   out string errorDescription,
                    //                   out int startErrPos,
                    //                   out int endErrPos)
                    // No sequence-context parameter — use typed cast to call it correctly.
                    var typedEngine =
                        (NationalInstruments.TestStand.Interop.API.IEngine)(object)_engine!;
                    typedEngine.CheckExprSyntax(expression,
                        out string errorDesc, out int _, out int __);
                    errMsg = errorDesc ?? "";
                }
                catch (Exception ex)
                {
                    errMsg = $"{ex.GetType().Name}: {ex.Message}";
                }
                return new ExpressionCheckResult
                {
                    IsValid      = string.IsNullOrEmpty(errMsg),
                    ErrorMessage = errMsg
                };
            }
            catch (Exception ex)
            {
                return new ExpressionCheckResult { IsValid = false, ErrorMessage = ex.Message };
            }
        });
    }

    /// <inheritdoc/>
    public async Task<string> ExpandPathMacrosAsync(string path)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                // IEngine.ExpandPathMacros only resolves relative/workspace macros — it
                // does NOT expand the well-known <TestStand*> location macros. We expand
                // those ourselves via GetTestStandPath, then let ExpandPathMacros handle
                // anything that remains.
                var typedEngine =
                    (NationalInstruments.TestStand.Interop.API.IEngine)(object)_engine!;

                string p = path ?? "";

                // Longest macro names first so e.g. <TestStandPublic> is not partially
                // matched by <TestStand>.
                (string Macro, int PathId)[] macros =
                {
                    ("<TestStandGlobalCommonAppData>", 13),
                    ("<TestStandGlobalLocalAppData>",  14),
                    ("<TestStandCommonAppData>",         5),
                    ("<TestStandLocalAppData>",          6),
                    ("<TestStandApplicationData>",       5),
                    ("<TestStandPublicComponents>",      7),
                    ("<TestStandNIComponents>",          8),
                    ("<TestStandGlobalConfig>",         11),
                    ("<TestStandGlobalPublic>",         12),
                    ("<TestStandConfig>",                3),
                    ("<TestStandPublic>",                4),
                    ("<TestStandTemp>",                  9),
                    ("<TestStandBin>",                   2),
                    ("<TestStand>",                      1),
                };

                foreach (var (macro, id) in macros)
                {
                    int idx = p.IndexOf(macro, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    string expanded;
                    try { expanded = (string)((dynamic)_engine!).GetTestStandPath((object)id); }
                    catch { continue; }
                    p = p.Substring(0, idx) + expanded + p.Substring(idx + macro.Length);
                }

                try { typedEngine.ExpandPathMacros(ref p); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to expand path macros via typed engine interface."); }
                return p;
            }
            catch { return path; }
        });
    }

    /// <inheritdoc/>
    public async Task<string> FindFileAsync(string filename)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                // Engine.FindFile returns a bool (found?) and yields the resolved path via the
                // 'absolutePath' OUT parameter — NOT via the return value. Force the call fully
                // headless: PromptDisable suppresses the "locate file" dialog and
                // AddDirToSrchList_No suppresses the "add directory to the search list?" dialog.
                // Either dialog is modal and would block the MCP server (and the integration
                // tests) on a missing file; with both disabled a not-found file just returns "".
                bool found = _engine!.FindFile(
                    filename,
                    out string absolutePath,
                    out _,
                    NiFindFilePrompt.FindFile_PromptDisable,
                    NiFindFileSrchList.FindFile_AddDirToSrchList_No);
                return found ? absolutePath ?? "" : "";
            }
            catch { return ""; }
        });
    }

    /// <inheritdoc/>
    public async Task BreakAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.BreakAll());
    }

    /// <inheritdoc/>
    public async Task AbortAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.AbortAll());
    }

    /// <inheritdoc/>
    public async Task TerminateAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.TerminateAll());
    }

    /// <inheritdoc/>
    public async Task<StationOptionsInfo> GetStationOptionsAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            return new StationOptionsInfo
            {
                TracingEnabled             = GetEngineProperty<bool>("TracingEnabled"),
                BreakpointsEnabled         = GetEngineProperty<bool>("BreakpointsEnabled"),
                DisableResults             = GetEngineProperty<bool>("DisableResults"),
                AlwaysGotoCleanupOnFailure = GetEngineProperty<bool>("AlwaysGotoCleanupOnFailure"),
                BreakOnRte                 = GetEngineProperty<bool>("BreakOnRTE"),
                StationId                  = GetEngineProperty<string>("StationID") ?? "",
                ProcessModelPath           = GetEngineProperty<string>("StationModelSequenceFilePath") ?? ""
            };
        });
    }

    /// <inheritdoc/>
    public async Task SetStationOptionsAsync(StationOptionsInfo options)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            try { _engine!.TracingEnabled             = options.TracingEnabled;             } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine TracingEnabled."); }
            try { _engine!.BreakpointsEnabled         = options.BreakpointsEnabled;         } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine BreakpointsEnabled."); }
            try { _engine!.DisableResults             = options.DisableResults;             } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine DisableResults."); }
            try { _engine!.AlwaysGotoCleanupOnFailure = options.AlwaysGotoCleanupOnFailure; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine AlwaysGotoCleanupOnFailure."); }
            try { _engine!.BreakOnRTE                 = options.BreakOnRte;                 } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine BreakOnRTE."); }
            if (!string.IsNullOrEmpty(options.StationId))
                try { _engine!.StationID = options.StationId; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine StationID."); }
            if (!string.IsNullOrEmpty(options.ProcessModelPath))
                try { _engine!.StationModelSequenceFilePath = options.ProcessModelPath; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set engine StationModelSequenceFilePath."); }
        });
    }

    // ── Execution Debug Control ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task BreakExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.Break();
        });
    }

    /// <inheritdoc/>
    public async Task ResumeExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.Resume();
        });
    }

    /// <inheritdoc/>
    public async Task AbortExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.Abort();
            _activeExecutions.Remove(executionId);
        });
    }

    /// <inheritdoc/>
    public async Task RestartExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            // Execution.Restart takes a required `breakOnEntry` bool. Calling it arg-less (or via the
            // dynamic binder) raises TargetParameterCountException — use the typed 1-arg overload.
            ((NiExecution)exec).Restart(false);
            _executionStartTimes[executionId] = DateTime.UtcNow;
        });
    }

    /// <inheritdoc/>
    public async Task StepOverAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.StepOver();
        });
    }

    /// <inheritdoc/>
    public async Task StepIntoAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.StepInto();
        });
    }

    /// <inheritdoc/>
    public async Task StepOutAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.StepOut();
        });
    }

    // ── Sequence File Operations ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task DeleteSequenceAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);

            // Locate the sequence by index: TestStand's SequenceFile.RemoveSequence
            // expects an integer index (DISP_E_TYPEMISMATCH was returned when
            // passing the name string or a Sequence object).
            int numSeqs = Convert.ToInt32((object)sf.NumSequences);
            int idx = -1;
            for (int i = 0; i < numSeqs; i++)
            {
                dynamic s = sf.GetSequence(i);
                string sn = (string)s.Name;
                if (string.Equals(sn, sequenceName, StringComparison.Ordinal))
                { idx = i; break; }
            }
            if (idx < 0)
                throw new KeyNotFoundException(
                    $"Sequence '{sequenceName}' not found in {filePath}.");

            // Dispatch via reflection so the COM type-library overload that
            // accepts an integer index gets resolved correctly.
            object sfObj = (object)sf;
            var sfType   = sfObj.GetType();
            Exception? lastEx = null;
            bool removed = false;
            foreach (var arg in new object[] { idx, (long)idx, (short)idx })
            {
                try
                {
                    sfType.InvokeMember(
                        "RemoveSequence",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null, sfObj, new[] { arg });
                    removed = true;
                    break;
                }
                catch (System.Reflection.TargetInvocationException tex)
                { lastEx = tex.InnerException ?? tex; }
                catch (Exception ex) { lastEx = ex; }
            }
            if (!removed)
                throw new InvalidOperationException(
                    $"RemoveSequence failed for '{sequenceName}' (idx={idx}). " +
                    $"Last error: {lastEx?.GetType().Name}: {lastEx?.Message}",
                    lastEx);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<bool> SequenceNameExistsAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);
            try { return (bool)sf.SequenceNameExists(sequenceName); }
            catch
            {
                // Fallback: try to get the sequence by name
                try { var _ = sf.GetSequenceByName(sequenceName); return true; }
                catch { return false; }
            }
        });
    }

    /// <inheritdoc/>
    public async Task RenameSequenceAsync(string filePath, string oldName, string newName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(oldName);
            seq.Name = newName;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    // ── Sequence Operations ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task DeleteStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            // NOTE: Sequence.DeleteStep/RemoveStep expect a numeric step index, NOT a Step
            // object (passing the object raises "Could not convert argument 1 ...").
            // Resolve the index by name within the step group, then delete by index.
            int numSteps = (int)seq.GetNumSteps((object)sgVal);
            int idx = -1;
            for (int i = 0; i < numSteps; i++)
            {
                var s = seq.GetStep(i, (object)sgVal);
                if (string.Equals((string)s.Name, stepName, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
                throw new InvalidOperationException(
                    $"Step '{stepName}' not found in sequence '{sequenceName}' [{stepGroup}].");
            seq.DeleteStep(idx, (object)sgVal);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task MoveStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, int newIndex)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // RemoveStep takes a numeric index (not a Step object). Resolve the current
            // index by name, detach the step, then re-insert it at the target position.
            int curIdx = (int)seq.GetStepIndex(stepName, (object)sgVal);
            seq.RemoveStep(curIdx, (object)sgVal);
            seq.InsertStep(step, newIndex, (object)sgVal);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<bool> StepNameExistsAsync(string filePath, string sequenceName,
        string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            try { return (bool)seq.StepNameExists(stepName); }
            catch
            {
                try { FindStepInAllGroups(seq, stepName); return true; }
                catch { return false; }
            }
        });
    }

    /// <inheritdoc/>
    public async Task<List<ParameterInfo>> GetSequenceParametersAsync(string filePath,
        string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapParameters(seq.Parameters);
        });
    }

    /// <inheritdoc/>
    public async Task InsertSequenceParameterAsync(string filePath, string sequenceName,
        string paramName, string dataType, string direction = "Input",
        string? defaultValue = null, bool? passByReference = null,
        string? representation = null, string? numberFormat = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            // Builtins map to their PropertyValueType; anything else is treated as a NAMED type
            // (PropValType_NamedType=4) — e.g. an enum or custom data type defined in the file.
            // Mirror InsertLocalVariableAsync so enum / reference / container / array parameters
            // are created with their REAL type instead of silently falling back to String.
            string rawType = dataType.Trim();
            bool   isArray = rawType.EndsWith("[]") ||
                             rawType.StartsWith("array:", StringComparison.OrdinalIgnoreCase);
            string baseDataType = rawType;
            if (baseDataType.EndsWith("[]")) baseDataType = baseDataType[..^2].Trim();
            if (baseDataType.StartsWith("array:", StringComparison.OrdinalIgnoreCase))
                baseDataType = baseDataType.Substring("array:".Length).Trim();

            int propType; string typeNameParam = "";
            switch (baseDataType.ToLowerInvariant())
            {
                case "string":                                          propType = 1; break;
                case "boolean": case "bool":                            propType = 2; break;
                case "number": case "double": case "float":
                case "int":    case "integer":                          propType = 3; break;
                case "reference": case "object reference":
                case "objectreference": case "objref":
                    propType = (int)NiPropValueTypes.PropValType_Reference; break;
                case "container":
                    propType = (int)NiPropValueTypes.PropValType_Container; break;
                default:        propType = 4; typeNameParam = baseDataType; break;
            }

            // NewSubProperty(lookupString, valueType, asArray, typeName, options)
            seq.Parameters.NewSubProperty(paramName, (NiPropValueTypes)propType, isArray, typeNameParam, 0);

            // Pass-by-reference toggles PropFlags_PassByReference (4). The explicit
            // passByReference flag wins; when it is null, fall back to the legacy 'direction'
            // mapping (InOut/byref → by reference, Input/Output → by value).
            bool byRef = passByReference ?? (direction.ToLowerInvariant() switch
            {
                "inout" or "inputoutput" or "passbyreference" or "byref" => true,
                _ => false
            });
            if (byRef)
            {
                var propObj2 = (object)seq.Parameters.GetPropertyObject(paramName, 0);
                propObj2.GetType().InvokeMember("SetFlags", _comFlags, null, propObj2,
                    new object[] { "", 0, 4 /* PropFlags_PassByReference */ });
            }

            // A numeric REPRESENTATION (UInt64/Int64) has to be applied BEFORE the value: once the
            // property is wide, the plain SetValNumber that SetPropertyValueByType uses is rejected.
            // ApplyNumericRepresentation therefore also writes the value through the matching
            // width-specific setter (and accepts a 0x… literal).
            bool repApplied = false;
            if (!string.IsNullOrWhiteSpace(representation) || numberFormat != null)
            {
                var target = (NiPropertyObject)(object)seq.Parameters.GetPropertyObject(paramName, 0);
                // For an ARRAY the representation lives on the element prototype AND on the array
                // itself; apply to both so elements come out wide too.
                ApplyNumericRepresentation(target, representation, numberFormat, isArray ? null : defaultValue);
                repApplied = !isArray && !string.IsNullOrWhiteSpace(representation) && defaultValue != null;
            }
            if (defaultValue != null && !repApplied)
                SetPropertyValueByType(seq.Parameters, paramName, defaultValue, propType);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task DeleteLocalVariableAsync(string filePath, string sequenceName,
        string variableName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            bool inLocals = false;
            try { seq.Locals.GetPropertyObject(variableName, 0); inLocals = true; } catch (Exception ex) { _logger.LogDebug(ex, "Variable '{Variable}' not found in Locals — will try Parameters.", variableName); }

            if (inLocals)
                seq.Locals.DeleteSubProperty(variableName, 0);
            else
                seq.Parameters.DeleteSubProperty(variableName, 0);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<List<StepTemplateInfo>> GetStepTemplatesAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            // Templates are stored globally in Engine.GetTemplatesFile (Templates.ini)
            // Structure: Data.Root = array of categories
            //   Root[0] = Steps,  Root[1] = Variables,  Root[2] = Sequences
            //   Root[0][i] = individual step templates
            var result = new List<StepTemplateInfo>();
            try
            {
                var engObj  = (object)_engine!;
                var engType = engObj.GetType();

                // GetTemplatesFile(options=1 = LoadIfNotLoaded)
                var tf = engType.InvokeMember("GetTemplatesFile",
                    _comFlags, null, engObj, new object[] { 1 });
                if (tf == null) return result;

                var tfObj  = (object)tf;
                var tfType = tfObj.GetType();

                var dataObj  = tfType.InvokeMember("Data", _comFlags, null, tfObj, null);
                var dataType = dataObj!.GetType();

                // Enumerate step templates from the Steps category (Root[0])
                for (int i = 0; i < 10000; i++)
                {
                    object? item;
                    try
                    {
                        item = dataType.InvokeMember("GetPropertyObject",
                            _comFlags, null, dataObj, new object[] { $"Root[0][{i}]", 0 });
                    }
                    catch { break; } // "Array index out of bounds" → end of list

                    if (item == null) break;

                    var iType = item.GetType();
                    string name = "";
                    string stepType = "";
                    string desc = "";

                    try { name = iType.InvokeMember("Name", _comFlags, null, item, null)?.ToString() ?? ""; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read template Name at index {Index}.", i); }

                    // StepType: step.StepType is an object; get its Name property
                    try
                    {
                        var stObj = iType.InvokeMember("StepType", _comFlags, null, item, null);
                        if (stObj != null)
                            stepType = stObj.GetType().InvokeMember("Name", _comFlags, null, stObj, null)?.ToString() ?? "";
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType for template '{Name}'.", name); }

                    try { desc = iType.InvokeMember("Description", _comFlags, null, item, null)?.ToString() ?? ""; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Description for template '{Name}'.", name); }
                    if (string.IsNullOrEmpty(desc))
                    {
                        try { desc = Convert.ToString(iType.InvokeMember("GetValString",
                            _comFlags, null, item, new object[] { "TS.Description", 0 })) ?? ""; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read TS.Description for template '{Name}'.", name); }
                    }

                    result.Add(new StepTemplateInfo { Name = name, StepType = stepType, Description = desc });
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate step templates from templates file."); }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task InsertStepFromTemplateAsync(string filePath, string sequenceName,
        string stepGroup, string templateName, string newStepName, int index = -1)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            try
            {
            var engObj  = (object)_engine!;
            var engType = engObj.GetType();

            // Load the global Templates.ini
            var tf = engType.InvokeMember("GetTemplatesFile",
                _comFlags, null, engObj, new object[] { 1 })
                ?? throw new InvalidOperationException("GetTemplatesFile returned null.");

            var tfObj   = (object)tf;
            var dataObj = tfObj.GetType().InvokeMember("Data", _comFlags, null, tfObj, null)
                          ?? throw new InvalidOperationException("Templates Data is null.");
            var dataType = dataObj.GetType();

            // Find the template by name in Root[0] (Steps category)
            object? templateStep = null;
            for (int i = 0; i < 10000; i++)
            {
                object? item;
                try
                {
                    item = dataType.InvokeMember("GetPropertyObject",
                        _comFlags, null, dataObj, new object[] { $"Root[0][{i}]", 0 });
                }
                catch { break; }

                if (item == null) break;
                string iName = item.GetType().InvokeMember("Name", _comFlags, null, item, null)?.ToString() ?? "";
                if (iName == templateName) { templateStep = item; break; }
            }

            if (templateStep == null)
                throw new InvalidOperationException($"Step template '{templateName}' not found.");

            // Clone the template step to get an independent copy
            // Clone(lookupString, options) — pass "" and 0 for a standard deep copy
            var clone = templateStep.GetType().InvokeMember("Clone",
                _comFlags, null, templateStep, new object[] { "", 0 })
                ?? throw new InvalidOperationException("Clone() returned null.");

            // Rename the clone
            clone.GetType().InvokeMember("Name",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, clone, new object[] { newStepName });

            // Insert into the target sequence
            var sf     = GetOrLoadSeqFile(filePath);
            var seq    = sf.GetSequenceByName(sequenceName);
            var seqObj = (object)seq;

            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "cleanup" => 2,
                _         => 1
            };

            int insertAt = index < 0
                ? Convert.ToInt32(seqObj.GetType().InvokeMember("GetNumSteps",
                    _comFlags, null, seqObj, new object[] { (object)sgValue }))
                : index;

            seqObj.GetType().InvokeMember("InsertStep",
                _comFlags, null, seqObj, new object[] { clone, insertAt, (object)sgValue });

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException?.Message ?? ex.Message;
                _logger.LogError(ex, "InsertStepFromTemplate failed: {Message}", msg);
                throw new InvalidOperationException(msg, ex);
            }
        });
    }

    /// <inheritdoc/>
    public async Task<SequenceProperties> GetSequencePropertiesAsync(string filePath,
        string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            var props = new SequenceProperties();
            try { props.Name                     = (string)seq.Name;                     } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence Name in GetSequencePropertiesAsync."); }
            try { props.Type                     = (string)seq.SequenceType.ToString();  } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence SequenceType."); }
            try { props.GotoCleanupOnFailure      = (bool)seq.GotoCleanupOnFailure;       } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence GotoCleanupOnFailure."); }
            try { props.DisableResults            = (bool)seq.DisableResults;             } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence DisableResults."); }
            try
            {
                int fa = (int)seq.FailureAction;
                props.FailureAction = fa switch { 0 => "Continue", 1 => "Terminate", 2 => "Abort", _ => fa.ToString() };
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence FailureAction."); }
            try { props.EntryPointNameExpression  = (string)seq.EntryPointNameExpression; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence EntryPointNameExpression."); }
            try { props.ShowEntryPointForAllWindows = (bool)seq.ShowEntryPointForAllWindows; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence ShowEntryPointForAllWindows."); }
            string? seqDesc = null;
            // TestStand stores sequence comments as "Comment" (not "Description")
            try { seqDesc = (string)seq.Comment; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence Comment in GetSequencePropertiesAsync."); }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)((NiSequence)(object)seq).AsPropertyObject().GetValString("TS.Comment", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read TS.Comment from sequence property bag in GetSequencePropertiesAsync."); }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)((NiSequence)(object)seq).AsPropertyObject().GetValString("Comment", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Comment from sequence property bag in GetSequencePropertiesAsync."); }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)seq.Description; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence Description in GetSequencePropertiesAsync."); }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)((NiSequence)(object)seq).AsPropertyObject().GetValString("TS.Description", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read TS.Description from sequence property bag in GetSequencePropertiesAsync."); }
            if (!string.IsNullOrEmpty(seqDesc)) props.Description = seqDesc;
            return props;
        });
    }

    /// <inheritdoc/>
    public async Task SetSequencePropertiesAsync(string filePath, string sequenceName,
        SequenceProperties props)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            if (!string.IsNullOrEmpty(props.Name) && props.Name != sequenceName)
                try { seq.Name = props.Name; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to rename sequence to '{Name}'.", props.Name); }
            try { seq.GotoCleanupOnFailure = props.GotoCleanupOnFailure; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence GotoCleanupOnFailure."); }
            try { seq.DisableResults       = props.DisableResults;       } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence DisableResults."); }
            if (!string.IsNullOrEmpty(props.FailureAction))
            {
                int fa = props.FailureAction.ToLowerInvariant() switch
                { "terminate" => 1, "abort" => 2, _ => 0 };
                try { seq.FailureAction = (object)fa; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence FailureAction."); }
            }
            if (!string.IsNullOrEmpty(props.EntryPointNameExpression))
                try { seq.EntryPointNameExpression = props.EntryPointNameExpression; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence EntryPointNameExpression."); }
            if (!string.IsNullOrEmpty(props.Description))
            {
                bool descSet = false;
                // TestStand uses "Comment" as the sequence comment property
                try { seq.Comment = props.Description; descSet = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence Comment property."); }
                if (!descSet)
                    try { ((NiSequence)(object)seq).AsPropertyObject().SetValString("TS.Comment", 0, props.Description); descSet = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set TS.Comment on sequence property bag."); }
                if (!descSet)
                    try { ((NiSequence)(object)seq).AsPropertyObject().SetValString("Comment", 0, props.Description); descSet = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set Comment on sequence property bag."); }
                if (!descSet)
                    try { seq.Description = props.Description; descSet = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set sequence Description property."); }
                if (!descSet)
                    try { ((NiSequence)(object)seq).AsPropertyObject().SetValString("TS.Description", 0, props.Description); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set TS.Description on sequence property bag."); }
            }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<List<VariableInfo>> GetLocalVariablesAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapVariables(seq.Locals);
        });
    }

    /// <inheritdoc/>
    public async Task<ReferenceAuditData> ReadReferenceAuditDataAsync(string filePath, string? sequenceName = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var data = new ReferenceAuditData();

            // File globals are file-level (shared by every sequence).
            try { foreach (var v in MapVariables(GetFileGlobals(sf))) data.FileGlobals.Add(v.Name); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read file globals for reference audit."); }

            // Which sequences to audit: the named one, or every sequence in the file.
            var seqNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(sequenceName))
                seqNames.Add(sequenceName!);
            else
            {
                int n = 0;
                try { n = Convert.ToInt32((object)sf.NumSequences); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read NumSequences for reference audit."); }
                for (int i = 0; i < n; i++)
                {
                    try { seqNames.Add((string)sf.GetSequence(i).Name); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence name at index {Index}.", i); }
                }
            }

            string[] groupNames = { "Setup", "Main", "Cleanup" };
            foreach (var sn in seqNames)
            {
                var seq = sf.GetSequenceByName(sn);

                var scope = new DeclaredScope { SequenceName = sn };
                try { foreach (var v in MapVariables(seq.Locals))     scope.Locals.Add(v.Name); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read locals of '{Seq}'.", sn); }
                try { foreach (var p in MapParameters(seq.Parameters)) scope.Parameters.Add(p.Name); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read parameters of '{Seq}'.", sn); }
                data.Scopes.Add(scope);

                for (int g = 0; g <= 2; g++)
                {
                    int count;
                    try { count = Convert.ToInt32((object)seq.GetNumSteps((NiStepGroups)g)); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed GetNumSteps group {Group} of '{Seq}'.", g, sn); continue; }

                    for (int i = 0; i < count; i++)
                    {
                        object step;
                        try { step = seq.GetStep(i, (NiStepGroups)g); }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed GetStep {Index} group {Group} of '{Seq}'.", i, g, sn); continue; }

                        string stepName = "";
                        try { stepName = (string)((NiStep)step).Name; } catch { /* best-effort */ }

                        NiPropertyObject po = ((NiStep)step).AsPropertyObject();
                        string grpName = groupNames[g];

                        void Collect(string prop, string? value)
                        {
                            if (!string.IsNullOrWhiteSpace(value))
                                data.Expressions.Add(new ExpressionEntry
                                {
                                    SequenceName = sn,
                                    StepGroup    = grpName,
                                    StepName     = stepName,
                                    Property     = prop,
                                    Expression   = value!
                                });
                        }

                        // Statement actions + Pre/Post/Status conditions live on these step properties;
                        // branch conditions live in ConditionExpr (If/ElseIf/While/DoWhile) / ItemExpr
                        // (Select/Case) — present only on flow steps, hence the per-read try/catch.
                        try { Collect("PreExpression",    (string)((NiStep)step).PreExpression); }    catch { }
                        try { Collect("PostExpression",   (string)((NiStep)step).PostExpression); }   catch { }
                        try { Collect("StatusExpression", (string)((NiStep)step).StatusExpression); } catch { }
                        try { Collect("ConditionExpr",    (string)po.GetValString("ConditionExpr", 0)); } catch { }
                        try { Collect("ItemExpr",         (string)po.GetValString("ItemExpr", 0)); }      catch { }
                    }
                }
            }
            return data;
        });
    }

    // ── Step Property Operations ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RenameStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string newName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            step.Name = newName;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<string> SetStepCommentAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string comment)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            var errors = new System.Text.StringBuilder();
            string method = "";
            // 1. step.Comment — the native COM Comment property on Step objects.
            try { step.Comment = comment; method = "step.Comment"; }
            catch (Exception ex) { errors.Append($"[step.Comment: {ex.Message}] "); }
            if (method == "")
            {
                // 2. PropertyObject.Comment — same field via the property-bag interface.
                try { ((NiStep)(object)step).AsPropertyObject().Comment = comment; method = "po.Comment"; }
                catch (Exception ex) { errors.Append($"[po.Comment: {ex.Message}] "); }
            }
            if (method == "")
                throw new InvalidOperationException($"Could not set step comment. Attempts: {errors}");
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            return method;
        });
    }

    /// <inheritdoc/>
    public async Task SetStepRunModeAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string runMode)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // RunModes constants: Normal="Normal", Skip="Skip", ForcePass="Pass", ForceFail="Fail"
            string modeStr = runMode.ToLowerInvariant() switch
            {
                "skip"        => "Skip",
                "pass"        => "Pass",
                "forcedpass"  => "Pass",
                "forced pass" => "Pass",
                "force pass"  => "Pass",
                "fail"        => "Fail",
                "forcedfail"  => "Fail",
                "forced fail" => "Fail",
                "force fail"  => "Fail",
                _             => "Normal"
            };
            step.SetRunModeEx(modeStr, System.Type.Missing);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepPreconditionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string precondition)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            step.Precondition = precondition;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepPassActionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string passAction, string? target = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // PostActionValues: Next, Break, Terminate, Goto, Cback
            string actionVal = MapPostAction(passAction);
            step.PassAction = actionVal;
            if (!string.IsNullOrEmpty(target) && actionVal == "Goto")
                try { step.PassActionTarget = target; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set PassActionTarget on step '{Step}'.", stepName); }
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepFailActionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string failAction, string? target = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            string actionVal = MapPostAction(failAction);
            step.FailAction = actionVal;
            if (!string.IsNullOrEmpty(target) && actionVal == "Goto")
                try { step.FailActionTarget = target; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set FailActionTarget on step '{Step}'.", stepName); }
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepLoopAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string loopType,
        string? initExpr = null, string? whileExpr = null, string? incExpr = null,
        string? statusExpr = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // StepLoopTypes: NoLooping, FixedNumLoops, PassFailCount, Custom.
            // Accepts the strings advertised by the set_step_loop schema
            // ('NoLoop','While','For','Condition') plus their natural aliases.
            // 'While'/'Condition' are condition-driven step loops, which TestStand
            // models as the Custom loop type (driven by LoopWhileExpression) — there
            // is no native 'While' StepLoopType. Without these cases the documented
            // 'While'/'Condition'/'NoLoop' strings silently fell through to NoLooping.
            string loopVal = loopType.ToLowerInvariant() switch
            {
                "noloop"         => "NoLooping",
                "nolooping"      => "NoLooping",
                "none"           => "NoLooping",
                "fixednumloops"  => "FixedNumLoops",
                "fixed"          => "FixedNumLoops",
                "for"            => "FixedNumLoops",
                "passfailcount"  => "PassFailCount",
                "passorfail"     => "PassFailCount",
                "while"          => "Custom",
                "condition"      => "Custom",
                "custom"         => "Custom",
                _                => "NoLooping"
            };
            step.LoopType = loopVal;
            if (!string.IsNullOrEmpty(initExpr))
                try { step.LoopInitExpression  = initExpr;  } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set LoopInitExpression on step '{Step}'.", stepName); }
            if (!string.IsNullOrEmpty(whileExpr))
                try { step.LoopWhileExpression = whileExpr; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set LoopWhileExpression on step '{Step}'.", stepName); }
            if (!string.IsNullOrEmpty(incExpr))
                try { step.LoopIncExpression   = incExpr;   } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set LoopIncExpression on step '{Step}'.", stepName); }
            // A 'Custom' step loop also has a status expression (TS.LoopStatus, e.g.
            // 'RunState.LoopNumPassed / RunState.LoopNumIterations < 1 ? "Failed" : "Passed"').
            // The .NET Step wrapper exposes it as LoopStatusExpression on most versions; fall back
            // to the raw property path so a Custom loop can be reproduced 1:1.
            if (!string.IsNullOrEmpty(statusExpr))
            {
                try { step.LoopStatusExpression = statusExpr; }
                catch
                {
                    try { ((NiStep)(object)step).AsPropertyObject().SetValString("TS.LoopStatus", 0, statusExpr); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to set LoopStatus on step '{Step}'.", stepName); }
                }
            }
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetFlowConditionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string condition, bool? isDefault = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            string stepType = "";
            try { stepType = (string)step.StepType.Name; }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType.Name for flow condition on '{Step}'.", stepName); }

            // Guard: a flow condition only has an effect on a branch step (If/ElseIf/While/DoWhile/
            // Select/Case). Writing it to an NI_Flow_End — a common DoWhile mistake — is silently
            // dropped: the loop condition belongs on the NI_Flow_DoWhile opener, not its End. Only
            // enforce when we could actually read the step type (empty ⇒ fall through as before).
            if (!string.IsNullOrEmpty(stepType))
            {
                var reject = InputGuards.DescribeInvalidFlowConditionTarget(stepName, stepType);
                if (reject != null) throw new ArgumentException(reject);
            }

            // The branch condition's home is a DEDICATED step property — NOT Pre/Post/Status:
            //   NI_Flow_If / ElseIf / While / DoWhile -> ConditionExpr (the boolean condition)
            //   NI_Flow_Select                        -> ItemExpr (the switch expression)
            //   NI_Flow_Case                          -> ItemExpr (the case value(s), e.g. "A","B")
            bool isSelectOrCase =
                stepType.IndexOf("Select", StringComparison.OrdinalIgnoreCase) >= 0 ||
                stepType.IndexOf("Case",   StringComparison.OrdinalIgnoreCase) >= 0;
            string propName = isSelectOrCase ? "ItemExpr" : "ConditionExpr";

            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
            stepPo.SetValString(propName, 0, condition ?? "");

            // Mark/clear default case when requested (NI_Flow_Case.IsDefault).
            if (isDefault.HasValue)
                try { stepPo.SetValBoolean("IsDefault", 0, isDefault.Value); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to set IsDefault on Case step '{Step}'.", stepName); }

            // Migrate the legacy mis-placement: if the same expression was previously written to
            // the Post Expression (the only slot the generic 'expression' field could reach), clear
            // it so the condition is not also evaluated-and-discarded after the step.
            try
            {
                string curPost = (string)step.PostExpression;
                if (!string.IsNullOrEmpty(curPost) && curPost == condition)
                    step.PostExpression = "";
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to clear duplicate PostExpression on '{Step}'.", stepName); }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task<ForLoopConfigResult> ConfigureForLoopAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, int? count = null,
        string? indexVar = null, string? initExpr = null, string? conditionExpr = null,
        string? incrementExpr = null, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            // Guard: only a counted NI_Flow_For carries InitializationExpr/ConditionExpr/IncrementExpr.
            string stepType = "";
            try { stepType = (string)step.StepType.Name; }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType.Name for for-loop config on '{Step}'.", stepName); }
            if (!string.IsNullOrEmpty(stepType) && !InputGuards.IsCountedForLoop(stepType))
                throw new ArgumentException(
                    $"Step '{stepName}' is '{stepType}' — configure_for_loop only applies to an NI_Flow_For step.");

            var result = new ForLoopConfigResult { StepName = stepName };

            // Counted-loop convenience: derive the standard 0..count-1 loop from count + index var.
            // Explicit expressions always take precedence over the generated ones.
            string iv = string.IsNullOrWhiteSpace(indexVar) ? "Locals.i" : indexVar!.Trim();
            if (count.HasValue)
            {
                initExpr      ??= $"{iv} = 0";
                conditionExpr ??= $"{iv} < {count.Value}";
                incrementExpr ??= $"{iv} += 1";
            }

            if (string.IsNullOrEmpty(initExpr) && string.IsNullOrEmpty(conditionExpr) &&
                string.IsNullOrEmpty(incrementExpr))
                throw new ArgumentException(
                    "configure_for_loop needs either 'count' (with optional 'index_var') or at least one " +
                    "of 'init_expr' / 'condition_expr' / 'increment_expr'.");

            var po = ((NiStep)(object)step).AsPropertyObject();
            if (initExpr      != null) po.SetValString("InitializationExpr", 0, initExpr);
            if (conditionExpr != null) po.SetValString("ConditionExpr",      0, conditionExpr);
            if (incrementExpr != null) po.SetValString("IncrementExpr",      0, incrementExpr);

            // Report the effective values by reading them back.
            try { result.InitializationExpr = (string)po.GetValString("InitializationExpr", 0); } catch { result.InitializationExpr = initExpr ?? ""; }
            try { result.ConditionExpr      = (string)po.GetValString("ConditionExpr",      0); } catch { result.ConditionExpr      = conditionExpr ?? ""; }
            try { result.IncrementExpr      = (string)po.GetValString("IncrementExpr",      0); } catch { result.IncrementExpr      = incrementExpr ?? ""; }

            // The loop index variable is NOT created here — remind the caller to declare it.
            if (count.HasValue && iv.StartsWith("Locals.", StringComparison.OrdinalIgnoreCase))
                result.Notes.Add(
                    $"Ensure the index variable '{iv}' is declared (insert_local_variable, type number) — " +
                    "configure_for_loop does not create it.");

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<ForEachLoopConfigResult> ConfigureForEachLoopAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string? arrayExpr = null,
        string? elementExpr = null, string? offsetExpr = null, string? subscriptExpr = null,
        bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            string stepType = "";
            try { stepType = (string)step.StepType.Name; }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType.Name for foreach config on '{Step}'.", stepName); }
            if (!string.IsNullOrEmpty(stepType) && !InputGuards.IsForEachLoop(stepType))
                throw new ArgumentException(
                    $"Step '{stepName}' is '{stepType}' — configure_foreach_loop only applies to an NI_Flow_ForEach step.");

            if (string.IsNullOrEmpty(arrayExpr) && string.IsNullOrEmpty(elementExpr) &&
                string.IsNullOrEmpty(offsetExpr) && string.IsNullOrEmpty(subscriptExpr))
                throw new ArgumentException(
                    "configure_foreach_loop needs at least 'array_expr' (the collection to iterate). " +
                    "'element_expr' (the per-element variable) is recommended.");

            var po = ((NiStep)(object)step).AsPropertyObject();
            if (arrayExpr     != null) po.SetValString("ArrayExpr",        0, arrayExpr);
            if (elementExpr   != null) po.SetValString("ArrayElementExpr", 0, elementExpr);
            if (offsetExpr    != null) po.SetValString("OffsetExpr",       0, offsetExpr);
            if (subscriptExpr != null) po.SetValString("SubscriptExpr",    0, subscriptExpr);

            var result = new ForEachLoopConfigResult { StepName = stepName };
            try { result.ArrayExpr     = (string)po.GetValString("ArrayExpr",        0); } catch { result.ArrayExpr     = arrayExpr ?? ""; }
            try { result.ElementExpr   = (string)po.GetValString("ArrayElementExpr", 0); } catch { result.ElementExpr   = elementExpr ?? ""; }
            try { result.OffsetExpr    = (string)po.GetValString("OffsetExpr",       0); } catch { result.OffsetExpr    = offsetExpr ?? ""; }
            try { result.SubscriptExpr = (string)po.GetValString("SubscriptExpr",    0); } catch { result.SubscriptExpr = subscriptExpr ?? ""; }

            if (!string.IsNullOrEmpty(elementExpr) &&
                elementExpr.StartsWith("Locals.", StringComparison.OrdinalIgnoreCase))
                result.Notes.Add(
                    $"Ensure the element variable '{elementExpr}' is declared (insert_local_variable) with a type " +
                    "matching the array's element type — configure_foreach_loop does not create it.");

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task SetStepRecordResultAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string recordingOption)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // ResultRecordingOptions enum (Step.ResultRecordingOption property):
            //   0 = Disabled
            //   1 = Enabled
            //   2 = EnabledAndOverrideSequenceSetting
            int optVal = recordingOption.ToLowerInvariant() switch
            {
                "disabled"                          => 0,
                "enabled"                           => 1,
                "enabledoverride"                   => 2,
                "enabled_override"                  => 2,
                "enabledandoverridesequencesetting"  => 2,
                _                                   => 1   // default: Enabled
            };
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // Use the typed Step interface to set the enum property correctly
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ResultRecordingOption =
                (NationalInstruments.TestStand.Interop.API.ResultRecordingOptions)optVal;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepEvalPrecondAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string option)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // EvalPrecondOptions: 0=UseStationOption, 1=EvaluatePrecond, 2=NoEvaluatePrecond
            int optVal = option.ToLowerInvariant() switch
            {
                "usestationoption"    => 0,
                "use_station_option"  => 0,
                "evaluateprecond"     => 1,
                "evaluate_precond"    => 1,
                "noevaluateprecond"   => 2,
                "no_evaluate_precond" => 2,
                _                    => 0   // default: UseStationOption
            };
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.EvalPrecondForInteractiveExecution =
                (NationalInstruments.TestStand.Interop.API.EvalPrecondOptions)optVal;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepModuleLoadOptionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string option)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // ModuleLoadOptions: 1=PreloadWhenOpened, 2=PreloadWhenExecuted,
            //                    3=DynamicLoad, 4=UseStepLoadOption
            int optVal = option.ToLowerInvariant() switch
            {
                "preloadwhenopened"    => 1,
                "preload_when_opened"  => 1,
                "preloadwhenexecuted"  => 2,
                "preload_when_executed"=> 2,
                "dynamicload"          => 3,
                "dynamic_load"         => 3,
                "usesteploadoption"    => 4,
                "use_step_load_option" => 4,
                _                     => 4   // default: UseStepLoadOption
            };
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ModuleLoadOption =
                (NationalInstruments.TestStand.Interop.API.ModuleLoadOptions)optVal;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepModuleUnloadOptionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string option)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // ModuleUnloadOptions: 1=OnPreconditionFailure, 2=AfterStepExecution,
            //                      3=AfterSequenceExecution, 4=WithSequenceFile,
            //                      5=UseStepUnloadOption
            int optVal = option.ToLowerInvariant() switch
            {
                "onpreconditionfailure"    => 1,
                "on_precondition_failure"  => 1,
                "afterstepexecution"       => 2,
                "after_step_execution"     => 2,
                "aftersequenceexecution"   => 3,
                "after_sequence_execution" => 3,
                "withsequencefile"         => 4,
                "with_sequence_file"       => 4,
                "usestepunloadoption"      => 5,
                "use_step_unload_option"   => 5,
                _                         => 5   // default: UseStepUnloadOption
            };
            // Guard: UseStepUnloadOption (5) is only valid at the sequence-file / model level.
            // TestStand rejects it on an individual step with an opaque COM error — turn that into
            // a clear, actionable message instead of letting the raw rejection surface.
            if (InputGuards.IsFileLevelOnlyUnloadOption(optVal))
                throw new ArgumentException(
                    "'UseStepUnloadOption' (5) is only valid at the sequence-file / model level — " +
                    "TestStand rejects it on an individual step. Use one of 'OnPreconditionFailure', " +
                    "'AfterStepExecution', 'AfterSequenceExecution' or 'WithSequenceFile' (1-4) for a " +
                    "per-step unload option.");
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ModuleUnloadOption =
                (NationalInstruments.TestStand.Interop.API.ModuleUnloadOptions)optVal;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task SetStepBatchSyncOptionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string option)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // BatchSynchronizationOptions: 0=UseSeqFileSetting, 1=UseModelSetting,
            //                              2=NoSync, 3=Serial, 4=Parallel, 5=OneThreadOnly
            int optVal = option.ToLowerInvariant() switch
            {
                "useseqfilesetting"     => 0,
                "use_seq_file_setting"  => 0,
                "usemodelsetting"       => 1,
                "use_model_setting"     => 1,
                "nosync"                => 2,
                "no_sync"               => 2,
                "serial"                => 3,
                "parallel"              => 4,
                "onethreadonly"         => 5,
                "one_thread_only"       => 5,
                _                      => 0   // default: UseSeqFileSetting
            };
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.BatchSyncOption =
                (NationalInstruments.TestStand.Interop.API.BatchSynchronizationOptions)optVal;
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    // Maps friendly adapter names (e.g. "None", ".NET", "LabVIEW") to TestStand
    // adapter KeyNames (e.g. "None Adapter", "DotNet Adapter"). Unknown names pass
    // through unchanged so an explicit KeyName still works.
    internal static string ResolveAdapterKeyName(string? name) => name?.ToLowerInvariant() switch
    {
        "labview" or "lv" or "g" or "vi"       => "G Std Prototype Adapter",
        "labview flex" or "g flex"             => "G Flexible VI Adapter",
        "cvi" or "c" or "c/cvi"                => "C/CVI Std Prototype Adapter",
        "cvi flex" or "c flex"                 => "C/CVI Flexible Prototype Adapter",
        "dll" or "c++" or "cpp" or
        "c/c++ dll" or "c++/dll" or "c++ dll"  => "DLL Flexible Prototype Adapter",
        "dotnet" or ".net"                     => "DotNet Adapter",
        "python"                               => "Python Adapter",
        "activex" or "com" or
        "activex/com" or "automation"          => "Automation Adapter",
        "none" or "<none>"                     => "None Adapter",
        "sequence adapter" or "sequence"       => "Sequence Adapter",
        _                                      => name ?? ""
    };

    // Resolve an adapter's friendly DisplayName (e.g. ".NET", "ActiveX/COM") from its
    // KeyName (e.g. "DotNet Adapter", "Automation Adapter") by scanning the loaded
    // adapters. Returns "" when the engine is unavailable or the key is not found.
    private string ResolveAdapterDisplayName(string keyName)
    {
        if (string.IsNullOrEmpty(keyName)) return "";
        try
        {
            int count = (int)_engine!.NumAdapters;
            for (int i = 0; i < count; i++)
            {
                dynamic adapter = _engine!.GetAdapter(i);
                if (string.Equals(TryGetString(adapter, "KeyName"), keyName,
                        StringComparison.OrdinalIgnoreCase))
                    return TryGetString(adapter, "DisplayName");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve adapter DisplayName for key '{Key}'.", keyName);
        }
        return "";
    }

    /// <inheritdoc/>
    public async Task ChangeStepAdapterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string newAdapter)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            // Step.ChangeAdapter takes the adapter KEY NAME string, not an Adapter object.
            step.ChangeAdapter((object)ResolveAdapterKeyName(newAdapter));
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
        });
    }

    /// <inheritdoc/>
    public async Task<string> GetStepUniqueIdAsync(string filePath, string sequenceName,
        string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);
            try { return (string)step.UniqueStepId; } catch { return ""; }
        });
    }

    // ── Report Operations ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SaveReportAsync(string executionId, string outputPath,
        string format = "HTML")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId);
            if (exec == null)
                throw new KeyNotFoundException($"Execution {executionId} not found.");
            try
            {
                // Get report from execution and save it
                dynamic report = exec.Report;
                int fmtVal = format.ToUpperInvariant() switch
                {
                    "XML"  => 1,
                    "TXT"  => 2,
                    "ATML" => 3,
                    _      => 0  // HTML
                };
                report.Format = (object)fmtVal;
                report.Save(outputPath);
                _logger.LogInformation("Report saved to {Path}", outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save report for execution {Id}", executionId);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task LaunchReportViewerAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId);
            if (exec == null)
                throw new KeyNotFoundException($"Execution {executionId} not found.");
            try
            {
                dynamic report = exec.Report;
                report.LaunchViewer();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch report viewer for execution {Id}", executionId);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<string> GetFullReportAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId);
            if (exec == null) return $"Execution {executionId} not found.";
            try
            {
                dynamic report = exec.Report;
                // Get_All returns the full report text
                var all = report.All;
                return all?.ToString() ?? "No report content available.";
            }
            catch
            {
                return $"Report not available for execution {executionId}.";
            }
        });
    }

    // ── User & Privilege Management ───────────────────────────────────────────

    // NOTE: These methods use the strongly-typed interop interfaces (vtable calls) rather
    // than `dynamic`. The DLR/IDispatch late-binding path intermittently throws
    // DISP_E_BADPARAMCOUNT ("TargetParameterCountException") on parameterless COM calls
    // such as AsPropertyObject() under cumulative load on a shared engine.

    /// <inheritdoc/>
    public async Task<List<UserInfo>> GetUsersAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<UserInfo>();
            NiPropertyObject userList = ((NiEngine)_engine!).UsersFile.UserList;
            int count = CountArrayElements(userList);

            for (int i = 0; i < count; i++)
            {
                try { result.Add(MapUser(userList.GetPropertyObjectByOffset(i, 0))); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to map user at index {Index}.", i); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                NiUser user = ((NiEngine)_engine!).CurrentUser;
                if (user == null) return (UserInfo?)null;
                return MapUser(user.AsPropertyObject());
            }
            catch { return null; }
        });
    }

    /// <inheritdoc/>
    public async Task<bool> UserNameExistsAsync(string loginName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try { return ((NiEngine)_engine!).UserNameExists(loginName); }
            catch { return false; }
        });
    }

    /// <inheritdoc/>
    public async Task CreateUserAsync(string loginName, string fullName,
        string password, string? profileName = null, bool persist = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var eng = (NiEngine)_engine!;
            if (string.IsNullOrWhiteSpace(loginName))
                throw new ArgumentException("loginName must not be empty.");
            if (eng.UserNameExists(loginName))
                throw new InvalidOperationException($"User '{loginName}' already exists.");

            NiUsersFile usersFile     = eng.UsersFile;
            NiPropertyObject userList = usersFile.UserList;

            // Engine.NewUser(profile) seeds the new user with the privileges of the given
            // user profile (e.g. "Administrator"); NewUser(null) yields minimal defaults.
            NiUser? profile = null;
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                profile = ResolveUserProfile(eng, profileName!)
                    ?? throw new ArgumentException(
                        $"User profile '{profileName}' not found. Available profiles: " +
                        string.Join(", ", EnumerateUserProfileNames(eng)));
            }

            NiUser newUser    = eng.NewUser(profile);
            newUser.LoginName = loginName;
            newUser.FullName  = fullName ?? "";
            if (!string.IsNullOrEmpty(password)) newUser.Password = password;

            // UserList is an array PropertyObject; its element count comes from offset
            // enumeration, NOT GetNumSubProperties (which returns 0 for arrays).
            int n = CountArrayElements(userList);
            userList.InsertElements(n, 1, 0);
            userList.SetPropertyObject($"[{n}]", 0, newUser.AsPropertyObject());

            if (persist) PersistUsersFile(usersFile);
        });
    }

    /// <inheritdoc/>
    public async Task DeleteUserAsync(string loginName, bool persist = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            NiUsersFile usersFile     = ((NiEngine)_engine!).UsersFile;
            NiPropertyObject userList = usersFile.UserList;
            int count = CountArrayElements(userList);

            for (int i = 0; i < count; i++)
            {
                NiPropertyObject user = userList.GetPropertyObjectByOffset(i, 0);
                string ln = "";
                try { ln = user.GetValString("LoginName", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read LoginName for user at index {Index}.", i); }
                if (string.Equals(ln, loginName, StringComparison.OrdinalIgnoreCase))
                {
                    userList.DeleteElements(i, 1, 0);
                    if (persist) PersistUsersFile(usersFile);
                    return;
                }
            }
            throw new KeyNotFoundException($"User '{loginName}' not found.");
        });
    }

    /// <inheritdoc/>
    public async Task SetUserPasswordAsync(string loginName, string password,
        bool persist = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            NiUser user = FindUser(loginName)
                ?? throw new KeyNotFoundException($"User '{loginName}' not found.");
            user.Password = password ?? "";
            if (persist) PersistUsersFile(((NiEngine)_engine!).UsersFile);
        });
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetUserPrivilegesAsync(string loginName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiUser user = FindUser(loginName)
                ?? throw new KeyNotFoundException($"User '{loginName}' not found.");
            var enabled = new List<string>();
            try { CollectEnabledPrivileges(user.Privileges, "", enabled); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to collect enabled privileges for user '{User}'.", loginName); }
            return enabled;
        });
    }

    /// <inheritdoc/>
    public async Task<bool> CheckUserPrivilegeAsync(string loginName, string privilege)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiUser user = FindUser(loginName)
                ?? throw new KeyNotFoundException($"User '{loginName}' not found.");
            try { return user.HasPrivilege(privilege); }
            catch { return false; }
        });
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetUserProfilesAsync()
    {
        EnsureConnected();
        return await Task.Run(() => EnumerateUserProfileNames((NiEngine)_engine!));
    }

    /// <summary>
    /// Resolve a user profile (privilege template) by name — exact match first, then
    /// case-insensitive against the UserProfileList. Returns null if no profile matches.
    /// </summary>
    private static NiUser? ResolveUserProfile(NiEngine eng, string profileName)
    {
        try { var p = eng.GetUserProfile(profileName); if (p != null) return p; } catch (Exception) { /* best-effort: exact-match profile lookup — intentionally ignored */ }
        foreach (var name in EnumerateUserProfileNames(eng))
        {
            if (string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase))
            {
                try { return eng.GetUserProfile(name); } catch (Exception) { /* best-effort: case-insensitive profile lookup — intentionally ignored */ }
            }
        }
        return null;
    }

    /// <summary>
    /// Login names of the user profiles defined in the users file (e.g. Administrator,
    /// Developer, Technician, Operator). Profiles live in UsersFile.UserProfileList, an
    /// array PropertyObject, so element count comes from offset enumeration.
    /// </summary>
    private static List<string> EnumerateUserProfileNames(NiEngine eng)
    {
        var names = new List<string>();
        try
        {
            NiPropertyObject profiles = eng.UsersFile.UserProfileList;
            int n = CountArrayElements(profiles);
            for (int i = 0; i < n; i++)
            {
                try { names.Add(profiles.GetPropertyObjectByOffset(i, 0).GetValString("LoginName", 0)); }
                catch (Exception) { /* best-effort: read user profile login name at index — intentionally ignored */ }
            }
        }
        catch (Exception) { /* best-effort: enumerate user profile list — intentionally ignored */ }
        return names;
    }

    private UserInfo MapUser(NiPropertyObject userPo)
    {
        var info = new UserInfo();
        try { info.LoginName = userPo.GetValString("LoginName", 0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read user LoginName."); }
        try { info.FullName  = userPo.GetValString("FullName", 0); }  catch (Exception ex) { _logger.LogDebug(ex, "Failed to read user FullName."); }
        // User-group entries use the "%GroupName" login-name convention.
        info.IsGroup = info.LoginName.StartsWith("%");
        return info;
    }

    private NiUser? FindUser(string loginName)
    {
        try
        {
            NiUser user = ((NiEngine)_engine!).GetUser(loginName);
            if (user != null) return user;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to find user '{LoginName}'.", loginName); }
        return null;
    }

    /// <summary>
    /// Returns the number of elements in a one-dimensional array PropertyObject.
    /// Array elements are indexed, not named, so GetNumSubProperties returns 0 — we
    /// enumerate by offset until GetPropertyObjectByOffset fails.
    /// </summary>
    private static int CountArrayElements(NiPropertyObject arr)
    {
        int c = 0;
        while (true)
        {
            try
            {
                var e = arr.GetPropertyObjectByOffset(c, 0);
                if (e == null) break;
                c++;
            }
            catch { break; }
        }
        return c;
    }

    private void CollectEnabledPrivileges(NiPropertyObject node, string prefix, List<string> sink)
    {
        int count;
        try { count = node.GetNumSubProperties(""); }
        catch { return; }

        for (int i = 0; i < count; i++)
        {
            try
            {
                NiPropertyObject child = node.GetNthSubProperty("", i, 0);
                string name = child.Name;
                string path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";

                bool isBoolLeaf = false;
                try
                {
                    bool val = child.GetValBoolean("", 0);
                    isBoolLeaf = true;
                    if (val) sink.Add(path);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read boolean privilege value at path '{Path}'.", path); }

                if (!isBoolLeaf)
                    CollectEnabledPrivileges(child, path, sink);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read privilege sub-property at index {Index}.", i); }
        }
    }

    private void PersistUsersFile(NiUsersFile usersFile)
    {
        try
        {
            usersFile.AsPropertyObjectFile().WriteFile(NiWriteFileFormat.WriteFileFormat_Current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist users file to disk");
        }
    }

    // ── Native Find / Replace ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<FindReplaceResult> FindInFileAsync(string filePath, string pattern,
        bool matchCase = false, bool wholeWord = false, bool regex = false,
        string elements = "all", int maxResults = 500)
        => await RunFindReplaceAsync(filePath, pattern, null, matchCase, wholeWord,
            regex, elements, false, maxResults);

    /// <inheritdoc/>
    public async Task<FindReplaceResult> ReplaceInFileAsync(string filePath, string pattern,
        string replacement, bool matchCase = false, bool wholeWord = false,
        bool regex = false, string elements = "all", bool save = true)
        => await RunFindReplaceAsync(filePath, pattern, replacement, matchCase, wholeWord,
            regex, elements, save, int.MaxValue);

    private async Task<FindReplaceResult> RunFindReplaceAsync(string filePath,
        string pattern, string? replacement, bool matchCase, bool wholeWord,
        bool regex, string elements, bool save, int maxResults)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            // Use the strongly-typed interop interfaces (vtable calls) rather than `dynamic`.
            // The DLR/IDispatch late-binding path intermittently throws DISP_E_BADPARAMCOUNT
            // ("TargetParameterCountException") under cumulative COM load in a shared engine.
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiPropertyObject root = seqFile.AsPropertyObject();

            int options = 0;
            if (matchCase) options |= (int)NiSearchOptions.SearchOptions_MatchCase;
            if (wholeWord) options |= (int)NiSearchOptions.SearchOptions_WholeWordOnly;
            if (regex)     options |= (int)NiSearchOptions.SearchOptions_RegExpr;

            int elementMask = elements.ToLowerInvariant() switch
            {
                "name"     => (int)NiSearchElements.SearchElement_Name,
                "comment"  => (int)NiSearchElements.SearchElement_Comment,
                "value"    => (int)NiSearchElements.SearchElement_AllValues,
                "values"   => (int)NiSearchElements.SearchElement_AllValues,
                _          => (int)NiSearchElements.SearchElement_All
            };

            var empty = Array.Empty<string>();
            // PropertyObject.Search(lookupString, searchString, searchOptions,
            //   filterOptions, elementsToSearch, limitToAdapters, limitToNamedProps,
            //   limitToPropsOfNamedTypes, subpropLookupStringsToExclude)
            var search = root.Search("", pattern, options,
                (int)NiSearchFilter.SearchFilterOptions_All, elementMask,
                empty, empty, empty, empty);

            // Wait for the asynchronous search to finish.
            try { search.IsComplete(true, false); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to wait for find/replace search completion."); }

            var res = new FindReplaceResult
            {
                Pattern     = pattern,
                Replacement = replacement
            };
            try { res.StatusMessage = search.StatusMessage; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read search StatusMessage."); }

            int numMatches = 0;
            try { numMatches = search.NumMatches; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read search NumMatches."); }
            res.TotalMatches = numMatches;

            for (int i = 0; i < numMatches && i < maxResults; i++)
            {
                try
                {
                    var m = search.GetMatch(i);
                    var fm = new FindMatch();
                    try { fm.FilePath    = m.FilePath; }                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read match FilePath at index {Index}.", i); }
                    try { fm.MatchedText = m.MatchedText; }             catch (Exception ex) { _logger.LogDebug(ex, "Failed to read match MatchedText at index {Index}.", i); }
                    try { fm.ValueType   = m.PropertyValueType.ToString(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read match PropertyValueType at index {Index}.", i); }

                    string editPath = "";
                    try { editPath = m.GetPropertyPath(false); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get edit property path for match at index {Index}.", i); }
                    try { fm.PropertyPath = m.GetPropertyPath(true); } catch { fm.PropertyPath = editPath; }

                    if (replacement != null && !string.IsNullOrEmpty(editPath))
                    {
                        // SearchMatch.UpdateForReplace does NOT edit the file — it only keeps
                        // neighbouring match offsets consistent. We must edit the property's
                        // string value ourselves, then notify the match.
                        if (TryReplacePropertyValue(root, editPath, pattern, replacement,
                                matchCase, wholeWord, regex))
                        {
                            fm.Replaced = true;
                            res.ReplacedCount++;
                            try { m.UpdateForReplace(replacement); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to notify search match of replacement at index {Index}.", i); }
                        }
                    }
                    res.Matches.Add(fm);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to process search match at index {Index}.", i); }
            }

            if (replacement != null && res.ReplacedCount > 0 && save)
            {
                SaveSequenceFileWithRetry(seqFile, filePath);
                _loadedSequenceFiles[filePath] = seqFile;
            }

            return res;
        });
    }

    /// <summary>
    /// Reads the string value of the property at <paramref name="path"/> (relative to
    /// <paramref name="root"/>), replaces occurrences of <paramref name="pattern"/> with
    /// <paramref name="replacement"/>, and writes it back. Returns true if the value changed.
    /// </summary>
    private bool TryReplacePropertyValue(NiPropertyObject root, string path, string pattern,
        string replacement, bool matchCase, bool wholeWord, bool regex)
    {
        string current;
        try { current = root.GetValString(path, 0); }
        catch { return false; }   // property has no string value (e.g. a name/comment match)

        string updated = ReplaceString(current, pattern, replacement, matchCase, wholeWord, regex);
        if (updated == current) return false;

        try { root.SetValString(path, 0, updated); return true; }
        catch { return false; }
    }

    internal static string ReplaceString(string input, string pattern, string replacement,
        bool matchCase, bool wholeWord, bool regex)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var options = matchCase
            ? System.Text.RegularExpressions.RegexOptions.None
            : System.Text.RegularExpressions.RegexOptions.IgnoreCase;

        string corePattern = regex ? pattern : System.Text.RegularExpressions.Regex.Escape(pattern);
        if (wholeWord) corePattern = $@"\b(?:{corePattern})\b";

        try
        {
            return System.Text.RegularExpressions.Regex.Replace(input, corePattern, replacement, options);
        }
        catch
        {
            // Fall back to a plain (case-sensitive) replace if the regex is invalid.
            return input.Replace(pattern, replacement);
        }
    }

    // ── Typed Adapter / Code-Module Configuration ─────────────────────────────

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureDotNetModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string assemblyPath,
        string className, string methodName, bool save = true, bool loadPrototype = true)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "DotNet", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                try { mod.SetAssembly((object)assemblyPath, (object)true); }
                catch { TrySetModuleProp(mod, "Assembly", assemblyPath); }
                applied["assemblyPath"] = assemblyPath;
                if (TrySetModuleProp(mod, "ClassName", className)) applied["className"] = className;
                // The member to invoke — property name differs across builds.
                if (TrySetModuleProp(mod, "NameOfMethodToCreate", methodName) ||
                    TrySetModuleProp(mod, "MemberName", methodName))
                    applied["methodName"] = methodName;
                return applied;
            },
            loadPrototype: loadPrototype);

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureDllModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string dllPath,
        string functionName, bool save = true, bool loadPrototype = true)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "CVI", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                // The path/function live on the CommonCModule base interface, which is
                // not the default dispatch interface of a CVI step's Module.
                dynamic target = mod;
                try { target = mod.AsCommonCModule(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to cast module to CommonCModule interface."); }
                if (TrySetModuleProp(target, "ModulePath", dllPath) ||
                    TrySetModuleProp(mod, "ModulePath", dllPath))
                    applied["dllPath"] = dllPath;
                if (TrySetModuleProp(target, "FunctionName", functionName) ||
                    TrySetModuleProp(mod, "FunctionName", functionName))
                    applied["functionName"] = functionName;
                return applied;
            },
            loadPrototype: loadPrototype);

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureLabViewModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string viPath,
        bool save = true, bool loadPrototype = true)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "LabVIEW", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                if (TrySetModuleProp(mod, "VIPath", viPath) ||
                    TrySetModuleProp(mod, "ModulePath", viPath))
                    applied["viPath"] = viPath;
                return applied;
            },
            // Guard: refuse None-adapter LabVIEW UTILITY steps (e.g. NI_LV_RunVIAsynchronously,
            // "Run VI Asynchronously"). Their VI config lives in the step's own properties
            // (VIModule.ViCall.VIPath, …); switching to the LabVIEW adapter here would corrupt it.
            step =>
            {
                string stepType = "";
                try { stepType = (string)step.StepType.Name; }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType.Name for LabVIEW-module guard on '{Step}'.", stepName); }
                string adapterKey = TryGetString(step, "AdapterKeyName");
                if (InputGuards.IsNoneAdapterLabViewUtilityStep(stepType, adapterKey))
                    throw new InvalidOperationException(
                        $"Step '{stepName}' is a None-adapter LabVIEW utility step ('{stepType}'); " +
                        "configure_labview_module would switch its adapter to LabVIEW and corrupt its " +
                        "configuration. Use set_step_property instead (e.g. property_path " +
                        "'VIModule.ViCall.VIPath' for the VI, 'RemoteHost'/'PortNumber'/'Timeout').");
            },
            loadPrototype: loadPrototype);

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigurePythonModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string modulePath,
        string functionName, bool save = true, bool loadPrototype = true,
        string? className = null, string? classInstanceLocation = null,
        int? operationType = null, int? operationScope = null,
        string? pythonVersion = null, string? virtualEnvPath = null,
        bool? useAdapterInterpreterSettings = null,
        IReadOnlyList<PythonParamSpec>? parameters = null)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "Python", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                if (TrySetModuleProp(mod, "ModulePath", modulePath))
                    applied["modulePath"] = modulePath;
                if (TrySetModuleProp(mod, "FunctionOrAttributeName", functionName) ||
                    TrySetModuleProp(mod, "FunctionName", functionName))
                    applied["functionName"] = functionName;
                return applied;
            },
            loadPrototype: loadPrototype,
            applyOnStep: (stepPo, applied) => ApplyPythonStepSettings(stepPo, applied,
                className, classInstanceLocation, operationType, operationScope,
                pythonVersion, virtualEnvPath, useAdapterInterpreterSettings, parameters));

    /// <summary>
    /// Writes the parts of a Python step's configuration that live in the STEP's property tree
    /// (<c>TS.SData.PythonCall.*</c>) rather than on the adapter Module object: the class / instance
    /// expression / operation kind (module function vs. constructor vs. method on an instance), the
    /// interpreter session settings, and the explicit argument list.
    /// <para>
    /// This is what makes an object-oriented Python step reproducible. Setting only ModulePath +
    /// FunctionOrAttributeName leaves a step that calls a module-level function with a Dynamic-typed
    /// empty argument list, which is neither the original configuration nor executable.
    /// </para>
    /// </summary>
    private void ApplyPythonStepSettings(NiPropertyObject stepPo, Dictionary<string, object> applied,
        string? className, string? classInstanceLocation, int? operationType, int? operationScope,
        string? pythonVersion, string? virtualEnvPath, bool? useAdapterInterpreterSettings,
        IReadOnlyList<PythonParamSpec>? parameters)
    {
        const string Base = "TS.SData.PythonCall";

        void SetStr(string leaf, string? v, string key)
        {
            if (v == null) return;
            stepPo.SetValString($"{Base}.{leaf}", 0, v);
            applied[key] = v;
        }
        void SetNum(string leaf, int? v, string key)
        {
            if (v == null) return;
            stepPo.SetValNumber($"{Base}.{leaf}", 0, v.Value);
            applied[key] = v.Value;
        }

        SetStr("ClassName",             className,             "className");
        SetStr("ClassInstanceLocation", classInstanceLocation, "classInstanceLocation");
        SetNum("OperationType",         operationType,         "operationType");
        SetNum("OperationScope",        operationScope,        "operationScope");
        SetStr("PythonVersion",                 pythonVersion,  "pythonVersion");
        SetStr("PythonVirtualEnvironmentPath",  virtualEnvPath, "virtualEnvPath");
        if (useAdapterInterpreterSettings.HasValue)
        {
            stepPo.SetValBoolean($"{Base}.UseAdapterSettingsForInterpreterSession", 0,
                useAdapterInterpreterSettings.Value);
            applied["useAdapterInterpreterSettings"] = useAdapterInterpreterSettings.Value;
        }

        if (parameters == null || parameters.Count == 0) return;

        // Size the argument array, then fill each entry. The elements are instantiated with the
        // array's NI_PythonParameter element type, so Name/Type/ArgumentValue already exist.
        NiPropertyObject arr = (NiPropertyObject)(object)stepPo.GetPropertyObject($"{Base}.Parameters", 0);
        arr.SetNumElements(parameters.Count, 0);
        for (int i = 0; i < parameters.Count; i++)
        {
            var spec = parameters[i];
            string p = $"{Base}.Parameters[{i}]";
            if (spec.Name != null)  stepPo.SetValString($"{p}.Name", 0, spec.Name);
            if (spec.Value != null) stepPo.SetValString($"{p}.ArgumentValue", 0, spec.Value);
            int? code = ParsePythonParamType(spec.Type);
            if (code.HasValue)      stepPo.SetValNumber($"{p}.Type", 0, code.Value);
        }
        applied["parameterCount"] = parameters.Count;
    }

    // A Python argument entry's Type code. Accepts the raw number (authoritative — TestStand stores
    // this as a plain number) or one of the aliases whose codes are confirmed from real files.
    private static int? ParsePythonParamType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        string t = type.Trim();
        if (int.TryParse(t, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;
        return t.ToLowerInvariant() switch
        {
            "none"    => 0,
            "boolean" or "bool" => 3,
            "dynamic" => 4,
            "object"  => 6,
            _ => throw new ArgumentException(
                    $"Unknown Python parameter type '{type}'. Pass the numeric Type code " +
                    "(0=None, 3=Boolean, 4=Dynamic, 6=Object, 7=by-name argument) or one of " +
                    "none/boolean/dynamic/object."),
        };
    }

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureSequenceCallModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        string targetSequenceName, string targetSequenceFile = "", bool save = true,
        string? executionMode = null, string? threadRefExpr = null, bool? autoWait = null,
        bool loadPrototype = true, string? storedFilePath = null)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "SequenceCall", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                mod.SequenceName   = targetSequenceName;
                mod.UseCurrentFile = string.IsNullOrEmpty(targetSequenceFile);
                applied["targetSequenceName"] = targetSequenceName;
                if (!string.IsNullOrEmpty(targetSequenceFile))
                {
                    string rel = MakeRelativePath(
                        Path.GetDirectoryName(filePath) ?? "", targetSequenceFile);
                    mod.SequenceFilePath = rel;
                    applied["targetSequenceFile"] = rel;
                }
                // NOTE: when the call targets the CURRENT file, the retained SData.SFPath is written
                // AFTER the prototype load (see applyAfterPrototype below) — the load blanks it, so a
                // write here would be lost.
                // NOTE: the callee prototype is loaded centrally by ConfigureModuleAsync AFTER this
                // 'apply' runs (so the target is set first) — see TryLoadModulePrototype there.

                // Optional threading / async options. These live on the SequenceCall module's own
                // PropertyObject (TS.SData): ThreadOpt (0 = run in the calling thread, 1 = new thread,
                // 2 = new execution), AsyncThreadExpr (expression to store the new thread/execution
                // reference) and AutoWaitAsync (wait for the async subsequence at the end of the
                // current sequence). Previously only settable via raw set_step_property.
                if (executionMode != null || threadRefExpr != null || autoWait.HasValue)
                {
                    try
                    {
                        NiPropertyObject sdata = ((dynamic)mod).AsPropertyObject();
                        if (executionMode != null)
                        {
                            int opt = executionMode.Trim().ToLowerInvariant() switch
                            {
                                "newthread" or "thread" or "new thread"            => 1,
                                "newexecution" or "execution" or "new execution"   => 2,
                                _                                                  => 0, // use current thread
                            };
                            sdata.SetValNumber("ThreadOpt", 0, opt);
                            applied["executionOption"] = opt;
                        }
                        if (threadRefExpr != null)
                        {
                            sdata.SetValString("AsyncThreadExpr", 0, threadRefExpr);
                            applied["threadRefExpr"] = threadRefExpr;
                        }
                        if (autoWait.HasValue)
                        {
                            sdata.SetValBoolean("AutoWaitAsync", 0, autoWait.Value);
                            applied["autoWait"] = autoWait.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to set async/threading options on SequenceCall step '{Step}'.", stepName);
                    }
                }
                return applied;
            },
            loadPrototype: loadPrototype,
            applyAfterPrototype: (stepPo, applied) =>
            {
                // "Use current file" does NOT mean the stored path is blank: the editor keeps the
                // last-known sequence-file path on the step (SData.SFPath) and only flags UseCurFile.
                // Leaving it empty is a difference on EVERY call step (46 of them in one real file).
                // Default it to this file's own name — what the editor leaves behind — and let
                // 'storedFilePath' override, which is the only way to reproduce an original that
                // retained a STALE path (real files carry paths from before a rename, e.g.
                // "KingFisherCOM.seq" on a step that calls the current file). Must run AFTER the
                // prototype load, which re-derives and blanks SFPath.
                if (!string.IsNullOrEmpty(targetSequenceFile)) return;
                string stored = storedFilePath ?? Path.GetFileName(filePath);
                try
                {
                    stepPo.SetValString("TS.SData.SFPath", 0, stored);
                    applied["storedFilePath"] = stored;
                }
                catch (Exception ex)
                { _logger.LogDebug(ex, "Writing SData.SFPath failed on '{Step}'.", stepName); }
            });

    /// <summary>
    /// Shared driver for the typed adapter-configuration tools: resolves the step,
    /// switches its adapter (when needed), applies the adapter-specific settings via
    /// the supplied callback, and saves the file.
    /// </summary>
    private async Task<ModuleConfigResult> ConfigureModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string adapterKey,
        bool save, Func<dynamic, Dictionary<string, object>> apply,
        Action<dynamic>? preAdapterGuard = null, bool loadPrototype = true,
        Action<NiPropertyObject, Dictionary<string, object>>? applyOnStep = null,
        Action<NiPropertyObject, Dictionary<string, object>>? applyAfterPrototype = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            // Optional guard: inspect the step BEFORE the adapter is switched (the switch itself is
            // what can corrupt a None-adapter utility step). May throw to abort the operation.
            preAdapterGuard?.Invoke(step);

            // Ensure the step uses the requested adapter before configuring its module.
            string resolvedKey = ResolveAdapterKeyName(adapterKey);
            string currentKey  = TryGetString(step, "AdapterKeyName");
            if (!string.Equals(currentKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                try { step.ChangeAdapter((object)resolvedKey); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to change step adapter to '{Adapter}'.", resolvedKey); }
            }

            dynamic mod = step.Module;
            var applied = apply(mod);

            // Adapter settings that live in the STEP's own property tree rather than on the Module
            // object (the Python adapter keeps class/instance/operation/interpreter under
            // TS.SData.PythonCall). Runs after 'apply' and BEFORE the prototype load, so an explicit
            // argument list is not overwritten by a load that cannot resolve the module anyway.
            if (applyOnStep != null)
            {
                try { applyOnStep((NiPropertyObject)(object)((NiStep)(object)step).AsPropertyObject(), applied); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Step-level module settings failed for '{Step}'.", stepName);
                    throw;
                }
            }

            // Load the code-module prototype so the step's parameter interface is populated from the
            // just-configured target — the programmatic equivalent of the editor's "Load Prototype":
            // a LabVIEW VI's connector pane, a DLL/.NET/ActiveX function prototype, a SequenceCall's
            // callee argument list. This MUST run AFTER 'apply' set the target/VI path (order matters —
            // without it the interface stays empty). Non-destructive: existing bindings are matched by
            // name and preserved. It is a logged no-op when the target is not resolvable (unlinked
            // placeholder, missing/not-loaded file, or a VI in an unloadable .lvlibp headless).
            if (loadPrototype)
                TryLoadModulePrototype(mod, stepName);

            // Settings the prototype load itself OVERWRITES have to be (re)applied after it. The
            // SequenceCall's retained file path is the case in point: the load re-derives it from
            // UseCurFile and blanks it, so writing it before the load has no effect.
            if (applyAfterPrototype != null)
            {
                try { applyAfterPrototype((NiPropertyObject)(object)((NiStep)(object)step).AsPropertyObject(), applied); }
                catch (Exception ex)
                { _logger.LogWarning(ex, "Post-prototype module settings failed for '{Step}'.", stepName); }
            }

            // Read the resulting interface back so the caller can SEE the parameters that were loaded.
            var parameters = new List<ModuleParameterInfo>();
            try { parameters = ReadModuleParameters(((NiStep)(object)step).AsPropertyObject(), stepName); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module parameters after configuring '{Step}'.", stepName); }

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
            }

            // Report the step's ACTUAL adapter, not the requested key: ChangeAdapter may have
            // normalised to the loaded adapter (e.g. 'G Std Prototype Adapter' request → the
            // step keeps/gets 'G Flexible VI Adapter') or failed silently for a missing one.
            string actualKey = TryGetString(step, "AdapterKeyName");
            return new ModuleConfigResult
            {
                StepName        = stepName,
                Adapter         = string.IsNullOrEmpty(actualKey) ? resolvedKey : actualKey,
                AppliedSettings = applied,
                Parameters      = parameters
            };
        });
    }

    private static bool TrySetModuleProp(dynamic mod, string propName, object value)
    {
        try
        {
            ((object)mod).GetType().InvokeMember(propName,
                System.Reflection.BindingFlags.SetProperty,
                null, mod, new[] { value });
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Best-effort programmatic equivalent of the Sequence Editor's <c>Load Prototype</c>
    /// button (<see cref="NationalInstruments.TestStand.Interop.API.Module.LoadPrototype"/>).
    /// When the step's target module is resolvable, TestStand reconciles the module's argument
    /// list against the callee prototype: for a SequenceCall it materialises one typed
    /// <c>SequenceArgument</c> per callee parameter in <c>TS.SData.ActualArgs</c> — each with the
    /// correct <c>ParamType</c>/<c>ParamRepresentation</c>/<c>Flags</c> and <c>UseDef=True</c> —
    /// and refreshes the cached <c>Prototype</c> container. Existing bindings are matched by name
    /// and preserved. Without this, arguments created bare via <c>NewSubProperty</c> carry wrong
    /// default flags (e.g. PassByReference 0x4 on a by-value arg) and the unbound parameters are
    /// missing entirely — both of which the native FileDiffer flags against a genuine call.
    /// The call is a no-op (logged and swallowed) when the target cannot be resolved — an
    /// unlinked placeholder, an unresolved external file, or a not-yet-loaded target — so target
    /// assignment always succeeds.
    /// </summary>
    private bool TryLoadModulePrototype(dynamic moduleObj, string stepName)
    {
        try
        {
            moduleObj.LoadPrototype(0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "LoadPrototype skipped for step '{Step}' (target/module not resolvable).", stepName);
            return false;
        }

        // After a prototype load every actual argument is freshly created with UseDef=False and
        // an empty Expr. A genuine call keeps UNBOUND arguments at UseDef=True ("use default") and
        // clears it only for bound ones. Enforce that invariant (UseDef ⇔ empty Expr) so the
        // native FileDiffer sees exactly the defaults the editor's Load Prototype would leave.
        // No-op for adapters without a SequenceCall ActualArgs list (e.g. LabVIEW).
        try
        {
            NiPropertyObject modPo = (NiPropertyObject)(object)moduleObj.AsPropertyObject();
            NiPropertyObject args  = (NiPropertyObject)(object)modPo.GetPropertyObject("ActualArgs", 0);
            int n = args.GetNumSubProperties("");
            for (int i = 0; i < n; i++)
            {
                try
                {
                    NiPropertyObject a = args.GetNthSubProperty("", i, 0);
                    string expr = "";
                    try { expr = a.GetValString("Expr", 0); } catch { /* no Expr on this entry */ }
                    a.SetValBoolean("UseDef", 0, string.IsNullOrEmpty(expr));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "UseDef normalization skipped for an ActualArgs entry on '{Step}'.", stepName); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No ActualArgs to normalize on step '{Step}'.", stepName); }
        return true;
    }

    // ── Sequence Analyzer (detailed) ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnalyzerResult> RunSequenceAnalyzerDetailedAsync(string filePath,
        string minSeverity = "Information", string groupBy = "severity", bool async = false,
        int timeoutSeconds = DefaultAnalyzerTimeoutSeconds)
    {
        // ASYNC: return a running handle right away so the RPC completes well within the MCP
        // transport's ~60s window. The analysis (which spawns AnalyzerApp.exe — an out-of-process
        // child that owns the slow/fault-prone LabVIEW module loads) runs on a background job; the
        // caller polls get_analysis_status. This is the ONLY real fix for the -32001 timeout: a
        // tool-side timeout_seconds cannot lift the client's transport cap.
        if (async)
        {
            EnsureConnected(); // surface a genuine "not connected" synchronously, before the job starts
            return StartAnalyzerJob(filePath, minSeverity, groupBy, timeoutSeconds);
        }

        var messages = await RunSequenceAnalyzerAsync(filePath);
        return BuildAnalyzerResult(filePath, messages, minSeverity, groupBy);
    }

    /// <summary>Test seam for <see cref="BuildAnalyzerResult"/> (pure, engine-free shaping).</summary>
    internal static AnalyzerResult BuildAnalyzerResultForTest(string filePath,
        List<AnalyzerMessage> messages, string minSeverity, string groupBy)
        => BuildAnalyzerResult(filePath, messages, minSeverity, groupBy);

    // Pure shaping of raw analyzer messages into the filtered/grouped result — shared by the
    // synchronous path and the async job so both produce an identical AnalyzerResult.
    private static AnalyzerResult BuildAnalyzerResult(string filePath,
        List<AnalyzerMessage> messages, string minSeverity, string groupBy)
    {
        int Rank(string s) => s switch
        {
            "Error" => 3, "Warning" => 2, "Information" => 1, _ => 0
        };
        int threshold = Rank(minSeverity);
        var filtered = messages.Where(m => Rank(m.Severity) >= threshold).ToList();

        bool grouped = AnalyzerGrouping.IsGrouped(groupBy);

        // Zero RAW messages (before severity filtering) means the analysis produced nothing at all.
        // The counting rules fire on any file, so that is the tell for "AnalyzerApp bailed out" rather
        // than "the file is clean" — flag it instead of reporting a silent, flattering zero. Note the
        // check is on 'messages', NOT 'filtered': min_severity='Error' legitimately filters to zero.
        bool suspect = messages.Count == 0;

        return new AnalyzerResult
        {
            FilePath         = filePath,
            TotalMessages    = filtered.Count,
            ErrorCount       = filtered.Count(m => m.Severity == "Error"),
            WarningCount     = filtered.Count(m => m.Severity == "Warning"),
            InformationCount = filtered.Count(m => m.Severity == "Information"),
            Messages         = filtered,
            GroupBy          = grouped ? groupBy.Trim().ToLowerInvariant() : "",
            Groups           = grouped
                                   ? AnalyzerGrouping.Group(filtered, groupBy)
                                   : new List<AnalyzerMessageGroup>(),
            ResultSuspect    = suspect,
            Note             = suspect
                ? "SUSPECT RESULT: the analyzer returned no messages at all. Even a clean file "
                  + "normally yields the counting rules (NI_SequenceFileCount / NI_SequenceCount / "
                  + "NI_StepCount), so this usually means AnalyzerApp.exe did not analyse the file — "
                  + "most often because LabVIEW or the Python interpreter was unavailable for the "
                  + "'module is loadable' rule. Do NOT read this as 'no findings'; make the code "
                  + "modules loadable and re-run. See ts_analyzer_diag.txt in %TEMP% for the child's "
                  + "exit code and output."
                : null
        };
    }

    // ── Async analyzer jobs ────────────────────────────────────────────────────
    // Mirrors the load_module_prototype async-job infra (PrototypeLoadJob/StartPrototypeLoadJob):
    // a "running" handle returns immediately, the work runs on a background Task, and the caller
    // polls get_analysis_status. NOTE: the analyzer needs NO isolated worker of its own — the slow,
    // possibly-crashing LabVIEW module loads happen inside AnalyzerApp.exe, a SEPARATE process the
    // analysis already spawns, so a native .lvlibp fault kills that child (surfaced as a job "error"),
    // never the MCP server. The background Task here is the SAME Task.Run context the synchronous
    // path already used — it only decouples the RPC response from the analysis duration.
    private sealed class AnalyzerJob
    {
        public string JobId = "";
        public string Status = "running";      // running | completed | error
        public AnalyzerResult? Result;
        public string? Error;
        public DateTime StartedUtc;
        public DateTime? FinishedUtc;
        public string FilePath = "";
        public string MinSeverity = "Information";
        public string GroupBy = "severity";

        public AnalyzerResult Snapshot()
        {
            if (Status == "completed" && Result != null)
            {
                Result.JobId = JobId;
                Result.Status = "completed";
                // Keep the suspect-result warning — clearing the note here would turn an
                // "analysis produced nothing" answer back into a silent, flattering zero.
                if (!Result.ResultSuspect) Result.Note = null;
                return Result;
            }
            return new AnalyzerResult
            {
                FilePath = FilePath,
                GroupBy  = AnalyzerGrouping.IsGrouped(GroupBy) ? GroupBy.Trim().ToLowerInvariant() : "",
                JobId    = JobId,
                Status   = Status,
                Note     = Status == "error"
                    ? "The analysis job faulted: " + (Error ?? "unknown error")
                    : "Analysis still running — poll get_analysis_status again after a short wait."
            };
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AnalyzerJob>
        _analyzerJobs = new();

    // Starts the analysis on a background task, tracks it as a job, and returns a "running" handle
    // immediately so the RPC returns well within the transport timeout.
    private AnalyzerResult StartAnalyzerJob(string filePath, string minSeverity, string groupBy,
        int timeoutSeconds = DefaultAnalyzerTimeoutSeconds)
    {
        PruneOldAnalyzerJobs();
        var job = new AnalyzerJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            StartedUtc = DateTime.UtcNow,
            FilePath = filePath, MinSeverity = minSeverity, GroupBy = groupBy
        };
        _analyzerJobs[job.JobId] = job;

        _ = Task.Run(async () =>
        {
            try
            {
                var messages = await RunSequenceAnalyzerAsync(filePath);
                job.Result = BuildAnalyzerResult(filePath, messages, minSeverity, groupBy);
                job.Status = "completed";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Async analyzer job {Job} faulted.", job.JobId);
                job.Error = ex.Message;
                job.Status = "error";
            }
            finally { job.FinishedUtc = DateTime.UtcNow; }
        });

        return new AnalyzerResult
        {
            FilePath = filePath,
            GroupBy  = AnalyzerGrouping.IsGrouped(groupBy) ? groupBy.Trim().ToLowerInvariant() : "",
            JobId    = job.JobId,
            Status   = "running",
            Note     = "Analysis started asynchronously (a cold analysis of LabVIEW .lvlibp steps can " +
                       "exceed the ~60s MCP transport timeout — the Sequence Editor does the same slow " +
                       "module loads). Poll get_analysis_status with job_id='" + job.JobId +
                       "' until status='completed'."
        };
    }

    // Keep the job map from growing unbounded: drop finished jobs older than 10 minutes.
    private void PruneOldAnalyzerJobs()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kv in _analyzerJobs)
            if (kv.Value.FinishedUtc is DateTime f && f < cutoff)
                _analyzerJobs.TryRemove(kv.Key, out _);
    }

    /// <inheritdoc/>
    public async Task<AnalyzerResult> GetAnalysisStatusAsync(string jobId)
    {
        if (!_analyzerJobs.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException(
                $"No analysis job '{jobId}' (unknown or expired). Start one with analyze_sequence_file (async=true).");
        return await Task.FromResult(job.Snapshot());
    }

    // ── Output & UI Messages ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<OutputMessageInfo> PostOutputMessageAsync(string message,
        string category = "", string severity = "Information")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sev = severity.ToLowerInvariant() switch
            {
                "error"   => NiOutputSeverity.OutputMessageSeverity_Error,
                "warning" => NiOutputSeverity.OutputMessageSeverity_Warning,
                _         => NiOutputSeverity.OutputMessageSeverity_Information
            };
            // NewOutputMessage(messageText, categoryText, severity, sequenceContext).
            // SequenceContext is optional — pass a typed null via the IEngine interface.
            dynamic msg = ((NiEngine)_engine!).NewOutputMessage(message, category ?? "", sev, null);

            // Add to the engine's retrievable output-message collection so the message
            // can be read back via GetOutputMessages. Post() additionally raises a UI
            // event for an operator interface (no-op headless), so call it best-effort.
            try { _engine!.GetOutputMessages().Add(msg); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to add output message to engine message collection."); }
            try { msg.Post(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to post output message via UI event."); }
            return MapOutputMessage(msg);
        });
    }

    /// <inheritdoc/>
    public async Task<List<OutputMessageInfo>> GetOutputMessagesAsync(int maxMessages = 200)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<OutputMessageInfo>();
            dynamic msgs = _engine!.GetOutputMessages();
            int count = 0;
            try { count = Convert.ToInt32((object)msgs.Count); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get output message count."); }
            // Return the MOST RECENT maxMessages (tail), in chronological order. The list is
            // append-ordered (index 0 = oldest), so the old `i < maxMessages` loop returned the
            // OLDEST N and hid all recent activity once more than maxMessages had accumulated.
            int start = Math.Max(0, count - maxMessages);
            for (int i = start; i < count; i++)
            {
                try { result.Add(MapOutputMessage(msgs.Item((object)i))); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to map output message at index {Index}.", i); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task ClearOutputMessagesAsync()
    {
        EnsureConnected();
        await Task.Run(() => { try { _engine!.GetOutputMessages().Clear(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to clear engine output messages."); } });
    }

    /// <inheritdoc/>
    public async Task PostUiMessageAsync(string executionId, string messageCode,
        double numericData = 0, string stringData = "")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            // Resolve the UIMessageCodes constant; default to a user message code.
            NiUIMessageCodes code;
            if (!Enum.TryParse<NiUIMessageCodes>(messageCode, true, out code) &&
                !Enum.TryParse<NiUIMessageCodes>("UIMsg_" + messageCode, true, out code))
                code = NiUIMessageCodes.UIMsg_UserMessageBase;

            dynamic thread = exec.GetThread((object)0);
            thread.PostUIMessage(code, (object)numericData, stringData ?? "", (object)false);
        });
    }

    private OutputMessageInfo MapOutputMessage(dynamic msg)
    {
        var info = new OutputMessageInfo();
        try { info.Id = Convert.ToInt32((object)msg.Id); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read output message Id."); }
        try { info.Category = (string)msg.Category; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read output message Category."); }
        try { info.Message = (string)msg.Message; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read output message Message."); }
        try { info.TimeInSeconds = Convert.ToDouble((object)msg.TimeInSeconds); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read output message TimeInSeconds."); }
        try
        {
            int sev = Convert.ToInt32((object)msg.Severity);
            info.Severity = sev switch { 0 => "Information", 1 => "Warning", 2 => "Error", _ => sev.ToString() };
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read output message Severity."); }
        return info;
    }

    // ── Search Directories ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<SearchDirectoryInfo>> GetSearchDirectoriesAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<SearchDirectoryInfo>();
            dynamic dirs = _engine!.SearchDirectories;
            int count = Convert.ToInt32((object)dirs.Count);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic d = dirs.Item((object)i);
                    result.Add(new SearchDirectoryInfo
                    {
                        Index                = i,
                        Path                 = TryGetString(d, "Path"),
                        Type                 = TryGetString(d, "Type"),
                        Disabled             = TryGetBool(d, "Disabled"),
                        SearchSubdirectories = TryGetBool(d, "SearchSubdirectories")
                    });
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read search directory at index {Index}.", i); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task AddSearchDirectoryAsync(string path, int index = -1,
        bool searchSubdirectories = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            dynamic dirs = _engine!.SearchDirectories;
            // Insert(path, index, searchSubDirs, fileExtRestrict, exclude, disabled)
            dirs.Insert(path, (object)index, (object)searchSubdirectories,
                "", (object)false, (object)false);
        });
    }

    /// <inheritdoc/>
    public async Task RemoveSearchDirectoryAsync(string path)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            dynamic dirs = _engine!.SearchDirectories;
            int count = Convert.ToInt32((object)dirs.Count);
            for (int i = count - 1; i >= 0; i--)
            {
                try
                {
                    dynamic d = dirs.Item((object)i);
                    if (string.Equals(TryGetString(d, "Path"), path,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        dirs.Remove((object)i);
                        return;
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read search directory at index {Index} during remove.", i); }
            }
            throw new KeyNotFoundException($"Search directory '{path}' not found.");
        });
    }

    // ── Data-Type Field Editing ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AddDataTypeFieldAsync(string filePath, string typeName,
        string fieldName, string fieldType, bool save = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf       = GetOrLoadSeqFile(filePath);
            dynamic sfPo = sf.AsPropertyObjectFile();
            dynamic typePo = sfPo.GetPropertyObject((object)typeName, (object)0);

            int valType; string typeParam;
            switch (fieldType.ToLowerInvariant())
            {
                case "number": case "double": valType = (int)NiPropValueTypes.PropValType_Number;  typeParam = ""; break;
                case "string":               valType = (int)NiPropValueTypes.PropValType_String;  typeParam = ""; break;
                case "boolean": case "bool": valType = (int)NiPropValueTypes.PropValType_Boolean; typeParam = ""; break;
                default:                     valType = (int)NiPropValueTypes.PropValType_NamedType; typeParam = fieldType; break;
            }

            typePo.NewSubProperty((object)fieldName, (object)valType, (object)false,
                (object)typeParam, (object)0);

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
        });
    }

    /// <inheritdoc/>
    public async Task<List<TypeFieldInfo>> GetDataTypeFieldsAsync(string filePath, string typeName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result   = new List<TypeFieldInfo>();
            var sf       = GetOrLoadSeqFile(filePath);
            dynamic sfPo = sf.AsPropertyObjectFile();
            dynamic typePo = sfPo.GetPropertyObject((object)typeName, (object)0);
            var tObj = (object)typePo;
            int count = Convert.ToInt32(tObj.GetType().InvokeMember(
                "GetNumSubProperties", _comFlags, null, tObj, new object[] { "" }));
            for (int i = 0; i < count; i++)
            {
                try
                {
                    string fname = tObj.GetType().InvokeMember(
                        "GetNthSubPropertyName", _comFlags, null, tObj, new object[] { "", i, 0 })?.ToString() ?? "";
                    dynamic fp = tObj.GetType().InvokeMember(
                        "GetNthSubProperty", _comFlags, null, tObj, new object[] { "", i, 0 })!;
                    // Type name via GetTypeDisplayString — PropertyObject has no `TypeName`.
                    string fieldType = "";
                    try { fieldType = (string)fp.GetTypeDisplayString("", (object)0); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to get type display string for field '{Field}'.", fname); }
                    result.Add(new TypeFieldInfo { Name = fname, DataType = fieldType });
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read data type field at index {Index}.", i); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task RemoveDataTypeFieldAsync(string filePath, string typeName,
        string fieldName, bool save = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf       = GetOrLoadSeqFile(filePath);
            dynamic sfPo = sf.AsPropertyObjectFile();
            dynamic typePo = sfPo.GetPropertyObject((object)typeName, (object)0);
            typePo.DeleteSubProperty((object)fieldName, (object)0);
            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
        });
    }

    // ── CSV Record Streams ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task WriteCsvLinesAsync(string filePath, List<string> lines)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            // NewCsvFileOutputRecordStream already opens the file. Cast to the typed
            // interface — the methods are not exposed via __ComObject late binding.
            NiCsvOut stream = (NiCsvOut)_engine!.NewCsvFileOutputRecordStream(
                filePath, (int)NiFileOpenModes.FileOpenMode_Truncate);
            try
            {
                foreach (var line in lines) stream.WriteLine(line ?? "");
            }
            finally { try { ((NiOutRecordStream)(object)stream).Close(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to close CSV output record stream."); } }
        });
    }

    /// <inheritdoc/>
    public async Task<CsvReadResult> ReadCsvLinesAsync(string filePath, int maxLines = 1000)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new CsvReadResult { FilePath = filePath };
            NiCsvIn stream = (NiCsvIn)_engine!.NewCsvFileInputRecordStream(filePath);
            try
            {
                for (int i = 0; i < maxLines; i++)
                {
                    string line;
                    // ReadLine returns 0 on success, non-zero at end of file.
                    int rc = stream.ReadLine(out line);
                    if (rc != 0) break;
                    result.Lines.Add(line ?? "");
                }
            }
            finally { try { ((NiInRecordStream)(object)stream).Close(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to close CSV input record stream."); } }
            result.LineCount = result.Lines.Count;
            return result;
        });
    }

    // ── Result Logging (smoke) ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> CreateResultLogAsync(string filePath, string format = "ASCII")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            // NewResultLog / NewResultLogger create logging helpers used by the model.
            // Headless we can only confirm the object is created.
            dynamic log = _engine!.NewResultLog();
            return log == null ? "ResultLog creation returned null" : "ResultLog created";
        });
    }

    // ── Batch Synchronization (best-effort) ───────────────────────────────────

    /// <inheritdoc/>
    public async Task CreateBatchSyncObjectAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var mgr = GetSyncManager();
            // Batch synchronization objects are normally created by the batch process
            // model. Try the SyncManager factory; surface a clear error if unavailable.
            try
            {
                dynamic obj = mgr.NewBatchSynchronization((object)name);
                _syncObjects[name] = obj;
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(
                    "Batch synchronization objects are created by the batch process model and " +
                    "are not exposed as a standalone SyncManager factory in this engine build.", ex);
            }
        });
    }

    // ── Interactive Execution (smoke) ─────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> RunStepsInteractivelyAsync(string filePath, string sequenceName,
        string stepGroup, List<string> stepNames, int timeoutSeconds = 60)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            // Interactive execution of selected steps requires NewInteractiveArgs plus an
            // execution created with interactive mode. We construct the args object to
            // confirm the path is wired; full interactive runs need an active editor context.
            dynamic args = _engine!.NewInteractiveArgs();
            if (args == null)
                throw new NotSupportedException("Engine did not return InteractiveArgs.");
            return $"InteractiveArgs created for {stepNames?.Count ?? 0} step(s) in {sequenceName}";
        });
    }

    // ── Report Sections (smoke) ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> AddReportSectionAsync(string executionId, string title, string body)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            dynamic report = exec.Report;
            dynamic section = report.NewReportSection((object)title, (object)"", (object)0);
            try { section.Body = body; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set report section body."); }
            return $"Report section '{title}' created for execution {executionId}";
        });
    }

    // ── Private Helpers (new) ─────────────────────────────────────────────────

    private dynamic GetOrLoadSeqFile(string filePath)
    {
        return _loadedSequenceFiles.TryGetValue(filePath, out var cached)
            ? cached
            : _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);
    }

    /// <summary>
    /// Saves a sequence file with a short retry/back-off. TestStand releases OS file handles
    /// asynchronously, so a Save issued right after another file operation — or while a stale
    /// engine instance (e.g. an orphaned MCP server / Sequence Editor from a prior session) still
    /// references the path — can transiently throw COMException "Error writing to file '...'".
    /// Retrying a few times with a back-off rides out that window; a genuinely persistent failure
    /// still surfaces (it is rethrown on the final attempt). Same rationale as the
    /// delete-before-create retry loop the integration TestDataBuilder uses for shared .seq files.
    /// </summary>
    private void SaveSequenceFileWithRetry(NiSequenceFile target, string filePath)
    {
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try { target.Save(filePath); return; }
            catch (Exception ex) when (attempt < maxAttempts &&
                       ex is System.Runtime.InteropServices.COMException or System.IO.IOException)
            {
                _logger.LogDebug(ex,
                    "Save of '{File}' failed (attempt {Attempt}/{Max}); retrying after back-off.",
                    filePath, attempt, maxAttempts);
                System.Threading.Thread.Sleep(300);
            }
        }
    }

    /// <summary>
    /// Reads a single string-valued step property by lookup path (e.g. <c>"MessageExpr"</c> or
    /// <c>"PropertyLoaderSources[0].Options.CommonOptions.Source.Location"</c>). Returns null when
    /// the path is absent or unreadable. Used by configuration round-trip tests.
    /// </summary>
    internal string? ReadStepPropertyString(string filePath, string sequenceName, string stepGroup,
        string stepName, string lookupPath)
    {
        var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
        NiSequence seq  = seqFile.GetSequenceByName(sequenceName);
        NiStep     step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
        try { return step.AsPropertyObject().GetValString(lookupPath, 0); }
        catch { return null; }
    }

    /// <summary>
    /// Reads a single numeric step property by lookup path (e.g. <c>"TimeToWait"</c>).
    /// Returns null when the path is absent or unreadable. Used by configuration round-trip tests.
    /// </summary>
    internal double? ReadStepPropertyNumber(string filePath, string sequenceName, string stepGroup,
        string stepName, string lookupPath)
    {
        var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
        NiSequence seq  = seqFile.GetSequenceByName(sequenceName);
        NiStep     step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
        try { return step.AsPropertyObject().GetValNumber(lookupPath, 0); }
        catch { return null; }
    }

    /// <summary>
    /// Returns <paramref name="targetPath"/> rewritten as a path relative to
    /// <paramref name="fromDirectory"/>. Falls back to <paramref name="targetPath"/>
    /// unchanged if the paths cannot be relativized (e.g. different drives) or
    /// if <paramref name="targetPath"/> is already relative.
    /// </summary>
    internal static string MakeRelativePath(string fromDirectory, string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))    return targetPath;
        if (!Path.IsPathRooted(targetPath))      return targetPath;        // already relative
        if (string.IsNullOrEmpty(fromDirectory)) return targetPath;

        try
        {
            string fromFull = Path.GetFullPath(
                fromDirectory.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? fromDirectory
                    : fromDirectory + Path.DirectorySeparatorChar);
            string toFull   = Path.GetFullPath(targetPath);

            var fromUri = new Uri(fromFull);
            var toUri   = new Uri(toFull);

            // Different schemes (e.g. file vs. UNC) — give up and keep absolute.
            if (fromUri.Scheme != toUri.Scheme) return targetPath;

            string rel = Uri.UnescapeDataString(
                            fromUri.MakeRelativeUri(toUri).ToString())
                         .Replace('/', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? targetPath : rel;
        }
        catch
        {
            return targetPath;
        }
    }

    private static int ParseStepGroup(string stepGroup) => stepGroup.ToLowerInvariant() switch
    {
        "setup"   => 0,
        "main"    => 1,
        "cleanup" => 2,
        _         => 1
    };

    private static string MapPostAction(string action) => action.ToLowerInvariant() switch
    {
        "break"      => "Break",
        "terminate"  => "Terminate",
        "goto"       => "Goto",
        "gotostep"   => "Goto",
        "callback"   => "Cback",
        "cback"      => "Cback",
        _            => "Next"  // NextStep
    };

    private void SetPropertyValueByType(dynamic propBlock, string name, string value, int propType)
    {
        try
        {
            if (propType == 3 && double.TryParse(value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                propBlock.SetValNumber(name, 0, d);
            else if (propType == 2 && bool.TryParse(value, out var b))
                propBlock.SetValBoolean(name, 0, b);
            else
            {
                // A named/enum-typed target (propType 4) rejects a plain string set; retry
                // coercing for-this-operation so an enum default can be set by its label.
                try { propBlock.SetValString(name, 0, value); }
                catch { propBlock.SetValString(name,
                    (int)NiPropOptions.PropOption_CoerceToEnum, value); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to set property '{Name}' value by type.", name); }
    }

    private List<ParameterInfo> MapParameters(dynamic paramBlock)
    {
        var parms = new List<ParameterInfo>();
        try
        {
            var propObj  = (object)paramBlock;
            var propType = propObj.GetType();
            int count = Convert.ToInt32(propType.InvokeMember("GetNumSubProperties",
                _comFlags, null, propObj, new object[] { "" }));

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic prop = propType.InvokeMember("GetNthSubProperty",
                        _comFlags, null, propObj, new object[] { "", i, 0 })!;
                    // PropertyObject has no `TypeName` property — the human-readable type
                    // name comes from GetTypeDisplayString (same as MapVariables).
                    string paramDataType = "";
                    try { paramDataType = (string)prop.GetTypeDisplayString("", (object)0); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to get type display string for parameter."); }
                    var pi = new ParameterInfo
                    {
                        Name     = (string)prop.Name,
                        DataType = paramDataType
                    };
                    try
                    {
                        var propObj2 = (object)prop;
                        int flags2 = Convert.ToInt32(propObj2.GetType().InvokeMember("GetFlags",
                            _comFlags, null, propObj2, new object[] { "", 0 }));
                        // PropFlags_PassByReference=4, PropFlags_Output=1 (direction bit 0x100 or similar)
                        // Fall back: treat bit 2 as Output, any PassByReference flag as InOut
                        pi.PassByReference = (flags2 & 4) != 0;
                        pi.Direction = (flags2 & 4) != 0 ? "InOut"
                                     : (flags2 & 2) != 0 ? "Output"
                                     : "Input";
                    }
                    catch { pi.Direction = "Input"; }
                    pi.DefaultValue = TryGetValue(prop);
                    parms.Add(pi);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read parameter at index {Index} in MapParameters.", i); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate sub-properties in MapParameters."); }
        return parms;
    }

    // ── Undo/Redo ─────────────────────────────────────────────────────────────
    //
    // IMPORTANT: automatic undo recording is a Sequence Editor (UI) feature. The
    // headless Engine API exposes NO per-file undo stack — neither SequenceFile nor
    // Engine has an UndoStack property, and edits performed through the Engine API are
    // never recorded for undo. Engine.NewUndoStack() only creates a detached, empty
    // stack; worse, holding such a COM object across engine shutdown makes the process
    // hang on teardown. We therefore expose undo/redo as honest no-ops that report
    // "nothing to undo" rather than fabricating COM state. Undo group bookkeeping is
    // tracked purely in memory so callers (and the MCP tools) behave predictably.

    private readonly Dictionary<string, string> _undoGroups =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<UndoStackInfo> GetUndoStackAsync(string? filePath = null)
    {
        EnsureConnected();
        return await Task.Run(() => new UndoStackInfo
        {
            FilePath     = filePath,
            CanUndo      = false,
            CanRedo      = false,
            NumUndoItems = 0,
            NumRedoItems = 0
        });
    }

    /// <inheritdoc/>
    public Task<bool> UndoAsync(string? filePath = null)
    {
        EnsureConnected();
        // No automatic undo stack in the headless Engine API → nothing to undo.
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> RedoAsync(string? filePath = null)
    {
        EnsureConnected();
        // No automatic redo stack in the headless Engine API → nothing to redo.
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public async Task BeginUndoGroupAsync(string groupName, string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() =>
            _undoGroups[string.IsNullOrEmpty(filePath) ? "<engine>" : filePath!] = groupName);
    }

    /// <inheritdoc/>
    public async Task EndUndoGroupAsync(string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() =>
            _undoGroups.Remove(string.IsNullOrEmpty(filePath) ? "<engine>" : filePath!));
    }

    /// <inheritdoc/>
    public async Task CancelUndoGroupAsync(string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() =>
            _undoGroups.Remove(string.IsNullOrEmpty(filePath) ? "<engine>" : filePath!));
    }

    // ── Sequence File Comparison ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SequenceFileDiff> CompareSequenceFilesAsync(
        string filePath1, string filePath2)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf1 = GetOrLoadSeqFile(filePath1);
            var sf2 = GetOrLoadSeqFile(filePath2);

            var diff = new SequenceFileDiff
            {
                File1 = filePath1,
                File2 = filePath2
            };

            // Build sequence name sets (explicit cast required: dynamic args make return type dynamic)
            var seqs1 = (List<string>)CollectSequenceNames(sf1);
            var seqs2 = (List<string>)CollectSequenceNames(sf2);

            // HashSet membership turns the O(n·m) name diff into O(n+m).
            var set1 = new HashSet<string>(seqs1);
            var set2 = new HashSet<string>(seqs2);

            diff.SequencesOnlyInFile1.AddRange(seqs1.Where(n => !set2.Contains(n)));
            diff.SequencesOnlyInFile2.AddRange(seqs2.Where(n => !set1.Contains(n)));

            // Compare sequences present in both files
            foreach (var name in seqs1.Intersect(seqs2))
            {
                try
                {
                    dynamic seq1 = sf1.GetSequenceByName(name);
                    dynamic seq2 = sf2.GetSequenceByName(name);
                    var seqDiff  = CompareSequences(seq1, seq2, name);

                    bool hasDiff = seqDiff.StepDiffs.Count > 0
                        || seqDiff.LocalVariableDiffs.Count > 0
                        || seqDiff.ParameterDiffs.Count > 0
                        || seqDiff.PropertyDiffs.Count > 0;

                    if (hasDiff) diff.ModifiedSequences.Add(seqDiff);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare sequence '{SeqName}' between files.", name); }
            }

            diff.TotalDifferences =
                diff.SequencesOnlyInFile1.Count +
                diff.SequencesOnlyInFile2.Count +
                diff.ModifiedSequences.Sum(s =>
                    s.StepDiffs.Count +
                    s.LocalVariableDiffs.Count +
                    s.ParameterDiffs.Count +
                    s.PropertyDiffs.Count);

            return diff;
        });
    }

    private static List<string> CollectSequenceNames(dynamic sf)
    {
        var names = new List<string>();
        int count = 0;
        try { count = Convert.ToInt32((object)sf.NumSequences); } catch (Exception) { /* best-effort: read NumSequences for comparison — intentionally ignored */ }
        for (int i = 0; i < count; i++)
        {
            try { names.Add((string)sf.GetSequence(i).Name); } catch (Exception) { /* best-effort: read sequence name at index for comparison — intentionally ignored */ }
        }
        return names;
    }

    private SequenceDiff CompareSequences(dynamic seq1, dynamic seq2, string seqName)
    {
        var diff = new SequenceDiff { SequenceName = seqName };

        // Compare sequence-level properties
        string[] seqProps = { "Description", "GotoCleanupOnFail", "DisableResults" };
        foreach (var p in seqProps)
        {
            try
            {
                var v1 = ((object)seq1).GetType().InvokeMember(p, _comFlags, null, seq1, null)?.ToString();
                var v2 = ((object)seq2).GetType().InvokeMember(p, _comFlags, null, seq2, null)?.ToString();
                if (v1 != v2) diff.PropertyDiffs.Add($"{p}: '{v1}' → '{v2}'");
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare sequence property '{Prop}' in CompareSequences.", p); }
        }

        // Compare steps in each group
        string[] groupNames = { "Setup", "Main", "Cleanup" };
        for (int g = 0; g <= 2; g++)
        {
            var steps1 = (Dictionary<string, string>)CollectStepNames(seq1, g);
            var steps2 = (Dictionary<string, string>)CollectStepNames(seq2, g);

            foreach (var s in steps1.Keys.Where(n => !steps2.ContainsKey(n)))
                diff.StepDiffs.Add(new StepDiff
                {
                    DiffType  = "Removed",
                    StepName  = s,
                    StepGroup = groupNames[g],
                    StepType  = steps1[s]
                });

            foreach (var s in steps2.Keys.Where(n => !steps1.ContainsKey(n)))
                diff.StepDiffs.Add(new StepDiff
                {
                    DiffType  = "Added",
                    StepName  = s,
                    StepGroup = groupNames[g],
                    StepType  = steps2[s]
                });

            foreach (var s in steps1.Keys.Intersect(steps2.Keys))
            {
                try
                {
                    var step1 = seq1.GetStepByName(s, (object)g);
                    var step2 = seq2.GetStepByName(s, (object)g);
                    var changed = CompareStepProperties(step1, step2);
                    if (changed.Count > 0)
                        diff.StepDiffs.Add(new StepDiff
                        {
                            DiffType          = "Modified",
                            StepName          = s,
                            StepGroup         = groupNames[g],
                            StepType          = steps1[s],
                            ChangedProperties = changed
                        });
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare step '{Step}' in group {Group}.", s, g); }
            }
        }

        // Compare local variables
        diff.LocalVariableDiffs.AddRange(ComparePropertyBlock(seq1.Locals, seq2.Locals, "Locals"));

        // Compare parameters
        try { diff.ParameterDiffs.AddRange(ComparePropertyBlock(seq1.Parameters, seq2.Parameters, "Parameters")); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare sequence Parameters property block."); }

        return diff;
    }

    private static Dictionary<string, string> CollectStepNames(dynamic seq, int group)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        int count = 0;
        try { count = Convert.ToInt32((object)seq.GetNumSteps((object)group)); } catch (Exception) { /* best-effort: get step count for comparison — intentionally ignored */ }
        for (int i = 0; i < count; i++)
        {
            try
            {
                dynamic step = seq.GetStep(i, (object)group);
                string  name = (string)step.Name;
                string  type = "";
                try { type = (string)step.StepType.Name; } catch (Exception) { /* best-effort: read step type name for comparison — intentionally ignored */ }
                dict[name] = type;
            }
            catch (Exception) { /* best-effort: read step entry at index for comparison — intentionally ignored */ }
        }
        return dict;
    }

    private List<string> CompareStepProperties(dynamic step1, dynamic step2)
    {
        var changed = new List<string>();
        string[] props = {
            "RunMode", "PreExpression", "PostExpression", "StatusExpression",
            "Comment", "StepEnabled"
        };
        foreach (var p in props)
        {
            try
            {
                var v1 = ((object)step1).GetType().InvokeMember(p, _comFlags, null, step1, null)?.ToString();
                var v2 = ((object)step2).GetType().InvokeMember(p, _comFlags, null, step2, null)?.ToString();
                if (v1 != v2) changed.Add($"{p}: '{v1}' → '{v2}'");
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare step property '{Prop}'.", p); }
        }

        // Compare module expression
        try
        {
            var m1 = (string)((NiStep)(object)step1).AsPropertyObject().GetValString("Module.Expression", 0);
            var m2 = (string)((NiStep)(object)step2).AsPropertyObject().GetValString("Module.Expression", 0);
            if (m1 != m2) changed.Add($"Module.Expression: '{m1}' → '{m2}'");
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare step Module.Expression property."); }

        // Compare adapter
        try
        {
            var a1 = TryGetString(step1, "AdapterKeyName");
            var a2 = TryGetString(step2, "AdapterKeyName");
            if (a1 != a2) changed.Add($"Adapter: '{a1}' → '{a2}'");
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare step AdapterKeyName property."); }

        return changed;
    }

    // ── Native FileDiffer (detailed, TestStand-faithful diff) ──────────────────

    /// <inheritdoc/>
    public async Task<FileDifferReport> DiffSequenceFilesAsync(string filePath1, string filePath2)
    {
        EnsureConnected();
        if (!System.IO.File.Exists(filePath1))
            throw new System.IO.FileNotFoundException("Diff file 1 not found.", filePath1);
        if (!System.IO.File.Exists(filePath2))
            throw new System.IO.FileNotFoundException("Diff file 2 not found.", filePath2);

        return await Task.Run(() =>
        {
            var diag = new System.Text.StringBuilder();
            string diagPath = Path.Combine(Path.GetTempPath(), "ts_differ_diag.txt");
            void Log(string msg) { diag.AppendLine(msg); }
            void Flush() { try { System.IO.File.WriteAllText(diagPath, diag.ToString()); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to write differ diagnostics."); } }

            // FileDiffer.exe ships in the connected engine's Bin directory — never hard-code a
            // release. Resolve on FileDiffer.exe ITSELF: an engine-only install carries the engine
            // but not the differ, and probing for a different tool would either reject the correct
            // Bin or hand back one that does not hold the differ at all.
            var (binDir, probed) = ResolveTestStandBin("FileDiffer.exe");
            string differExe = !string.IsNullOrEmpty(binDir)
                ? Path.Combine(binDir, "FileDiffer.exe")
                : "FileDiffer.exe";
            if (!System.IO.File.Exists(differExe))
                throw new InvalidOperationException(
                    $"FileDiffer.exe not found at: {differExe}." +
                    (string.IsNullOrEmpty(probed) ? "" : $" Probed: {probed}"));

            string reportPath = Path.Combine(Path.GetTempPath(),
                "ts_mcp_diff_" + Path.GetFileNameWithoutExtension(filePath1) + "_vs_"
                + Path.GetFileNameWithoutExtension(filePath2) + ".xml");
            try { if (System.IO.File.Exists(reportPath)) System.IO.File.Delete(reportPath); }
            catch (Exception) { /* best-effort: clear stale report */ }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = differExe,
                Arguments              = $"/GenerateReport \"{reportPath}\" \"{filePath1}\" \"{filePath2}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            ApplyTestStandToolChildEnv(psi);
            Log($"Launching: {differExe} {psi.Arguments}");
            Flush();

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start FileDiffer.exe.");
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            bool exited   = proc.WaitForExit(120_000);
            if (!exited)
            {
                try { proc.Kill(); } catch (Exception) { /* best-effort: kill timed-out FileDiffer */ }
                throw new InvalidOperationException("FileDiffer.exe timed out after 120 seconds.");
            }
            Log($"FileDiffer exit code: {proc.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout)) Log($"stdout: {stdout.Trim()}");
            if (!string.IsNullOrWhiteSpace(stderr)) Log($"stderr: {stderr.Trim()}");

            // FileDiffer reliably WRITES the report but then crashes on teardown with a negative exit
            // code (0xC0000409), exactly like the engine / AnalyzerApp. Success is therefore judged by
            // the report existing and parsing — NOT by the exit code.
            if (!System.IO.File.Exists(reportPath))
            {
                Flush();
                throw new InvalidOperationException(
                    $"FileDiffer.exe produced no report (exit {proc.ExitCode}). stderr: {stderr.Trim()}");
            }

            string reportXml = System.IO.File.ReadAllText(reportPath, System.Text.Encoding.UTF8);
            var report = ParseDifferReport(reportXml, filePath1, filePath2, Log);
            try { System.IO.File.Delete(reportPath); } catch (Exception) { /* best-effort cleanup */ }

            Log($"Parsed {report.Changes.Count} change(s); identical={report.Identical}");
            Flush();
            return report;
        });
    }

    /// <summary>
    /// Normalises a child <see cref="System.Diagnostics.ProcessStartInfo"/> environment so NI tools
    /// (AnalyzerApp.exe, FileDiffer.exe) and the LabVIEW RTE they may load find the system variables
    /// they require — notably ProgramFiles(x86), whose absence crashes lvrt.dll with 0xC0000409. The
    /// MCP host can inherit a heavily reduced environment, so the values are derived from the OS
    /// rather than from this process's own (possibly empty) variables.
    /// Requires <c>UseShellExecute=false</c>.
    /// <para>
    /// <b>Fill, never overwrite.</b> This host is x86, so
    /// <see cref="Environment.SpecialFolder.ProgramFiles"/> and
    /// <see cref="Environment.SpecialFolder.CommonProgramFiles"/> are WOW64-redirected to the "(x86)"
    /// paths. Writing those into the 64-bit variable NAMES is correct for a 32-bit child (which is
    /// what Windows would hand it anyway) but wrong for a 64-bit one — and with an explicit
    /// environment block Windows performs no redirection of its own to correct it. Since the tools
    /// may legitimately resolve to a 64-bit install, an already-present value is therefore left
    /// alone and only genuinely missing variables are supplied. <c>ProgramW6432</c> /
    /// <c>CommonProgramW6432</c> name the 64-bit roots unambiguously in either bitness and are
    /// passed through, as 64-bit-aware NI components read them.
    /// </para>
    /// </summary>
    internal static void ApplyTestStandToolChildEnv(System.Diagnostics.ProcessStartInfo psi)
    {
        // Supply only what the child does not already have — see the "fill, never overwrite" note.
        void Ensure(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (psi.Environment.TryGetValue(key, out var existing) && !string.IsNullOrEmpty(existing)) return;
            psi.Environment[key] = value;
        }

        var pf86  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var cpf86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
        // %ProgramW6432% / %CommonProgramW6432% are visible from WOW64 and always name the 64-bit
        // roots; on a 32-bit-only Windows they are absent and the plain folders are the 64-bit ones.
        var pf64  = Environment.GetEnvironmentVariable("ProgramW6432")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var cpf64 = Environment.GetEnvironmentVariable("CommonProgramW6432")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);

        Ensure("ProgramFiles(x86)",       pf86);
        Ensure("CommonProgramFiles(x86)", cpf86);
        Ensure("ProgramW6432",            pf64);
        Ensure("CommonProgramW6432",      cpf64);
        Ensure("ProgramFiles",            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Ensure("CommonProgramFiles",      Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        Ensure("ProgramData",             Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("ALLUSERSPROFILE",         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("ComSpec",                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        Ensure("TMP",                     Path.GetTempPath());
        Ensure("TEMP",                    Path.GetTempPath());
        Ensure("NUMBER_OF_PROCESSORS",    Environment.ProcessorCount.ToString());
    }

    /// <summary>
    /// Reconstructs the FULL logon environment (Machine then User, per Windows semantics) onto a child
    /// <see cref="System.Diagnostics.ProcessStartInfo"/>. A Claude/stdio-launched MCP host can inherit a
    /// heavily REDUCED environment (empirically ~15 vars vs. ~81 for a normal shell launch) that is
    /// MISSING the NI/TestStand variables (<c>TESTSTAND</c>, <c>TESTSTANDBIN</c>, <c>NIEXTCCOMPILERSUPP</c>,
    /// <c>NIDAQMXSWITCHDIR</c>, …) and core folders (<c>ProgramData</c>, <c>ALLUSERSPROFILE</c>,
    /// <c>PUBLIC</c>, <c>CommonProgramFiles(x86)</c>, …) that a LabVIEW/TestStand native load needs to
    /// resolve its DLL/license chain. That single discrepancy is why a <c>.lvlibp</c> prototype load
    /// FAULTS (native delay-load 0xC06D007E ERROR_MOD_NOT_FOUND) when the worker inherits the MCP host's
    /// stripped env, yet SUCCEEDS when the same exe is launched from a normal PowerShell (full env).
    /// Composing the child's env from the OS registry removes the discrepancy so the worker sees the
    /// same environment a real logon session has. Requires <c>UseShellExecute=false</c>. Variables the
    /// launcher injected (e.g. CLAUDECODE) are preserved; registry values win over the inherited ones.
    /// </summary>
    private static void ComposeChildEnvironmentFromRegistry(System.Diagnostics.ProcessStartInfo psi)
    {
        static string Expand(string? v) => string.IsNullOrEmpty(v) ? "" : Environment.ExpandEnvironmentVariables(v!);

        void Merge(EnvironmentVariableTarget target)
        {
            System.Collections.IDictionary vars;
            try { vars = Environment.GetEnvironmentVariables(target); }
            catch { return; }
            foreach (System.Collections.DictionaryEntry kv in vars)
            {
                var key = kv.Key as string;
                var val = kv.Value?.ToString();
                if (string.IsNullOrEmpty(key) || val == null) continue;
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase)) continue; // merged below
                psi.Environment[key!] = Expand(val);   // User (applied 2nd) overrides Machine (1st)
            }
        }
        Merge(EnvironmentVariableTarget.Machine);
        Merge(EnvironmentVariableTarget.User);

        // PATH = Machine + User (+ anything the parent already had), deduped, order-preserving —
        // matching how Windows builds a logon PATH.
        string machine  = Expand(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        string user     = Expand(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
        string existing = psi.Environment.TryGetValue("PATH", out var ep) ? (ep ?? "") : "";
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var seg in string.Join(";", machine, user, existing).Split(';'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            if (seen.Add(s.TrimEnd('\\'))) parts.Add(s);
        }
        if (parts.Count > 0) psi.Environment["PATH"] = string.Join(";", parts);

        // Belt-and-suspenders: supply the volatile standard folders (they are not in the registry's
        // environment keys at all) for whatever is still missing. Fill-only, so the values merged
        // from the registry above stay authoritative.
        ApplyTestStandToolChildEnv(psi);
    }

    /// <summary>
    /// Parses a native FileDiffer report (DifferReport XML) into a <see cref="FileDifferReport"/>:
    /// per-file tallies from the Header, plus a flat list of the leaf changes. The report is a
    /// row/column tree (col0 = node name + BlockLevel, col1 = file-1 value, col2 = file-2 value +
    /// StyleID); we walk it depth-first, track the ancestor path via BlockLevel, and emit only the
    /// real leaf changes (Insert/Delete/ValueChange/Conflict/Move) — ID_Children rows are context.
    /// The report carries the default DifferReport namespace, so all matching is by local-name.
    /// </summary>
    internal static FileDifferReport ParseDifferReport(string reportXml, string file1, string file2, Action<string> Log)
    {
        var report = new FileDifferReport { File1 = file1, File2 = file2 };
        if (string.IsNullOrWhiteSpace(reportXml)) return report;

        var doc = new System.Xml.XmlDocument();
        try { doc.LoadXml(reportXml); }
        catch (Exception ex) { Log($"Differ report parse error: {ex.Message}"); return report; }

        static System.Xml.XmlNode? Child(System.Xml.XmlNode n, string local)
        {
            foreach (System.Xml.XmlNode c in n.ChildNodes)
                if (string.Equals(c.LocalName, local, StringComparison.Ordinal)) return c;
            return null;
        }

        // ── Header: per-file change tallies (Count attribute on Changes/Insertions/Deletions) ──
        var fileNodes = doc.SelectNodes("//*[local-name()='Header']/*[local-name()='File']");
        if (fileNodes != null)
        {
            foreach (System.Xml.XmlNode fileNode in fileNodes)
            {
                int Count(string local)
                {
                    if (Child(fileNode, local) is System.Xml.XmlElement e
                        && int.TryParse(e.GetAttribute("Count"), out int v)) return v;
                    return 0;
                }
                report.FileSummaries.Add(new FileDifferFileSummary
                {
                    Name       = Child(fileNode, "Name")?.InnerText?.Trim() ?? "",
                    Path       = Child(fileNode, "Path")?.InnerText?.Trim() ?? "",
                    Changes    = Count("Changes"),
                    Insertions = Count("Insertions"),
                    Deletions  = Count("Deletions"),
                });
            }
        }

        // ── Rows: DFS over the property tree; build the path, emit leaf changes ──
        var pathStack = new List<string>();
        var rowNodes  = doc.SelectNodes("//*[local-name()='RowDifference']");
        if (rowNodes != null)
        {
            foreach (System.Xml.XmlNode row in rowNodes)
            {
                var cells = new List<System.Xml.XmlNode>();
                foreach (System.Xml.XmlNode c in row.ChildNodes)
                    if (string.Equals(c.LocalName, "ColDifference", StringComparison.Ordinal)) cells.Add(c);
                if (cells.Count == 0) continue;

                // col0 carries the node name + BlockLevel (nesting depth).
                var (name0, level0) = DifferNameAndLevel(cells[0]);
                if (level0 >= 0 && !string.IsNullOrEmpty(name0))
                {
                    while (pathStack.Count > level0) pathStack.RemoveAt(pathStack.Count - 1);
                    while (pathStack.Count < level0) pathStack.Add("");
                    pathStack.Add(name0);
                }

                // The change indicator (StyleID) sits on the result/last column.
                string style = "";
                for (int i = cells.Count - 1; i >= 0; i--)
                {
                    string s = DifferStyleOf(cells[i]);
                    if (!string.IsNullOrEmpty(s)) { style = s; break; }
                }

                string? changeType = MapDifferStyle(style);
                if (changeType == null) continue;   // ID_Children / NoDifference / ignored → context only

                int upto = level0 >= 0 ? Math.Min(level0, pathStack.Count) : pathStack.Count;
                var ancestors = new List<string>();
                for (int i = 0; i < upto; i++)
                    if (!string.IsNullOrEmpty(pathStack[i])) ancestors.Add(pathStack[i]);

                report.Changes.Add(new FileDifferChange
                {
                    ChangeType = changeType,
                    Path       = string.Join(" > ", ancestors),
                    Name       = name0,
                    Level      = level0 < 0 ? 0 : level0,
                    File1Value = cells.Count > 1 ? DifferCellText(cells[1]) : "",
                    File2Value = cells.Count > 2 ? DifferCellText(cells[2]) : "",
                });
            }
        }

        report.TotalDifferences = report.FileSummaries.Sum(s => s.Changes + s.Insertions + s.Deletions);
        if (report.TotalDifferences == 0) report.TotalDifferences = report.Changes.Count;
        return report;
    }

    /// <summary>Reads a ColDifference cell's DifferenceInfo Text + BlockLevel (name column).</summary>
    private static (string name, int level) DifferNameAndLevel(System.Xml.XmlNode cell)
    {
        foreach (System.Xml.XmlNode c in cell.ChildNodes)
        {
            if (string.Equals(c.LocalName, "DifferenceInfo", StringComparison.Ordinal)
                && c is System.Xml.XmlElement e)
            {
                string text = "";
                foreach (System.Xml.XmlNode t in e.ChildNodes)
                    if (string.Equals(t.LocalName, "Text", StringComparison.Ordinal))
                    { text = t.InnerText?.Trim() ?? ""; break; }
                int level = int.TryParse(e.GetAttribute("BlockLevel"), out int bl) ? bl : -1;
                return (text, level);
            }
        }
        return ("", -1);
    }

    /// <summary>Reads a ColDifference cell's StyleID (empty when the cell has no DifferenceInfo).</summary>
    private static string DifferStyleOf(System.Xml.XmlNode cell)
    {
        foreach (System.Xml.XmlNode c in cell.ChildNodes)
            if (string.Equals(c.LocalName, "DifferenceInfo", StringComparison.Ordinal)
                && c is System.Xml.XmlElement e)
                return e.GetAttribute("StyleID") ?? "";
        return "";
    }

    /// <summary>Reads a ColDifference cell's displayed text (or "" for an Empty side).</summary>
    private static string DifferCellText(System.Xml.XmlNode cell)
    {
        foreach (System.Xml.XmlNode c in cell.ChildNodes)
        {
            if (string.Equals(c.LocalName, "DifferenceInfo", StringComparison.Ordinal))
            {
                foreach (System.Xml.XmlNode t in c.ChildNodes)
                    if (string.Equals(t.LocalName, "Text", StringComparison.Ordinal))
                        return t.InnerText?.Trim() ?? "";
                return "";
            }
            if (string.Equals(c.LocalName, "IsEmptyOrHashed", StringComparison.Ordinal))
                return string.Equals(c.InnerText?.Trim(), "Hashed", StringComparison.OrdinalIgnoreCase)
                    ? "(unchanged)" : "";
        }
        return "";
    }

    /// <summary>Maps a DifferReport StyleID to a public change type, or null for context/ignored rows.</summary>
    private static string? MapDifferStyle(string styleId) => styleId switch
    {
        "ID_Insert"        => "Insert",
        "ID_Delete"        => "Delete",
        "ID_ValueChange"   => "ValueChange",
        "ID_Conflict"      => "Conflict",
        "ID_NoDifference0" => "Moved",
        "ID_NoDifference1" => "MovedModified",
        _                  => null,
    };

    private List<string> ComparePropertyBlock(dynamic block1, dynamic block2, string prefix)
    {
        var diffs  = new List<string>();
        var names1 = (List<string>)GetSubPropertyNames(block1);
        var names2 = (List<string>)GetSubPropertyNames(block2);

        foreach (var n in names1.Where(x => !names2.Contains(x)))
            diffs.Add($"{prefix}.{n}: removed");
        foreach (var n in names2.Where(x => !names1.Contains(x)))
            diffs.Add($"{prefix}.{n}: added");

        foreach (var n in names1.Intersect(names2))
        {
            try
            {
                var v1 = TryGetSubPropertyValue(block1, n);
                var v2 = TryGetSubPropertyValue(block2, n);
                if (v1 != v2) diffs.Add($"{prefix}.{n}: '{v1}' → '{v2}'");
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to compare property block sub-property '{Name}' in '{Prefix}'.", n, prefix); }
        }
        return diffs;
    }

    private List<string> GetSubPropertyNames(dynamic block)
    {
        var names  = new List<string>();
        var propObj = (object)block;
        try
        {
            int count = Convert.ToInt32(propObj.GetType().InvokeMember(
                "GetNumSubProperties", _comFlags, null, propObj, new object[] { "" }));
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic p = propObj.GetType().InvokeMember(
                        "GetNthSubProperty", _comFlags, null, propObj,
                        new object[] { "", i, 0 })!;
                    names.Add((string)p.Name);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sub-property name at index {Index} in GetSubPropertyNames.", i); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to get sub-property count in GetSubPropertyNames."); }
        return names;
    }

    private static string TryGetSubPropertyValue(dynamic block, string name)
    {
        try { return block.GetValString(name, (object)0)?.ToString() ?? ""; }
        catch (Exception) { /* best-effort: probe sub-property as string — intentionally ignored */ }
        try { return block.GetValNumber(name, (object)0).ToString(); }
        catch (Exception) { /* best-effort: probe sub-property as number — intentionally ignored */ }
        try { return block.GetValBoolean(name, (object)0).ToString(); }
        catch (Exception) { /* best-effort: probe sub-property as boolean — intentionally ignored */ }
        return "";
    }

    // ── Sync Manager ─────────────────────────────────────────────────────────

    private dynamic GetSyncManager()
    {
        try { return ((dynamic)_engine!).SyncManager; }
        catch (Exception ex) { _logger.LogDebug(ex, "SyncManager not available, trying LocalProcessSyncMgr."); }
        try { return ((dynamic)_engine!).LocalProcessSyncMgr; }
        catch (Exception ex) { _logger.LogDebug(ex, "LocalProcessSyncMgr also not available."); }
        throw new InvalidOperationException(
            "TestStand SyncManager is not available. Ensure TestStand supports synchronization.");
    }

    /// <inheritdoc/>
    public async Task<List<SyncObjectInfo>> GetSyncObjectsAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<SyncObjectInfo>();

            // Return cached objects first
            foreach (var kvp in _syncObjects)
            {
                var info = new SyncObjectInfo { Name = kvp.Key };
                try
                {
                    string typeName = ((object)kvp.Value).GetType().Name;
                    info.Type = typeName.Contains("Semaphore") ? "Semaphore"
                        : typeName.Contains("Mutex") ? "Mutex"
                        : typeName.Contains("Queue") ? "Queue"
                        : typeName.Contains("Notification") ? "Notification"
                        : typeName.Contains("Rendezvous") ? "Rendezvous"
                        : "Unknown";
                    try { info.Properties["Count"] = (object)(int)kvp.Value.Count; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Count property on sync object '{Name}'.", kvp.Key); }
                    try { info.Properties["MaxCount"] = (object)(int)kvp.Value.MaxCount; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read MaxCount property on sync object '{Name}'.", kvp.Key); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to determine type of sync object '{Name}'.", kvp.Key); }
                result.Add(info);
            }

            // Also query engine sync manager for additional objects
            try
            {
                var mgr = GetSyncManager();
                int num = (int)mgr.NumSyncObjects;
                for (int i = 0; i < num; i++)
                {
                    try
                    {
                        string name = (string)mgr.GetSyncObjectNameByIndex((object)i);
                        if (!_syncObjects.ContainsKey(name))
                            result.Add(new SyncObjectInfo { Name = name, Type = "Unknown" });
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sync object name at index {Index} from sync manager.", i); }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to query engine sync manager for additional sync objects."); }

            return result;
        });
    }

    /// <inheritdoc/>
    public async Task CreateSyncObjectAsync(string name, string type,
        int initialValue = 1, int maxValue = 1)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var mgr = GetSyncManager();
            dynamic obj = type.ToLowerInvariant() switch
            {
                "semaphore" => mgr.NewSemaphore((object)name, (object)initialValue, (object)maxValue),
                "mutex"     => mgr.NewMutex((object)name),
                "queue"     => mgr.NewQueue((object)name, (object)maxValue, (object)0),
                "notification" => mgr.NewNotification((object)name),
                "rendezvous"   => mgr.NewRendezvous((object)name, (object)maxValue),
                _ => throw new ArgumentException($"Unknown sync object type: {type}. " +
                     "Valid types: Semaphore, Mutex, Queue, Notification, Rendezvous")
            };
            _syncObjects[name] = obj;
        });
    }

    /// <inheritdoc/>
    public async Task DeleteSyncObjectAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            if (_syncObjects.TryGetValue(name, out var obj))
            {
                try { obj.Delete(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete sync object '{Name}'.", name); }
                _syncObjects.Remove(name);
            }
        });
    }

    private dynamic GetSyncObject(string name, string expectedType)
    {
        if (_syncObjects.TryGetValue(name, out var cached))
            return cached;

        try
        {
            var mgr = GetSyncManager();
            int typeVal = expectedType.ToLowerInvariant() switch
            {
                "semaphore"    => 1,
                "mutex"        => 2,
                "queue"        => 3,
                "notification" => 4,
                "rendezvous"   => 5,
                _              => 0
            };
            var obj = mgr.GetSyncObject((object)name, (object)typeVal);
            _syncObjects[name] = obj;
            return obj;
        }
        catch
        {
            throw new KeyNotFoundException(
                $"Sync object '{name}' not found. Create it first with create_sync_object.");
        }
    }

    /// <inheritdoc/>
    public async Task SyncSemaphoreWaitAsync(string name, double timeoutSeconds = 30)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sem = GetSyncObject(name, "semaphore");
            int ms = timeoutSeconds < 0 ? -1 : (int)(timeoutSeconds * 1000);
            bool acquired;
            try { acquired = (bool)sem.Wait((object)ms); }
            catch { acquired = (bool)sem.TryWait(); }
            if (!acquired)
                throw new TimeoutException(
                    $"Semaphore '{name}' wait timed out after {timeoutSeconds}s.");
        });
    }

    /// <inheritdoc/>
    public async Task SyncSemaphoreReleaseAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "semaphore").Release());
    }

    /// <inheritdoc/>
    public async Task SyncMutexLockAsync(string name, double timeoutSeconds = 30)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var mutex = GetSyncObject(name, "mutex");
            int ms = timeoutSeconds < 0 ? -1 : (int)(timeoutSeconds * 1000);
            bool locked;
            try { locked = (bool)mutex.Lock((object)ms); }
            catch { locked = (bool)mutex.TryLock(); }
            if (!locked)
                throw new TimeoutException(
                    $"Mutex '{name}' lock timed out after {timeoutSeconds}s.");
        });
    }

    /// <inheritdoc/>
    public async Task SyncMutexUnlockAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "mutex").Unlock());
    }

    /// <inheritdoc/>
    public async Task SyncQueueEnqueueAsync(string name, string value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var queue = GetSyncObject(name, "queue");
            try { queue.Enqueue((object)value, (object)0); }
            catch { queue.Enqueue((object)value); }
        });
    }

    /// <inheritdoc/>
    public async Task<string> SyncQueueDequeueAsync(string name, double timeoutSeconds = 30)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var queue = GetSyncObject(name, "queue");
            int ms = timeoutSeconds < 0 ? -1 : (int)(timeoutSeconds * 1000);
            try
            {
                var val = queue.Dequeue((object)ms);
                return val?.ToString() ?? "";
            }
            catch
            {
                dynamic outVal = "";
                bool ok = (bool)queue.TryDequeue(ref outVal);
                if (!ok)
                    throw new TimeoutException(
                        $"Queue '{name}' dequeue timed out after {timeoutSeconds}s.");
                return outVal?.ToString() ?? "";
            }
        });
    }

    /// <inheritdoc/>
    public async Task SyncQueueFlushAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "queue").Flush());
    }

    /// <inheritdoc/>
    public async Task SyncNotificationSetAsync(string name, string value = "")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var notif = GetSyncObject(name, "notification");
            try { notif.Set((object)value); }
            catch { notif.Set(); }
        });
    }

    /// <inheritdoc/>
    public async Task SyncNotificationResetAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "notification").Reset());
    }

    /// <inheritdoc/>
    public async Task<string> SyncNotificationWaitAsync(string name, double timeoutSeconds = 30)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var notif = GetSyncObject(name, "notification");
            int ms = timeoutSeconds < 0 ? -1 : (int)(timeoutSeconds * 1000);
            try
            {
                var result = notif.WaitForNotification((object)ms);
                return result?.ToString() ?? "";
            }
            catch
            {
                dynamic outVal = "";
                bool ok = (bool)notif.TryWaitForNotification(ref outVal);
                if (!ok)
                    throw new TimeoutException(
                        $"Notification '{name}' wait timed out after {timeoutSeconds}s.");
                return outVal?.ToString() ?? "";
            }
        });
    }

    // ── Advanced Adapter Introspection ────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdapterDetailInfo> GetAdapterDetailsAsync(string adapterName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            string resolvedKey = ResolveAdapterKeyName(adapterName);
            int count = (int)_engine!.NumAdapters;
            for (int i = 0; i < count; i++)
            {
                dynamic adapter = _engine!.GetAdapter(i);
                string key = TryGetString(adapter, "KeyName");
                string name = TryGetString(adapter, "DisplayName");

                if (!string.Equals(key, adapterName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, adapterName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(key, resolvedKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new AdapterDetailInfo
                {
                    KeyName     = key,
                    DisplayName = name,
                    Type        = key,
                    IsConfigurable        = TryGetBool(adapter, "IsConfigurable"),
                    IsSupported           = TryGetBool(adapter, "IsSupported"),
                    Hidden                = TryGetBool(adapter, "Hidden"),
                    ShowArgsInStepDescription = TryGetBool(adapter, "ShowArgsInStepDescription"),
                    IconName    = TryGetString(adapter, "IconName")
                };

                // Collect additional string properties
                string[] extras = {
                    "Version", "ShortName", "DefaultCodeTemplatePath",
                    "DefaultModuleParameterHandlerPath"
                };
                foreach (var prop in extras)
                {
                    var val = TryGetStringOrNull(adapter, prop);
                    if (val != null) info.Properties[prop] = val;
                }

                return info;
            }
            throw new KeyNotFoundException(
                $"Adapter '{adapterName}' not found. Use get_loaded_adapters to list available adapters.");
        });
    }

    /// <inheritdoc/>
    public async Task<StepModuleInfo> GetStepModuleInfoAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            // The Step COM property is AdapterKeyName (e.g. "DotNet Adapter"),
            // NOT "AdapterName" — the latter does not exist and always read back "".
            string adapterKey  = TryGetString(step, "AdapterKeyName");
            // Resolve the friendly DisplayName from the loaded adapters by KeyName.
            // step.StepType.Adapter would return the step TYPE's default adapter, not
            // the adapter actually assigned to this step.
            string adapterDisp = ResolveAdapterDisplayName(adapterKey);

            var info = new StepModuleInfo
            {
                StepName          = stepName,
                AdapterName       = adapterKey,
                AdapterDisplayName = adapterDisp
            };

            // Common module properties
            dynamic mod = step.Module;
            string[] commonProps = {
                "VIPath", "FunctionName", "LibraryFilePath", "AssemblyName",
                "ClassName", "MethodName", "ModulePath", "DllPath",
                "Expression", "SequenceName", "SequenceFilePath",
                "ConnectionType", "ServerName", "CallOptions",
                "InitCode", "CleanupCode", "DebuggingEnabled",
                "FunctionType", "ParameterCount", "Overloads"
            };

            foreach (var prop in commonProps)
            {
                try
                {
                    var val = ((object)mod).GetType().InvokeMember(
                        prop, _comFlags, null, mod, null);
                    if (val != null)
                        info.ModuleProperties[prop] = val;
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module property '{Prop}' for step '{Step}'.", prop, stepName); }
            }

            // Try generic property object access for remaining module props
            try
            {
                NiPropertyObject propObj = ((NiStep)(object)step).AsPropertyObject();
                string[] modulePaths = {
                    "Module.VIPath", "Module.FunctionName", "Module.LibraryFilePath",
                    "Module.AssemblyName", "Module.ClassName", "Module.MethodName",
                    "Module.ModulePath", "Module.Expression"
                };
                foreach (var mp in modulePaths)
                {
                    string key = mp[(mp.LastIndexOf('.') + 1)..];
                    if (info.ModuleProperties.ContainsKey(key)) continue;
                    try
                    {
                        var val = propObj.GetValString(mp, 0);
                        if (!string.IsNullOrEmpty(val))
                            info.ModuleProperties[key] = (object)(string)val;
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module property path '{Path}' for step '{Step}'.", mp, stepName); }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module properties via property object for step '{Step}'.", stepName); }

            // NI_LV utility steps (e.g. NI_LV_RunVIAsynchronously = "Run VI Asynchronously") use the
            // None adapter and store their REAL VI config in the step's own VIModule.ViCall — NOT
            // step.Module, which for these steps returns the async-launch SequenceCall wrapper
            // (SequenceName "MainSequence", SequenceFilePath "Evaluate(Step.SequenceFileExpr)") and is
            // therefore misleading. Surface the actual VI + async target so callers see what runs.
            try
            {
                NiPropertyObject spo = ((NiStep)(object)step).AsPropertyObject();
                string viModuleViPath = "";
                try { viModuleViPath = spo.GetValString("VIModule.ViCall.VIPath", 0); }
                catch (Exception ex) { _logger.LogDebug(ex, "No VIModule.ViCall.VIPath on step '{Step}'.", stepName); }
                if (!string.IsNullOrEmpty(viModuleViPath))
                {
                    info.ModuleProperties["VIModuleVIPath"] = viModuleViPath;
                    foreach (var (path, key) in new[]
                    {
                        ("VIModule.ViCall.Namespace", "VIModuleNamespace"),
                        ("TS.SData.SeqNameExpr",      "AsyncSequenceNameExpr"),
                        ("TS.SData.SFPathExpr",       "AsyncSequenceFileExpr"),
                    })
                    {
                        try { var v = spo.GetValString(path, 0); if (!string.IsNullOrEmpty(v)) info.ModuleProperties[key] = (object)v; }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read '{Path}' for LV utility step '{Step}'.", path, stepName); }
                    }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to inspect VIModule for step '{Step}'.", stepName); }

            return info;
        });
    }

    // ── Search ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SearchResult> SearchStepsAsync(string filePath, string pattern,
        string searchIn = "all", bool caseSensitive = false)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            var result = new SearchResult
            {
                SearchPattern = pattern,
                SearchIn      = searchIn
            };

            int numSeqs = 0;
            try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get sequence count for step search."); }

            string[] groupNames = { "Setup", "Main", "Cleanup" };

            for (int si = 0; si < numSeqs; si++)
            {
                dynamic seq;
                string seqName;
                try
                {
                    seq     = sf.GetSequence(si);
                    seqName = (string)seq.Name;
                }
                catch { continue; }

                for (int g = 0; g <= 2; g++)
                {
                    int stepCount = 0;
                    try { stepCount = Convert.ToInt32((object)seq.GetNumSteps((object)g)); }
                    catch { continue; }

                    for (int i = 0; i < stepCount; i++)
                    {
                        dynamic step;
                        string sName;
                        try { step = seq.GetStep(i, (object)g); sName = (string)step.Name; }
                        catch { continue; }

                        var matches = FindMatchesInStep(
                            step, sName, seqName, groupNames[g], filePath,
                            pattern, searchIn, comparison);
                        result.Matches.AddRange(matches);
                    }
                }

                // Also search local variables
                if (searchIn is "all" or "variables")
                {
                    try
                    {
                        var locals   = seq.Locals;
                        var propObj  = (object)locals;
                        int varCount = Convert.ToInt32(propObj.GetType().InvokeMember(
                            "GetNumSubProperties", _comFlags, null, propObj,
                            new object[] { "" }));

                        for (int vi = 0; vi < varCount; vi++)
                        {
                            try
                            {
                                dynamic v    = propObj.GetType().InvokeMember(
                                    "GetNthSubProperty", _comFlags, null, propObj,
                                    new object[] { "", vi, 0 })!;
                                string vName = (string)v.Name;
                                if (vName.IndexOf(pattern, comparison) >= 0)
                                {
                                    result.Matches.Add(new SearchMatch
                                    {
                                        FilePath     = filePath,
                                        SequenceName = seqName,
                                        StepGroup    = "",
                                        StepName     = "",
                                        MatchedText  = vName,
                                        MatchType    = "LocalVariable",
                                        PropertyPath = $"Locals.{vName}"
                                    });
                                }
                            }
                            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read local variable at index {Index} while searching.", vi); }
                        }
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate local variables for sequence '{Seq}' while searching.", seqName); }
                }
            }

            result.TotalMatches = result.Matches.Count;
            return result;
        });
    }

    private static List<SearchMatch> FindMatchesInStep(
        dynamic step, string stepName, string seqName, string groupName,
        string filePath, string pattern, string searchIn, StringComparison cmp)
    {
        var matches = new List<SearchMatch>();

        void AddMatch(string matchedText, string matchType, string propPath) =>
            matches.Add(new SearchMatch
            {
                FilePath     = filePath,
                SequenceName = seqName,
                StepGroup    = groupName,
                StepName     = stepName,
                MatchedText  = matchedText,
                MatchType    = matchType,
                PropertyPath = propPath
            });

        if (searchIn is "all" or "name")
            if (stepName.IndexOf(pattern, cmp) >= 0)
                AddMatch(stepName, "StepName", $"{seqName}.{stepName}");

        if (searchIn is "all" or "type")
        {
            try
            {
                string typeName = (string)step.StepType.Name;
                if (typeName.IndexOf(pattern, cmp) >= 0)
                    AddMatch(typeName, "StepType", $"{seqName}.{stepName}.StepType");
            }
            catch (Exception) { /* best-effort: probe step type name for search — intentionally ignored */ }
        }

        if (searchIn is "all" or "expression" or "expressions")
        {
            string[] exprProps = { "PreExpression", "PostExpression", "StatusExpression" };
            string[] exprNames = { "PreExpression", "PostExpression", "StatusExpression" };
            for (int ei = 0; ei < exprProps.Length; ei++)
            {
                try
                {
                    var val = ((object)step).GetType().InvokeMember(
                        exprProps[ei], _comFlags, null, step, null)?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(val) && val.IndexOf(pattern, cmp) >= 0)
                        AddMatch(val, exprNames[ei],
                            $"{seqName}.{stepName}.{exprNames[ei]}");
                }
                catch (Exception) { /* best-effort: probe step expression property for search — intentionally ignored */ }
            }

            // Module expression
            try
            {
                string modExpr = (string)((NiStep)(object)step).AsPropertyObject()
                    .GetValString("Module.Expression", 0);
                if (!string.IsNullOrEmpty(modExpr) && modExpr.IndexOf(pattern, cmp) >= 0)
                    AddMatch(modExpr, "ModuleExpression",
                        $"{seqName}.{stepName}.Module.Expression");
            }
            catch (Exception) { /* best-effort: probe step Module.Expression for search — intentionally ignored */ }
        }

        if (searchIn is "all" or "comment")
        {
            try
            {
                string comment = (string)step.Comment;
                if (!string.IsNullOrEmpty(comment) && comment.IndexOf(pattern, cmp) >= 0)
                    AddMatch(comment, "Comment", $"{seqName}.{stepName}.Comment");
            }
            catch (Exception) { /* best-effort: probe step Comment for search — intentionally ignored */ }
        }

        return matches;
    }

    // ── Thread-Level Execution Control ────────────────────────────────────────

    private dynamic FindThread(string executionId, string threadId)
    {
        var exec = FindExecution(executionId)
            ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

        int numThreads = 0;
        try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get thread count in FindThread."); }

        // Try by ID first, then by index
        for (int i = 0; i < numThreads; i++)
        {
            try
            {
                dynamic t  = exec.GetThread((object)i);
                string  id = TryGetString(t, "ID");
                if (id == threadId || i.ToString() == threadId)
                    return t;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read thread at index {Index} in FindThread.", i); }
        }
        throw new KeyNotFoundException(
            $"Thread '{threadId}' not found in execution {executionId}.");
    }

    private static ThreadInfo MapThreadInfo(dynamic thread, int index, string execState = "")
    {
        var info = new ThreadInfo { ThreadIndex = index };
        var t = (NiThread)thread;

        try { info.ThreadId = TryGetString(thread, "ID"); } catch (Exception) { /* best-effort: read thread ID — intentionally ignored */ }
        if (string.IsNullOrEmpty(info.ThreadId)) info.ThreadId = index.ToString();

        // TestStand's Thread has no own run-state property; report the owning execution's state
        // (exact for the common single-thread case).
        info.State = execState;

        // Thread.GetSequenceContext takes (callStackIndex, out frameId). The old code called it via
        // `dynamic` with a single arg, so the out-param never bound — it threw and the position
        // fields came back empty even while the thread was parked on a step. Use the typed call;
        // index 0 = the current (top) call-stack frame.
        try
        {
            NiSequenceContext ctx = t.GetSequenceContext(0, out int _);
            try { info.CurrentStepName     = ctx.Step.Name;         } catch (Exception) { /* no current step in this frame */ }
            try { info.CurrentSequenceName = ctx.Sequence.Name;     } catch (Exception) { /* no current sequence in this frame */ }
            try { info.CurrentFilePath     = ctx.SequenceFile.Path; } catch (Exception) { /* no sequence file in this frame */ }
        }
        catch (Exception) { /* thread has no active call-stack frame (not currently in a sequence) */ }

        // The depth property is CallStackSize (the old code used a non-existent "StackDepth").
        try { info.StackDepth = t.CallStackSize; } catch (Exception) { /* best-effort: read call-stack size — intentionally ignored */ }

        return info;
    }

    /// <inheritdoc/>
    public async Task<List<ThreadInfo>> GetExecutionThreadsAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            var result = new List<ThreadInfo>();
            string execState = MapExecutionState(GetExecutionRunState((object)exec));
            int numThreads = 0;
            try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get thread count in GetExecutionThreadsAsync."); }

            for (int i = 0; i < numThreads; i++)
            {
                try
                {
                    dynamic t = exec.GetThread((object)i);
                    result.Add(MapThreadInfo(t, i, execState));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to map thread at index {Index}.", i); }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<ThreadInfo> GetThreadStatusAsync(string executionId, string threadId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            string execState = MapExecutionState(GetExecutionRunState((object)exec));
            int numThreads = 0;
            try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get thread count in GetThreadStatusAsync."); }

            for (int i = 0; i < numThreads; i++)
            {
                try
                {
                    dynamic t  = exec.GetThread((object)i);
                    string  id = TryGetString(t, "ID");
                    if (id == threadId || i.ToString() == threadId)
                        return MapThreadInfo(t, i, execState);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read thread at index {Index} in GetThreadStatusAsync.", i); }
            }
            throw new KeyNotFoundException(
                $"Thread '{threadId}' not found in execution {executionId}.");
        });
    }

    /// <inheritdoc/>
    public async Task BreakThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            try { thread.Break(); }
            catch { thread.SetStepOver(); }
        });
    }

    /// <inheritdoc/>
    public async Task ResumeThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() => FindThread(executionId, threadId).Resume());
    }

    /// <inheritdoc/>
    public async Task StepOverThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            try { thread.SetStepOver(); }
            catch { thread.StepOver(); }
        });
    }

    /// <inheritdoc/>
    public async Task StepIntoThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            try { thread.SetStepInto(); }
            catch { thread.StepInto(); }
        });
    }

    /// <inheritdoc/>
    public async Task StepOutThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            try { thread.SetStepOut(); }
            catch { thread.StepOut(); }
        });
    }

    /// <inheritdoc/>
    public async Task<List<CallStackFrame>> GetThreadCallStackAsync(
        string executionId, string threadId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            var t = (NiThread)thread;
            var frames = new List<CallStackFrame>();

            // Depth is CallStackSize. The old code read a non-existent "StackDepth", so depth stayed
            // 0, the loop never ran and this method ALWAYS returned an empty stack — exactly the bug
            // fixed in MapThreadInfo. See memory teststand-getstates-reflection-fails.
            int depth = 0;
            try { depth = t.CallStackSize; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get call-stack size."); }

            for (int d = 0; d < depth; d++)
            {
                try
                {
                    // GetSequenceContext takes (callStackIndex, out frameId); the old single-arg
                    // dynamic call threw (out-params don't bind through the dynamic binder). d=0 is
                    // the current (top) frame; higher d walks up toward the entry point.
                    NiSequenceContext ctx = t.GetSequenceContext(d, out int _);
                    var frame   = new CallStackFrame { Depth = d };
                    try { frame.SequenceName = (string)ctx.Sequence.Name;   } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read sequence name at stack depth {Depth}.", d); }
                    try { frame.FilePath     = (string)ctx.SequenceFile.Path; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read file path at stack depth {Depth}.", d); }
                    try { frame.StepName     = (string)ctx.Step.Name;        } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step name at stack depth {Depth}.", d); }
                    try
                    {
                        int grp = (int)ctx.StepGroup;
                        frame.StepGroup = grp switch { 0 => "Setup", 2 => "Cleanup", _ => "Main" };
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step group at stack depth {Depth}.", d); }
                    frames.Add(frame);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read call stack frame at depth {Depth}.", d); }
            }
            return frames;
        });
    }

    // ── Live Thread-Context Inspection (runtime debugging) ────────────────────
    // All methods below read/write the LIVE SequenceContext of a running or paused thread at a
    // chosen call-stack frame. Access path: FindThread → typed NiThread →
    // GetSequenceContext(frame, out _) → AsPropertyObject() == the ThisContext property tree
    // (subprops Locals/Parameters/FileGlobals/StationGlobals/RunState/Step/Sequence). This is the
    // ONLY path that sees runtime values; get_property_tree / evaluate_expression resolve against
    // engine Globals and never reach it. See memory teststand-runstate-inaccessible-at-breakpoint.

    // Resolves the live SequenceContext of (executionId, threadId ?? first thread) at callStackIndex.
    private NiSequenceContext ResolveThreadContext(string executionId, string? threadId,
        int callStackIndex)
    {
        var thread = FindThread(executionId, string.IsNullOrEmpty(threadId) ? "0" : threadId!);
        var t = (NiThread)thread;

        int depth = 0;
        try { depth = t.CallStackSize; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read CallStackSize."); }
        if (depth <= 0)
            throw new InvalidOperationException(
                "Thread has no active call-stack frame (it is not currently executing a sequence). " +
                "Inspect only while the thread is running/paused inside a sequence.");
        if (callStackIndex < 0 || callStackIndex >= depth)
            throw new ArgumentOutOfRangeException(nameof(callStackIndex),
                $"call_stack_index {callStackIndex} is out of range; thread has {depth} frame(s) (0..{depth - 1}).");

        return t.GetSequenceContext(callStackIndex, out int _);
    }

    // The ThisContext property tree of a frame — root for Locals/Parameters/RunState/Step/Sequence.
    private NiPropertyObject ThisContextPropertyObject(string executionId, string? threadId,
        int callStackIndex)
        => (NiPropertyObject)(object)
           ResolveThreadContext(executionId, threadId, callStackIndex).AsPropertyObject();

    /// <inheritdoc/>
    public async Task<PropertyNode> InspectThreadContextAsync(string executionId, string? threadId,
        int callStackIndex, string scope, string? lookupString, int maxDepth,
        bool includeHidden, int maxArrayElements)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiPropertyObject ctxPo = ThisContextPropertyObject(executionId, threadId, callStackIndex);

            NiPropertyObject start;
            string rootLabel;
            switch ((scope ?? "runstate").Trim().ToLowerInvariant())
            {
                case "full":
                case "thiscontext":
                    start = ctxPo; rootLabel = "ThisContext"; break;
                case "locals":
                    start = (NiPropertyObject)(object)ctxPo.GetPropertyObject("Locals", 0);     rootLabel = "Locals";     break;
                case "parameters":
                    start = (NiPropertyObject)(object)ctxPo.GetPropertyObject("Parameters", 0); rootLabel = "Parameters"; break;
                case "step":
                    start = (NiPropertyObject)(object)ctxPo.GetPropertyObject("Step", 0);        rootLabel = "Step";       break;
                case "sequence":
                    start = (NiPropertyObject)(object)ctxPo.GetPropertyObject("Sequence", 0);    rootLabel = "Sequence";   break;
                case "runstate":
                default:
                    start = (NiPropertyObject)(object)ctxPo.GetPropertyObject("RunState", 0);    rootLabel = "RunState";   break;
            }

            if (!string.IsNullOrWhiteSpace(lookupString))
            {
                start     = (NiPropertyObject)(object)start.GetPropertyObject(lookupString, 0);
                rootLabel = lookupString!;
            }

            int budget = 200_000;
            return BuildPropertyNode(start, rootLabel, 0, maxDepth, includeHidden,
                Math.Max(0, maxArrayElements), ref budget);
        });
    }

    /// <inheritdoc/>
    public async Task<ExpressionResult> EvaluateInThreadContextAsync(string executionId,
        string? threadId, int callStackIndex, string expression)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new ExpressionResult { Expression = expression };
            try
            {
                NiPropertyObject ctxPo = ThisContextPropertyObject(executionId, threadId, callStackIndex);
                // EvaluateEx on the ThisContext property tree resolves names as its subproperties —
                // exactly the scope the Sequence Editor uses (Locals./Parameters./RunState./Step.…).
                NiPropertyObject resultPo =
                    ctxPo.EvaluateEx(expression, (int)NiEvalOptions.EvalOption_NoOptions);
                if (resultPo == null) { result.ValueType = "Empty"; result.Value = null; }
                else { result.ValueType = InferValueKind(resultPo, out _, out _); result.Value = TryGetValue(resultPo); }
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.IsValid      = false;
                result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task<RuntimeVariableInfo> GetRuntimeVariableAsync(string executionId,
        string? threadId, int callStackIndex, string propertyPath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiPropertyObject ctxPo = ThisContextPropertyObject(executionId, threadId, callStackIndex);
            NiPropertyObject prop  = (NiPropertyObject)(object)ctxPo.GetPropertyObject(propertyPath, 0);

            var info = new RuntimeVariableInfo { PropertyPath = propertyPath };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElem);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElem;
            if (info.ValueType is "Number" or "Boolean" or "String")
                info.Value = TryGetValue(prop);
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task<RuntimeVariableInfo> SetRuntimeVariableAsync(string executionId,
        string? threadId, int callStackIndex, string propertyPath, string value, string? valueType)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiPropertyObject ctxPo = ThisContextPropertyObject(executionId, threadId, callStackIndex);

            // Explicit value_type wins; when omitted, auto-detect from the literal (parses as a
            // number → number; "true"/"false" → boolean; otherwise string) — like SetPropertyAsync.
            string kind    = (valueType ?? "").Trim().ToLowerInvariant();
            bool asNumber  = kind is "number" or "double" or "float" or "int" or "integer";
            bool asBoolean = kind is "boolean" or "bool";
            bool asString  = kind is "string";
            if (!asNumber && !asBoolean && !asString)
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) asNumber = true;
                else if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                      || value.Equals("false", StringComparison.OrdinalIgnoreCase)) asBoolean = true;
                else asString = true;
            }

            if (asNumber)
                ctxPo.SetValNumber(propertyPath, 0, double.Parse(value,
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture));
            else if (asBoolean)
                ctxPo.SetValBoolean(propertyPath, 0,
                    value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
            else
                ctxPo.SetValString(propertyPath, 0, value);

            // Read the value back so the caller sees the applied result.
            NiPropertyObject prop = (NiPropertyObject)(object)ctxPo.GetPropertyObject(propertyPath, 0);
            var info = new RuntimeVariableInfo { PropertyPath = propertyPath, Written = true };
            info.ValueType = InferValueKind(prop, out bool isArray, out int numElem);
            info.IsArray   = isArray;
            if (isArray) info.NumElements = numElem;
            if (info.ValueType is "Number" or "Boolean" or "String")
                info.Value = TryGetValue(prop);
            return info;
        });
    }

    /// <inheritdoc/>
    public async Task<RunStateSummary> GetRunStateSummaryAsync(string executionId,
        string? threadId, int callStackIndex)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            NiSequenceContext ctx = ResolveThreadContext(executionId, threadId, callStackIndex);
            NiPropertyObject ctxPo = (NiPropertyObject)(object)ctx.AsPropertyObject();
            NiPropertyObject rs    = (NiPropertyObject)(object)ctxPo.GetPropertyObject("RunState", 0);

            var s = new RunStateSummary();
            // Position — from the typed SequenceContext (same reads proven in MapThreadInfo).
            try { s.CurrentStepName     = (string)ctx.Step.Name;         } catch (Exception) { /* no current step */ }
            try { s.CurrentSequenceName = (string)ctx.Sequence.Name;     } catch (Exception) { /* no sequence */ }
            try { s.CurrentFilePath     = (string)ctx.SequenceFile.Path; } catch (Exception) { /* no file */ }

            int    Num(string p) { try { return (int)Math.Round(rs.GetValNumber(p, 0)); } catch { return 0; } }
            string Str(string p) { try { return rs.GetValString(p, 0); }                 catch { return ""; } }
            bool   Bl (string p) { try { return rs.GetValBoolean(p, 0); }                catch { return false; } }

            s.StepGroup         = Str("StepGroup");
            s.StepIndex         = Num("StepIndex");
            s.NextStepIndex     = Num("NextStepIndex");
            s.PreviousStepIndex = Num("PreviousStepIndex");
            s.CallStackDepth    = Num("CallStackDepth");
            s.LoopIndex         = Num("LoopIndex");
            s.NumStepsExecuted  = Num("NumStepsExecuted");
            s.SequenceFailed    = Bl ("SequenceFailed");
            s.GotoCleanup       = Bl ("GotoCleanup");
            s.ErrorReported     = Bl ("ErrorReported");
            s.ErrorCode         = Num("SequenceError.Code");
            s.ErrorMessage      = Str("SequenceError.Msg");
            s.ErrorOccurred     = Bl ("SequenceError.Occurred");
            return s;
        });
    }

    // ── Array Variable Operations ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<ArrayElementInfo>> GetArrayVariableAsync(string filePath,
        string? sequenceName, string variableName, int maxElements = 100)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);
            dynamic prop;
            if (string.IsNullOrEmpty(sequenceName))
            {
                var fg = GetFileGlobals(sf);
                prop = ((NiPropertyObject)(object)fg).GetPropertyObject(variableName, 0);
            }
            else
            {
                var seq = sf.GetSequenceByName(sequenceName);
                prop = seq.Locals.GetPropertyObject(variableName, 0);
            }

            var result = new List<ArrayElementInfo>();
            int numElements = 0;
            try { numElements = Convert.ToInt32((object)prop.GetNumElements()); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get element count for array variable '{Variable}'.", variableName); }

            int count = Math.Min(numElements, maxElements);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    // GetPropertyObjectByOffset gives the i-th element as a PropertyObject
                    dynamic elem = prop.GetPropertyObjectByOffset((object)i, (object)0);
                    var elemInfo = new ArrayElementInfo { Index = i };
                    // Determine element type and read value using typed ByOffset methods
                    try
                    {
                        elemInfo.Value = (double)prop.GetValNumberByOffset((object)i, (object)0);
                        elemInfo.Type  = "Number";
                    }
                    catch
                    {
                        try
                        {
                            elemInfo.Value = (bool)prop.GetValBooleanByOffset((object)i, (object)0);
                            elemInfo.Type  = "Boolean";
                        }
                        catch
                        {
                            try
                            {
                                elemInfo.Value = (string)prop.GetValStringByOffset((object)i, (object)0);
                                elemInfo.Type  = "String";
                            }
                            catch { elemInfo.Type = "Unknown"; }
                        }
                    }
                    result.Add(elemInfo);
                }
                catch
                {
                    result.Add(new ArrayElementInfo { Index = i, Value = null, Type = "Error" });
                }
            }
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task SetArrayElementAsync(string filePath, string? sequenceName,
        string variableName, int index, string value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);
            dynamic prop;
            if (string.IsNullOrEmpty(sequenceName))
            {
                var fg = GetFileGlobals(sf);
                prop = ((NiPropertyObject)(object)fg).GetPropertyObject(variableName, 0);
            }
            else
            {
                var seq = sf.GetSequenceByName(sequenceName);
                prop = seq.Locals.GetPropertyObject(variableName, 0);
            }

            // Use typed ByOffset methods directly on the array PropertyObject
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
                prop.SetValNumberByOffset((object)index, (object)0, (object)d);
            else if (bool.TryParse(value, out bool b))
                prop.SetValBooleanByOffset((object)index, (object)0, (object)b);
            else
                prop.SetValStringByOffset((object)index, (object)0, (object)value);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task ResizeArrayVariableAsync(string filePath, string? sequenceName,
        string variableName, int newSize)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(filePath);
            dynamic prop;
            if (string.IsNullOrEmpty(sequenceName))
            {
                var fg = GetFileGlobals(sf);
                prop = ((NiPropertyObject)(object)fg).GetPropertyObject(variableName, 0);
            }
            else
            {
                var seq = sf.GetSequenceByName(sequenceName);
                prop = seq.Locals.GetPropertyObject(variableName, 0);
            }

            // SetNumElements(numElements, options) — two parameters required
            prop.SetNumElements((object)newSize, (object)0);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Data Type Operations ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DataTypeInfo> CreateDataTypeAsync(string filePath, string typeName,
        string baseType = "Object")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            // AsPropertyObjectFile() gives the root PropertyObject of the sequence file.
            // PropValType_Container (0) creates a container/struct type (no base-type lookup).
            // PropValType_NamedType (4) requires an existing named type as a base.
            dynamic sfPo = sf.AsPropertyObjectFile();

            bool useNamedType = !string.IsNullOrEmpty(baseType)
                             && !baseType.Equals("Object", StringComparison.OrdinalIgnoreCase);
            int valType = useNamedType
                ? (int)NiPropValueTypes.PropValType_NamedType
                : (int)NiPropValueTypes.PropValType_Container;
            string typeParam = useNamedType ? baseType : "";

            sfPo.NewSubProperty(
                (object)typeName,
                (object)valType,
                (object)false,
                (object)typeParam,
                (object)0);

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;

            return new DataTypeInfo { Name = typeName, BaseType = baseType };
        });
    }

    /// <inheritdoc/>
    public async Task DeleteDataTypeAsync(string filePath, string typeName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            dynamic sfPo = sf.AsPropertyObjectFile();

            // Check existence first to give a meaningful error
            bool exists = false;
            try { exists = (bool)sfPo.Exists((object)typeName, (object)0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to check existence of data type '{TypeName}'.", typeName); }
            if (!exists)
                throw new InvalidOperationException(
                    $"Data type '{typeName}' not found in '{Path.GetFileName(filePath)}'.");

            sfPo.DeleteSubProperty((object)typeName, (object)0);
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Enumeration Data Types ────────────────────────────────────────────────
    //
    // An enum is a NAMED data type (TypeDef), NOT an anonymous subproperty: TestStand rejects
    // NewSubProperty(PropValType_Enum) with "Unrecognized value", and UpdateEnumerators rejects
    // a plain Number ("Expected Enumeration, found Number"). The supported route (per NI's API
    // docs) is: Engine.NewDataType(PropValType_Enum) → set Name → UpdateEnumerators → register
    // in the file's TypeUsageList as a custom data type, attached so it persists in the .seq.
    //
    // UpdateEnumerators takes an ARRAY of containers — each element carrying the subproperties
    // "EnumeratorName" (string), "EnumeratorValue" (number) and optionally "OldEnumeratorName"
    // (for renames). The array is built with Engine.NewPropertyObject(PropValType_Container,
    // asArray:true). UpdateEnumerators REPLACES the whole list, so add/remove/rename read the
    // current set, mutate it, and write the full list back.

    // PropertyOptions.PropOption_InsertIfMissing — creates the subproperty if absent when set.
    private const int PropOption_InsertIfMissing = 1;
    // Coercion flags: the Enumerators getter returns enum-TYPED values; to read each one's
    // underlying number / display name (TestStand otherwise rejects with "Expected type Number/
    // String. Found type <Enum>."), pass these to GetValNumber / GetValString respectively.
    private const int PropOption_CoerceToNumber = 64;
    private const int PropOption_CoerceToString = 128;

    /// <summary>
    /// Builds an enumerators array (the argument for PropertyObject.UpdateEnumerators) from a list
    /// of (name, value, oldName) tuples. oldName, when non-empty, renames an existing enumerator.
    /// Typed interop calls are used throughout — the C# dynamic-COM binder mishandles several of
    /// these methods (TargetParameterCountException / wrong overload resolution).
    /// </summary>
    private NiPropertyObject BuildEnumeratorArray(IReadOnlyList<(string name, double value, string? oldName)> items)
    {
        // Array of containers; each element gets EnumeratorName/EnumeratorValue subproperties.
        NiPropertyObject arr = _engine!.NewPropertyObject(
            NiPropValueTypes.PropValType_Container, true, "", 0);
        arr.SetNumElements(items.Count, 0);
        for (int i = 0; i < items.Count; i++)
        {
            NiPropertyObject elem = arr.GetPropertyObjectByOffset(i, 0);
            elem.SetValString("EnumeratorName",  PropOption_InsertIfMissing, items[i].name ?? "");
            elem.SetValNumber("EnumeratorValue", PropOption_InsertIfMissing, items[i].value);
            if (!string.IsNullOrEmpty(items[i].oldName))
                elem.SetValString("OldEnumeratorName", PropOption_InsertIfMissing, items[i].oldName!);
        }
        return arr;
    }

    /// <summary>Reads the enumerators (name → value) of an enum type/property, in definition order.</summary>
    private List<EnumValueInfo> ReadEnumerators(NiPropertyObject enumProp)
    {
        var result = new List<EnumValueInfo>();
        NiPropertyObject? arr;
        try { arr = enumProp.Enumerators; }
        catch (Exception ex) { _logger.LogDebug(ex, "Property has no Enumerators (not an enum?)."); return result; }
        if (arr == null) return result;

        int count;
        try { count = arr.GetNumElements(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read enumerator element count."); return result; }

        for (int i = 0; i < count; i++)
        {
            try
            {
                // Each element is an enum-typed value; coerce to read its name + numeric value.
                NiPropertyObject elem = arr.GetPropertyObjectByOffset(i, 0);
                result.Add(new EnumValueInfo
                {
                    Name  = elem.GetValString("", PropOption_CoerceToString),
                    Value = elem.GetValNumber("", PropOption_CoerceToNumber),
                });
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read enumerator at index {Index}.", i); }
        }
        return result;
    }

    /// <summary>
    /// Maps a numeric enum ordinal back to its symbolic enumerator NAME. Tries the enum type's
    /// definition in <paramref name="sf"/>'s type usage list first, then falls back to an
    /// ENGINE-WIDE lookup (<c>Engine.NewPropertyObject(PropValType_NamedType, typeName)</c>), which
    /// resolves types that live in a type palette or another loaded file but are not (yet) in this
    /// file's TypeUsageList. Returns null when the type is not an enum, is unresolvable, or no
    /// enumerator has that value (a bare/combined ordinal). Used so an enum value supplied as an
    /// ordinal can still be written by name — which stores it as an explicitly-set value (FileDiffer
    /// "[val]") rather than a default-flagged "{val}".
    /// </summary>
    private string? ResolveEnumeratorName(dynamic sf, string typeName, double value, string filePath)
    {
        // 1) The destination file's own TypeUsageList.
        try
        {
            NiTypeUsageList tul = GetTypeUsageList(sf);
            NiPropertyObject enumType = ResolveEnumType(tul, typeName, filePath);
            foreach (var e in ReadEnumerators(enumType))
                if (e.Value == value) return e.Name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ResolveEnumeratorName (file TUL) failed for {Type}={Value}.", typeName, value);
        }

        // 2) ENGINE-WIDE: a standalone instance of the named type exposes the same enumerators. This
        // is the case that used to fail — during a fresh rebuild the enum type is reachable
        // engine-wide (palette / the still-open original) but has not been pulled into the new file's
        // TypeUsageList yet, so step 1 found nothing and the caller fell back to the ordinal write,
        // which the FileDiffer then reports as "{val}" instead of "[val]".
        try
        {
            NiPropertyObject inst = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                NiPropValueTypes.PropValType_NamedType, false, typeName, 0);
            foreach (var e in ReadEnumerators(inst))
                if (e.Value == value) return e.Name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ResolveEnumeratorName (engine-wide) failed for {Type}={Value}.", typeName, value);
        }
        return null;
    }

    /// <summary>
    /// Writes an ENUM leaf so that TestStand stores it as an EXPLICITLY-SET value — the FileDiffer's
    /// "[val]" — rather than a type-default-flagged "{val}".
    /// <para>
    /// Only the by-NAME write (<c>SetValString + PropOption_CoerceToEnum</c>) marks the value
    /// explicit; the by-ORDINAL write (<c>SetValNumber + CoerceToEnum</c>) leaves it default-flagged
    /// even though the stored ordinal is correct. Empirically confirmed on a 30-sequence rebuild: the
    /// members written before their enum type reached the new file's TypeUsageList (ordinal path) all
    /// came out "{val}", the later ones (name path) "[val]".
    /// </para>
    /// Resolution order: (1) the caller's ordinal → enumerator name via
    /// <see cref="ResolveEnumeratorName"/> (file TUL, then engine-wide); (2) a symbolic name passed
    /// straight through in <paramref name="value"/>; (3) LAST RESORT — write the ordinal, then read
    /// the symbolic name back OFF THE TARGET PROPERTY ITSELF (it is already enum-typed, so
    /// <c>GetValString + CoerceToString</c> yields the enumerator name) and re-write it by that name.
    /// Step 3 needs no type lookup at all and therefore succeeds wherever the property exists.
    /// </summary>
    private void WriteEnumLeafExplicit(NiPropertyObject container, string leafPath,
        int? ordinal, string? value, string? typeName, dynamic? sf, string filePath)
    {
        double? enumVal = ordinal
            ?? (value != null && double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed : (double?)null);

        // (1) ordinal → name. NOTE: keep the dynamic 'sf' out of the && chain — a dynamic operand
        // makes the whole condition dynamic and the compiler then cannot prove the pattern variable
        // is assigned (CS0165).
        object? sfObj = sf;
        if (enumVal.HasValue && !string.IsNullOrWhiteSpace(typeName) && sfObj != null)
        {
            string? byName = ResolveEnumeratorName(sf!, typeName!, enumVal.Value, filePath);
            if (byName != null)
            {
                container.SetValString(leafPath, (int)NiPropOptions.PropOption_CoerceToEnum, byName);
                return;
            }
        }

        // (2) a symbolic name supplied directly (not parseable as a number).
        if (!enumVal.HasValue && value != null)
        {
            container.SetValString(leafPath, (int)NiPropOptions.PropOption_CoerceToEnum, value);
            return;
        }

        if (!enumVal.HasValue) return;   // nothing to write

        // (3) ordinal write, then promote to an explicit by-name write using the name the property
        // itself reports. This is the fallback that makes the explicit state independent of whether
        // the enum type is resolvable through any type list.
        container.SetValNumber(leafPath, (int)NiPropOptions.PropOption_CoerceToEnum, enumVal.Value);
        PromoteEnumLeafToExplicit(container, leafPath);
    }

    /// <summary>
    /// Re-writes an already-set ENUM leaf by its SYMBOLIC NAME so TestStand marks the value
    /// explicitly-set (FileDiffer "[val]"). The property is already enum-typed at this point, so
    /// <c>GetValString + PropOption_CoerceToString</c> yields its enumerator name without any type
    /// lookup. No-op when the leaf is not an enum or reports no symbolic name (a bare/combined
    /// ordinal), so it is safe to call on the numeric fallback path of any setter.
    /// </summary>
    private void PromoteEnumLeafToExplicit(NiPropertyObject container, string leafPath)
    {
        try
        {
            NiPropertyObject leaf = (NiPropertyObject)(object)container.GetPropertyObject(leafPath, 0);
            string sym = leaf.GetValString("", PropOption_CoerceToString);
            if (!string.IsNullOrEmpty(sym) &&
                !double.TryParse(sym, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                container.SetValString(leafPath, (int)NiPropOptions.PropOption_CoerceToEnum, sym);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Enum explicit-promote read-back failed for '{Path}'.", leafPath);
        }
    }

    // The file's type usage list (where custom data types — including enums — are stored).
    private static NiTypeUsageList GetTypeUsageList(dynamic sf) =>
        ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).TypeUsageList;

    // Resolves the enum TYPE definition from the file's type usage list, throwing a clear error
    // when it is missing. GetTypeIndex returns -1 (or throws) for an unknown type name.
    private NiPropertyObject ResolveEnumType(NiTypeUsageList tul, string enumName, string filePath)
    {
        int idx = -1;
        try { idx = tul.GetTypeIndex(enumName); }
        catch (Exception ex) { _logger.LogDebug(ex, "GetTypeIndex failed for enum '{Enum}'.", enumName); }
        if (idx < 0)
            throw new InvalidOperationException(
                $"Enum '{enumName}' not found in '{Path.GetFileName(filePath)}'.");
        return tul.GetTypeDefinition(idx);
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> CreateEnumAsync(string filePath, string enumName,
        IReadOnlyList<EnumValueInfo> values, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);

            // Create a named enumeration TYPE, register it in the file (attached so the definition
            // persists embedded in the .seq), then populate the file-owned type's enumerators.
            NiPropertyObject created = _engine!.NewDataType(NiPropValueTypes.PropValType_Enum, false, "", 0);
            created.Name = enumName;
            tul.InsertType(created, 0, NiTypeCategories.TypeCategory_CustomDataTypes);
            int idx = tul.GetTypeIndex(enumName);
            if (idx >= 0) tul.SetIsTypeAttachedToFile(idx, true);

            NiPropertyObject stored = tul.GetTypeDefinition(idx);
            var items = values.Select(v => (v.Name, v.Value, (string?)null)).ToList();
            stored.UpdateEnumerators(BuildEnumeratorArray(items));
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(stored) };
        });
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> GetEnumValuesAsync(string filePath, string enumName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(ResolveEnumType(tul, enumName, filePath)) };
        });
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> SetEnumValuesAsync(string filePath, string enumName,
        IReadOnlyList<EnumValueInfo> values, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            NiPropertyObject enumType = ResolveEnumType(tul, enumName, filePath);

            var items = values.Select(v => (v.Name, v.Value, (string?)null)).ToList();
            enumType.UpdateEnumerators(BuildEnumeratorArray(items));
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(enumType) };
        });
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> AddEnumValueAsync(string filePath, string enumName,
        string valueName, double? value = null, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            NiPropertyObject enumType = ResolveEnumType(tul, enumName, filePath);

            List<EnumValueInfo> current = ReadEnumerators(enumType);
            double newVal = value ?? (current.Count == 0 ? 0 : current.Max(c => c.Value) + 1);

            var items = current.Select(v => (v.Name, v.Value, (string?)v.Name)).ToList();
            items.Add((valueName, newVal, (string?)null));
            enumType.UpdateEnumerators(BuildEnumeratorArray(items));
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(enumType) };
        });
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> RemoveEnumValueAsync(string filePath, string enumName,
        string valueName, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            NiPropertyObject enumType = ResolveEnumType(tul, enumName, filePath);

            List<EnumValueInfo> current = ReadEnumerators(enumType);
            if (!current.Any(v => v.Name == valueName))
                throw new InvalidOperationException(
                    $"Enumerator '{valueName}' not found in enum '{enumName}'.");

            // Map surviving enumerators by old name so identity is preserved across the update.
            var items = current.Where(v => v.Name != valueName)
                               .Select(v => (v.Name, v.Value, (string?)v.Name)).ToList();
            enumType.UpdateEnumerators(BuildEnumeratorArray(items));
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(enumType) };
        });
    }

    /// <inheritdoc/>
    public async Task<EnumInfo> RenameEnumValueAsync(string filePath, string enumName,
        string oldName, string newName, double? value = null, bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);
            NiPropertyObject enumType = ResolveEnumType(tul, enumName, filePath);

            List<EnumValueInfo> current = ReadEnumerators(enumType);
            if (!current.Any(v => v.Name == oldName))
                throw new InvalidOperationException(
                    $"Enumerator '{oldName}' not found in enum '{enumName}'.");

            // The renamed element carries OldEnumeratorName=oldName so TestStand maps it to the
            // existing enumerator; unchanged elements map by their (current) name.
            var items = current.Select(v => v.Name == oldName
                ? (newName, value ?? v.Value, (string?)oldName)
                : (v.Name,  v.Value,          (string?)v.Name)).ToList();
            enumType.UpdateEnumerators(BuildEnumeratorArray(items));
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();

            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
            return new EnumInfo { Name = enumName, Values = ReadEnumerators(enumType) };
        });
    }

    /// <inheritdoc/>
    public async Task DeleteEnumAsync(string filePath, string enumName, bool save = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            NiTypeUsageList tul = GetTypeUsageList(sf);

            int idx = -1;
            try { idx = tul.GetTypeIndex(enumName); }
            catch (Exception ex) { _logger.LogDebug(ex, "GetTypeIndex failed for enum '{Enum}'.", enumName); }
            if (idx < 0)
                throw new InvalidOperationException(
                    $"Enum '{enumName}' not found in '{Path.GetFileName(filePath)}'.");

            tul.RemoveType(idx);
            ((PropertyObjectFile)(object)sf.AsPropertyObjectFile()).IncChangeCount();
            if (save) { SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath); _loadedSequenceFiles[filePath] = sf; }
        });
    }

    // ── Module Parameter Operations ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<ModuleParameterInfo>> GetModuleParametersAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
            return ReadModuleParameters(stepPo, stepName);
        });
    }

    /// <summary>
    /// Reads a step's code-module interface (parameters/arguments) from its PropertyObject, in the
    /// same precedence order as get_module_parameters: LabVIEW connector-pane bindings
    /// (TS.SData.ViCall.Parms — cluster members flattened as 'parent.child'), the step-root VIModule
    /// of utility steps (NI_LV_RunVIAsynchronously), SequenceCall actual arguments
    /// (TS.SData.ActualArgs) and finally the legacy flat Module.Parameters container
    /// (DLL / .NET / Python / ActiveX). Returns the first non-empty source. Shared by
    /// get_module_parameters and the "Load Prototype" paths so they report an identical interface.
    /// </summary>
    private List<ModuleParameterInfo> ReadModuleParameters(NiPropertyObject stepPo, string stepName)
    {
        var result = new List<ModuleParameterInfo>();

        // 1) LabVIEW (G Flexible VI) connector-pane bindings: TS.SData.ViCall.Parms —
        //    an ARRAY of VIParameter containers (Label/ArgVal/Direction, clusters nest
        //    via ArrayClusterEls). 2) The same shape on utility steps that embed their VI
        //    call at the step root (NI_LV_RunVIAsynchronously → VIModule.ViCall.Parms).
        foreach (var parmsPath in new[] { "TS.SData.ViCall.Parms", "VIModule.ViCall.Parms" })
        {
            try
            {
                NiPropertyObject parms =
                    (NiPropertyObject)(object)stepPo.GetPropertyObject(parmsPath, 0);
                CollectViCallParms(parms, "", result);
                if (result.Count > 0) return result;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "No VI parms at '{Path}'.", parmsPath); }
        }

        // 3) SequenceCall arguments: TS.SData.ActualArgs — named SequenceArgument
        //    containers whose Expr is the bound expression (UseDef=true → default used).
        try
        {
            NiPropertyObject args =
                (NiPropertyObject)(object)stepPo.GetPropertyObject("TS.SData.ActualArgs", 0);
            int n = args.GetNumSubProperties("");
            for (int i = 0; i < n; i++)
            {
                try
                {
                    NiPropertyObject a = args.GetNthSubProperty("", i, 0);
                    var pi = new ModuleParameterInfo
                    {
                        Name = a.Name,
                        Type = "SequenceArgument",
                    };
                    try { pi.Value = a.GetValString("Expr", 0); } catch { }
                    try
                    {
                        if (a.GetValBoolean("UseDef", 0) && string.IsNullOrEmpty(pi.Value))
                            pi.Value = null; // default used, nothing bound
                    }
                    catch { }
                    try { pi.DataType = a.GetTypeDisplayString("", 0); } catch { }
                    result.Add(pi);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read ActualArgs entry {Index}.", i); }
            }
            if (result.Count > 0) return result;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No SequenceCall ActualArgs on step '{Step}'.", stepName); }

        // 4) PYTHON adapter arguments: TS.SData.PythonCall.Parameters — an ARRAY of NI_PythonParameter
        //    containers (Name / Type / ArgumentValue). This is neither a ViCall.Parms array nor a
        //    named-subproperty container, so the readers above and the flat Module.Parameters reader
        //    below both miss it: get_module_parameters used to return [] for every Python step even
        //    though the step had a fully bound argument list.
        try
        {
            NiPropertyObject pyParms =
                (NiPropertyObject)(object)stepPo.GetPropertyObject("TS.SData.PythonCall.Parameters", 0);
            int n = pyParms.GetNumElements();
            for (int i = 0; i < n; i++)
            {
                try
                {
                    NiPropertyObject e = (NiPropertyObject)(object)pyParms.GetPropertyObjectByOffset(i, 0);
                    var pi = new ModuleParameterInfo { Type = "PythonParameter" };
                    try { pi.Name  = e.GetValString("Name", 0); } catch { }
                    try { pi.Value = e.GetValString("ArgumentValue", 0); } catch { }
                    // The entry's Type code (0=None, 3=Boolean, 4=Dynamic, 6=Object, 7=by-name arg) —
                    // reported as the DataType so a rebuild can feed it straight back into
                    // configure_python_module's 'parameters'.
                    try { pi.DataType = ((int)e.GetValNumber("Type", 0)).ToString(
                            System.Globalization.CultureInfo.InvariantCulture); } catch { }
                    result.Add(pi);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read PythonCall parameter {Index}.", i); }
            }
            if (result.Count > 0) return result;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No PythonCall parameters on step '{Step}'.", stepName); }

        // 5) Legacy adapters that expose a flat Module.Parameters container.
        try
        {
            NiPropertyObject moduleParams;
            try { moduleParams = (NiPropertyObject)(object)stepPo.GetPropertyObject("TS.Module.Parameters", 0); }
            catch { moduleParams = (NiPropertyObject)(object)stepPo.GetPropertyObject("Module.Parameters", 0); }

            int count = moduleParams.GetNumSubProperties("");
            for (int i = 0; i < count; i++)
            {
                try
                {
                    NiPropertyObject param = moduleParams.GetNthSubProperty("", i, 0);
                    var pi = new ModuleParameterInfo { Name = param.Name };
                    try { pi.DataType = param.GetTypeDisplayString("", 0); } catch { }
                    try
                    {
                        int flags2 = param.GetFlags("", 0);
                        pi.Direction = (flags2 & 4) != 0 ? "InOut"
                                     : (flags2 & 2) != 0 ? "Output"
                                     : "Input";
                    }
                    catch { pi.Direction = "Input"; }
                    pi.Value = TryGetValue(param)?.ToString();
                    result.Add(pi);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module parameter at index {Index}.", i); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate module parameters for step '{Step}'.", stepName); }

        return result;
    }

    /// <summary>
    /// Flattens a ViCall.Parms array (VIParameter entries) into ModuleParameterInfo rows.
    /// Cluster members (ArrayClusterEls) recurse with a "parent.child" name so every
    /// bindable slot is visible/addressable.
    /// </summary>
    /// <summary>
    /// Walks a <c>ViCall.Parms</c> array and emits one entry per control — including cluster members,
    /// flattened as "parent.child", the Label form <c>set_module_parameter</c> binds by — carrying the
    /// ArgVal expression AND the control's <c>UseDefaultValues</c> flag.
    /// <para>
    /// The flag has to travel with the value: <c>set_module_parameter</c> always CLEARS it, so a
    /// rebuild that writes every binding flips it on the controls where the source has it set (the
    /// editor keeps a remembered expression while still using the VI's own default — the same
    /// asymmetry as a SequenceCall argument's UseDef). Writing bindings blindly made a 31-difference
    /// rebuild 68; writing only the ones whose flag is FALSE is what reproduces both classes.
    /// </para>
    /// </summary>
    private void CollectViCallBindings(NiPropertyObject parms, string prefix,
        List<ModuleArgModel> result)
    {
        int n = 0;
        try { n = parms.GetNumElements(); } catch { return; }
        for (int i = 0; i < n; i++)
        {
            try
            {
                NiPropertyObject p = parms.GetPropertyObjectByOffset(i, 0);
                string label = "";
                try { label = p.GetValString("Label", 0); } catch { }
                string name = string.IsNullOrEmpty(prefix) ? label : prefix + "." + label;

                var a = new ModuleArgModel { Name = name };
                try { a.Value      = p.GetValString("ArgVal", 0); } catch { }
                try { a.UseDefault = p.GetValBoolean("UseDefaultValues", 0); } catch { }
                result.Add(a);

                try
                {
                    NiPropertyObject els =
                        (NiPropertyObject)(object)p.GetPropertyObject("ArrayClusterEls", 0);
                    CollectViCallBindings(els, name, result);
                }
                catch { /* scalar parameter — no cluster members */ }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read VI binding at index {Index}.", i); }
        }
    }

    private void CollectViCallParms(NiPropertyObject parms, string prefix,
        List<ModuleParameterInfo> result)
    {
        int n = 0;
        try { n = parms.GetNumElements(); } catch { return; }
        for (int i = 0; i < n; i++)
        {
            try
            {
                NiPropertyObject p = parms.GetPropertyObjectByOffset(i, 0);
                string label = "";
                try { label = p.GetValString("Label", 0); } catch { }
                string name = string.IsNullOrEmpty(prefix) ? label : prefix + "." + label;

                var pi = new ModuleParameterInfo { Name = name, Type = "VIParameter" };
                try { pi.Value = p.GetValString("ArgVal", 0); } catch { }
                try { pi.DataType = p.GetValString("DisplayType", 0); } catch { }
                try
                {
                    double dir = p.GetValNumber("Direction", 0);
                    pi.Direction = dir >= 1 ? "Output" : "Input";
                }
                catch { pi.Direction = "Input"; }
                result.Add(pi);

                try
                {
                    NiPropertyObject els =
                        (NiPropertyObject)(object)p.GetPropertyObject("ArrayClusterEls", 0);
                    CollectViCallParms(els, name, result);
                }
                catch { /* scalar parameter — no cluster members */ }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read VI parm at index {Index}.", i); }
        }
    }

    /// <summary>
    /// Resolves a VIParameter entry in a ViCall.Parms array by its Label path — one segment
    /// per nesting level ("error out", "status" descends into the cluster's ArrayClusterEls).
    /// Returns null when no entry matches.
    /// </summary>
    private NiPropertyObject? FindViCallParm(NiPropertyObject parms, string[] labelPath)
    {
        if (labelPath.Length == 0) return null;
        int n = 0;
        try { n = parms.GetNumElements(); } catch { return null; }
        for (int i = 0; i < n; i++)
        {
            NiPropertyObject p;
            string label = "";
            try
            {
                p = parms.GetPropertyObjectByOffset(i, 0);
                try { label = p.GetValString("Label", 0); } catch { }
            }
            catch { continue; }
            if (!string.Equals(label, labelPath[0], StringComparison.OrdinalIgnoreCase)) continue;
            if (labelPath.Length == 1) return p;
            try
            {
                NiPropertyObject els =
                    (NiPropertyObject)(object)p.GetPropertyObject("ArrayClusterEls", 0);
                return FindViCallParm(els, labelPath[1..]);
            }
            catch { return null; }
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task<LoadPrototypeResult> LoadModulePrototypeAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, bool save = true,
        bool? isolate = null, int timeoutSeconds = 120, bool? async = null,
        string? labviewServer = null, IReadOnlyList<string>? calleeFiles = null)
    {
        EnsureConnected();

        // Probe the adapter + VI path (safe — no LoadPrototype), then dispatch. LabVIEW ("G …")
        // adapters resolve the VI connector pane through LabVIEW; the load is SLOW (LabVIEW must
        // attach/start — the same work the editor's "Reload Prototype" does) and can exceed the MCP
        // transport's ~60s window (→ -32001) OR hit the native delay-load SEH 0xC06D007E.
        var (adapterKey, viPath, isLabVIEW) = await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            var step  = ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            string ak = TryGetString(step, "AdapterKeyName");
            return (ak, ReadStepViPath(((NiStep)(object)step).AsPropertyObject()), IsLabVIEWAdapter(ak));
        });
        bool isPackedLib = (viPath ?? "").IndexOf(".lvlibp", StringComparison.OrdinalIgnoreCase) >= 0;

        // Non-LabVIEW adapters (SequenceCall/.NET/DLL·CVI/ActiveX) are fast and never delay-load a
        // LabVIEW runtime → in-process & synchronous by default (no behaviour change, no regression).
        //
        // EXCEPTION — isolate=true forces the worker even here, and that is the ONLY way to load a
        // cross-file SequenceCall prototype in a process that has already loaded a LabVIEW connector
        // pane. Measured: the two load kinds poison each other BOTH ways. After any LabVIEW pane load,
        // a cross-file SequenceCall load returns "could not resolve the target/module"; after a
        // SequenceCall load, LabVIEW loads fail the same way. A fresh worker process is unpoisoned, so
        // both can be had in one rebuild. openFiles carries the CALLEE files: the worker's fresh engine
        // only fills TS.SData.Prototype when the target file is loaded there too.
        if (!isLabVIEW)
        {
            if (isolate == true)
                return await RunLoadPrototypeViaWorkerAsync(filePath, sequenceName, stepGroup,
                    stepName, save, timeoutSeconds, adapterKey, isPackedLib,
                    labviewServer ?? "deferred", calleeFiles);

            var r = await LoadPrototypeInProcessAsync(filePath, sequenceName, stepGroup, stepName, save);
            r.ExecutionMode = "in-process";
            return r;
        }

        // LabVIEW dispatch. The KEY fix: route the adapter to the LabVIEW ExecServer (the running
        // LabVIEW ADE via ActiveX — what the editor uses) instead of AutoDetect→Run-Time (lvrt.dll),
        // whose delay-load fails headless with MOD_NOT_FOUND. ExecServer/ActiveX works cross-process,
        // so the ISOLATED WORKER (default) can bind LabVIEW too — giving crash-safety AND a real load
        // together. ASYNC by default (job id + poll) so LabVIEW's slow attach never trips the RPC
        // timeout. isolate=false runs in-process (also ExecServer-routed) but is not crash-contained.
        string lvServer = string.IsNullOrWhiteSpace(labviewServer) ? "deferred" : labviewServer!.Trim();
        bool useWorker = isolate ?? true;
        bool useAsync  = async  ?? true;

        Func<Task<LoadPrototypeResult>> work = () => useWorker
            ? RunLoadPrototypeViaWorkerAsync(filePath, sequenceName, stepGroup, stepName, save,
                                             timeoutSeconds, adapterKey, isPackedLib, lvServer)
            : RunLoadPrototypeInProcessLabVIEWAsync(filePath, sequenceName, stepGroup, stepName,
                                                    save, adapterKey, isPackedLib, lvServer);

        if (!useAsync)
            return await work();

        return StartPrototypeLoadJob(work, adapterKey, useWorker ? "worker" : "in-process", stepName);
    }

    // The in-process LabVIEW path: route/init the LabVIEW adapter to its ExecServer (like the editor)
    // FIRST, then run the shared in-process core. Adds the packed-lib hint when the load did not resolve.
    private async Task<LoadPrototypeResult> RunLoadPrototypeInProcessLabVIEWAsync(
        string filePath, string sequenceName, string stepGroup, string stepName,
        bool save, string adapterKey, bool isPackedLib, string labviewServer)
    {
        var r = await LoadPrototypeInProcessAsync(filePath, sequenceName, stepGroup, stepName, save, labviewServer);
        r.ExecutionMode = "in-process";
        if (isPackedLib && !r.PrototypeLoaded) r.Note = AppendPackedLibHint(r.Note);
        return r;
    }

    /// <inheritdoc/>
    public async Task<LoadPrototypeResult> GetPrototypeLoadStatusAsync(string jobId)
    {
        if (!_prototypeJobs.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException(
                $"No prototype-load job '{jobId}' (unknown or expired). Start one with load_module_prototype.");
        return await Task.FromResult(job.Snapshot());
    }

    // ── Async prototype-load jobs ──────────────────────────────────────────────
    private sealed class PrototypeLoadJob
    {
        public string JobId = "";
        public string Status = "running";      // running | completed | error
        public LoadPrototypeResult? Result;
        public string? Error;
        public DateTime StartedUtc;
        public DateTime? FinishedUtc;
        public string Adapter = "";
        public string ExecutionMode = "";
        public string StepName = "";

        public LoadPrototypeResult Snapshot()
        {
            if (Status == "completed" && Result != null)
            {
                Result.JobId = JobId;
                Result.Status = "completed";
                return Result;
            }
            return new LoadPrototypeResult
            {
                StepName = StepName, Adapter = Adapter, ExecutionMode = ExecutionMode,
                PrototypeLoaded = false, JobId = JobId, Status = Status,
                Note = Status == "error"
                    ? "The prototype-load job faulted: " + (Error ?? "unknown error")
                    : "Prototype load still running — poll get_prototype_load_status again."
            };
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrototypeLoadJob>
        _prototypeJobs = new();

    // Starts the work on a background task, tracks it as a job, and returns a "running" handle
    // immediately so the RPC returns well within the transport timeout.
    private LoadPrototypeResult StartPrototypeLoadJob(
        Func<Task<LoadPrototypeResult>> work, string adapterKey, string executionMode, string stepName)
    {
        PruneOldPrototypeJobs();
        var job = new PrototypeLoadJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            StartedUtc = DateTime.UtcNow,
            Adapter = adapterKey, ExecutionMode = executionMode, StepName = stepName
        };
        _prototypeJobs[job.JobId] = job;

        _ = Task.Run(async () =>
        {
            try
            {
                var r = await work();
                job.Result = r;
                job.Status = "completed";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Async prototype-load job {Job} faulted.", job.JobId);
                job.Error = ex.Message;
                job.Status = "error";
            }
            finally { job.FinishedUtc = DateTime.UtcNow; }
        });

        return new LoadPrototypeResult
        {
            StepName = stepName, Adapter = adapterKey, ExecutionMode = executionMode,
            PrototypeLoaded = false, JobId = job.JobId, Status = "running",
            Note = "LabVIEW prototype load started asynchronously (LabVIEW attach/load can take a " +
                   "while — the Sequence Editor does the same work). Poll get_prototype_load_status " +
                   $"with job_id='{job.JobId}' until status='completed'."
        };
    }

    // Keep the job map from growing unbounded: drop finished jobs older than 10 minutes.
    private void PruneOldPrototypeJobs()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kv in _prototypeJobs)
            if (kv.Value.FinishedUtc is DateTime f && f < cutoff)
                _prototypeJobs.TryRemove(kv.Key, out _);
    }

    // Captured previous LabVIEW-adapter server config, so a per-load override can be restored.
    private sealed class LvServerRestore
    {
        public string AdapterKey = "";
        public NiLabVIEWServerTypes PrevType;
        public string PrevServer = "";
        public bool Changed;
        public string Diag = "";   // compact routing diagnostic surfaced in the result note
    }

    // Routes the LabVIEW adapter to the requested LabVIEW server and connects to it BEFORE the load —
    // the fix for the headless .lvlibp fault. The generic Adapter from GetAdapterByKeyName is QI'd to
    // the typed LabVIEWAdapter interface (the AdapterAPI methods Initialize/Get/SetServerInfo live on
    // that interface, NOT on the generic Adapter dispinterface — which is why the earlier late-bound
    // `dynamic adapter.Initialize()` silently no-op'd). server modes:
    //   "deferred" (default) → ExecServerDeferred, "LabVIEW": the running LabVIEW ADE via ActiveX,
    //                          launched on first use — matches the editor, no lvrt.dll delay-load.
    //   "exec"               → ExecServer, "LabVIEW": same, connected immediately.
    //   "rte"                → RTEServer, "AutoDetect": the legacy run-time path (may fault headless).
    //   "auto"               → leave the configured server as-is; just Initialize().
    // Best-effort: any failure is logged and the load falls back to the engine's lazy connect.
    private LvServerRestore? ConfigureLabVIEWAdapter(string adapterKey, string? mode)
    {
        var restore = new LvServerRestore { AdapterKey = adapterKey };
        try
        {
            var adapterObj = _engine!.GetAdapterByKeyName(adapterKey);
            var lva = (NiLabVIEWAdapter)(object)adapterObj;   // QI to the LabVIEW adapter interface
            restore.Diag = "cast=ok";

            string m = string.IsNullOrWhiteSpace(mode) ? "deferred" : mode!.Trim().ToLowerInvariant();
            if (m != "auto")
            {
                (NiLabVIEWServerTypes type, string server) target = m switch
                {
                    "exec"     => (NiLabVIEWServerTypes.LabVIEWServer_ExecServer,         "LabVIEW"),
                    "deferred" => (NiLabVIEWServerTypes.LabVIEWServer_ExecServerDeferred, "LabVIEW"),
                    "rte"      => (NiLabVIEWServerTypes.LabVIEWServer_RTEServer,          "AutoDetect"),
                    _          => (NiLabVIEWServerTypes.LabVIEWServer_ExecServerDeferred, "LabVIEW"),
                };
                try
                {
                    lva.GetServerInfo(out restore.PrevType, out restore.PrevServer);
                    restore.Diag += $"; prev={restore.PrevType}/{restore.PrevServer}";
                    if (restore.PrevType != target.type ||
                        !string.Equals(restore.PrevServer, target.server, StringComparison.OrdinalIgnoreCase))
                    {
                        lva.SetServerInfo(target.type, target.server);
                        restore.Changed = true;
                        restore.Diag += $"; set={target.type}/{target.server}";
                        _logger.LogInformation("LabVIEW adapter '{Key}' server routed to {Type}/{Server} " +
                            "for the prototype load (was {PrevType}/{PrevServer}).", adapterKey,
                            target.type, target.server, restore.PrevType, restore.PrevServer);
                    }
                    else restore.Diag += "; already-target";
                }
                catch (Exception ex)
                {
                    restore.Diag += $"; serverinfo-failed={Short(ex)}";
                    _logger.LogDebug(ex, "LabVIEW adapter Get/SetServerInfo skipped.");
                }
            }
            else restore.Diag += "; mode=auto";

            try { lva.Initialize(); restore.Diag += "; init=ok"; }
            catch (Exception ex)
            {
                restore.Diag += $"; init-failed={Short(ex)}";
                _logger.LogDebug(ex, "LabVIEW adapter Initialize() skipped/failed.");
            }
        }
        catch (Exception ex)
        {
            restore.Diag = $"cast/adapter-failed={Short(ex)}";
            _logger.LogInformation(ex, "ConfigureLabVIEWAdapter('{Key}') could not route/attach the " +
                "LabVIEW adapter (typed cast or server call failed); relying on the engine's lazy " +
                "connect for the load.", adapterKey);
        }
        return restore;
    }

    // A compact one-line exception tag for the routing diagnostic embedded in the result note.
    private static string Short(Exception ex)
        => (ex.GetType().Name + ": " + (ex.Message ?? "")).Replace("\r", " ").Replace("\n", " ")
           is var s && s.Length > 120 ? s.Substring(0, 120) : s;

    // Restore the adapter's previous server config (only if we changed it) so a per-load override does
    // not permanently alter the station's LabVIEW Adapter configuration.
    private void RestoreLabVIEWAdapter(LvServerRestore? restore)
    {
        if (restore is not { Changed: true }) return;
        try
        {
            var adapterObj = _engine!.GetAdapterByKeyName(restore.AdapterKey);
            var lva = (NiLabVIEWAdapter)(object)adapterObj;
            lva.SetServerInfo(restore.PrevType, restore.PrevServer);
            _logger.LogDebug("LabVIEW adapter '{Key}' server restored to {Type}/{Server}.",
                restore.AdapterKey, restore.PrevType, restore.PrevServer);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Restoring LabVIEW adapter server config failed."); }
    }

    /// <inheritdoc/>
    public async Task<LoadPrototypeResult> LoadPrototypeInProcessAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, bool save = true,
        string? labviewServer = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            string adapterKey = TryGetString(step, "AdapterKeyName");
            dynamic mod       = step.Module;

            // For a LabVIEW step, route/attach the adapter to its LabVIEW server BEFORE the load — the
            // decisive fix. Default "deferred"/"exec" = the running LabVIEW ADE (ActiveX, like the
            // editor), which avoids the AutoDetect→Run-Time (lvrt.dll) delay-load that faults headless.
            // Restored afterward so the station's adapter config is not permanently changed.
            LvServerRestore? lvRestore = null;
            if (IsLabVIEWAdapter(adapterKey))
                lvRestore = ConfigureLabVIEWAdapter(adapterKey, labviewServer);

            // Editor "Load Prototype": reconcile the module's argument list against the current
            // target. Returns false (logged) when the target/module cannot be resolved — an unlinked
            // SequenceCall placeholder, a missing/not-loaded target file, or a VI/DLL that cannot load
            // headless. Non-destructive: existing bindings are matched by name and preserved.
            bool loaded;
            try { loaded = TryLoadModulePrototype(mod, stepName); }
            finally { RestoreLabVIEWAdapter(lvRestore); }

            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
            var parameters = ReadModuleParameters(stepPo, stepName);

            string? note = null;
            if (!loaded)
                note = "LoadPrototype could not resolve the target/module. Nothing was updated. " +
                       "Ensure the target is set first (order matters) and reachable: for a " +
                       "SequenceCall the target sequence/file must be loaded or on the search path; " +
                       "for LabVIEW the VI must be loadable (LabVIEW available, not an unloadable " +
                       ".lvlibp headless).";
            else if (parameters.Count == 0)
                note = "Prototype loaded, but the interface has no parameters — either the target " +
                       "genuinely has none, or its interface could not be read headless.";

            // Surface the LabVIEW-adapter routing diagnostic (cast/server/init outcome) in the note so
            // it comes back via stdout regardless of log capture — the key signal for why a headless
            // .lvlibp load did or did not bind LabVIEW.
            if (lvRestore != null && !string.IsNullOrEmpty(lvRestore.Diag))
                note = $"[lv-route: {lvRestore.Diag}] " + (note ?? (loaded ? "Prototype loaded." : ""));

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
            }

            return new LoadPrototypeResult
            {
                StepName        = stepName,
                Adapter         = adapterKey,
                PrototypeLoaded = loaded,
                Note            = note,
                Parameters      = parameters
            };
        });
    }

    // A LabVIEW step uses the "G Std Prototype Adapter" or "G Flexible VI Adapter" (both key names
    // start with "G "). These are the only adapters whose prototype load reaches LabVIEW and can
    // trigger the native .lvlibp delay-load crash.
    private static bool IsLabVIEWAdapter(string? adapterKey)
        => !string.IsNullOrEmpty(adapterKey)
           && (adapterKey.StartsWith("G ", StringComparison.OrdinalIgnoreCase)
               || adapterKey.IndexOf("LabVIEW", StringComparison.OrdinalIgnoreCase) >= 0);

    // Best-effort read of a step's VI path (to detect a packed library .lvlibp). Tries the common
    // homes for the VI reference; returns "" if none present. Never calls LoadPrototype.
    private string ReadStepViPath(NiPropertyObject stepPo)
    {
        foreach (var p in new[] {
            "TS.SData.ViCall.VIPath", "VIModule.ViCall.VIPath",
            "TS.SData.ViCall.Namespace", "VIModule.ViCall.Namespace" })
        {
            try { var v = stepPo.GetValString(p, 0); if (!string.IsNullOrEmpty(v)) return v; }
            catch (Exception ex) { _logger.LogDebug(ex, "No VI path at '{Path}'.", p); }
        }
        return "";
    }

    private static string AppendPackedLibHint(string? note)
    {
        const string hint = "The VI is in a packed library (.lvlibp) whose connector pane cannot be " +
            "regenerated headless. Use copy_step_module to copy the cached ViCall metadata verbatim " +
            "from a source .seq instead.";
        return string.IsNullOrEmpty(note) ? hint : note + " " + hint;
    }

    // Runs the prototype load in a short-lived child instance of this same executable
    // (--load-prototype-worker). The child owns its own engine, opens the file, loads the prototype,
    // saves on success and prints a one-line result. A native delay-load crash kills only the child;
    // this parent survives. On success the file on disk carries the loaded pane, so we reload it and
    // read the interface back; on crash/timeout/clean-failure the on-disk file is unchanged.
    private async Task<LoadPrototypeResult> RunLoadPrototypeViaWorkerAsync(
        string filePath, string sequenceName, string stepGroup, string stepName,
        bool save, int timeoutSeconds, string adapterKey, bool isPackedLib, string labviewServer,
        IReadOnlyList<string>? openFiles = null)
    {
        var result = new LoadPrototypeResult
        {
            StepName = stepName, Adapter = adapterKey, ExecutionMode = "worker"
        };

        var (outcome, workerNote) = await RunPrototypeWorkerProcessAsync(
            filePath, sequenceName, stepGroup, stepName, Math.Max(5, timeoutSeconds), labviewServer,
            openFiles);
        result.WorkerOutcome = outcome;

        if (outcome == "loaded")
        {
            // The worker saved the loaded pane to disk — drop our stale in-memory copy and re-read.
            var fresh = await Task.Run(() =>
            {
                var sf   = ReloadSequenceFileFromDisk(filePath);
                var seq  = sf.GetSequenceByName(sequenceName);
                var step = ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
                return ReadModuleParameters(((NiStep)(object)step).AsPropertyObject(), stepName);
            });
            result.Parameters.AddRange(fresh);
            result.PrototypeLoaded = true;
            result.Note = fresh.Count == 0
                ? "Prototype loaded in an isolated worker, but the interface has no parameters."
                : (save ? null
                        : "Prototype loaded in an isolated worker. NOTE: an isolated LabVIEW load " +
                          "always persists to disk (that is how the worker returns the loaded pane), " +
                          "so save=false could not be honored.");
            return result;
        }

        // Not loaded / crashed / timed out → the on-disk file is unchanged; read the CURRENT pane so
        // the caller still sees whatever bindings already exist, and attach an explanatory note.
        var current = await Task.Run(() =>
        {
            try
            {
                var sf   = GetOrLoadSeqFile(filePath);
                var seq  = sf.GetSequenceByName(sequenceName);
                var step = ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
                return ReadModuleParameters(((NiStep)(object)step).AsPropertyObject(), stepName);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not read current params after worker '{Outcome}'.", outcome); return new List<ModuleParameterInfo>(); }
        });
        result.Parameters.AddRange(current);
        result.PrototypeLoaded = false;
        result.Note = outcome switch
        {
            "crashed" => "The LabVIEW prototype load crashed the isolated worker process (native " +
                         "delay-load fault, e.g. 0xC06D007E ERROR_MOD_NOT_FOUND — a LabVIEW runtime/" +
                         "adapter DLL could not be bound). The main server was protected and stays " +
                         "alive. Nothing was changed.",
            "timeout" => $"The LabVIEW prototype load did not finish within {timeoutSeconds}s and the " +
                         "isolated worker was terminated. The main server stays alive; nothing was " +
                         "changed. Ensure LabVIEW is reachable, or raise timeout_seconds.",
            _         => workerNote ??
                         "LoadPrototype could not resolve the target/module in the isolated worker. " +
                         "Nothing was changed. For LabVIEW the VI must be loadable (LabVIEW available)."
        };
        if (isPackedLib) result.Note = AppendPackedLibHint(result.Note);
        return result;
    }

    // Spawns the worker child and interprets its exit. Returns (outcome, note):
    //   "loaded"     – worker reported the prototype loaded (file saved to disk)
    //   "not-loaded" – worker ran but the target was unresolvable (clean managed failure)
    //   "crashed"    – worker died abnormally (native SEH / non-zero exit / no result line)
    //   "timeout"    – worker exceeded the timeout and was killed (process tree)
    private async Task<(string outcome, string? note)> RunPrototypeWorkerProcessAsync(
        string filePath, string sequenceName, string stepGroup, string stepName, int timeoutSeconds,
        string labviewServer, IReadOnlyList<string>? openFiles = null)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe))
        {
            _logger.LogWarning("Cannot locate own executable to spawn the prototype worker; " +
                "falling back to reporting a clean failure.");
            return ("not-loaded", "Could not launch the isolated worker (own exe path unknown).");
        }

        // Propagate THIS engine's search directories to the worker's fresh engine. The worker owns a
        // brand-new engine that only reads the station's default SearchDirectories.cfg, so any directory
        // added at runtime (e.g. add_search_directory pointing at a project's library folder) — needed
        // to resolve a relative module path like "MyLib.lvlibp\...\Foo.vi" — would be invisible to it.
        // Without this the worker fails "Could not find file 'MyLib.lvlibp'" before it can even attempt
        // the load. We serialise the non-empty, enabled search dirs to a temp file and pass it along.
        string? searchDirsFile = null;
        try
        {
            var sdirs = await GetSearchDirectoriesAsync();
            var toPropagate = new List<object>();
            foreach (var d in sdirs)
                if (!string.IsNullOrWhiteSpace(d.Path) && !d.Disabled)
                    toPropagate.Add(new { path = d.Path, subdirs = d.SearchSubdirectories });
            if (toPropagate.Count > 0)
            {
                searchDirsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "ts_mcp_lp_searchdirs_" + Guid.NewGuid().ToString("N") + ".json");
                System.IO.File.WriteAllText(searchDirsFile,
                    System.Text.Json.JsonSerializer.Serialize(toPropagate));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not gather search directories to propagate to the worker.");
            searchDirsFile = null;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = exe,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        foreach (var a in new[] {
            "--load-prototype-worker",
            "--file",     filePath,
            "--seq",      sequenceName,
            "--group",    stepGroup,
            "--step",     stepName,
            "--lv-server", labviewServer ?? "deferred" })
            psi.ArgumentList.Add(a);
        if (searchDirsFile != null)
        {
            psi.ArgumentList.Add("--search-dirs");
            psi.ArgumentList.Add(searchDirsFile);
        }

        // Callee files the worker must open before the load. A CROSS-FILE SequenceCall's prototype
        // cache (TS.SData.Prototype) is only filled when the target file is loaded in the SAME engine,
        // and the worker owns a fresh one that knows nothing of what the parent had open.
        string? openFilesFile = null;
        if (openFiles != null && openFiles.Count > 0)
        {
            try
            {
                openFilesFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "ts_mcp_lp_openfiles_" + Guid.NewGuid().ToString("N") + ".json");
                System.IO.File.WriteAllText(openFilesFile,
                    System.Text.Json.JsonSerializer.Serialize(openFiles));
                psi.ArgumentList.Add("--open-files");
                psi.ArgumentList.Add(openFilesFile);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not write the worker callee-file list.");
                openFilesFile = null;
            }
        }

        // Give the worker the FULL logon environment. A Claude/stdio-launched MCP host inherits a
        // stripped env (missing TESTSTAND*/NIEXTCCOMPILERSUPP/ProgramData/PUBLIC/…), which is the root
        // cause of the native .lvlibp delay-load fault (0xC06D007E) — the load works from a normal
        // PowerShell launch precisely because that has the complete env. Composing it here makes the
        // worker independent of however the parent was launched.
        ComposeChildEnvironmentFromRegistry(psi);

        try
        {
        return await Task.Run(() =>
        {
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            string? resultLine = null;
            var stdoutSb = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdoutSb.AppendLine(e.Data);
                if (e.Data.StartsWith(WorkerResultSentinel, StringComparison.Ordinal))
                    resultLine = e.Data.Substring(WorkerResultSentinel.Length).Trim();
            };
            proc.ErrorDataReceived += (_, __) => { /* worker logs → drop (kept off our stdout) */ };

            try { proc.Start(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start the prototype worker process.");
                return ("not-loaded", "Could not start the isolated worker: " + ex.Message);
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            try { proc.StandardInput.Close(); } catch (Exception ex) { _logger.LogDebug(ex, "Closing worker stdin failed."); }

            if (!proc.WaitForExit(timeoutSeconds * 1000))
            {
                _logger.LogWarning("Prototype worker exceeded {Timeout}s — killing its process tree.", timeoutSeconds);
                try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { _logger.LogDebug(ex, "Killing the worker tree failed."); }
                try { proc.WaitForExit(5000); } catch (Exception ex) { _logger.LogDebug(ex, "Waiting for killed worker failed."); }
                return ("timeout", null);
            }
            proc.WaitForExit(); // flush async readers

            int code = proc.ExitCode;
            if (resultLine != null)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(resultLine);
                    var root = doc.RootElement;
                    bool loaded = root.TryGetProperty("loaded", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.True;
                    string? note = root.TryGetProperty("note", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
                    return (loaded ? "loaded" : "not-loaded", note);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not parse worker result line: {Line}", resultLine); }
            }

            // No result line → the worker died before reporting (native crash) or exited abnormally.
            _logger.LogWarning("Prototype worker produced no result (exit code 0x{Code:X8}).", code);
            return ("crashed", null);
        });
        }
        finally
        {
            if (searchDirsFile != null)
            {
                try { System.IO.File.Delete(searchDirsFile); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not delete temp search-dirs file '{File}'.", searchDirsFile); }
            }
        }
    }

    // Drops any cached copy of the file (releasing the engine's reference) and re-reads it from disk,
    // so changes another process (the prototype worker) saved become visible here.
    private dynamic ReloadSequenceFileFromDisk(string filePath)
    {
        if (_loadedSequenceFiles.TryGetValue(filePath, out var old))
        {
            try { _engine!.ReleaseSequenceFileEx(old, 0); } catch (Exception ex) { _logger.LogDebug(ex, "ReleaseSequenceFileEx failed during reload of '{File}'.", filePath); }
            _loadedSequenceFiles.Remove(filePath);
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(old); } catch (Exception ex) { _logger.LogDebug(ex, "ReleaseComObject failed during reload of '{File}'.", filePath); }
        }
        var fresh = _engine!.GetSequenceFileEx(filePath, 0, (NiConflictHandler)4);
        _loadedSequenceFiles[filePath] = fresh;
        return fresh;
    }

    /// <summary>Sentinel prefix the worker prints before its one-line JSON result on stdout, so the
    /// parent can pick the result out from any other child stdout.</summary>
    internal const string WorkerResultSentinel = "__LPWORKER_RESULT__ ";

    /// <inheritdoc/>
    public async Task SetModuleParameterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string parameterName, string value,
        bool useExpression = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = ResolveStepInGroup(seq, sgVal, stepName);

            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
            bool set = false;

            // 1) LabVIEW connector-pane binding (TS.SData.ViCall.Parms or the step-root
            //    VIModule.ViCall.Parms of utility steps): match by Label — nested cluster
            //    members via "parent.child" (e.g. "error out.status"). Writes ArgVal and
            //    clears UseDefaultValues so the binding takes effect.
            foreach (var parmsPath in new[] { "TS.SData.ViCall.Parms", "VIModule.ViCall.Parms" })
            {
                try
                {
                    NiPropertyObject parms =
                        (NiPropertyObject)(object)stepPo.GetPropertyObject(parmsPath, 0);
                    NiPropertyObject? parm = FindViCallParm(parms, parameterName.Split('.'));
                    if (parm != null)
                    {
                        parm.SetValString("ArgVal", 0, value);
                        try { parm.SetValBoolean("UseDefaultValues", 0, string.IsNullOrEmpty(value)); }
                        catch (Exception ex) { _logger.LogDebug(ex, "UseDefaultValues not settable on '{Param}'.", parameterName); }
                        set = true;
                        break;
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "No VI parms at '{Path}'.", parmsPath); }
            }

            // 2) SequenceCall argument (TS.SData.ActualArgs.<name>): bind the Expr and clear
            //    UseDef. When the entry is missing, first try to materialise the WHOLE callee
            //    prototype (editor "Load Prototype") so every parameter becomes a correctly-typed
            //    SequenceArgument (right ParamType/ParamRepresentation/Flags, UseDef=True) and the
            //    cached Prototype container is filled. Only if the target cannot be resolved
            //    (headless / missing file) do we fall back to a bare on-demand entry.
            if (!set)
            {
                try
                {
                    bool ArgExists(NiPropertyObject a)
                    { try { a.GetPropertyObject(parameterName, 0); return true; } catch { return false; } }

                    NiPropertyObject args =
                        (NiPropertyObject)(object)stepPo.GetPropertyObject("TS.SData.ActualArgs", 0);
                    if (!ArgExists(args))
                    {
                        TryLoadModulePrototype(((NiStep)(object)step).Module, stepName);
                        args = (NiPropertyObject)(object)stepPo.GetPropertyObject("TS.SData.ActualArgs", 0);
                    }
                    if (!ArgExists(args))
                        args.NewSubProperty(parameterName,
                            (NiPropValueTypes)((int)NiPropValueTypes.PropValType_NamedType),
                            false, "SequenceArgument", 0);
                    NiPropertyObject arg =
                        (NiPropertyObject)(object)args.GetPropertyObject(parameterName, 0);
                    arg.SetValString("Expr", 0, value);
                    try { arg.SetValBoolean("UseDef", 0, string.IsNullOrEmpty(value)); }
                    catch (Exception ex) { _logger.LogDebug(ex, "UseDef not settable on '{Param}'.", parameterName); }
                    set = true;
                }
                catch (Exception ex) { _logger.LogDebug(ex, "No SequenceCall ActualArgs on step '{Step}'.", stepName); }
            }

            // 3) Legacy flat Module.Parameters container.
            if (!set)
            {
                string[] paramPaths = {
                    $"TS.Module.Parameters.{parameterName}",
                    $"Module.Parameters.{parameterName}"
                };
                foreach (var path in paramPaths)
                {
                    try
                    {
                        if (useExpression)
                        {
                            stepPo.SetValString(path, 0x8, value);
                        }
                        else
                        {
                            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double d))
                                stepPo.SetValNumber(path, 0, d);
                            else if (bool.TryParse(value, out bool b))
                                stepPo.SetValBoolean(path, 0, b);
                            else
                                stepPo.SetValString(path, 0, value);
                        }
                        set = true;
                        break;
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to set module parameter '{Param}' via path '{Path}' on step '{Step}'.", parameterName, path, stepName); }
                }
            }

            if (!set)
                throw new InvalidOperationException(
                    $"Could not set module parameter '{parameterName}' on step '{stepName}'.");

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Step Configuration ────────────────────────────────────────────────────

    /// <summary>
    /// Wraps plain text as a TestStand expression string literal (e.g. <c>Connect DUT</c> →
    /// <c>"Connect DUT"</c>). Embedded double-quotes are doubled, per TestStand string syntax.
    /// </summary>
    private static string ToExpressionLiteral(string text)
        => "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";

    /// <inheritdoc/>
    public async Task ConfigureMessagePopupAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string message,
        string? title = null, string buttons = "OK", double timeout = -1)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // MessagePopup settings are TOP-LEVEL step properties (NOT under "TS.MessagePopup",
            // which does not exist — the old writes were silently swallowed). Verified via a
            // property-tree dump:
            //   • MessageExpr / TitleExpr are EXPRESSION strings → wrap plain text as a "literal"
            //   • the button set is defined by Button1Label..Button6Label (expression strings),
            //     NOT by a numeric "Buttons" property
            //   • TimeToWait (seconds) + TimerButton (button auto-pressed on timeout) drive timeout
            po.SetValString("MessageExpr", 0, ToExpressionLiteral(message));
            if (!string.IsNullOrEmpty(title))
                po.SetValString("TitleExpr", 0, ToExpressionLiteral(title!));

            string[] labels = buttons.ToLowerInvariant() switch
            {
                "okcancel"    or "ok cancel"     => new[] { "OK", "Cancel" },
                "yesno"       or "yes no"        => new[] { "Yes", "No" },
                "yesnocancel" or "yes no cancel" => new[] { "Yes", "No", "Cancel" },
                _                                => new[] { "OK" }
            };
            for (int i = 1; i <= 6; i++)
                po.SetValString($"Button{i}Label", 0,
                    i <= labels.Length ? ToExpressionLiteral(labels[i - 1]) : "\"\"");

            // Timeout: TimeToWait in seconds; TimerButton = which button is pressed on timeout
            // (use the last button). timeout <= 0 disables the timer.
            if (timeout > 0)
            {
                po.SetValNumber("TimeToWait", 0, timeout);
                po.SetValNumber("TimerButton", 0, labels.Length);
            }
            else
            {
                po.SetValNumber("TimeToWait", 0, 0);
                po.SetValNumber("TimerButton", 0, 0);
            }

            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    /// <inheritdoc/>
    public async Task ConfigurePropertyLoaderAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string filePathExpr, string mode = "Read")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // A real PropertyLoader step (created with step type "NI_PropertyLoader") stores its
            // file in the FIRST element of the PropertyLoaderSources array, under
            // Options.CommonOptions.Source.Location (verified via a property-tree dump). The old
            // "TS.PropertyLoader.*" paths do not exist on the step and were silently swallowed —
            // and on a plain Action step there is no PropertyLoaderSources at all.
            NiPropertyObject sources;
            try { sources = po.GetPropertyObject("PropertyLoaderSources", 0); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Step '{stepName}' is not a PropertyLoader step (no PropertyLoaderSources " +
                    "property). Create it with step type 'NI_PropertyLoader'.", ex);
            }
            if (CountArrayElements(sources) == 0)
                sources.InsertElements(0, 1, 0);

            // Source.Location is a plain file path; UseAlias must be false for Location to apply.
            po.SetValString("PropertyLoaderSources[0].Options.CommonOptions.Source.Location", 0, filePathExpr);
            po.SetValBoolean("PropertyLoaderSources[0].Options.CommonOptions.Source.UseAlias", 0, false);

            // An NI_PropertyLoader step always IMPORTS properties from the source — its property
            // structure has no read/write toggle. 'mode' is kept for API compatibility; only
            // "Read" is meaningful. Note when a write was requested so it is not silently dropped.
            if (!string.Equals(mode, "Read", StringComparison.OrdinalIgnoreCase))
                _logger.LogDebug("PropertyLoader step '{Step}': mode '{Mode}' has no effect — the step only imports.", stepName, mode);

            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    // ── Numeric / String Limit Configuration ─────────────────────────────────

    /// <inheritdoc/>
    public async Task SetNumericLimitsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName,
        double? lowLimit, double? highLimit, string? units,
        string comparisonType = "GELE")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // Correct paths: NumericLimitTest stores limits under Limits.Low/High/Units
            // and comparison type under Comp (not TS.NumericLimitTest.*)
            // The NumericLimitTest comparison operator is stored as the STRING property "Comp"
            // (e.g. "GELE", "GT", "EQ") — NOT as a number. Writing it via SetValNumber throws
            // (and was silently caught), so the comparison type never actually persisted.
            string compStr = comparisonType.ToUpperInvariant() switch
            {
                "GE" => "GE",
                "LE" => "LE",
                "EQ" => "EQ",
                "NE" => "NE",
                "GT" => "GT",
                "LT" => "LT",
                _    => "GELE"
            };
            try { po.SetValString("Comp", 0, compStr); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set numeric comparison type on step '{Step}'.", stepName); }

            if (lowLimit.HasValue)
                try { po.SetValNumber("Limits.Low", 0, lowLimit.Value); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set low limit on step '{Step}'.", stepName); }

            if (highLimit.HasValue)
                try { po.SetValNumber("Limits.High", 0, highLimit.Value); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set high limit on step '{Step}'.", stepName); }

            if (units != null)
                try { po.SetValString("Limits.Units", 0, units); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set limit units on step '{Step}'.", stepName); }

            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object?>> GetNumericLimitsAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            var result = new Dictionary<string, object?>();

            double? GetNum(string path)
            {
                try { return po.GetValNumber(path, 0); }
                catch { return null; }
            }
            string? GetStr(string path)
            {
                try
                {
                    var v = po.GetValString(path, 0);
                    return string.IsNullOrEmpty(v) ? null : v;
                }
                catch { return null; }
            }

            // Correct paths: Limits.Low, Limits.High, Limits.Units, Comp, DataSource
            result["low_limit"]              = GetNum("Limits.Low");
            result["high_limit"]             = GetNum("Limits.High");
            result["units"]                  = GetStr("Limits.Units");
            result["measurement_expression"] = GetStr("DataSource");

            // "Comp" is a STRING property holding the operator token directly (e.g. "GELE", "EQ").
            result["comparison_type"] = GetStr("Comp") ?? "GELE";

            return result;
        });
    }

    /// <inheritdoc/>
    public async Task SetStepMeasurementAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();
            // DataSource is the measurement expression for NumericLimitTest
            po.SetValString("DataSource", 0, expression);
            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    /// <inheritdoc/>
    public async Task SetWaitTimeAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string timeExpression)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // NI_Wait: WaitForTarget=0 selects the "wait a time interval" mode; TimeExpr holds the
            // seconds (as an expression). A freshly inserted Wait step has an EMPTY TimeExpr and so
            // never actually waits — and there was previously no tool to set it.
            po.SetValNumber("WaitForTarget", 0, 0);   // 0 = wait a fixed time interval
            po.SetValString("TimeExpr", 0, timeExpression);
            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    /// <inheritdoc/>
    public async Task ConfigureWaitAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string waitMode, string? expression = null,
        string? timeoutExpr = null, bool? timeoutEnabled = null, bool? errorOnTimeout = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // WaitForTarget selects what the NI_Wait waits on: 0 = time interval, 1 = execution,
            // 2 = thread (verified against real files). Each mode reads a different expression prop.
            var mode = (waitMode ?? "time").Trim().ToLowerInvariant();
            (int target, string exprProp) = mode switch
            {
                "thread"           => (2, "ThreadRefExpr"),
                "execution" or "exec" => (1, "ExecutionRefExpr"),
                _                  => (0, "TimeExpr"),   // time interval
            };
            po.SetValNumber("WaitForTarget", 0, target);
            if (expression != null)
                po.SetValString(exprProp, 0, expression);

            // A fresh NI_Wait can carry SpecifyBySeqCall=true / SeqCallStepGroupIdx=0, which would make
            // it wait on a sequence-call step instead of the target we just set. Clear them so the
            // explicit time/thread/execution target actually takes effect (and matches editor output).
            try { po.SetValBoolean("SpecifyBySeqCall", 0, false); } catch (Exception ex) { _logger.LogDebug(ex, "No SpecifyBySeqCall on wait step '{Step}'.", stepName); }
            try { po.SetValNumber("SeqCallStepGroupIdx", 0, -1); } catch (Exception ex) { _logger.LogDebug(ex, "No SeqCallStepGroupIdx on wait step '{Step}'.", stepName); }

            if (timeoutExpr != null)
                try { po.SetValString("TimeoutExpr", 0, timeoutExpr); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set TimeoutExpr on wait step '{Step}'.", stepName); }
            if (timeoutEnabled.HasValue)
                try { po.SetValBoolean("TimeoutEnabled", 0, timeoutEnabled.Value); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set TimeoutEnabled on wait step '{Step}'.", stepName); }
            if (errorOnTimeout.HasValue)
                try { po.SetValBoolean("ErrorOnTimeout", 0, errorOnTimeout.Value); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set ErrorOnTimeout on wait step '{Step}'.", stepName); }

            SaveSequenceFileWithRetry(seqFile, filePath);
            _loadedSequenceFiles[filePath] = seqFile;
        });
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> ConfigureRunViAsyncAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string viPath,
        string? viNamespace = null, int threadOption = 1, string? threadRefExpr = null,
        bool autoWait = true, string sequenceNameExpr = "\"MainSequence\"",
        string sequenceFileExpr = "Evaluate(Step.SequenceFileExpr)", bool save = true)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();
            var applied = new Dictionary<string, object>();

            // Build the async-launch module (TS.SData). An NI_LV_RunVIAsynchronously step inserts with a
            // NoneStepAdditions module, but the real "Run VI Asynchronously" step drives the async launch
            // through a Sequence-adapter SeqCallStepAdditions module (it calls "MainSequence" in a new
            // thread to host the VI). Switching the adapter to get that module CORRUPTS the step (it turns
            // into an Action and drops VIModule), so build the module by RETYPING the container directly —
            // Engine.NewPropertyObject(PropValType_NamedType,"SeqCallStepAdditions") + SetPropertyObject.
            string sdataType = "";
            try { sdataType = ((NiPropertyObject)(object)po.GetPropertyObject("TS.SData", 0)).GetTypeDisplayString("", 0); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not read TS.SData type on step '{Step}'.", stepName); }
            if (!sdataType.StartsWith("SeqCallStepAdditions", StringComparison.Ordinal))
            {
                NiPropertyObject sdataNew = (NiPropertyObject)(object)_engine!.NewPropertyObject(
                    NiPropValueTypes.PropValType_NamedType, false, "SeqCallStepAdditions", 0);
                po.SetPropertyObject("TS.SData", 0, sdataNew);
            }
            applied["module"] = "SeqCallStepAdditions";

            // Async-launch defaults (the "Run VI Asynchronously" template): launch "MainSequence" in a
            // new thread, using the cached prototype, resolving the file/sequence by expression.
            po.SetValString ("TS.SData.SFPathExpr",          0, sequenceFileExpr);
            po.SetValString ("TS.SData.SeqNameExpr",         0, sequenceNameExpr);
            po.SetValBoolean("TS.SData.SpecifyByExpr",       0, true);
            po.SetValBoolean("TS.SData.UsePrototype",        0, true);
            po.SetValNumber ("TS.SData.ThreadOpt",           0, threadOption);   // 1 = new thread
            po.SetValBoolean("TS.SData.AutoWaitAsync",       0, autoWait);
            // The async-VI step uses a plain "-1" affinity, not the SeqCall type default expression.
            try { po.SetValString("TS.SData.CustomThreadAffinity", 0, "-1"); } catch (Exception ex) { _logger.LogDebug(ex, "Could not set CustomThreadAffinity on '{Step}'.", stepName); }
            if (!string.IsNullOrEmpty(threadRefExpr))
            {
                po.SetValString("TS.SData.AsyncThreadExpr", 0, threadRefExpr);
                applied["threadRefExpr"] = threadRefExpr!;
            }
            applied["threadOpt"] = threadOption;
            applied["autoWait"]  = autoWait;

            // The VI itself lives in the step-own VIModule.ViCall (independent of the launch module).
            po.SetValString("VIModule.ViCall.VIPath", 0, viPath);
            applied["viPath"] = viPath;
            if (!string.IsNullOrEmpty(viNamespace))
            {
                po.SetValString("VIModule.ViCall.Namespace", 0, viNamespace);
                applied["namespace"] = viNamespace!;
            }

            // The module-marker PropFlag (0x200000) TestStand puts on the VIModule container.
            try { po.SetFlags("VIModule", 0, 0x200000); } catch (Exception ex) { _logger.LogDebug(ex, "Could not set VIModule flag on '{Step}'.", stepName); }

            if (save)
            {
                SaveSequenceFileWithRetry(seqFile, filePath);
                _loadedSequenceFiles[filePath] = seqFile;
            }
            return applied;
        });
    }

    /// <inheritdoc/>
    /// <summary>The subtrees a step-module copy clones by default: the code module plus the authored
    /// step-config the step-type template does not instantiate on a fresh insert.</summary>
    private static readonly string[] AllStepModulePaths =
    {
        "TS.SData", "VIModule",
        "TS.AdditionalResultsHints", "TS.CustomResults",
        "TS.ErrorDialogOptions", "Result.TimeoutOccurred",
    };

    /// <summary>Only the AUTHORED step config — no code module. Used by import_sequence_file to reproduce
    /// result-logging hints and dialog options on every step without touching the module it just
    /// configured from the model.</summary>
    internal static readonly string[] AuthoredStepConfigPaths =
    {
        "TS.AdditionalResultsHints", "TS.CustomResults",
        "TS.ErrorDialogOptions", "Result.TimeoutOccurred",
    };

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> CopyStepModuleAsync(
        string sourceFilePath, string sourceSequenceName, string sourceStepGroup, string sourceStepName,
        string targetFilePath, string targetSequenceName, string targetStepGroup, string targetStepName,
        bool save = true, IReadOnlyList<string>? paths = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var srcSf = (NiSequenceFile)(object)GetOrLoadSeqFile(sourceFilePath);
            var tgtSf = (NiSequenceFile)(object)GetOrLoadSeqFile(targetFilePath);
            NiSequence srcSeq = srcSf.GetSequenceByName(sourceSequenceName);
            NiSequence tgtSeq = tgtSf.GetSequenceByName(targetSequenceName);
            NiStep srcStep = (NiStep)(object)ResolveStepInGroup(srcSeq, ParseStepGroup(sourceStepGroup), sourceStepName);
            NiStep tgtStep = (NiStep)(object)ResolveStepInGroup(tgtSeq, ParseStepGroup(targetStepGroup), targetStepName);
            NiPropertyObject srcPo = srcStep.AsPropertyObject();
            NiPropertyObject tgtPo = tgtStep.AsPropertyObject();

            var copied   = new List<string>();
            var warnings = new List<string>();
            var result   = new Dictionary<string, object>();

            // 1) Align the adapter FIRST (so the target owns the right-shaped module container);
            //    ChangeAdapter would otherwise reset the module we are about to copy.
            //    Skipped for a restricted path set: the caller is then copying authored step CONFIG only
            //    and the target's module is already configured — ChangeAdapter would reset it.
            string srcAdapter = TryGetString(srcStep, "AdapterKeyName");
            string tgtAdapter = TryGetString(tgtStep, "AdapterKeyName");
            if (paths is null && !string.IsNullOrEmpty(srcAdapter) &&
                !string.Equals(srcAdapter, tgtAdapter, StringComparison.OrdinalIgnoreCase))
            {
                try { ((dynamic)tgtStep).ChangeAdapter((object)srcAdapter); result["adapterChangedTo"] = srcAdapter; }
                catch (Exception ex) { warnings.Add($"Could not change target adapter to '{srcAdapter}': {ex.Message}"); }
            }

            // 2) Deep-copy each module-bearing subtree present on the source, carrying the ViCall metadata
            //    (Namespace/VIDescription/Checksum/Parms) and ActualArgs — no LabVIEW load. The module types
            //    must already exist in the target file (copy_typedefs).
            //    Beyond the code module we also clone the AUTHORED step-config subtrees that the step-type
            //    template carries but a freshly-inserted step does not always instantiate: the result-logging
            //    hints (TS.AdditionalResultsHints / TS.CustomResults), the error-dialog options that steps like
            //    NI_Wait/MessagePopup and the DQMH pattern author (TS.ErrorDialogOptions), and the NI_Wait
            //    timeout-result flag (Result.TimeoutOccurred). Each path is probed on the source and cloned
            //    only if present, so it is a harmless no-op for a step type that lacks it. This makes
            //    copy_step_module reproduce non-adapter step config (e.g. an NI_Wait) faithfully, not just the
            //    LabVIEW/DLL/.NET/SequenceCall module.
            //    IMPORTANT: the object returned by GetPropertyObject still BELONGS to the source tree, and
            //    SetPropertyObject refuses an object that "already has a parent object. You must first clone
            //    the item". So CLONE the subtree first (PropOption_CopyAllFlags = a flag-preserving deep copy
            //    that returns a detached, independent object) and attach the clone to the target.
            foreach (var path in paths ?? AllStepModulePaths)
            {
                // Existence probe on the source step (GetPropertyObject throws if the path is absent).
                try { _ = (NiPropertyObject)(object)srcPo.GetPropertyObject(path, 0); }
                catch { continue; } // not present on the source step
                try
                {
                    NiPropertyObject srcClone = (NiPropertyObject)(object)srcPo.Clone(
                        path, 0x20000000 /* PropOption_CopyAllFlags */);
                    tgtPo.SetPropertyObject(path, 0x1 /* PropOption_InsertIfMissing */, srcClone);
                    copied.Add(path);
                    // Mirror the node's own PropFlags too (e.g. the 0x200000 module marker on VIModule),
                    // in case the container flag differs from what CopyAllFlags carried on the leaf.
                    try { tgtPo.SetFlags(path, 0, srcPo.GetFlags(path, 0)); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Flag mirror skipped for '{Path}'.", path); }
                }
                catch (Exception ex) { warnings.Add($"Could not copy '{path}': {ex.Message}"); }
            }

            // Only a full copy is expected to find a module; a restricted path set legitimately finds
            // nothing when the source step authored no result hints or dialog options.
            if (copied.Count == 0 && paths is null)
                warnings.Add("No module subtree (TS.SData / VIModule) was found on the source step.");

            if (save)
            {
                SaveSequenceFileWithRetry(tgtSf, targetFilePath);
                _loadedSequenceFiles[targetFilePath] = tgtSf;
            }

            result["sourceStep"]  = sourceStepName;
            result["targetStep"]  = targetStepName;
            result["adapter"]     = string.IsNullOrEmpty(srcAdapter) ? "" : srcAdapter;
            result["copiedPaths"] = copied;
            result["warnings"]    = warnings;
            return result;
        });
    }

    /// <inheritdoc/>
    public async Task ConfigureStringValueTestAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression, string expectedValue,
        string comparisonType = "CaseSensitive")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var seqFile = (NiSequenceFile)(object)GetOrLoadSeqFile(filePath);
            NiSequence seq = seqFile.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            NiStep step = (NiStep)(object)ResolveStepInGroup(seq, sgVal, stepName);
            NiPropertyObject po = step.AsPropertyObject();

            // StringValueTest: DataSource = expression being tested
            try { po.SetValString("DataSource", 0, expression); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set StringValueTest DataSource on step '{Step}'.", stepName); }
            // Expected string value and comparison type stored under Limits[0]
            try { po.SetValString("Limits[0].String", 0, expectedValue); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set StringValueTest expected value on step '{Step}'.", stepName); }

            // The StringValueTest comparison is stored as the STRING property "Comp"
            // ("CaseSensitive" / "IgnoreCase") — NOT a number under "Limits[0].ComparisonType".
            // Writing the old (non-existent) path threw and was silently caught, so the
            // comparison never persisted.
            string compStr = comparisonType.ToLowerInvariant() switch
            {
                "caseinsensitive" or "case insensitive" or "ignorecase" => "IgnoreCase",
                "ignore"                                                => "IgnoreCase",
                _                                                       => "CaseSensitive"
            };
            try { po.SetValString("Comp", 0, compStr); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set StringValueTest comparison type on step '{Step}'.", stepName); }

            SaveSequenceFileWithRetry(seqFile, filePath);
        });
    }

    // ── Breakpoints ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SetStepBreakpointAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, bool enabled, string breakpointType = "Before")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            dynamic step = ResolveStepInGroup(seq, sgVal, stepName);

            // TestStand API: Step.BreakOnStep (bool) sets a "break before" breakpoint.
            // For "After" we set it on the next step conceptually; TestStand's file-level
            // API only exposes BreakOnStep (before). Use SetBreakOnStepEx for execution-level.
            bool breakBefore = enabled && breakpointType.ToLowerInvariant() != "after";
            bool breakAfter  = enabled && (breakpointType.ToLowerInvariant() == "after"
                                        || breakpointType.ToLowerInvariant() == "both");

            var stepObj   = (object)step;
            var stepType2 = stepObj.GetType();

            // Set BreakOnStep via .NET property (file-level before-break)
            try
            {
                stepType2.InvokeMember("BreakOnStep",
                    System.Reflection.BindingFlags.SetProperty |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public,
                    null, stepObj, new object[] { breakBefore || breakAfter });
            }
            catch
            {
                try { step.BreakOnStep = (object)(breakBefore || breakAfter); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set BreakOnStep on step '{Step}'.", stepName); }
            }

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task<List<Dictionary<string, string>>> GetBreakpointsAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf     = GetOrLoadSeqFile(filePath);
            var result = new List<Dictionary<string, string>>();

            int numSeqs = 0;
            try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to get NumSequences from sequence file."); }

            string[] groupNames = { "Setup", "Main", "Cleanup" };

            for (int si = 0; si < numSeqs; si++)
            {
                dynamic seq2;
                string seqName;
                try { seq2 = sf.GetSequence(si); seqName = (string)seq2.Name; }
                catch { continue; }

                for (int g = 0; g <= 2; g++)
                {
                    int cnt = 0;
                    try { cnt = Convert.ToInt32((object)seq2.GetNumSteps((object)g)); }
                    catch { continue; }

                    for (int i = 0; i < cnt; i++)
                    {
                        try
                        {
                            dynamic step2   = seq2.GetStep(i, (object)g);
                            bool hasBreak   = false;
                            var s2Obj       = (object)step2;
                            var s2Type      = s2Obj.GetType();
                            try
                            {
                                hasBreak = (bool)s2Type.InvokeMember("BreakOnStep",
                                    System.Reflection.BindingFlags.GetProperty |
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.Public,
                                    null, s2Obj, null)!;
                            }
                            catch
                            {
                                try { hasBreak = (bool)step2.BreakOnStep; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read BreakOnStep via dynamic fallback."); }
                            }

                            if (!hasBreak) continue;
                            string bpType = "Before";

                            result.Add(new Dictionary<string, string>
                            {
                                ["sequence_name"]   = seqName,
                                ["step_group"]      = groupNames[g],
                                ["step_name"]       = (string)step2.Name,
                                ["breakpoint_type"] = bpType
                            });
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read breakpoint info for a step."); }
                    }
                }
            }

            return result;
        });
    }

    // ── Execution Results ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Dictionary<string, object?>> GetStepResultAsync(string executionId,
        string sequenceName, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            var result = new Dictionary<string, object?>();

            try
            {
                dynamic rootResult  = exec.ResultObject;
                dynamic? stepResult = FindStepResultByName(rootResult, stepName);

                if (stepResult == null)
                {
                    result["error"] = $"Step result for '{stepName}' not found.";
                    return result;
                }

                foreach (var f in new[] { "TS.StepName", "TS.SequenceName",
                                          "TS.Result.Status", "TS.StepType" })
                {
                    string key = f.Replace("TS.Result.", "").Replace("TS.", "").ToLowerInvariant();
                    try { result[key] = (string)stepResult.GetValString((object)f, (object)0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read step result field '{Field}'.", f); }
                }
                try { result["numeric_value"] = (double)stepResult.GetValNumber((object)"TS.Result.NumericValue", (object)0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read NumericValue from step result."); }
                try { result["string_value"]  = (string)stepResult.GetValString((object)"TS.Result.StringValue",  (object)0); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StringValue from step result."); }
            }
            catch (Exception ex)
            {
                result["error"] = ex.Message;
            }

            return result;
        });
    }

    private static dynamic? FindStepResultByName(dynamic resultObj, string stepName, int depth = 0)
    {
        if (depth > 4) return null;

        // Try array access first
        int arrayCount = 0;
        try { arrayCount = Convert.ToInt32((object)resultObj.GetNumElements()); } catch (Exception) { /* best-effort: probe result array count — intentionally ignored */ }

        if (arrayCount > 0)
        {
            for (int i = 0; i < arrayCount; i++)
            {
                try
                {
                    dynamic sr = resultObj.GetPropertyObjectByOffset((object)i, (object)0);
                    string sn  = "";
                    try { sn = (string)sr.GetValString((object)"TS.StepName", (object)0); } catch (Exception) { /* best-effort: read StepName from result element — intentionally ignored */ }
                    if (sn == stepName) return sr;

                    // Check nested ResultList
                    try
                    {
                        dynamic sub = sr.GetPropertyObject((object)"ResultList", (object)0);
                        var found = FindStepResultByName(sub, stepName, depth + 1);
                        if (found != null) return found;
                    }
                    catch (Exception) { /* best-effort: traverse nested ResultList — intentionally ignored */ }
                }
                catch (Exception) { /* best-effort: access result element by offset — intentionally ignored */ }
            }
            return null;
        }

        // Fall back to named sub-properties
        int count = 0;
        try { count = Convert.ToInt32((object)resultObj.GetNumSubProperties((object)"")); }
        catch { return null; }
        for (int i = 0; i < count; i++)
        {
            try
            {
                string name = (string)resultObj.GetNthSubPropertyName((object)"", (object)i, (object)0);
                dynamic sr  = resultObj.GetPropertyObject((object)name, (object)0);
                string sn   = "";
                try { sn = (string)sr.GetValString((object)"TS.StepName", (object)0); } catch (Exception) { /* best-effort: read StepName from named sub-property — intentionally ignored */ }
                if (sn == stepName) return sr;

                var nested = FindStepResultByName(sr, stepName, depth + 1);
                if (nested != null) return nested;
            }
            catch (Exception) { /* best-effort: enumerate named sub-property — intentionally ignored */ }
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object?>> GetExecutionResultsAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            var result = new Dictionary<string, object?>();
            result["retrieved_at"] = (object)DateTime.UtcNow.ToString("O");

            // ── Execution-level info from the Execution object directly ─────────
            try { result["overall_status"]  = (string)exec.ResultStatus;           } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read execution ResultStatus."); }
            try { result["seconds_elapsed"] = (double)exec.SecondsExecuting;       } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read execution SecondsExecuting."); }
            try { result["display_name"]    = TryGetString(exec, "DisplayName");   } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read execution DisplayName."); }
            try { result["elapsed_seconds_from_start"] =
                    _executionStartTimes.TryGetValue(executionId, out DateTime t0)
                    ? (DateTime.UtcNow - t0).TotalSeconds : 0; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to compute elapsed seconds for execution."); }

            // ── Step results from the result object ──────────────────────────────
            try
            {
                dynamic rootResult = exec.ResultObject;

                // Read scalar string fields stored directly as sub-properties
                foreach (var f in new[] { "Status", "SequenceFile", "Sequence" })
                {
                    try
                    {
                        dynamic sub = rootResult.GetPropertyObject((object)f, (object)0);
                        // Try to read the value of the sub-property
                        try   { result[f.ToLowerInvariant()] = (string)sub.GetValString((object)"", (object)0); }
                        catch { /* property is a container, not a leaf */ }
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read execution result sub-property '{Field}'.", f); }
                }

                // Step results are in "ResultList" array (only populated by UUT process model runs)
                dynamic resultList = rootResult.GetPropertyObject((object)"ResultList", (object)0);
                result["step_results"] = CollectStepResults(resultList, 0);
            }
            catch
            {
                result["step_results"] = new List<Dictionary<string, object?>>();
            }

            if (result["step_results"] is List<Dictionary<string, object?>> sl && sl.Count == 0)
                result["result_note"] = "Step-level results are only available when running via a UUT process model. " +
                                        "Use overall_status / seconds_elapsed for direct sequence calls.";

            return result;
        });
    }

    private static List<Dictionary<string, object?>> CollectStepResults(dynamic resultObj, int depth = 0)
    {
        var list = new List<Dictionary<string, object?>>();
        if (depth > 4) return list;

        // ── Try array access first (ResultList is a PropertyObject array) ──────
        int arrayCount = 0;
        try { arrayCount = Convert.ToInt32((object)resultObj.GetNumElements()); } catch (Exception) { /* best-effort: probe result list array count — intentionally ignored */ }

        if (arrayCount > 0)
        {
            for (int i = 0; i < arrayCount; i++)
            {
                try
                {
                    dynamic sr  = resultObj.GetPropertyObjectByOffset((object)i, (object)0);
                    var entry   = new Dictionary<string, object?>();
                    entry["index"] = i;
                    ReadStepResultFields(sr, entry, depth);
                    list.Add(entry);
                }
                catch (Exception) { /* best-effort: collect step result at array offset — intentionally ignored */ }
            }
            return list;
        }

        // ── Fall back: named sub-properties ──────────────────────────────────
        int namedCount = 0;
        try { namedCount = Convert.ToInt32((object)resultObj.GetNumSubProperties((object)"")); }
        catch { return list; }

        for (int i = 0; i < namedCount; i++)
        {
            try
            {
                string name = (string)resultObj.GetNthSubPropertyName((object)"", (object)i, (object)0);
                dynamic sr  = resultObj.GetPropertyObject((object)name, (object)0);
                var entry   = new Dictionary<string, object?>();
                entry["property_name"] = name;
                ReadStepResultFields(sr, entry, depth);
                list.Add(entry);
            }
            catch (Exception) { /* best-effort: collect step result by name — intentionally ignored */ }
        }
        return list;
    }

    private static void ReadStepResultFields(dynamic sr, Dictionary<string, object?> entry, int depth)
    {
        // Scalar string fields
        foreach (var (path, key) in new[]
        {
            ("TS.StepName",            "step_name"),
            ("TS.SequenceName",        "sequence_name"),
            ("TS.Result.Status",       "status"),
            ("TS.StepType",            "step_type"),
            ("TS.Result.String.String","string_value"),
        })
            try { entry[key] = (string)sr.GetValString((object)path, (object)0); } catch (Exception) { /* best-effort: read step result string field — intentionally ignored */ }

        // Numeric measurement
        try { entry["numeric_value"] = (double)sr.GetValNumber((object)"TS.Result.Numeric.Value", (object)0); } catch (Exception) { /* best-effort: read numeric result value — intentionally ignored */ }

        // Nested ResultList (e.g. sub-sequence results)
        if (depth < 2)
        {
            try
            {
                dynamic subList = sr.GetPropertyObject((object)"ResultList", (object)0);
                var nested = CollectStepResults(subList, depth + 1);
                if (nested.Count > 0) entry["sub_results"] = nested;
            }
            catch (Exception) { /* best-effort: read nested ResultList — intentionally ignored */ }
        }
    }

    /// <inheritdoc/>
    public async Task<double> GetExecutionTimeAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            try { return (double)exec.ElapsedTime; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read ElapsedTime from execution; falling back to wall-clock."); }

            if (_executionStartTimes.TryGetValue(executionId, out var st))
                return (DateTime.UtcNow - st).TotalSeconds;

            return 0.0;
        });
    }

    // ── Bulk writers ──────────────────────────────────────────────────────────
    //
    // Every mutating tool saves the whole sequence file, and the MCP round-trip dominates the cost of
    // an edit. Building a 30-sequence file needed 30 insert_sequence calls, ~140 variable inserts and
    // ~250 property-node writes — all of them one-per-call with one full-file save each. These batch
    // the same operations behind a single call and a single save.

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> InsertSequencesBulkAsync(string filePath,
        IReadOnlyList<(string Name, string? Description)> sequences, bool save = true)
    {
        EnsureConnected();
        var created  = new List<string>();
        var warnings = new List<string>();
        foreach (var (name, description) in sequences)
        {
            try
            {
                await InsertSequenceAsync(filePath, name);
                created.Add(name);
                if (!string.IsNullOrEmpty(description))
                    await SetSequencePropertiesAsync(filePath, name,
                        new SequenceProperties { Name = name, Description = description! });
            }
            catch (Exception ex) { warnings.Add($"'{name}': {ex.Message}"); }
        }
        if (save) await SaveSequenceFileAsync(filePath);
        return new Dictionary<string, object>
        {
            ["insertedCount"]   = created.Count,
            ["insertedSequences"] = created,
            ["warnings"]        = warnings,
        };
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> InsertVariablesBulkAsync(string filePath,
        string scope, string? sequenceName, IReadOnlyList<VarModel> variables, bool save = true)
    {
        EnsureConnected();
        string sc = (scope ?? "").Trim().ToLowerInvariant();
        if (sc is not ("locals" or "parameters" or "fileglobals"))
            throw new ArgumentException(
                $"Unknown scope '{scope}'. Use Locals, Parameters or FileGlobals.");
        if (sc is "locals" or "parameters" && string.IsNullOrWhiteSpace(sequenceName))
            throw new ArgumentException($"scope='{scope}' requires sequence_name.");

        var created  = new List<string>();
        var warnings = new List<string>();
        foreach (var v in variables)
        {
            try
            {
                switch (sc)
                {
                    case "locals":
                        await InsertLocalVariableAsync(filePath, sequenceName!, v.Name,
                            v.DataType ?? "string", v.Value, v.Representation, v.NumberFormat);
                        if (v.Comment != null)
                            await SetLocalVariableCommentAsync(filePath, sequenceName!, v.Name, v.Comment);
                        break;
                    case "parameters":
                        await InsertSequenceParameterAsync(filePath, sequenceName!, v.Name,
                            v.DataType ?? "string", v.Direction ?? "Input", v.Value,
                            v.PassByReference, v.Representation, v.NumberFormat);
                        if (v.Comment != null)
                            await SetParameterCommentAsync(filePath, sequenceName!, v.Name, v.Comment);
                        break;
                    default:
                        await InsertFileGlobalAsync(filePath, v.Name, v.DataType ?? "string");
                        if (v.Comment != null)
                            await SetFileGlobalCommentAsync(filePath, v.Name, v.Comment);
                        break;
                }
                created.Add(v.Name);
            }
            catch (Exception ex) { warnings.Add($"'{v.Name}': {ex.Message}"); }
        }
        if (save) await SaveSequenceFileAsync(filePath);
        return new Dictionary<string, object>
        {
            ["insertedCount"]     = created.Count,
            ["insertedVariables"] = created,
            ["scope"]             = scope ?? "",
            ["warnings"]          = warnings,
        };
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> SetPropertyNodesBulkAsync(string filePath,
        IReadOnlyList<PropertyNodeSpec> nodes, bool save = true)
    {
        EnsureConnected();
        var results  = new List<PropertyNodeInfo>();
        var warnings = new List<string>();
        // Applied strictly in list order: a nested member's parent must exist first, and container
        // member ORDER is significant (the FileDiffer pairs members positionally as well as by name).
        foreach (var n in nodes)
        {
            try
            {
                results.Add(await SetPropertyNodeAsync(filePath, n.Scope, n.SequenceName,
                    n.LookupString, n.ValueType, n.TypeName, n.Value, n.Ordinal, n.NumElements,
                    n.Flags, n.CreateMissingParents, save: false,
                    n.Representation, n.NumberFormat, n.ClearFlags));
            }
            catch (Exception ex)
            { warnings.Add($"'{n.Scope}{(n.SequenceName is null ? "" : "/" + n.SequenceName)}:{n.LookupString}': {ex.Message}"); }
        }
        if (save) await SaveSequenceFileAsync(filePath);
        return new Dictionary<string, object>
        {
            ["appliedCount"] = results.Count,
            ["nodes"]        = results,
            ["warnings"]     = warnings,
        };
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> SetModuleParametersBulkAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        IReadOnlyList<(string Name, string Value)> parameters, bool save = true)
    {
        EnsureConnected();
        var applied  = new List<string>();
        var warnings = new List<string>();
        foreach (var (name, value) in parameters)
        {
            try { await SetModuleParameterAsync(filePath, sequenceName, stepGroup, stepName, name, value, true); applied.Add(name); }
            catch (Exception ex) { warnings.Add($"'{name}': {ex.Message}"); }
        }
        if (save) await SaveSequenceFileAsync(filePath);
        return new Dictionary<string, object>
        {
            ["appliedCount"] = applied.Count,
            ["applied"]      = applied,
            ["stepName"]     = stepName,
            ["warnings"]     = warnings,
        };
    }

    // ── Whole-file export / import ────────────────────────────────────────────

    // Step properties read/written verbatim by export/import. Kept in one place so the two stay in
    // sync: every entry is (model field, step property path relative to the step).
    private const string PPreCond   = "TS.PreCond";
    private const string PPreExpr   = "TS.PreExpr";
    private const string PPostExpr  = "TS.PostExpr";
    private const string PStatusExpr= "TS.StatusExpr";
    private const string PMode      = "TS.Mode";
    private const string PPassAct   = "TS.PassAct";
    private const string PFailAct   = "TS.FailAct";
    private const string PLoopType  = "TS.LoopType";
    private const string PResultOpt = "TS.ResultOption";
    private const string PIgnoreRTE = "TS.IgnoreRTE";
    private const string PStepFCSeqF= "TS.StepFCSeqF";
    private const string PLoadOpt   = "TS.LoadOpt";
    private const string PUnloadOpt = "TS.UnloadOpt";

    /// <inheritdoc/>
    public async Task<SequenceFileModel> ExportSequenceFileAsync(string filePath,
        bool includeTypeDefs = true, string? sequenceName = null,
        IReadOnlyList<string>? sequenceNames = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            dynamic sf = GetOrLoadSeqFile(filePath);
            // Optional subset filter. Types/file globals are always exported in full: a subset that
            // silently dropped the types its sequences reference would not import.
            HashSet<string>? wanted = null;
            if (sequenceNames is { Count: > 0 })
                wanted = new HashSet<string>(sequenceNames, StringComparer.OrdinalIgnoreCase);
            else if (!string.IsNullOrWhiteSpace(sequenceName))
                wanted = new HashSet<string>(new[] { sequenceName! }, StringComparer.OrdinalIgnoreCase);
            var model = new SequenceFileModel
            {
                SourcePath         = filePath,
                TypeDefsSourcePath = filePath,
            };

            try { model.File.Comment = (string)sf.Comment; } catch (Exception ex) { _logger.LogDebug(ex, "Reading file comment failed."); }
            try { model.File.Version = (string)sf.AsPropertyObject().GetValString("Data.Version", 0); }
            catch (Exception ex) { _logger.LogDebug(ex, "Reading file version failed."); }

            if (includeTypeDefs)
            {
                try
                {
                    NiTypeUsageList tul = GetTypeUsageList(sf);
                    foreach (var (name, attached, _) in EnumerateFileTypeDefs(tul))
                        model.TypeDefs.Add(new TypeDefModel { Name = name, Attached = attached });
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Enumerating type definitions failed."); }
            }

            // File globals — full node trees so nested authored payloads round-trip.
            try
            {
                NiPropertyObject fg = GetFileGlobals(sf);
                int n = fg.GetNumSubProperties("");
                for (int i = 0; i < n; i++)
                {
                    var child = fg.GetNthSubProperty("", i, 0);
                    var vm = ExportVarNode(child, SafeName(child, i), 0);
                    if (vm != null) model.FileGlobals.Add(vm);
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Exporting file globals failed."); }

            int seqCount = 0;
            try { seqCount = Convert.ToInt32((object)sf.NumSequences); }
            catch (Exception ex) { _logger.LogDebug(ex, "Reading NumSequences failed."); }

            for (int s = 0; s < seqCount; s++)
            {
                dynamic seq;
                try { seq = sf.GetSequence(s); }
                catch (Exception ex) { _logger.LogDebug(ex, "GetSequence({Index}) failed.", s); continue; }

                string name = "";
                try { name = (string)seq.Name; } catch { }
                if (wanted != null && !wanted.Contains(name)) continue;

                var sm = new SequenceModel { Name = name };
                try { sm.Description = NullIfEmpty((string)seq.Comment); } catch { }
                // "Record Results" on a sequence is the inverse DisableResults flag.
                try { sm.DisableResults       = (bool)seq.DisableResults;       } catch { }
                try { sm.GotoCleanupOnFailure = (bool)seq.GotoCleanupOnFailure; } catch { }

                try
                {
                    NiPropertyObject prms = (NiPropertyObject)(object)seq.Parameters;
                    int pn = prms.GetNumSubProperties("");
                    for (int i = 0; i < pn; i++)
                    {
                        var child = prms.GetNthSubProperty("", i, 0);
                        var vm = ExportVarNode(child, SafeName(child, i), 0);
                        if (vm == null) continue;
                        // A parameter's pass mode is the PropFlags_PassByReference bit; the direction
                        // is not separately stored, so it is derived the same way the writer maps it.
                        vm.PassByReference = ((vm.Flags ?? 0) & 0x4) != 0;
                        vm.Direction       = vm.PassByReference == true ? "InOut" : "Input";
                        sm.Parameters.Add(vm);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Exporting parameters of '{Seq}' failed.", name); }

                try
                {
                    NiPropertyObject locals = (NiPropertyObject)(object)seq.Locals;
                    int ln = locals.GetNumSubProperties("");
                    for (int i = 0; i < ln; i++)
                    {
                        var child  = locals.GetNthSubProperty("", i, 0);
                        string cn  = SafeName(child, i);
                        // Every new sequence gets ResultList automatically — exporting it would make
                        // import try to create a duplicate.
                        if (string.Equals(cn, "ResultList", StringComparison.Ordinal)) continue;
                        var vm = ExportVarNode(child, cn, 0);
                        if (vm != null) sm.Locals.Add(vm);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Exporting locals of '{Seq}' failed.", name); }

                string[] groups = { "Setup", "Main", "Cleanup" };
                for (int g = 0; g <= 2; g++)
                {
                    int count = 0;
                    try { count = Convert.ToInt32((object)seq.GetNumSteps((NiStepGroups)g)); }
                    catch (Exception ex) { _logger.LogDebug(ex, "GetNumSteps({Group}) failed on '{Seq}'.", g, name); }
                    for (int i = 0; i < count; i++)
                    {
                        try { sm.Steps.Add(ExportStep(seq.GetStep(i, (NiStepGroups)g), groups[g])); }
                        catch (Exception ex)
                        { _logger.LogDebug(ex, "Exporting step {Index} of '{Seq}' group {Group} failed.", i, name, g); }
                    }
                }
                model.Sequences.Add(sm);
            }
            return model;
        });
    }

    // Recursively exports a variable/parameter node. Depth-capped: an authored payload is a handful of
    // levels deep, and a runaway recursion on a self-referential type would otherwise hang the export.
    private VarModel? ExportVarNode(NiPropertyObject po, string name, int depth)
    {
        if (depth > 12) return null;
        var vm = new VarModel { Name = name };
        try { vm.TypeDisplay = NullIfEmpty(po.GetTypeDisplayString("", 0)); } catch { }
        try { vm.Flags       = po.GetFlags("", 0); } catch { }
        try { vm.Comment     = NullIfEmpty(po.Comment); } catch { }

        int numSub = 0;
        try { numSub = po.GetNumSubProperties(""); } catch { }
        int numElem = 0;
        try { numElem = po.GetNumElements(); } catch { }

        if (numElem > 0)
        {
            vm.ValueType      = "Array";
            vm.NumElements    = numElem;
            vm.Representation = TryReadRepresentation(po);
            vm.NumberFormat   = TryReadNumericFormat(po);
            vm.DataType       = DeriveCreationDataType(vm.TypeDisplay, isArray: true);
            vm.Members        = new List<VarModel>();
            for (int i = 0; i < numElem; i++)
            {
                try
                {
                    var e  = (NiPropertyObject)(object)po.GetPropertyObjectByOffset(i, 0);
                    var em = ExportVarNode(e, $"[{i}]", depth + 1);
                    if (em == null) continue;
                    // Array elements INHERIT the array's representation/format — TestStand rejects
                    // setting them per element ("Unable to change an array element representation
                    // individually"), so they must not be carried on the element.
                    em.Representation = null;
                    em.NumberFormat   = null;
                    vm.Members.Add(em);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Exporting array element {Index} of '{Name}' failed.", i, name); }
            }
            return vm;
        }
        if (numSub > 0)
        {
            vm.ValueType = "Container";
            vm.DataType  = DeriveCreationDataType(vm.TypeDisplay, isArray: false) ?? "container";
            vm.Members   = new List<VarModel>();
            for (int i = 0; i < numSub; i++)
            {
                try
                {
                    var c  = po.GetNthSubProperty("", i, 0);
                    var cm = ExportVarNode(c, SafeName(c, i), depth + 1);
                    if (cm != null) vm.Members.Add(cm);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Exporting member {Index} of '{Name}' failed.", i, name); }
            }
            return vm;
        }

        // Scalar leaf. ORDER MATTERS and must match TryGetValue/BuildPropertyNode: probe the PLAIN
        // readers first and the enum LAST. The enum read goes through PropOption_CoerceTo*, and
        // coercion on a genuine Number/Boolean/String succeeds as a no-op — so probing for an enum
        // first misclassifies every plain number as an enum. (That is exactly what happened: an
        // exported "Number" came back as valueType "Enum" with dataType "number", and the import then
        // tried to instantiate a named type called 'number'.)
        try
        {
            double d = po.GetValNumber("", 0);
            vm.ValueType      = "Number";
            vm.Value          = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vm.Representation = TryReadRepresentation(po);
            vm.NumberFormat   = TryReadNumericFormat(po);
            vm.DataType       = "number";
            return vm;
        }
        catch { }
        var wide = TryReadWideInteger(po);
        if (wide != null)
        {
            vm.ValueType      = "Number";
            vm.Value          = Convert.ToString(wide, System.Globalization.CultureInfo.InvariantCulture);
            vm.Representation = TryReadRepresentation(po);
            vm.NumberFormat   = TryReadNumericFormat(po);
            vm.DataType       = "number";
            return vm;
        }
        try { vm.ValueType = "Boolean"; vm.Value = po.GetValBoolean("", 0) ? "true" : "false";
              vm.DataType = "boolean"; return vm; } catch { }
        try { vm.ValueType = "String";  vm.Value = po.GetValString("", 0);
              vm.DataType = "string";  return vm; } catch { }

        var enumVal = TryReadEnumValue(po);
        if (enumVal != null)
        {
            vm.ValueType = "Enum";
            vm.Ordinal   = (int)enumVal.Ordinal;
            // Prefer the SYMBOLIC name: import writes enums by name, which is what makes TestStand
            // store them as explicitly set rather than type-default-flagged.
            vm.Value     = NullIfEmpty(enumVal.SymbolicName);
            // An enum still sitting at its TYPE DEFAULT reads back with an EMPTY symbolic name, while
            // an explicitly-set one reports its enumerator; that is the same asymmetry that makes an
            // ordinal-only write come back nameless. It maps exactly onto the FileDiffer's
            // {val}/[val], so it is the signal import uses to decide whether to write at all —
            // writing a value the original leaves at its default would flip {val}→[val] and create a
            // difference out of nothing.
            vm.IsDefault = string.IsNullOrEmpty(enumVal.SymbolicName);
            vm.DataType  = DeriveCreationDataType(vm.TypeDisplay, isArray: false);
            return vm;
        }

        // Neither container, array nor readable scalar: an Object Reference or an empty typed slot.
        vm.ValueType = "Empty";
        vm.DataType  = (vm.TypeDisplay ?? "").Contains("Reference", StringComparison.OrdinalIgnoreCase)
                       ? "reference"
                       : DeriveCreationDataType(vm.TypeDisplay, isArray: false);
        return vm;
    }

    // Turns a TestStand type DISPLAY string ("RespStatusEnum (Enumeration)", "Array of Numbers
    // {Unsigned 64-bit Integer}[0..3]", "Number") into the data_type string the insert_* tools accept.
    private static string? DeriveCreationDataType(string? typeDisplay, bool isArray)
    {
        if (string.IsNullOrWhiteSpace(typeDisplay)) return isArray ? "number[]" : null;
        string t = typeDisplay!;
        int paren = t.IndexOf(" (", StringComparison.Ordinal);
        string bare = paren > 0 ? t.Substring(0, paren).Trim() : t.Trim();

        // "Array of X[0..n]" → the element type.
        const string arrOf = "Array of ";
        if (bare.StartsWith(arrOf, StringComparison.OrdinalIgnoreCase))
        {
            string elem = bare.Substring(arrOf.Length);
            int brace = elem.IndexOf('{'); if (brace > 0) elem = elem.Substring(0, brace);
            int brack = elem.IndexOf('['); if (brack > 0) elem = elem.Substring(0, brack);
            elem = elem.Trim().TrimEnd('s');            // "Numbers" → "Number"
            return MapBuiltinOrNamed(elem) + "[]";
        }
        int b2 = bare.IndexOf('{'); if (b2 > 0) bare = bare.Substring(0, b2).Trim();
        return MapBuiltinOrNamed(bare) + (isArray ? "[]" : "");

        static string MapBuiltinOrNamed(string n) => n.Trim().ToLowerInvariant() switch
        {
            "number"           => "number",
            "string"           => "string",
            "boolean"          => "boolean",
            "container"        => "container",
            "object reference" => "reference",
            _                  => n.Trim(),   // a named custom/enum type
        };
    }

    // Exports one step: the curated property set plus its module configuration.
    private StepModel ExportStep(dynamic step, string group)
    {
        var sm = new StepModel { Group = group };
        try { sm.Name     = (string)step.Name; } catch { }
        try { sm.StepType = (string)step.StepType.Name; } catch { }
        try { sm.Adapter  = NullIfEmpty(TryGetString(step, "AdapterKeyName")); } catch { }

        NiPropertyObject po;
        try { po = ((NiStep)(object)step).AsPropertyObject(); }
        catch (Exception ex) { _logger.LogDebug(ex, "AsPropertyObject failed for step '{Step}'.", sm.Name); return sm; }

        string? Str(string path) { try { return NullIfEmpty(po.GetValString(path, 0)); } catch { return null; } }
        bool?   Bool(string path) { try { return po.GetValBoolean(path, 0); } catch { return null; } }
        int?    Num(string path) { try { return (int)po.GetValNumber(path, 0); } catch { return null; } }

        try { sm.Enabled = !(bool)step.RunMode.Equals(null); } catch { }
        // A skipped step is Mode="Skip"; expose it both as the raw mode and the boolean the
        // insert/enable tools use.
        sm.RunMode  = Str(PMode);
        sm.Enabled  = string.Equals(sm.RunMode, "Skip", StringComparison.OrdinalIgnoreCase) ? false : (bool?)null;

        sm.Precondition                     = Str(PPreCond);
        sm.PreExpression                    = Str(PPreExpr);
        sm.PostExpression                   = Str(PPostExpr);
        sm.StatusExpression                 = Str(PStatusExpr);
        sm.PassAction                       = Str(PPassAct);
        sm.FailAction                       = Str(PFailAct);
        sm.LoopType                         = Str(PLoopType);
        sm.ResultOption                     = Num(PResultOpt);
        sm.IgnoreRuntimeErrors              = Bool(PIgnoreRTE);
        sm.StepFailureCausesSequenceFailure = Bool(PStepFCSeqF);
        sm.LoadOption                       = Str(PLoadOpt);
        sm.UnloadOption                     = Str(PUnloadOpt);
        sm.ConditionExpr                    = Str("ConditionExpr");
        sm.ItemExpr                         = Str("ItemExpr");
        // Loop-shape expressions live in their own step properties, not in Pre/Post: a ForEach with an
        // empty ArrayExpr never iterates, so these are functional, not cosmetic.
        sm.ArrayExpr                        = Str("ArrayExpr");
        sm.ArrayElementExpr                 = Str("ArrayElementExpr");
        sm.InitializationExpr               = Str("InitializationExpr");
        sm.IncrementExpr                    = Str("IncrementExpr");
        sm.IsDefaultCase                    = Bool("IsDefault");
        try { sm.Comment = NullIfEmpty((string)step.Description); } catch { }

        sm.Module = ExportStepModule(po, sm.StepType, sm.Name);
        return sm;
    }

    // Reads whichever module shape the step actually has. Discriminating on the stored subtree rather
    // than on the adapter name keeps this working for an Action step switched to the Sequence adapter.
    private StepModuleModel? ExportStepModule(NiPropertyObject po, string stepType, string stepName)
    {
        var m = new StepModuleModel();
        string? Str(string path) { try { return NullIfEmpty(po.GetValString(path, 0)); } catch { return null; } }
        bool?   Bool(string path) { try { return po.GetValBoolean(path, 0); } catch { return null; } }
        int?    Num(string path) { try { return (int)po.GetValNumber(path, 0); } catch { return null; } }

        // NI_Wait keeps its target in step-root properties, not in a module.
        if (stepType.Contains("Wait", StringComparison.OrdinalIgnoreCase))
        {
            m.WaitTimeExpression = Str("TimeExpr");
            if (m.WaitTimeExpression != null) { m.Kind = "Wait"; return m; }
        }

        // Python.
        string pyBase = "TS.SData.PythonCall";
        string? modulePath = Str($"{pyBase}.ModulePath");
        if (modulePath != null || Str($"{pyBase}.FunctionOrAttributeName") != null)
        {
            m.Kind                         = "Python";
            m.ModulePath                   = modulePath;
            m.FunctionName                 = Str($"{pyBase}.FunctionOrAttributeName");
            m.ClassName                    = Str($"{pyBase}.ClassName");
            m.ClassInstanceLocation        = Str($"{pyBase}.ClassInstanceLocation");
            m.OperationType                = Num($"{pyBase}.OperationType");
            m.OperationScope               = Num($"{pyBase}.OperationScope");
            m.PythonVersion                = Str($"{pyBase}.PythonVersion");
            m.VirtualEnvPath               = Str($"{pyBase}.PythonVirtualEnvironmentPath");
            m.UseAdapterInterpreterSettings= Bool($"{pyBase}.UseAdapterSettingsForInterpreterSession");
            m.Arguments                    = new List<ModuleArgModel>();
            try
            {
                var arr = (NiPropertyObject)(object)po.GetPropertyObject($"{pyBase}.Parameters", 0);
                int n = arr.GetNumElements();
                for (int i = 0; i < n; i++)
                {
                    var e = (NiPropertyObject)(object)arr.GetPropertyObjectByOffset(i, 0);
                    var a = new ModuleArgModel();
                    try { a.Name  = e.GetValString("Name", 0); } catch { }
                    try { a.Value = e.GetValString("ArgumentValue", 0); } catch { }
                    try { a.Type  = ((int)e.GetValNumber("Type", 0))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture); } catch { }
                    m.Arguments.Add(a);
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Reading PythonCall parameters of '{Step}' failed.", stepName); }
            return m;
        }

        // SequenceCall.
        string? seqTarget = Str("TS.SData.SeqName");
        if (seqTarget != null)
        {
            m.Kind               = "SequenceCall";
            m.TargetSequenceName = seqTarget;
            m.UseCurrentFile     = Bool("TS.SData.UseCurFile");
            m.StoredFilePath     = Str("TS.SData.SFPath");
            if (m.UseCurrentFile == false) m.TargetSequenceFile = m.StoredFilePath;
            m.Arguments          = new List<ModuleArgModel>();
            try
            {
                var args = (NiPropertyObject)(object)po.GetPropertyObject("TS.SData.ActualArgs", 0);
                int n = args.GetNumSubProperties("");
                for (int i = 0; i < n; i++)
                {
                    var e = args.GetNthSubProperty("", i, 0);
                    var a = new ModuleArgModel { Name = SafeName(e, i) };
                    // A SequenceArgument carries its whole binding state in SUBPROPERTIES. All of them
                    // have to round-trip: UseDef is independent of Expr (the editor keeps a remembered
                    // expression while using the default), and 'Flags'/'ParamRepresentation' are copied
                    // from the CALLEE by a prototype load, so a caller whose original differs must have
                    // them written back.
                    try { a.Value               = NullIfEmpty(e.GetValString("Expr", 0)); } catch { }
                    try { a.UseDefault          = e.GetValBoolean("UseDef", 0); } catch { }
                    try { a.ArgFlags            = (int)e.GetValNumber("Flags", 0); } catch { }
                    try { a.ParamType           = (int)e.GetValNumber("ParamType", 0); } catch { }
                    try { a.ParamRepresentation = (int)e.GetValNumber("ParamRepresentation", 0); } catch { }
                    try { a.Flags               = e.GetFlags("", 0); } catch { }
                    m.Arguments.Add(a);
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Reading ActualArgs of '{Step}' failed.", stepName); }
            return m;
        }

        // LabVIEW — either the adapter module or a utility step's own VIModule.
        foreach (var p in new[] { "TS.SData.ViCall.VIPath", "VIModule.ViCall.VIPath" })
        {
            string? vi = Str(p);
            if (vi == null) continue;
            m.Kind   = "LabVIEW";
            m.ViPath = vi;
            // The connector-pane BINDINGS (which TestStand expression is wired to which control).
            // Loading the pane only recreates its STRUCTURE — the bindings are authored per step and
            // have to be carried, otherwise every wired control comes out empty and the ones TestStand
            // wires by default (error out → Step.Result.Error) come out wired where the original is
            // not. ReadModuleParameters already flattens cluster members as "parent.child", which is
            // exactly the Label form set_module_parameter binds by.
            m.Arguments = new List<ModuleArgModel>();
            foreach (var parmsPath in new[] { "TS.SData.ViCall.Parms", "VIModule.ViCall.Parms" })
            {
                try
                {
                    var parms = (NiPropertyObject)(object)po.GetPropertyObject(parmsPath, 0);
                    CollectViCallBindings(parms, "", m.Arguments);
                    if (m.Arguments.Count > 0) break;
                }
                catch (Exception ex)
                { _logger.LogDebug(ex, "No ViCall bindings at '{Path}' on '{Step}'.", parmsPath, stepName); }
            }
            return m;
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task<ImportOutcome> ImportSequenceFileAsync(SequenceFileModel model,
        string destFilePath, bool copyTypeDefs = true, bool save = true,
        string labViewPanes = "copy", int prototypeTimeoutSeconds = 120,
        string crossFilePrototypes = "copy", bool keepUnusedTypes = true,
        string variables = "copy")
    {
        EnsureConnected();
        var outcome = new ImportOutcome();

        if (model.SchemaVersion != 1)
            throw new ArgumentException(
                $"Unsupported model schemaVersion {model.SchemaVersion} (this server writes/reads 1).");

        // Both fidelity passes that need a REAL prototype load have a cheap, safe alternative: clone the
        // cached module subtree straight out of the file the model was exported from. That needs the
        // source file, so resolve it once and downgrade the modes that depend on it.
        string paneMode  = NormalizeModuleMode(labViewPanes,        nameof(labViewPanes));
        string protoMode = NormalizeModuleMode(crossFilePrototypes, nameof(crossFilePrototypes));
        string varMode   = NormalizeVariableMode(variables);
        string? sourcePath = FirstExistingPath(model.SourcePath, model.TypeDefsSourcePath);
        if (sourcePath is null)
        {
            if (varMode == "copy")
            {
                varMode = "model";
                outcome.Warnings.Add(
                    "variables='copy' needs the file the model was exported from — falling back to " +
                    "'model'. Variables are rebuilt declaratively, which cannot reproduce a type " +
                    "instance's member that has NO value of its own (the FileDiffer shows [val] where " +
                    "the original has {val}).");
            }
            if (paneMode == "copy")
            {
                paneMode = "skip";
                outcome.Warnings.Add(
                    "labview_panes='copy' needs the file the model was exported from, and neither " +
                    $"sourcePath ('{model.SourcePath}') nor typeDefsSourcePath " +
                    $"('{model.TypeDefsSourcePath}') exists — falling back to 'skip'. The VI paths are " +
                    "still written but ViCall.Parms stays empty. Use labview_panes='load' to attempt a " +
                    "real LabVIEW load instead (see its warning).");
            }
            if (protoMode == "copy")
            {
                protoMode = "skip";
                outcome.Warnings.Add(
                    "cross_file_prototypes='copy' needs the model's source file (see above) — falling " +
                    "back to 'skip'. Cross-file calls work; only the cached TS.SData.Prototype stays " +
                    "empty.");
            }
        }
        outcome.LabViewPaneMode        = paneMode;
        outcome.CrossFilePrototypeMode = protoMode;
        outcome.VariableMode           = varMode;

        // 1) Types first — cloned sequences, typed locals and enum members all resolve against them.
        if (copyTypeDefs && !string.IsNullOrWhiteSpace(model.TypeDefsSourcePath))
        {
            var names = model.TypeDefs.Count > 0
                ? model.TypeDefs.ConvertAll(t => t.Name)
                : null;
            try
            {
                var copied = await CopyTypeDefsAsync(model.TypeDefsSourcePath!, destFilePath, names,
                    save: false, attach: "preserve");
                outcome.TypeDefsCopied = copied.Count;
            }
            catch (Exception ex)
            {
                outcome.Warnings.Add($"copy_typedefs from '{model.TypeDefsSourcePath}' failed: {ex.Message}");
            }
        }

        // 2) File metadata + globals.
        if (model.File.Comment != null || model.File.Version != null)
        {
            try { await SetFilePropertiesAsync(destFilePath, model.File.Comment, model.File.Version); }
            catch (Exception ex) { outcome.Warnings.Add($"file properties: {ex.Message}"); }
        }
        var globalsFromModel = model.FileGlobals;
        if (varMode == "copy")
        {
            var cloned = await CloneVariablesAsync(sourcePath!, destFilePath, "FileGlobals", null,
                model.FileGlobals, outcome);
            outcome.VariablesCopied += cloned.Count;
            globalsFromModel = model.FileGlobals.FindAll(g => !cloned.Contains(g.Name));
        }
        foreach (var g in globalsFromModel)
        {
            try
            {
                await InsertFileGlobalAsync(destFilePath, g.Name, g.DataType ?? "string");
                outcome.VariablesCreated++;
                // insert_file_global takes no default value (unlike the local/parameter inserters), so
                // a scalar global's VALUE has to be written separately — otherwise every non-zero file
                // global silently comes out at its type default.
                if (g.Value != null && g.ValueType is "Number" or "Boolean" or "String" or "Enum"
                    && !(g.ValueType == "Enum" && g.IsDefault == true))
                {
                    string gvt = g.ValueType switch
                    {
                        "Number"  => "number",
                        "Boolean" => "boolean",
                        "Enum"    => "enum",
                        _         => "string",
                    };
                    await SetPropertyNodeAsync(destFilePath, "FileGlobals", null, g.Name, gvt,
                        gvt == "enum" ? g.DataType : null, g.Value, g.Ordinal, null, null,
                        true, false, g.Representation, g.NumberFormat);
                }
                await ApplyVarNodeAsync(destFilePath, "FileGlobals", null, g.Name, g, outcome);
            }
            catch (Exception ex) { outcome.Warnings.Add($"file global '{g.Name}': {ex.Message}"); }
        }

        // 3) Sequences, in model order, so the file's sequence indices match the source.
        foreach (var seq in model.Sequences)
        {
            try { await InsertSequenceAsync(destFilePath, seq.Name); outcome.SequencesCreated++; }
            catch (Exception ex) { outcome.Warnings.Add($"sequence '{seq.Name}': {ex.Message}"); continue; }

            if (seq.Description != null || seq.DisableResults.HasValue)
            {
                try
                {
                    await SetSequencePropertiesAsync(destFilePath, seq.Name, new SequenceProperties
                    {
                        Name                 = seq.Name,
                        Description          = seq.Description ?? "",
                        DisableResults       = seq.DisableResults ?? false,
                        GotoCleanupOnFailure = seq.GotoCleanupOnFailure ?? false,
                    });
                }
                catch (Exception ex) { outcome.Warnings.Add($"sequence properties '{seq.Name}': {ex.Message}"); }
            }

            // Parameters BEFORE steps: a SequenceCall's prototype load reads the callee's parameters,
            // so the interface has to exist before any caller is configured. The clone runs at exactly
            // this point for the same reason.
            var paramsFromModel = seq.Parameters;
            var localsFromModel = seq.Locals;
            if (varMode == "copy")
            {
                var clonedP = await CloneVariablesAsync(sourcePath!, destFilePath, "Parameters",
                    seq.Name, seq.Parameters, outcome);
                var clonedL = await CloneVariablesAsync(sourcePath!, destFilePath, "Locals",
                    seq.Name, seq.Locals, outcome);
                outcome.VariablesCopied += clonedP.Count + clonedL.Count;
                paramsFromModel = seq.Parameters.FindAll(p => !clonedP.Contains(p.Name));
                localsFromModel = seq.Locals.FindAll(l => !clonedL.Contains(l.Name));
            }
            foreach (var p in paramsFromModel)
            {
                try
                {
                    await InsertSequenceParameterAsync(destFilePath, seq.Name, p.Name,
                        p.DataType ?? "string", p.Direction ?? "Input",
                        p.ValueType == "Container" || p.ValueType == "Array" ? null : p.Value,
                        p.PassByReference, p.Representation, p.NumberFormat);
                    outcome.VariablesCreated++;
                    await ApplyVarNodeAsync(destFilePath, "Parameters", seq.Name, p.Name, p, outcome);
                    if (p.Comment != null)
                        await SetParameterCommentAsync(destFilePath, seq.Name, p.Name, p.Comment);
                }
                catch (Exception ex) { outcome.Warnings.Add($"parameter '{seq.Name}.{p.Name}': {ex.Message}"); }
            }
            foreach (var l in localsFromModel)
            {
                try
                {
                    await InsertLocalVariableAsync(destFilePath, seq.Name, l.Name,
                        l.DataType ?? "string",
                        l.ValueType == "Container" || l.ValueType == "Array" ? null : l.Value,
                        l.Representation, l.NumberFormat);
                    outcome.VariablesCreated++;
                    await ApplyVarNodeAsync(destFilePath, "Locals", seq.Name, l.Name, l, outcome);
                    if (l.Comment != null)
                        await SetLocalVariableCommentAsync(destFilePath, seq.Name, l.Name, l.Comment);
                }
                catch (Exception ex) { outcome.Warnings.Add($"local '{seq.Name}.{l.Name}': {ex.Message}"); }
            }
        }

        // 4) Steps — a second pass over all sequences, so every callee already has its interface.
        var pendingViLoads     = new List<(string Seq, string Group, string Selector, StepModel Step)>();
        var pendingModulePasses = new List<(string Seq, string Group, List<StepModel> Steps)>();
        foreach (var seq in model.Sequences)
        {
            foreach (var group in new[] { "Setup", "Main", "Cleanup" })
            {
                var groupSteps = seq.Steps.FindAll(s =>
                    string.Equals(s.Group, group, StringComparison.OrdinalIgnoreCase));
                if (groupSteps.Count == 0) continue;

                var specs = groupSteps.ConvertAll(s => new BulkStepSpec
                {
                    Name     = s.Name,
                    StepType = s.StepType,
                    Adapter  = s.Adapter,
                });
                try
                {
                    await InsertStepsBulkAsync(destFilePath, seq.Name, group, specs, save: false);
                    outcome.StepsInserted += specs.Count;
                }
                catch (Exception ex)
                { outcome.Warnings.Add($"steps '{seq.Name}'/{group}: {ex.Message}"); continue; }

                // Per-step details, addressed by 0-based group index so duplicate step names
                // (multiple "End"/"If") are unambiguous. FIRST pass only: step properties + the
                // LabVIEW/Wait module. SequenceCall and Python follow after the VI panes are loaded.
                for (int i = 0; i < groupSteps.Count; i++)
                {
                    string sel = $"@idx:{i}";
                    try { await ApplyStepDetailsAsync(destFilePath, seq.Name, group, sel, groupSteps[i],
                              outcome, pendingViLoads, StepPass.PropertiesAndLabView); }
                    catch (Exception ex)
                    { outcome.Warnings.Add($"step '{seq.Name}'/{group}[{i}] '{groupSteps[i].Name}': {ex.Message}"); }
                }
                pendingModulePasses.Add((seq.Name, group, groupSteps));
            }
        }

        // 5) LabVIEW VI connector panes.
        //
        // DEFAULT IS 'copy', NOT a prototype load, because the load is PROCESS-FATAL here. Measured
        // 2026-07-29 on this file, with LabVIEW 2026 32-bit already started and responsive: an
        // in-process load of a .lvlibp VI raised the MSVC delay-load SEH 0xC06D007E (the LabVIEW
        // Run-Time lvrt.dll), which escapes managed try/catch, killed the server process and put up the
        // NI Error Reporter. The ExecServer routing that was supposed to avoid the Run-Time did not
        // prevent it, and the in-process path has none of the worker's silent-death guards — those live
        // in LoadPrototypeWorker, which cannot help a fault raised in the server itself. The isolated
        // worker is crash-safe but cannot bind the running LabVIEW ADE, so it times out instead.
        //
        // 'copy' clones the cached ViCall subtree (Namespace, VI Description, connector-pane checksum,
        // Parms with their ArgVal/UseDefaultValues bindings) out of the file the model was exported
        // from — the same thing copy_step_module does. Measured: all 5 packed-library steps of this
        // file reproduced in ~1 s each with 0 pane differences, versus a dead process for the load.
        if (paneMode == "copy" && pendingViLoads.Count > 0)
        {
            foreach (var (vSeq, vGroup, vSel, vStep) in pendingViLoads)
            {
                try
                {
                    var res = await CopyStepModuleAsync(sourcePath!, vSeq, vGroup, vSel,
                        destFilePath, vSeq, vGroup, vSel, save: false);
                    if (res.TryGetValue("warnings", out var w) && w is List<string> wl && wl.Count > 0)
                        foreach (var msg in wl)
                            outcome.Warnings.Add($"{vSeq}/{vGroup}/{vStep.Name} pane copy: {msg}");
                    else
                        outcome.PanesCopied++;
                }
                catch (Exception ex)
                {
                    outcome.Warnings.Add(
                        $"{vSeq}/{vGroup}/{vStep.Name}: connector-pane copy from '{sourcePath}' failed: " +
                        $"{ex.Message}. ViCall.Parms stays empty.");
                }
            }
        }
        else if (paneMode == "load" && pendingViLoads.Count > 0)
        {
            // Save AND RELOAD before loading any connector pane. Saving alone is not enough: the
            // in-memory file object that the import just assembled resolves a packed-library VI
            // differently from one the engine loaded from disk — measured on this file, every load
            // failed with "LoadPrototype could not resolve the target/module" against the assembled
            // object while the identical call against the same file reopened from disk succeeded in ~5s
            // with 19 parameters. Reopening establishes the file context the LabVIEW adapter needs.
            await SaveSequenceFileAsync(destFilePath);
            await CloseSequenceFileAsync(destFilePath);
            await OpenSequenceFileAsync(destFilePath);
            foreach (var (vSeq, vGroup, vSel, vStep) in pendingViLoads)
            {
                string vLabel = vStep.Name;
                try
                {
                    // isolate:FALSE — in-process, and THIS IS THE CALL THAT KILLS THE PROCESS when the
                    // adapter resolves the LabVIEW Run-Time instead of the running ADE (0xC06D007E,
                    // measured with LabVIEW warm; see the block above pass 5). It stays reachable only
                    // because a station where the ExecServer routing DOES take effect gets a genuine
                    // load out of it, and because 'copy' needs the source file. The isolated worker is
                    // the crash-safe variant but cannot bind the running LabVIEW, so it only times out.
                    // Synchronous so one import call is self-contained.
                    var lp = await LoadModulePrototypeAsync(destFilePath, vSeq, vGroup, vSel,
                        save: true, isolate: false, timeoutSeconds: prototypeTimeoutSeconds,
                        async: false, labviewServer: null);
                    if (lp.PrototypeLoaded)
                    {
                        outcome.PrototypesLoaded++;
                        // Bind the connector pane. The load recreated its STRUCTURE; the per-control
                        // bindings are authored per step and have to be carried.
                        // ArgVal and UseDefaultValues are written SEPARATELY and verbatim.
                        // set_module_parameter cannot be used here: it always clears
                        // UseDefaultValues as a side effect, which flips it on every control where the
                        // source keeps the VI's own default (a remembered expression next to
                        // "use default" — the same asymmetry as a SequenceCall argument's UseDef).
                        // Measured on this file: writing through set_module_parameter turned 31
                        // differences into 68 (naive), 41 (non-empty only) and 39 (flag-aware),
                        // whereas setting the two fields independently reproduces both classes.
                        try
                        {
                            await ApplyViCallBindingsAsync(destFilePath, vSeq, vGroup, vSel,
                                vStep.Module?.Arguments, outcome, vLabel);
                        }
                        catch (Exception ex)
                        { outcome.Warnings.Add($"{vSeq}/{vGroup}/{vLabel} pane bindings: {ex.Message}"); }
                    }
                    else
                        outcome.Warnings.Add(
                            $"{vSeq}/{vGroup}/{vLabel}: VI prototype not loaded " +
                            $"(outcome={lp.WorkerOutcome ?? "n/a"}){(lp.Note is null ? "" : " — " + lp.Note)}. " +
                            "The connector-pane properties stay empty; copy_step_module is the fallback.");
                }
                catch (Exception ex)
                { outcome.Warnings.Add($"{vSeq}/{vGroup}/{vLabel}: VI prototype load failed: {ex.Message}"); }
            }
        }
        else if (pendingViLoads.Count > 0)
        {
            outcome.Warnings.Add(
                $"labview_panes='skip' — {pendingViLoads.Count} LabVIEW step(s) got their VI path but NO " +
                "connector pane, so ViCall.Parms and the VI metadata stay empty.");
        }

        // 6) SequenceCall + Python modules. This sets every target and authors every argument, but
        // WITHOUT an in-process SequenceCall prototype load: that load would (a) be poisoned by the
        // LabVIEW pane loads just done and (b) poison any further LabVIEW load. Pass 7 does the load
        // out-of-process instead.
        foreach (var (mSeq, mGroup, mSteps) in pendingModulePasses)
        {
            for (int i = 0; i < mSteps.Count; i++)
            {
                try
                {
                    await ApplyStepDetailsAsync(destFilePath, mSeq, mGroup, $"@idx:{i}", mSteps[i],
                        outcome, null, StepPass.SequenceCallAndPython);
                }
                catch (Exception ex)
                { outcome.Warnings.Add($"module '{mSeq}'/{mGroup}[{i}] '{mSteps[i].Name}': {ex.Message}"); }
            }
        }

        // 6b) AUTHORED step-config subtrees, for EVERY step. These are not part of the model and no
        // configure_* tool writes them: the result-logging hints (TS.AdditionalResultsHints,
        // TS.CustomResults), the error-dialog options and the NI_Wait timeout-result flag are authored in
        // the editor and a freshly inserted step does not inherit them from its step-type template. They
        // showed up as the last non-module residual — an NI_Wait whose AdditionalResultsHints array had
        // one element in the original and none in the rebuild. Cloning them from the source step is
        // value-neutral: the source IS the target, so this can only remove differences. The module
        // subtrees are deliberately NOT in this set — those belong to passes 5/6/7.
        if (sourcePath is not null)
        {
            foreach (var (mSeq, mGroup, mSteps) in pendingModulePasses)
                for (int i = 0; i < mSteps.Count; i++)
                {
                    try
                    {
                        await CopyStepModuleAsync(sourcePath, mSeq, mGroup, $"@idx:{i}",
                            destFilePath, mSeq, mGroup, $"@idx:{i}", save: false,
                            paths: AuthoredStepConfigPaths);
                    }
                    catch (Exception ex)
                    {
                        outcome.Warnings.Add(
                            $"step config '{mSeq}'/{mGroup}[{i}] '{mSteps[i].Name}': could not copy the " +
                            $"authored result/dialog subtrees: {ex.Message}");
                    }
                }
        }

        // 7) CROSS-FILE SequenceCall prototype caches.
        //
        // TS.SData.Prototype caches the callee's parameter list so the editor can validate a cross-file
        // call without loading the other file. A real LoadPrototype fills it only when the callee file is
        // loaded in the SAME engine, and it cannot run here in-process: the two prototype-load kinds
        // poison each other in BOTH directions (measured — after any LabVIEW pane load a cross-file
        // SequenceCall load reports "could not resolve the target/module", and vice versa). That left an
        // isolated worker, which has to start its own engine and open every callee file — measured on
        // this file, one 3 MB callee ran the worker into its 300 s timeout and produced nothing.
        //
        // So the DEFAULT is 'copy': clone the cached subtree from the model's source file, which brings
        // the Prototype AND the authored ActualArgs verbatim. Measured: ~1 s, and the 6 Prototype
        // differences the worker left behind all closed. 'load' keeps the worker route for a rebuild that
        // has no source file to clone from.
        if (save) await SaveSequenceFileAsync(destFilePath);

        var crossFileCalls = new List<(string Seq, string Group, int Idx, StepModel Step)>();
        foreach (var (mSeq, mGroup, mSteps) in pendingModulePasses)
            for (int i = 0; i < mSteps.Count; i++)
            {
                var mod = mSteps[i].Module;
                if (mod?.Kind == "SequenceCall" && mod.UseCurrentFile == false
                    && !string.IsNullOrWhiteSpace(mod.TargetSequenceFile))
                    crossFileCalls.Add((mSeq, mGroup, i, mSteps[i]));
            }

        outcome.CrossFilePrototypeCandidates = crossFileCalls.Count;
        if (crossFileCalls.Count > 0 && protoMode == "copy")
        {
            foreach (var (cSeq, cGroup, cIdx, cStep) in crossFileCalls)
            {
                try
                {
                    var res = await CopyStepModuleAsync(sourcePath!, cSeq, cGroup, $"@idx:{cIdx}",
                        destFilePath, cSeq, cGroup, $"@idx:{cIdx}", save: false);
                    if (res.TryGetValue("warnings", out var w) && w is List<string> wl && wl.Count > 0)
                        foreach (var msg in wl)
                            outcome.Warnings.Add($"{cSeq}/{cGroup}/{cStep.Name} prototype copy: {msg}");
                    else
                        outcome.CrossFilePrototypesCopied++;
                }
                catch (Exception ex)
                {
                    outcome.Warnings.Add(
                        $"{cSeq}/{cGroup}/{cStep.Name}: cross-file prototype copy from '{sourcePath}' " +
                        $"failed: {ex.Message}. The call works — only the cached TS.SData.Prototype " +
                        "stays empty.");
                }
            }
        }
        else if (crossFileCalls.Count > 0 && protoMode == "skip")
        {
            outcome.Warnings.Add(
                $"cross_file_prototypes='skip' — {crossFileCalls.Count} cross-file SequenceCall(s) keep " +
                "an empty TS.SData.Prototype cache. The calls themselves work.");
        }
        else if (crossFileCalls.Count > 0)
        {
            string baseDir = Path.GetDirectoryName(destFilePath) ?? "";
            var callees = crossFileCalls
                .Select(c => c.Step.Module!.TargetSequenceFile!.Trim())
                .Select(f => Path.IsPathRooted(f) ? f : Path.Combine(baseDir, f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(System.IO.File.Exists)
                .ToList();

            foreach (var (cSeq, cGroup, cIdx, cStep) in crossFileCalls)
            {
                try
                {
                    // The worker pays a fresh-process cost the in-process LabVIEW loads do not: its own
                    // engine start plus opening the destination AND every callee file (a real callee can
                    // be megabytes and pull in further files). Measured: 120 s was not enough for one
                    // 3 MB callee, so the floor here is well above the caller's per-VI budget.
                    int crossFileTimeout = Math.Max(prototypeTimeoutSeconds, 300);
                    var lr = await LoadModulePrototypeAsync(destFilePath, cSeq, cGroup, $"@idx:{cIdx}",
                        save: true, isolate: true, timeoutSeconds: crossFileTimeout,
                        async: false, labviewServer: null, calleeFiles: callees);

                    if (lr.PrototypeLoaded)
                    {
                        outcome.CrossFilePrototypesLoaded++;
                        // The load rewrote ActualArgs from the callee — restore the source's bindings.
                        if (cStep.Module!.Arguments != null)
                            await ApplySequenceCallArgsAsync(destFilePath, cSeq, cGroup, $"@idx:{cIdx}",
                                cStep.Name, cStep.Module!.Arguments!, outcome);
                    }
                    else
                    {
                        outcome.Warnings.Add(
                            $"{cSeq}/{cGroup}/{cStep.Name}: cross-file prototype cache NOT loaded " +
                            $"(workerOutcome={lr.WorkerOutcome ?? "n/a"}; {lr.Note}). The call works — " +
                            "only the cached TS.SData.Prototype stays empty, which the FileDiffer shows " +
                            "as missing Prototype members.");
                    }
                }
                catch (Exception ex)
                {
                    outcome.Warnings.Add(
                        $"{cSeq}/{cGroup}/{cStep.Name}: cross-file prototype load failed: {ex.Message}");
                }
            }
        }

        if (save) await SaveSequenceFileAsync(destFilePath);

        // 8) TYPES THAT THE SAVE DROPPED. A type survives in a file only if it is ATTACHED to the file or
        // still REFERENCED by something in it; a type that is neither is garbage-collected on save. With
        // attach='preserve' (what a 1:1 rebuild wants) that silently loses every type the original keeps
        // alive through a sequence the import did not carry — measured: importing 8 of 30 sequences lost 5
        // enum typedefs, with no warning and after copy_typedefs had reported all 59 as copied.
        // Attaching those few in the destination brought them all back and produced ZERO additional
        // FileDiffer differences, so 'keep' is the safe default; the deviation is that the destination
        // then embeds more types than the original, which the FileDiffer does not report.
        //
        // The check MUST run against the file as it now exists ON DISK. GetFileTypeDefsAsync reads the
        // in-memory object, which still lists a type the save has just dropped — measured: it reported
        // all 59 types present while the FileDiffer found 5 of them gone. So reload first.
        if (copyTypeDefs && save && model.TypeDefs.Count > 0 && sourcePath is not null)
        {
            try
            {
                async Task<HashSet<string>> ReloadedTypeNamesAsync()
                {
                    await CloseSequenceFileAsync(destFilePath);
                    await OpenSequenceFileAsync(destFilePath);
                    return new HashSet<string>(
                        (await GetFileTypeDefsAsync(destFilePath)).Select(t => t.Name),
                        StringComparer.OrdinalIgnoreCase);
                }

                var present = await ReloadedTypeNamesAsync();
                var missing = model.TypeDefs.ConvertAll(t => t.Name)
                    .FindAll(n => !string.IsNullOrWhiteSpace(n) && !present.Contains(n));

                if (missing.Count > 0 && keepUnusedTypes)
                {
                    var rescued = await CopyTypeDefsAsync(sourcePath, destFilePath, missing,
                        save: true, attach: "all");
                    outcome.TypeDefsForceAttached = rescued.Count;
                    if (rescued.Count > 0)
                        outcome.Warnings.Add(
                            $"{rescued.Count} type(s) were dropped by the save because the imported " +
                            "sequences do not reference them and the original does not attach them; they " +
                            "were re-copied ATTACHED so they persist (" + string.Join(", ", rescued) +
                            "). The destination therefore embeds more types than the original — the " +
                            "FileDiffer does not report that. Pass keep_unused_types=false to let them go.");

                    present = await ReloadedTypeNamesAsync();
                    missing = missing.FindAll(n => !present.Contains(n));
                }

                outcome.TypeDefsMissing = missing.Count;
                if (missing.Count > 0)
                    outcome.Warnings.Add(
                        $"{missing.Count} type(s) from the model are NOT in the destination: " +
                        string.Join(", ", missing) +
                        (keepUnusedTypes ? ". Re-copying them attached did not make them persist."
                                         : ". keep_unused_types=false, so unreferenced types were let go."));
            }
            catch (Exception ex)
            { outcome.Warnings.Add($"type-survival check failed: {ex.Message}"); }
        }

        // The outcome is the only report of what could NOT be applied, and an import can outlive the
        // caller's RPC window (measured: 5.5 min, which the ~60 s MCP transport gave up on long before
        // the work finished — the result was complete but unreachable). Persist it next to the rebuilt
        // file so a transport timeout costs latency, never the warnings.
        try
        {
            outcome.OutcomePath = destFilePath + ".import.json";
            await System.IO.File.WriteAllTextAsync(outcome.OutcomePath,
                System.Text.Json.JsonSerializer.Serialize(outcome, SequenceFileModel.Json));
        }
        catch (Exception ex)
        {
            outcome.OutcomePath = null;
            _logger.LogDebug(ex, "Could not persist the import outcome next to '{Dest}'.", destFilePath);
        }

        return outcome;
    }

    /// <summary>Validates a copy/load/skip module-reproduction mode and normalises its casing.</summary>
    private static string NormalizeModuleMode(string? mode, string paramName)
    {
        string m = (mode ?? "copy").Trim().ToLowerInvariant();
        return m switch
        {
            "copy" or "load" or "skip" => m,
            "" => "copy",
            _ => throw new ArgumentException(
                $"{paramName} must be 'copy', 'load' or 'skip' (got '{mode}').", paramName),
        };
    }

    /// <summary>Validates the variable-reproduction mode: 'copy' clones each variable from the model's
    /// source file, 'model' rebuilds it declaratively from the model's own description.</summary>
    private static string NormalizeVariableMode(string? mode)
    {
        string m = (mode ?? "copy").Trim().ToLowerInvariant();
        return m switch
        {
            "copy" or "model" => m,
            "" => "copy",
            _ => throw new ArgumentException(
                $"variables must be 'copy' or 'model' (got '{mode}').", nameof(mode)),
        };
    }

    /// <summary>First of the candidate paths that exists on disk, or null.</summary>
    private static string? FirstExistingPath(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c) && System.IO.File.Exists(c)) return c;
        return null;
    }

    // CLONES top-level variables from the model's source file into the destination, one per name, into
    // the very container insert_local_variable/_parameter/_file_global would write to. Returns the names
    // that actually made it, so the caller can fall back to the declarative path for the rest.
    //
    // WHY: the declarative path cannot reproduce "this member has no value of its own". Instantiating a
    // named type (insert_local_variable dataType:"LogEvent") materialises its members, and an ENUM member
    // comes out with its default enumerator NAME written — which TestStand counts as explicitly set, so
    // the FileDiffer shows `[Debug]` where the editor-authored original has `{Debug}`. The import already
    // avoids WRITING that value (the model records isDefault:true and it is skipped); the marker comes
    // from the instantiation itself, so no amount of not-writing fixes it. A flag-preserving Clone carries
    // the state verbatim instead — the same mechanism that reproduces the LabVIEW panes exactly.
    //
    // Per-variable rather than replacing the whole Locals/Parameters container: that keeps the
    // engine-created ResultList untouched and reproduces the model's ORDER, since SetPropertyObject with
    // InsertIfMissing appends in call order. Requires the types to exist already (pass 1).
    private async Task<HashSet<string>> CloneVariablesAsync(string sourceFilePath, string destFilePath,
        string scope, string? sequenceName, List<VarModel> vars, ImportOutcome outcome)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        if (vars.Count == 0) return done;

        return await Task.Run(() =>
        {
            dynamic srcSf = GetOrLoadSeqFile(sourceFilePath);
            dynamic dstSf = GetOrLoadSeqFile(destFilePath);
            NiPropertyObject srcRoot, dstRoot;
            try
            {
                srcRoot = ResolveScopeRoot(srcSf, scope, sequenceName);
                dstRoot = ResolveScopeRoot(dstSf, scope, sequenceName);
            }
            catch (Exception ex)
            {
                outcome.Warnings.Add(
                    $"variables='copy': could not resolve {scope}" +
                    (sequenceName is null ? "" : $" of '{sequenceName}'") +
                    $": {ex.Message}. Falling back to rebuilding them from the model.");
                return done;
            }

            foreach (var v in vars)
            {
                if (string.IsNullOrWhiteSpace(v.Name)) continue;
                try
                {
                    // A live source object still has a parent and SetPropertyObject rejects it, so clone
                    // first (PropOption_CopyAllFlags = detached, flag-preserving deep copy).
                    NiPropertyObject clone = (NiPropertyObject)(object)srcRoot.Clone(
                        v.Name, 0x20000000 /* PropOption_CopyAllFlags */);
                    dstRoot.SetPropertyObject(v.Name, PropOption_InsertIfMissing, clone);
                    // Mirror the node's own PropFlags (e.g. 0x4 PassByReference on a parameter) in case
                    // the container flag differs from what CopyAllFlags carried on the leaf.
                    try { dstRoot.SetFlags(v.Name, 0, srcRoot.GetFlags(v.Name, 0)); }
                    catch (Exception ex)
                    { _logger.LogDebug(ex, "Flag mirror skipped for '{Scope}.{Name}'.", scope, v.Name); }
                    done.Add(v.Name);
                }
                catch (Exception ex)
                {
                    outcome.Warnings.Add(
                        $"variables='copy': {scope}" +
                        (sequenceName is null ? "" : $" of '{sequenceName}'") +
                        $" variable '{v.Name}' could not be cloned ({ex.Message}); rebuilt from the model " +
                        "instead.");
                }
            }
            return done;
        });
    }

    // Writes a variable's value/flags/representation and recurses into its members. The top-level
    // node itself is already created by the caller (insert_local_variable / _parameter / _file_global).
    private async Task ApplyVarNodeAsync(string filePath, string scope, string? sequenceName,
        string path, VarModel vm, ImportOutcome outcome)
    {
        // Arrays: size first, then fill the elements by index.
        if (vm.ValueType == "Array" && vm.NumElements.HasValue)
        {
            await SetPropertyNodeAsync(filePath, scope, sequenceName, path, "array_elements",
                null, null, null, vm.NumElements, null, true, false,
                vm.Representation, vm.NumberFormat);
        }
        // Write PropFlags only when there is actually a bit to set. A freshly created node already has
        // 0, and writing 0 is NOT a no-op for TestStand: SetFlags on a TYPE INSTANCE marks it as
        // overridden, which turns the FileDiffer's {val} into [val] on the instance's members — a
        // difference produced purely by writing. (Observed on a LogEvent local whose LogLevel matched
        // the original perfectly until the redundant flags write.)
        if ((vm.Flags ?? 0) != 0)
        {
            await SetPropertyNodeAsync(filePath, scope, sequenceName, path, "container",
                null, null, null, null, vm.Flags, true, false, null, null, clearFlags: true);
        }

        if (vm.Members == null) return;
        for (int i = 0; i < vm.Members.Count; i++)
        {
            var child = vm.Members[i];
            // Array elements are addressed positionally, container members by name.
            string childPath = vm.ValueType == "Array" ? $"{path}[{i}]" : $"{path}.{child.Name}";
            try
            {
                string vt = child.ValueType switch
                {
                    "Enum"      => "enum",
                    "Container" => child.DataType is { Length: > 0 } dt && dt != "container"
                                   ? "named_type" : "container",
                    "Array"     => "array_elements",
                    "Number"    => "number",
                    "Boolean"   => "boolean",
                    "String"    => "string",
                    _           => child.DataType == "reference" ? "reference" : "container",
                };
                string? typeName = vt is "enum" or "named_type" ? child.DataType : null;

                // An enum the source leaves at its TYPE DEFAULT must be created but NOT written:
                // writing it would mark the value explicitly set and produce a difference where the
                // original has none. Creating it with no ordinal/value leaves it at the default.
                bool skipValue = vt is "array_elements" or "container" or "named_type"
                                 || (vt == "enum" && child.IsDefault == true);

                // Members that a named type already MATERIALISED and that carry nothing to change must
                // not be touched at all. Even a no-op write (flags 0 onto a property that already has
                // 0) makes TestStand treat the value as explicitly set, which turns the FileDiffer's
                // {val} into [val] — a difference created purely by writing.
                int? writeFlags = (child.Flags ?? 0) != 0 ? child.Flags : null;
                bool nothingToWrite = skipValue
                                      && writeFlags is null
                                      && child.NumElements is null
                                      && string.IsNullOrEmpty(child.Representation)
                                      && string.IsNullOrEmpty(child.NumberFormat);

                if (!nothingToWrite)
                    await SetPropertyNodeAsync(filePath, scope, sequenceName, childPath, vt,
                        typeName,
                        skipValue ? null : child.Value,
                        skipValue ? null : child.Ordinal,
                        child.NumElements, writeFlags, true, false,
                        child.Representation, child.NumberFormat, clearFlags: writeFlags.HasValue);
                else
                    // Still ensure the member EXISTS (an anonymous container member is not materialised
                    // by any type), without writing a value or flags.
                    await SetPropertyNodeAsync(filePath, scope, sequenceName, childPath, vt,
                        typeName, null, null, null, null, true, false);

                if (child.Comment != null && scope != "StationGlobals")
                {
                    try
                    {
                        if (scope == "Parameters")
                            await SetParameterCommentAsync(filePath, sequenceName!, childPath, child.Comment);
                        else if (scope == "Locals")
                            await SetLocalVariableCommentAsync(filePath, sequenceName!, childPath, child.Comment);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Member comment write failed for '{Path}'.", childPath); }
                }

                if (child.Members != null)
                    await ApplyVarNodeAsync(filePath, scope, sequenceName, childPath, child, outcome);
            }
            catch (Exception ex)
            { outcome.Warnings.Add($"member '{childPath}': {ex.Message}"); }
        }
    }

    // Applies one step's properties and module configuration. 'selector' is an @idx: form so
    // duplicate step names stay addressable.
    // Which part of a step's configuration a pass applies. The split exists because a SequenceCall
    // prototype load POISONS the LabVIEW adapter for the rest of the process: after one
    // Module.LoadPrototype on a SequenceCall step, every later LabVIEW VI connector-pane load fails
    // with "could not resolve the target/module" (measured — the same load succeeds in ~5s in a
    // process that has not done one). So all LabVIEW work must finish BEFORE the first SequenceCall
    // load.
    private enum StepPass { PropertiesAndLabView, SequenceCallAndPython }

    private async Task ApplyStepDetailsAsync(string filePath, string seqName, string group,
        string selector, StepModel s, ImportOutcome outcome,
        List<(string Seq, string Group, string Selector, StepModel Step)>? pendingViLoads,
        StepPass pass)
    {
        async Task Prop(string path, string? value, string? kind = null)
        {
            if (value == null) return;
            try { await SetStepPropertyAsync(filePath, seqName, group, selector, path, value, kind, false); }
            catch (Exception ex) { outcome.Warnings.Add($"{seqName}/{group}/{s.Name}:{path}: {ex.Message}"); }
        }

        if (pass == StepPass.PropertiesAndLabView)
        {
        await Prop(PPreCond,    s.Precondition,     "string");
        await Prop(PPreExpr,    s.PreExpression,    "string");
        await Prop(PPostExpr,   s.PostExpression,   "string");
        await Prop(PStatusExpr, s.StatusExpression, "string");
        await Prop(PMode,       s.RunMode,          "string");
        await Prop(PPassAct,    s.PassAction,       "string");
        await Prop(PFailAct,    s.FailAction,       "string");
        await Prop(PLoopType,   s.LoopType,         "string");
        await Prop(PLoadOpt,    s.LoadOption,       "string");
        await Prop(PUnloadOpt,  s.UnloadOption,     "string");
        if (s.ResultOption.HasValue)
            await Prop(PResultOpt, s.ResultOption.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "number");
        if (s.IgnoreRuntimeErrors.HasValue)
            await Prop(PIgnoreRTE, s.IgnoreRuntimeErrors.Value ? "true" : "false", "boolean");
        if (s.StepFailureCausesSequenceFailure.HasValue)
            await Prop(PStepFCSeqF, s.StepFailureCausesSequenceFailure.Value ? "true" : "false", "boolean");
        await Prop("ConditionExpr",      s.ConditionExpr,      "string");
        await Prop("ItemExpr",           s.ItemExpr,           "string");
        await Prop("ArrayExpr",          s.ArrayExpr,          "string");
        await Prop("ArrayElementExpr",   s.ArrayElementExpr,   "string");
        await Prop("InitializationExpr", s.InitializationExpr, "string");
        await Prop("IncrementExpr",      s.IncrementExpr,      "string");
        if (s.IsDefaultCase.HasValue)
            await Prop("IsDefault", s.IsDefaultCase.Value ? "true" : "false", "boolean");
        }

        var mod = s.Module;
        if (mod == null) return;
        bool wantLabView = pass == StepPass.PropertiesAndLabView;
        if (wantLabView != (mod.Kind is "LabVIEW" or "Wait")) return;
        try
        {
            switch (mod.Kind)
            {
                case "Wait":
                    if (mod.WaitTimeExpression != null)
                        await SetWaitTimeAsync(filePath, seqName, group, selector, mod.WaitTimeExpression);
                    break;

                case "LabVIEW":
                    // Set the VI path WITHOUT the built-in auto-load: that one runs in-process and via
                    // the adapter's AutoDetect, which resolves a LabVIEW Run-Time and faults on a
                    // packed-library VI. The connector pane is then loaded through
                    // load_module_prototype instead, which routes the adapter to the LabVIEW
                    // ExecServer (the running ADE, via ActiveX) and runs in the crash-isolated worker —
                    // that is what makes a .lvlibp VI loadable headless at all. Skipping the load
                    // entirely would leave ViCall.Parms empty and every connector-pane property blank.
                    await ConfigureLabViewModuleAsync(filePath, seqName, group, selector,
                        mod.ViPath ?? "", save: false, loadPrototype: false);
                    outcome.ModulesConfigured++;
                    // The load itself is DEFERRED to a pass after the file has been saved: it runs in
                    // an isolated WORKER PROCESS with its own engine, which reads the file from DISK.
                    // Loading here — while the import still holds everything in memory with save:false
                    // — makes the worker look at a file that does not contain the step yet ("@idx:0 is
                    // out of range — the group has 0 step(s)").
                    pendingViLoads?.Add((seqName, group, selector, s));
                    break;

                case "Python":
                    var pyArgs = mod.Arguments?.ConvertAll(a => new PythonParamSpec
                    {
                        Name = a.Name, Type = a.Type, Value = a.Value
                    });
                    await ConfigurePythonModuleAsync(filePath, seqName, group, selector,
                        mod.ModulePath ?? "", mod.FunctionName ?? "", save: false, loadPrototype: false,
                        mod.ClassName, mod.ClassInstanceLocation, mod.OperationType, mod.OperationScope,
                        mod.PythonVersion, mod.VirtualEnvPath, mod.UseAdapterInterpreterSettings, pyArgs);
                    outcome.ModulesConfigured++;
                    break;

                case "SequenceCall":
                    // loadPrototype:FALSE on purpose. The arguments are authored in full from the model
                    // right below, so the load buys nothing here for the interface — and an in-process
                    // SequenceCall load would poison the LabVIEW adapter for the rest of the process
                    // (and is itself already poisoned by the pane loads that ran before). The only thing
                    // it does add, the cross-file Prototype cache, is done out-of-process in pass 7.
                    await ConfigureSequenceCallModuleAsync(filePath, seqName, group, selector,
                        mod.TargetSequenceName ?? "",
                        mod.UseCurrentFile == false ? (mod.TargetSequenceFile ?? "") : "",
                        save: false, executionMode: null, threadRefExpr: null, autoWait: null,
                        loadPrototype: false, storedFilePath: mod.StoredFilePath);
                    outcome.ModulesConfigured++;
                    if (mod.Arguments != null)
                        await ApplySequenceCallArgsAsync(filePath, seqName, group, selector,
                            s.Name, mod.Arguments, outcome);
                    break;
            }
        }
        catch (Exception ex)
        { outcome.Warnings.Add($"module '{seqName}/{group}/{s.Name}' ({mod.Kind}): {ex.Message}"); }
    }

    /// <summary>
    /// Reconciles a SequenceCall step's <c>TS.SData.ActualArgs</c> with the exported argument list.
    /// <para>
    /// The prototype load that runs during configuration regenerates the arguments from the callee's
    /// CURRENT parameters. That is right for authoring but wrong for reproduction, in three ways this
    /// method fixes: (1) it copies <c>Flags</c>/<c>ParamRepresentation</c> from the callee, overwriting
    /// what the caller had; (2) it couples <c>UseDef</c> to whether an expression is present, while the
    /// editor keeps a remembered expression AND uses the default; (3) it names entries after the
    /// callee's current parameters, so a real file whose caller still carries a since-renamed argument
    /// (e.g. <c>vis</c> where the callee now says <c>vid</c>) gets the wrong name — and an argument the
    /// original does not have at all appears out of nowhere.
    /// </para>
    /// So: create any missing entry, write every field verbatim, and delete the surplus.
    /// </summary>
    private async Task ApplySequenceCallArgsAsync(string filePath, string seqName, string group,
        string selector, string stepLabel, IReadOnlyList<ModuleArgModel> args, ImportOutcome outcome)
    {
        const string Base = "TS.SData.ActualArgs";

        // Rebuild the list from scratch rather than patching it. Argument ORDER is part of the file —
        // the FileDiffer pairs the entries positionally — and a load-generated list can differ from
        // the source in both membership and order, which patching cannot fix by appending. Deleting
        // everything first and recreating in the exported order makes the outcome deterministic.
        foreach (var existing in await ListActualArgNamesAsync(filePath, seqName, group, selector))
        {
            try { await DeleteStepPropertyAsync(filePath, seqName, group, selector, $"{Base}.{existing}", false); }
            catch (Exception ex)
            { outcome.Warnings.Add($"{seqName}/{stepLabel} clearing arg '{existing}': {ex.Message}"); }
        }

        foreach (var a in args)
        {
            if (string.IsNullOrEmpty(a.Name)) continue;
            string p = $"{Base}.{a.Name}";
            try
            {
                // Missing (the callee has no such parameter any more) → author the entry.
                await CreateStepPropertyAsync(filePath, seqName, group, selector, p,
                    "named_type", "SequenceArgument", null, null, false, false);

                if (a.ParamType.HasValue)
                    await SetStepPropertyAsync(filePath, seqName, group, selector, $"{p}.ParamType",
                        a.ParamType.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "number", false);
                if (a.ParamRepresentation.HasValue)
                    await SetStepPropertyAsync(filePath, seqName, group, selector,
                        $"{p}.ParamRepresentation",
                        a.ParamRepresentation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "number", false);
                if (a.ArgFlags.HasValue)
                    await SetStepPropertyAsync(filePath, seqName, group, selector, $"{p}.Flags",
                        a.ArgFlags.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "number", false);
                // Expr BEFORE UseDef: some writers derive UseDef from the expression, so UseDef has to
                // be the last word.
                await SetStepPropertyAsync(filePath, seqName, group, selector, $"{p}.Expr",
                    a.Value ?? "", "string", false);
                if (a.UseDefault.HasValue)
                    await SetStepPropertyAsync(filePath, seqName, group, selector, $"{p}.UseDef",
                        a.UseDefault.Value ? "true" : "false", "boolean", false);
                if (a.Flags.HasValue)
                    await SetStepPropertyFlagsAsync(filePath, seqName, group, selector, p,
                        a.Flags.Value, false, exact: true);
            }
            catch (Exception ex)
            { outcome.Warnings.Add($"{seqName}/{stepLabel} arg '{a.Name}': {ex.Message}"); }
        }
    }

    /// <summary>
    /// Writes a LabVIEW step's connector-pane bindings verbatim: each control's <c>ArgVal</c>
    /// expression AND its <c>UseDefaultValues</c> flag, set independently. Controls are addressed by
    /// their flattened Label path ("error out.status"), matching what the exporter recorded.
    /// Silently skips a label the freshly loaded pane does not have — a pane loaded from a different
    /// VI revision legitimately differs, and that belongs in the diff, not in an exception.
    /// </summary>
    private async Task ApplyViCallBindingsAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, IReadOnlyList<ModuleArgModel>? args, ImportOutcome outcome, string stepLabel)
    {
        if (args == null || args.Count == 0) return;
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var seq  = sf.GetSequenceByName(sequenceName);
            dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
            NiPropertyObject po = ((NiStep)(object)step).AsPropertyObject();

            NiPropertyObject? parms = null;
            foreach (var path in new[] { "TS.SData.ViCall.Parms", "VIModule.ViCall.Parms" })
            {
                try { parms = (NiPropertyObject)(object)po.GetPropertyObject(path, 0); break; }
                catch { /* try the next shape */ }
            }
            if (parms == null) return;

            foreach (var a in args)
            {
                if (string.IsNullOrEmpty(a.Name)) continue;
                var target = FindViCallParm(parms, a.Name!.Split('.'));
                if (target == null) continue;
                try
                {
                    if (a.Value != null) target.SetValString("ArgVal", 0, a.Value);
                    if (a.UseDefault.HasValue)
                        target.SetValBoolean("UseDefaultValues", 0, a.UseDefault.Value);
                }
                catch (Exception ex)
                { outcome.Warnings.Add($"{sequenceName}/{stepGroup}/{stepLabel} binding '{a.Name}': {ex.Message}"); }
            }
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // The argument entry names currently present on a SequenceCall step.
    private async Task<List<string>> ListActualArgNamesAsync(string filePath, string sequenceName,
        string stepGroup, string stepName)
    {
        return await Task.Run(() =>
        {
            var names = new List<string>();
            try
            {
                var sf   = GetOrLoadSeqFile(filePath);
                var seq  = sf.GetSequenceByName(sequenceName);
                dynamic step = (NiStep)(object)ResolveStepInGroup(seq, ParseStepGroup(stepGroup), stepName);
                NiPropertyObject po = ((NiStep)(object)step).AsPropertyObject();
                NiPropertyObject args = (NiPropertyObject)(object)
                    po.GetPropertyObject("TS.SData.ActualArgs", 0);
                int n = args.GetNumSubProperties("");
                for (int i = 0; i < n; i++)
                    try { names.Add(args.GetNthSubProperty("", i, 0).Name); } catch { }
            }
            catch (Exception ex)
            { _logger.LogDebug(ex, "Listing ActualArgs of '{Step}' failed.", stepName); }
            return names;
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            DisconnectAsync().GetAwaiter().GetResult();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
