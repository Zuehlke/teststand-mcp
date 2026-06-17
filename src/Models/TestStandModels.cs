using System;
using System.Collections.Generic;

namespace TestStandMCP.Models;

// ── Execution Models ─────────────────────────────────────────────────────────

/// <summary>Summary of a single TestStand execution.</summary>
public class ExecutionInfo
{
    /// <summary>Engine-assigned execution identifier.</summary>
    public string ExecutionId { get; set; } = "";
    /// <summary>Path of the sequence file being executed.</summary>
    public string SequenceFilePath { get; set; } = "";
    /// <summary>Entry-point sequence name.</summary>
    public string EntryPoint { get; set; } = "";
    /// <summary>Current run state (e.g. Running, Completed).</summary>
    public string Status { get; set; } = "";
    /// <summary>When the execution started.</summary>
    public DateTime StartTime { get; set; }
    /// <summary>When the execution finished, or null if still running.</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Overall result (e.g. Passed, Failed).</summary>
    public string Result { get; set; } = "";
    /// <summary>Error message if the execution failed, else null.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Detailed result of a completed execution, including per-step results.</summary>
public class ExecutionResult
{
    /// <summary>Engine-assigned execution identifier.</summary>
    public string ExecutionId { get; set; } = "";
    /// <summary>Final run state.</summary>
    public string Status { get; set; } = "";
    /// <summary>Overall result (e.g. Passed, Failed).</summary>
    public string Result { get; set; } = "";
    /// <summary>Total elapsed run time in seconds.</summary>
    public double ElapsedSeconds { get; set; }
    /// <summary>Error message if the execution failed, else null.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Result of each executed step.</summary>
    public List<StepResult> StepResults { get; init; } = new();
}

/// <summary>Result of a single executed step.</summary>
public class StepResult
{
    /// <summary>Step name.</summary>
    public string StepName { get; set; } = "";
    /// <summary>Step type (e.g. NumericLimitTest).</summary>
    public string StepType { get; set; } = "";
    /// <summary>Run status of the step.</summary>
    public string Status { get; set; } = "";
    /// <summary>Pass/Fail/Done result of the step.</summary>
    public string Result { get; set; } = "";
    /// <summary>Numeric limit applied to the step, if any.</summary>
    public double? NumericLimit { get; set; }
    /// <summary>Measured value, if the step recorded one.</summary>
    public double? MeasuredValue { get; set; }
    /// <summary>Error message if the step failed, else null.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Elapsed run time of the step in seconds.</summary>
    public double ElapsedSeconds { get; set; }
}

// ── Bulk Insert Models ───────────────────────────────────────────────────────

/// <summary>
/// One step to insert via insert_steps_bulk. Steps are appended in list order.
/// Optional fields let a single bulk call also set comment, expression and the
/// SequenceCall target — collapsing what used to be ~4 separate tool calls per step.
/// </summary>
public class BulkStepSpec
{
    /// <summary>Step name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Step type (e.g. Statement, SequenceCall, NI_Flow_If).</summary>
    public string StepType { get; set; } = "";
    /// <summary>Adapter name for code-module steps (optional).</summary>
    public string? Adapter { get; set; }
    /// <summary>Step comment / description (optional).</summary>
    public string? Comment { get; set; }
    /// <summary>Expression to assign to the step (optional).</summary>
    public string? Expression { get; set; }
    /// <summary>Which expression field the <see cref="Expression"/> targets (optional).</summary>
    public string? ExpressionType { get; set; }
    /// <summary>Target sequence for a SequenceCall step (optional).</summary>
    public string? TargetSequenceName { get; set; }
    /// <summary>Target sequence file (empty/omitted = same/current file).</summary>
    public string? TargetSequenceFile { get; set; }
}

/// <summary>Outcome of an insert_steps_bulk operation.</summary>
public class BulkInsertResult
{
    /// <summary>Sequence the steps were inserted into.</summary>
    public string SequenceName { get; set; } = "";
    /// <summary>Step group the steps were inserted into.</summary>
    public string StepGroup { get; set; } = "";
    /// <summary>Number of steps inserted.</summary>
    public int InsertedCount { get; set; }
    /// <summary>Number of comments applied.</summary>
    public int CommentsSet { get; set; }
    /// <summary>Number of expressions applied.</summary>
    public int ExpressionsSet { get; set; }
    /// <summary>Number of SequenceCall targets linked.</summary>
    public int TargetsSet { get; set; }
    /// <summary>Names of the inserted steps, in order.</summary>
    public List<string> InsertedSteps { get; init; } = new();
    /// <summary>Non-fatal warnings raised during the operation.</summary>
    public List<string> Warnings { get; init; } = new();
}

// ── Sequence File Models ─────────────────────────────────────────────────────

/// <summary>Full description of a loaded sequence file.</summary>
public class SequenceFileInfo
{
    /// <summary>Absolute path of the sequence file.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>File name without directory.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Sequences contained in the file.</summary>
    public List<SequenceInfo> Sequences { get; init; } = new();
    /// <summary>File-global variables.</summary>
    public List<VariableInfo> FileGlobals { get; init; } = new();
    /// <summary>Station-global variables visible to the file.</summary>
    public List<VariableInfo> StationGlobals { get; init; } = new();
    /// <summary>File comment/description, if any.</summary>
    public string? Description { get; set; }
    /// <summary>File version string, if any.</summary>
    public string? Version { get; set; }
}

/// <summary>
/// Lightweight overview of a loaded sequence file: paths, sequence names and
/// count only — no steps, locals or globals. Used by get_loaded_sequence_files
/// in its default "summary" mode to avoid huge payloads. Detail is retrieved
/// on demand via get_sequence / get_steps.
/// </summary>
public class SequenceFileSummary
{
    /// <summary>Absolute path of the sequence file.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>File name without directory.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Number of sequences in the file.</summary>
    public int SequenceCount { get; set; }
    /// <summary>Names of the sequences in the file.</summary>
    public List<string> Sequences { get; init; } = new();
}

/// <summary>Description of a single sequence: its steps, locals and parameters.</summary>
public class SequenceInfo
{
    /// <summary>Sequence name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Sequence comment/description, if any.</summary>
    public string? Description { get; set; }
    /// <summary>Steps in the sequence (Main group by default).</summary>
    public List<StepInfo> Steps { get; init; } = new();
    /// <summary>Local variables of the sequence.</summary>
    public List<VariableInfo> Locals { get; init; } = new();
    /// <summary>Parameters of the sequence.</summary>
    public List<ParameterInfo> Parameters { get; init; } = new();
}

/// <summary>Description of a single step.</summary>
public class StepInfo
{
    /// <summary>Step name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Step type.</summary>
    public string StepType { get; set; } = "";
    /// <summary>Step comment/description, if any.</summary>
    public string? Description { get; set; }
    /// <summary>Null/omitted = enabled (the common case); false = step is skipped.
    /// Kept nullable so the serializer drops it for the overwhelmingly common
    /// enabled steps (token optimization — absence means enabled).</summary>
    public bool? Enabled { get; set; }
    /// <summary>Step group: "Setup" or "Cleanup". Null/omitted = "Main"
    /// (the default group — omitted to save tokens).</summary>
    public string? StepGroup { get; set; }
    /// <summary>Null/omitted when there are no sub-steps.</summary>
    public List<StepInfo>? SubSteps { get; set; }
    /// <summary>Null/omitted when empty (get_steps never populates this).</summary>
    public Dictionary<string, string>? Properties { get; set; }
}

// ── Variable / Property Models ───────────────────────────────────────────────

/// <summary>Description of a variable (local, file-global or station-global).</summary>
public class VariableInfo
{
    /// <summary>Variable name.</summary>
    public string Name { get; set; } = "";
    /// <summary>TestStand data type.</summary>
    public string DataType { get; set; } = "";
    /// <summary>Current value, if scalar.</summary>
    public object? Value { get; set; }
    /// <summary>Variable comment/description, if any.</summary>
    public string? Description { get; set; }
    /// <summary>True when the variable is an array.</summary>
    public bool IsArray { get; set; }
    /// <summary>Number of array elements (0 for scalars).</summary>
    public int ArraySize { get; set; }
}

/// <summary>
/// A node in a recursively-walked TestStand property tree (see <c>get_property_tree</c>).
/// Containers and arrays carry <see cref="Children"/>; scalar leaves carry <see cref="Value"/>.
/// </summary>
public class PropertyNode
{
    /// <summary>Property name (array elements use an index label like "[0]").</summary>
    public string Name { get; set; } = "";
    /// <summary>Human-readable TestStand type (GetTypeDisplayString), if available.</summary>
    public string? Type { get; set; }
    /// <summary>Node kind: "Container", "Array", "Number", "Boolean", "String" or "Empty".</summary>
    public string ValueType { get; set; } = "";
    /// <summary>Scalar value for leaf nodes; null for containers/arrays.</summary>
    public object? Value { get; set; }
    /// <summary>True when PropFlags_Hidden (0x08) is set — a normally hidden property.</summary>
    public bool IsHidden { get; set; }
    /// <summary>True when PropFlags_HiddenInTypes (0x10) is set.</summary>
    public bool IsHiddenInTypes { get; set; }
    /// <summary>Raw property flags bitfield (PropFlags_*), for reference.</summary>
    public int Flags { get; set; }
    /// <summary>True when the node is an array.</summary>
    public bool IsArray { get; set; }
    /// <summary>Number of array elements (only when <see cref="IsArray"/>).</summary>
    public int? ArraySize { get; set; }
    /// <summary>Number of named subproperties reported by the engine.</summary>
    public int SubPropertyCount { get; set; }
    /// <summary>True when children were cut off by a depth, element or node-budget cap.</summary>
    public bool Truncated { get; set; }
    /// <summary>Child nodes for containers/arrays; null for scalar leaves.</summary>
    public List<PropertyNode>? Children { get; set; }
}

/// <summary>Description of a sequence parameter.</summary>
public class ParameterInfo
{
    /// <summary>Parameter name.</summary>
    public string Name { get; set; } = "";
    /// <summary>TestStand data type.</summary>
    public string DataType { get; set; } = "";
    /// <summary>Default value, if any.</summary>
    public object? DefaultValue { get; set; }
    /// <summary>Direction of the parameter: "Input", "Output" or "InOut".</summary>
    public string Direction { get; set; } = "Input";
    /// <summary>True when the parameter is passed BY REFERENCE (PropFlags_PassByReference); false = BY VALUE.</summary>
    public bool PassByReference { get; set; }
    /// <summary>Parameter comment/description, if any.</summary>
    public string? Description { get; set; }
}

/// <summary>A named property value with its type and optional lookup string.</summary>
public class PropertyValue
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = "";
    /// <summary>TestStand data type.</summary>
    public string DataType { get; set; } = "";
    /// <summary>The property value.</summary>
    public object? Value { get; set; }
    /// <summary>Lookup string used to locate the property, if applicable.</summary>
    public string? LookupString { get; set; }
}

// ── Report Models ────────────────────────────────────────────────────────────

/// <summary>Summary information about a generated execution report.</summary>
public class ReportInfo
{
    /// <summary>Execution the report belongs to.</summary>
    public string ExecutionId { get; set; } = "";
    /// <summary>Path of the generated report file.</summary>
    public string ReportPath { get; set; } = "";
    /// <summary>Report format (e.g. HTML, XML).</summary>
    public string Format { get; set; } = "";
    /// <summary>When the report was generated.</summary>
    public DateTime GeneratedAt { get; set; }
    /// <summary>Overall result captured in the report.</summary>
    public string OverallResult { get; set; } = "";
    /// <summary>Total number of steps recorded.</summary>
    public int TotalSteps { get; set; }
    /// <summary>Number of passed steps.</summary>
    public int PassedSteps { get; set; }
    /// <summary>Number of failed steps.</summary>
    public int FailedSteps { get; set; }
    /// <summary>Number of skipped steps.</summary>
    public int SkippedSteps { get; set; }
    /// <summary>Total recorded run time in seconds.</summary>
    public double TotalTime { get; set; }
}

// ── Station / Engine Models ──────────────────────────────────────────────────

/// <summary>Information about the TestStand station and engine.</summary>
public class StationInfo
{
    /// <summary>Station name.</summary>
    public string StationName { get; set; } = "";
    /// <summary>TestStand engine version.</summary>
    public string TestStandVersion { get; set; } = "";
    /// <summary>Host operating-system description.</summary>
    public string OperatingSystem { get; set; } = "";
    /// <summary>Logged-in TestStand user.</summary>
    public string Username { get; set; } = "";
    /// <summary>True when the engine is licensed.</summary>
    public bool IsLicensed { get; set; }
    /// <summary>Paths of currently loaded sequence files.</summary>
    public List<string> LoadedSequenceFiles { get; init; } = new();
    /// <summary>Currently active executions.</summary>
    public List<ExecutionInfo> ActiveExecutions { get; init; } = new();
}

// ── Batch Models ─────────────────────────────────────────────────────────────

/// <summary>Information about a batch run of multiple UUTs.</summary>
public class BatchInfo
{
    /// <summary>Batch identifier.</summary>
    public string BatchId { get; set; } = "";
    /// <summary>Batch serial number.</summary>
    public string SerialNumber { get; set; } = "";
    /// <summary>When the batch started.</summary>
    public DateTime StartTime { get; set; }
    /// <summary>When the batch finished, or null if running.</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Batch status.</summary>
    public string Status { get; set; } = "";
    /// <summary>Executions belonging to the batch.</summary>
    public List<ExecutionInfo> Executions { get; init; } = new();
}

// ── UUT / DUT Models ─────────────────────────────────────────────────────────

/// <summary>Information about a unit under test (UUT) and its measurements.</summary>
public class UutInfo
{
    /// <summary>UUT serial number.</summary>
    public string SerialNumber { get; set; } = "";
    /// <summary>UUT part number.</summary>
    public string PartNumber { get; set; } = "";
    /// <summary>Serial number of the batch the UUT belongs to.</summary>
    public string BatchSerialNumber { get; set; } = "";
    /// <summary>Overall UUT result.</summary>
    public string Result { get; set; } = "";
    /// <summary>When testing of the UUT started.</summary>
    public DateTime StartTime { get; set; }
    /// <summary>When testing of the UUT finished, or null if running.</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Recorded measurement results.</summary>
    public List<StepResult> MeasurementResults { get; init; } = new();
}

// ── Adapter / Instrument Models ──────────────────────────────────────────────

/// <summary>Information about a TestStand module adapter.</summary>
public class AdapterInfo
{
    /// <summary>Adapter name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Adapter type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Adapter version.</summary>
    public string Version { get; set; } = "";
    /// <summary>True when the adapter is loaded.</summary>
    public bool IsLoaded { get; set; }
    /// <summary>Additional adapter properties.</summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

// ── Sequence Editor Models ────────────────────────────────────────────────────

/// <summary>State of the external Sequence Editor process.</summary>
public class SequenceEditorInfo
{
    /// <summary>True when the editor is running.</summary>
    public bool IsRunning { get; set; }
    /// <summary>Editor process id, or 0 when not running.</summary>
    public int ProcessId { get; set; }
    /// <summary>Path to the editor executable.</summary>
    public string EditorPath { get; set; } = "";
    /// <summary>Title of the editor's main window.</summary>
    public string MainWindowTitle { get; set; } = "";
}

// ── Type Palette Models ──────────────────────────────────────────────────────

/// <summary>Information about a loaded type-palette file.</summary>
public class TypePaletteInfo
{
    /// <summary>Palette file path.</summary>
    public string Path { get; set; } = "";
    /// <summary>Palette name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Number of step types defined in the palette.</summary>
    public int StepTypeCount { get; set; }
    /// <summary>Names of the step types in the palette.</summary>
    public List<string> StepTypeNames { get; init; } = new();
}

/// <summary>Information about a step type.</summary>
public class StepTypeInfo
{
    /// <summary>Step type name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Description of the step type, if any.</summary>
    public string? Description { get; set; }
    /// <summary>Palette file the step type is defined in.</summary>
    public string PaletteFile { get; set; } = "";
    /// <summary>Default adapter for the step type, if any.</summary>
    public string? AdapterName { get; set; }
    /// <summary>Additional step-type properties.</summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>Information about a custom data type.</summary>
public class DataTypeInfo
{
    /// <summary>Data type name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Description of the data type, if any.</summary>
    public string? Description { get; set; }
    /// <summary>Base type the data type derives from.</summary>
    public string BaseType { get; set; } = "";
    /// <summary>True when the data type is an array.</summary>
    public bool IsArray { get; set; }
    /// <summary>Fields/properties of the data type.</summary>
    public List<DataTypePropertyInfo> Properties { get; init; } = new();
}

/// <summary>A single field/property of a custom data type.</summary>
public class DataTypePropertyInfo
{
    /// <summary>Field name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Field data type.</summary>
    public string DataType { get; set; } = "";
    /// <summary>Default value, if any.</summary>
    public object? DefaultValue { get; set; }
    /// <summary>Field description, if any.</summary>
    public string? Description { get; set; }
}

/// <summary>A single named constant of an enumeration data type (name → numeric value).</summary>
public class EnumValueInfo
{
    /// <summary>Enumerator name (the label, stored as the EnumeratorName subproperty).</summary>
    public string Name { get; set; } = "";
    /// <summary>Numeric value of the enumerator (the EnumeratorValue subproperty).</summary>
    public double Value { get; set; }
}

/// <summary>An enumeration data type and its ordered list of named constants.</summary>
public class EnumInfo
{
    /// <summary>Enum data type name.</summary>
    public string Name { get; set; } = "";
    /// <summary>The enumerators (name → value pairs) in definition order.</summary>
    public List<EnumValueInfo> Values { get; init; } = new();
}

// ── Log / Trace Models ───────────────────────────────────────────────────────

/// <summary>A single execution-log entry.</summary>
public class LogEntry
{
    /// <summary>When the entry was recorded.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Log level.</summary>
    public string Level { get; set; } = "";
    /// <summary>Log message.</summary>
    public string Message { get; set; } = "";
    /// <summary>Originating source, if any.</summary>
    public string? Source { get; set; }
    /// <summary>Associated execution id, if any.</summary>
    public string? ExecutionId { get; set; }
}

/// <summary>A single message produced by the sequence analyzer.</summary>
public class AnalyzerMessage
{
    /// <summary>Message severity (Error, Warning, Information).</summary>
    public string Severity { get; set; } = "";
    /// <summary>Analyzer rule identifier.</summary>
    public string RuleId { get; set; } = "";
    /// <summary>Message text.</summary>
    public string Text { get; set; } = "";
    /// <summary>Location description of the finding.</summary>
    public string Location { get; set; } = "";
    /// <summary>Sequence the finding relates to.</summary>
    public string SequenceName { get; set; } = "";
    /// <summary>Step the finding relates to.</summary>
    public string StepName { get; set; } = "";
}

/// <summary>Aggregated result of a sequence-analyzer run, incl. severity counts.</summary>
public class AnalyzerResult
{
    /// <summary>Analyzed file path.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Total number of analyzer messages.</summary>
    public int TotalMessages { get; set; }
    /// <summary>Number of error-severity messages.</summary>
    public int ErrorCount { get; set; }
    /// <summary>Number of warning-severity messages.</summary>
    public int WarningCount { get; set; }
    /// <summary>Number of information-severity messages.</summary>
    public int InformationCount { get; set; }
    /// <summary>The analyzer messages.</summary>
    public List<AnalyzerMessage> Messages { get; init; } = new();
}

// ── User / Privilege Models ──────────────────────────────────────────────────

/// <summary>Information about a TestStand user or group.</summary>
public class UserInfo
{
    /// <summary>Login name.</summary>
    public string LoginName { get; set; } = "";
    /// <summary>Full display name.</summary>
    public string FullName { get; set; } = "";
    /// <summary>True when the entry is a group rather than a user.</summary>
    public bool IsGroup { get; set; }
    /// <summary>Groups the user belongs to.</summary>
    public List<string> GroupMemberships { get; init; } = new();
}

// ── Output Message Models ────────────────────────────────────────────────────

/// <summary>An engine output message.</summary>
public class OutputMessageInfo
{
    /// <summary>Message id.</summary>
    public int Id { get; set; }
    /// <summary>Message category.</summary>
    public string Category { get; set; } = "";
    /// <summary>Message text.</summary>
    public string Message { get; set; } = "";
    /// <summary>Message severity.</summary>
    public string Severity { get; set; } = "";
    /// <summary>Timestamp in seconds since the engine epoch.</summary>
    public double TimeInSeconds { get; set; }
}

// ── Search Directory Models ──────────────────────────────────────────────────

/// <summary>A configured TestStand search directory.</summary>
public class SearchDirectoryInfo
{
    /// <summary>Index of the directory in the search list.</summary>
    public int Index { get; set; }
    /// <summary>Directory path.</summary>
    public string Path { get; set; } = "";
    /// <summary>Directory type.</summary>
    public string Type { get; set; } = "";
    /// <summary>True when the entry is disabled.</summary>
    public bool Disabled { get; set; }
    /// <summary>True when subdirectories are searched.</summary>
    public bool SearchSubdirectories { get; set; }
}

// ── Data-Type Field Models ───────────────────────────────────────────────────

/// <summary>A single field of a data type.</summary>
public class TypeFieldInfo
{
    /// <summary>Field name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Field data type.</summary>
    public string DataType { get; set; } = "";
}

// ── CSV Stream Models ────────────────────────────────────────────────────────

/// <summary>Result of reading lines from a CSV file.</summary>
public class CsvReadResult
{
    /// <summary>CSV file path.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Number of lines read.</summary>
    public int LineCount { get; set; }
    /// <summary>The lines read.</summary>
    public List<string> Lines { get; init; } = new();
}

// ── Engine / Station Models ──────────────────────────────────────────────────

/// <summary>Resolved TestStand engine and station paths/version.</summary>
public class EnginePaths
{
    /// <summary>Engine bin directory.</summary>
    public string BinDirectory { get; set; } = "";
    /// <summary>Configuration directory.</summary>
    public string ConfigDirectory { get; set; } = "";
    /// <summary>TestStand installation directory.</summary>
    public string TestStandDirectory { get; set; } = "";
    /// <summary>Full version string.</summary>
    public string VersionString { get; set; } = "";
    /// <summary>Major version number.</summary>
    public int MajorVersion { get; set; }
    /// <summary>Minor version number.</summary>
    public int MinorVersion { get; set; }
    /// <summary>Station identifier.</summary>
    public string StationId { get; set; } = "";
    /// <summary>Host computer name.</summary>
    public string ComputerName { get; set; } = "";
}

/// <summary>Selected engine/station option flags.</summary>
public class StationOptionsInfo
{
    /// <summary>Whether tracing is enabled.</summary>
    public bool TracingEnabled { get; set; }
    /// <summary>Whether breakpoints are enabled.</summary>
    public bool BreakpointsEnabled { get; set; }
    /// <summary>Whether result recording is disabled.</summary>
    public bool DisableResults { get; set; }
    /// <summary>Whether execution always goes to Cleanup on failure.</summary>
    public bool AlwaysGotoCleanupOnFailure { get; set; }
    /// <summary>Whether the engine breaks on run-time errors.</summary>
    public bool BreakOnRte { get; set; }
    /// <summary>Station identifier.</summary>
    public string StationId { get; set; } = "";
    /// <summary>Configured process-model path.</summary>
    public string ProcessModelPath { get; set; } = "";
}

/// <summary>Result of validating a TestStand expression's syntax.</summary>
public class ExpressionCheckResult
{
    /// <summary>True when the expression is valid.</summary>
    public bool IsValid { get; set; }
    /// <summary>Validation error message, empty when valid.</summary>
    public string ErrorMessage { get; set; } = "";
}

/// <summary>Result of evaluating a TestStand expression (PropertyObject.EvaluateEx).</summary>
public class ExpressionResult
{
    /// <summary>The evaluated expression.</summary>
    public string Expression { get; set; } = "";
    /// <summary>True when evaluation succeeded.</summary>
    public bool IsValid { get; set; }
    /// <summary>The computed value (number/boolean/string) or null for container/empty results.</summary>
    public object? Value { get; set; }
    /// <summary>Number / Boolean / String / Container / Array / Empty / Unknown.</summary>
    public string ValueType { get; set; } = "";
    /// <summary>Error message if evaluation failed, else null.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Structured view of a PropertyObject: its value type, scalar value and (for
/// containers) its immediate subproperties.</summary>
public class PropertyObjectInfo
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Number / Boolean / String / Container / Array / Unknown.</summary>
    public string ValueType { get; set; } = "";
    /// <summary>Named-type name if the property is an instance of a custom type, else null.</summary>
    public string? TypeName { get; set; }
    /// <summary>Scalar value for simple properties; null for containers/arrays.</summary>
    public object? Value { get; set; }
    /// <summary>True when the property is an array.</summary>
    public bool IsArray { get; set; }
    /// <summary>Number of elements for arrays, else null.</summary>
    public int? NumElements { get; set; }
    /// <summary>Immediate subproperties for containers.</summary>
    public List<PropertySubInfo> SubProperties { get; init; } = new();
}

/// <summary>A single immediate subproperty of a container PropertyObject.</summary>
public class PropertySubInfo
{
    /// <summary>Subproperty name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Subproperty value type.</summary>
    public string ValueType { get; set; } = "";
    /// <summary>Subproperty scalar value, if any.</summary>
    public object? Value { get; set; }
}

// ── Undo/Redo Models ─────────────────────────────────────────────────────────

/// <summary>A single entry on the undo or redo stack.</summary>
public class UndoItemInfo
{
    /// <summary>Index of the item on the stack.</summary>
    public int Index { get; set; }
    /// <summary>Action name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Action description.</summary>
    public string Description { get; set; } = "";
}

/// <summary>Snapshot of a file's undo/redo stacks.</summary>
public class UndoStackInfo
{
    /// <summary>True when an undo is available.</summary>
    public bool CanUndo { get; set; }
    /// <summary>True when a redo is available.</summary>
    public bool CanRedo { get; set; }
    /// <summary>Number of items on the undo stack.</summary>
    public int NumUndoItems { get; set; }
    /// <summary>Number of items on the redo stack.</summary>
    public int NumRedoItems { get; set; }
    /// <summary>File the stacks belong to, if any.</summary>
    public string? FilePath { get; set; }
    /// <summary>Items on the undo stack.</summary>
    public List<UndoItemInfo> UndoItems { get; init; } = new();
    /// <summary>Items on the redo stack.</summary>
    public List<UndoItemInfo> RedoItems { get; init; } = new();
}

// ── Sequence File Comparison Models ─────────────────────────────────────────

/// <summary>Result of comparing two sequence files.</summary>
public class SequenceFileDiff
{
    /// <summary>Path of the first file.</summary>
    public string File1 { get; set; } = "";
    /// <summary>Path of the second file.</summary>
    public string File2 { get; set; } = "";
    /// <summary>Total number of differences found.</summary>
    public int TotalDifferences { get; set; }
    /// <summary>Sequences present only in the first file.</summary>
    public List<string> SequencesOnlyInFile1 { get; init; } = new();
    /// <summary>Sequences present only in the second file.</summary>
    public List<string> SequencesOnlyInFile2 { get; init; } = new();
    /// <summary>Sequences present in both files but modified.</summary>
    public List<SequenceDiff> ModifiedSequences { get; init; } = new();
}

/// <summary>Differences within a single sequence present in both files.</summary>
public class SequenceDiff
{
    /// <summary>Sequence name.</summary>
    public string SequenceName { get; set; } = "";
    /// <summary>Step-level differences.</summary>
    public List<StepDiff> StepDiffs { get; init; } = new();
    /// <summary>Local-variable differences.</summary>
    public List<string> LocalVariableDiffs { get; init; } = new();
    /// <summary>Parameter differences.</summary>
    public List<string> ParameterDiffs { get; init; } = new();
    /// <summary>Sequence-property differences.</summary>
    public List<string> PropertyDiffs { get; init; } = new();
}

/// <summary>A single step-level difference between two sequences.</summary>
public class StepDiff
{
    /// <summary>Kind of difference: "Added", "Removed" or "Modified".</summary>
    public string DiffType { get; set; } = "";
    /// <summary>Step name.</summary>
    public string StepName { get; set; } = "";
    /// <summary>Step group.</summary>
    public string StepGroup { get; set; } = "";
    /// <summary>Step type.</summary>
    public string StepType { get; set; } = "";
    /// <summary>Names of the properties that changed (for "Modified").</summary>
    public List<string> ChangedProperties { get; init; } = new();
}

// ── Sync Manager Models ──────────────────────────────────────────────────────

/// <summary>Information about a synchronization object.</summary>
public class SyncObjectInfo
{
    /// <summary>Sync-object name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Sync-object type (e.g. Mutex, Queue).</summary>
    public string Type { get; set; } = "";
    /// <summary>Additional sync-object properties.</summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}

// ── Advanced Adapter Models ──────────────────────────────────────────────────

/// <summary>Detailed information about a module adapter.</summary>
public class AdapterDetailInfo
{
    /// <summary>Adapter key name.</summary>
    public string KeyName { get; set; } = "";
    /// <summary>Adapter display name.</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Adapter type.</summary>
    public string Type { get; set; } = "";
    /// <summary>True when the adapter is configurable.</summary>
    public bool IsConfigurable { get; set; }
    /// <summary>True when the adapter is supported on this station.</summary>
    public bool IsSupported { get; set; }
    /// <summary>True when the adapter is hidden in the UI.</summary>
    public bool Hidden { get; set; }
    /// <summary>True when arguments are shown in the step description.</summary>
    public bool ShowArgsInStepDescription { get; set; }
    /// <summary>Icon name for the adapter.</summary>
    public string IconName { get; set; } = "";
    /// <summary>Additional adapter properties.</summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>Module/code-module information for a step.</summary>
public class StepModuleInfo
{
    /// <summary>Step name.</summary>
    public string StepName { get; set; } = "";
    /// <summary>Adapter key name.</summary>
    public string AdapterName { get; set; } = "";
    /// <summary>Adapter display name.</summary>
    public string AdapterDisplayName { get; set; } = "";
    /// <summary>Module properties (adapter-specific).</summary>
    public Dictionary<string, object> ModuleProperties { get; init; } = new();
}

// ── Search Models ────────────────────────────────────────────────────────────

/// <summary>A single match from a sequence-content search.</summary>
public class SearchMatch
{
    /// <summary>File the match was found in.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Sequence the match was found in.</summary>
    public string SequenceName { get; set; } = "";
    /// <summary>Step group the match was found in.</summary>
    public string StepGroup { get; set; } = "";
    /// <summary>Step the match was found in.</summary>
    public string StepName { get; set; } = "";
    /// <summary>The matched text.</summary>
    public string MatchedText { get; set; } = "";
    /// <summary>What kind of element matched.</summary>
    public string MatchType { get; set; } = "";
    /// <summary>Property path of the match.</summary>
    public string PropertyPath { get; set; } = "";
}

/// <summary>Result of a sequence-content search.</summary>
public class SearchResult
{
    /// <summary>Total number of matches.</summary>
    public int TotalMatches { get; set; }
    /// <summary>The search pattern used.</summary>
    public string SearchPattern { get; set; } = "";
    /// <summary>Scope the search ran over.</summary>
    public string SearchIn { get; set; } = "";
    /// <summary>The matches found.</summary>
    public List<SearchMatch> Matches { get; init; } = new();
}

// ── Native Find/Replace Models (PropertyObject.Search) ───────────────────────

/// <summary>A single match returned by the native TestStand search engine.</summary>
public class FindMatch
{
    /// <summary>File the match was found in.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Property path of the match.</summary>
    public string PropertyPath { get; set; } = "";
    /// <summary>The matched text.</summary>
    public string MatchedText { get; set; } = "";
    /// <summary>Value type of the matched property.</summary>
    public string ValueType { get; set; } = "";
    /// <summary>True when this match was replaced.</summary>
    public bool Replaced { get; set; }
}

/// <summary>Result of a native find or find/replace operation across a file.</summary>
public class FindReplaceResult
{
    /// <summary>The search pattern used.</summary>
    public string Pattern { get; set; } = "";
    /// <summary>The replacement text, if this was a replace operation.</summary>
    public string? Replacement { get; set; }
    /// <summary>Total number of matches.</summary>
    public int TotalMatches { get; set; }
    /// <summary>Number of matches replaced.</summary>
    public int ReplacedCount { get; set; }
    /// <summary>Human-readable status message.</summary>
    public string StatusMessage { get; set; } = "";
    /// <summary>The matches found.</summary>
    public List<FindMatch> Matches { get; init; } = new();
}

// ── Adapter Module Configuration Models ──────────────────────────────────────

/// <summary>Result of configuring a step's code module via a typed adapter tool.</summary>
public class ModuleConfigResult
{
    /// <summary>Step that was configured.</summary>
    public string StepName { get; set; } = "";
    /// <summary>Adapter applied to the step.</summary>
    public string Adapter { get; set; } = "";
    /// <summary>Settings that were applied.</summary>
    public Dictionary<string, object> AppliedSettings { get; init; } = new();
}

// ── Thread Models ────────────────────────────────────────────────────────────

/// <summary>Information about an execution thread.</summary>
public class ThreadInfo
{
    /// <summary>Thread identifier.</summary>
    public string ThreadId { get; set; } = "";
    /// <summary>Thread index within the execution.</summary>
    public int ThreadIndex { get; set; }
    /// <summary>Thread run state.</summary>
    public string State { get; set; } = "";
    /// <summary>Name of the step the thread is on.</summary>
    public string CurrentStepName { get; set; } = "";
    /// <summary>Name of the sequence the thread is in.</summary>
    public string CurrentSequenceName { get; set; } = "";
    /// <summary>File the thread is executing.</summary>
    public string CurrentFilePath { get; set; } = "";
    /// <summary>Current call-stack depth.</summary>
    public int StackDepth { get; set; }
}

/// <summary>A single frame of an execution call stack.</summary>
public class CallStackFrame
{
    /// <summary>Depth of the frame (0 = innermost).</summary>
    public int Depth { get; set; }
    /// <summary>Sequence at this frame.</summary>
    public string SequenceName { get; set; } = "";
    /// <summary>File at this frame.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Step at this frame.</summary>
    public string StepName { get; set; } = "";
    /// <summary>Step group at this frame.</summary>
    public string StepGroup { get; set; } = "";
}

// ── Sequence Properties Model ────────────────────────────────────────────────

/// <summary>Editable properties of a sequence.</summary>
public class SequenceProperties
{
    /// <summary>Sequence name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Sequence description.</summary>
    public string Description { get; set; } = "";
    /// <summary>Sequence type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Whether execution goes to Cleanup on failure.</summary>
    public bool GotoCleanupOnFailure { get; set; }
    /// <summary>Whether result recording is disabled for the sequence.</summary>
    public bool DisableResults { get; set; }
    /// <summary>Failure action setting.</summary>
    public string FailureAction { get; set; } = "";
    /// <summary>Expression that computes the entry-point name.</summary>
    public string EntryPointNameExpression { get; set; } = "";
    /// <summary>Whether the entry point is shown for all windows.</summary>
    public bool ShowEntryPointForAllWindows { get; set; }
}

/// <summary>A step template available for insertion.</summary>
public class StepTemplateInfo
{
    /// <summary>Template name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Step type the template produces.</summary>
    public string StepType { get; set; } = "";
    /// <summary>Template description.</summary>
    public string Description { get; set; } = "";
}

// ── Workspace Models ─────────────────────────────────────────────────────────

/// <summary>Information about the open workspace.</summary>
public class WorkspaceInfo
{
    /// <summary>Workspace file path, or null when none is open.</summary>
    public string? WorkspacePath { get; set; }
    /// <summary>Sequence files referenced by the workspace.</summary>
    public List<string> SequenceFiles { get; init; } = new();
}

// ── Watch Expression Models ──────────────────────────────────────────────────

/// <summary>A user-managed watch expression.</summary>
public class WatchExpressionInfo
{
    /// <summary>Index of the watch in the list.</summary>
    public int Index { get; set; }
    /// <summary>The watched expression.</summary>
    public string Expression { get; set; } = "";
    /// <summary>Display label for the watch.</summary>
    public string Label { get; set; } = "";
    /// <summary>Last evaluated value, if any.</summary>
    public string? Value { get; set; }
    /// <summary>Value type, if known.</summary>
    public string? Type { get; set; }
}

// ── Callback Models ──────────────────────────────────────────────────────────

/// <summary>Information about a sequence-file callback.</summary>
public class CallbackInfo
{
    /// <summary>Callback name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Sequence assigned to the callback.</summary>
    public string AssignedSequence { get; set; } = "";
}

// ── File Properties Models ───────────────────────────────────────────────────

/// <summary>General properties of a sequence file.</summary>
public class FilePropertiesInfo
{
    /// <summary>File path.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>File comment, if any.</summary>
    public string? Comment { get; set; }
    /// <summary>File version, if any.</summary>
    public string? Version { get; set; }
    /// <summary>File GUID, if any.</summary>
    public string? FileGuid { get; set; }
    /// <summary>True when the file has unsaved changes.</summary>
    public bool IsModified { get; set; }
    /// <summary>Number of sequences in the file.</summary>
    public int NumSequences { get; set; }
}

// ── Array Variable Models ────────────────────────────────────────────────────

/// <summary>A single element of an array variable.</summary>
public class ArrayElementInfo
{
    /// <summary>Element index.</summary>
    public int Index { get; set; }
    /// <summary>Element value.</summary>
    public object? Value { get; set; }
    /// <summary>Element type.</summary>
    public string Type { get; set; } = "";
}

// ── Module Parameter Models ──────────────────────────────────────────────────

/// <summary>A single parameter of a step's code module.</summary>
public class ModuleParameterInfo
{
    /// <summary>Parameter name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Parameter value, if any.</summary>
    public string? Value { get; set; }
    /// <summary>Parameter type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Parameter direction.</summary>
    public string Direction { get; set; } = "";
    /// <summary>Parameter data type.</summary>
    public string DataType { get; set; } = "";
}
