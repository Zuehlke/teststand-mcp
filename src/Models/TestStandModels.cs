using System;
using System.Collections.Generic;

namespace TestStandMCP.Models;

// ── Execution Models ─────────────────────────────────────────────────────────

public class ExecutionInfo
{
    public string ExecutionId { get; set; } = "";
    public string SequenceFilePath { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Result { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

public class ExecutionResult
{
    public string ExecutionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Result { get; set; } = "";
    public double ElapsedSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public List<StepResult> StepResults { get; set; } = new();
}

public class StepResult
{
    public string StepName { get; set; } = "";
    public string StepType { get; set; } = "";
    public string Status { get; set; } = "";
    public string Result { get; set; } = "";
    public double? NumericLimit { get; set; }
    public double? MeasuredValue { get; set; }
    public string? ErrorMessage { get; set; }
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
    public string Name { get; set; } = "";
    public string StepType { get; set; } = "";
    public string? Adapter { get; set; }
    public string? Comment { get; set; }
    public string? Expression { get; set; }
    public string? ExpressionType { get; set; }
    /// <summary>Target sequence for a SequenceCall step (optional).</summary>
    public string? TargetSequenceName { get; set; }
    /// <summary>Target sequence file (empty/omitted = same/current file).</summary>
    public string? TargetSequenceFile { get; set; }
}

public class BulkInsertResult
{
    public string SequenceName { get; set; } = "";
    public string StepGroup { get; set; } = "";
    public int InsertedCount { get; set; }
    public int CommentsSet { get; set; }
    public int ExpressionsSet { get; set; }
    public int TargetsSet { get; set; }
    public List<string> InsertedSteps { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

// ── Sequence File Models ─────────────────────────────────────────────────────

public class SequenceFileInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<SequenceInfo> Sequences { get; set; } = new();
    public List<VariableInfo> FileGlobals { get; set; } = new();
    public List<VariableInfo> StationGlobals { get; set; } = new();
    public string? Description { get; set; }
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
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int SequenceCount { get; set; }
    public List<string> Sequences { get; set; } = new();
}

public class SequenceInfo
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<StepInfo> Steps { get; set; } = new();
    public List<VariableInfo> Locals { get; set; } = new();
    public List<ParameterInfo> Parameters { get; set; } = new();
}

public class StepInfo
{
    public string Name { get; set; } = "";
    public string StepType { get; set; } = "";
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

public class VariableInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public object? Value { get; set; }
    public string? Description { get; set; }
    public bool IsArray { get; set; }
    public int ArraySize { get; set; }
}

public class ParameterInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public object? DefaultValue { get; set; }
    public string Direction { get; set; } = "Input"; // Input, Output, InOut
    public string? Description { get; set; }
}

public class PropertyValue
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public object? Value { get; set; }
    public string? LookupString { get; set; }
}

// ── Report Models ────────────────────────────────────────────────────────────

public class ReportInfo
{
    public string ExecutionId { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string OverallResult { get; set; } = "";
    public int TotalSteps { get; set; }
    public int PassedSteps { get; set; }
    public int FailedSteps { get; set; }
    public int SkippedSteps { get; set; }
    public double TotalTime { get; set; }
}

// ── Station / Engine Models ──────────────────────────────────────────────────

public class StationInfo
{
    public string StationName { get; set; } = "";
    public string TestStandVersion { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsLicensed { get; set; }
    public List<string> LoadedSequenceFiles { get; set; } = new();
    public List<ExecutionInfo> ActiveExecutions { get; set; } = new();
}

// ── Batch Models ─────────────────────────────────────────────────────────────

public class BatchInfo
{
    public string BatchId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "";
    public List<ExecutionInfo> Executions { get; set; } = new();
}

// ── UUT / DUT Models ─────────────────────────────────────────────────────────

public class UutInfo
{
    public string SerialNumber { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string BatchSerialNumber { get; set; } = "";
    public string Result { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<StepResult> MeasurementResults { get; set; } = new();
}

// ── Adapter / Instrument Models ──────────────────────────────────────────────

public class AdapterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsLoaded { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}

// ── Sequence Editor Models ────────────────────────────────────────────────────

public class SequenceEditorInfo
{
    public bool IsRunning { get; set; }
    public int ProcessId { get; set; }
    public string EditorPath { get; set; } = "";
    public string MainWindowTitle { get; set; } = "";
}

// ── Type Palette Models ──────────────────────────────────────────────────────

public class TypePaletteInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int StepTypeCount { get; set; }
    public List<string> StepTypeNames { get; set; } = new();
}

public class StepTypeInfo
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string PaletteFile { get; set; } = "";
    public string? AdapterName { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class DataTypeInfo
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string BaseType { get; set; } = "";
    public bool IsArray { get; set; }
    public List<DataTypePropertyInfo> Properties { get; set; } = new();
}

public class DataTypePropertyInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public object? DefaultValue { get; set; }
    public string? Description { get; set; }
}

// ── Log / Trace Models ───────────────────────────────────────────────────────

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Source { get; set; }
    public string? ExecutionId { get; set; }
}

public class AnalyzerMessage
{
    public string Severity { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Location { get; set; } = "";
    public string SequenceName { get; set; } = "";
    public string StepName { get; set; } = "";
}

/// <summary>Aggregated result of a sequence-analyzer run, incl. severity counts.</summary>
public class AnalyzerResult
{
    public string FilePath { get; set; } = "";
    public int TotalMessages { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InformationCount { get; set; }
    public List<AnalyzerMessage> Messages { get; set; } = new();
}

// ── User / Privilege Models ──────────────────────────────────────────────────

public class UserInfo
{
    public string LoginName { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsGroup { get; set; }
    public List<string> GroupMemberships { get; set; } = new();
}

// ── Output Message Models ────────────────────────────────────────────────────

public class OutputMessageInfo
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "";
    public double TimeInSeconds { get; set; }
}

// ── Search Directory Models ──────────────────────────────────────────────────

public class SearchDirectoryInfo
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Disabled { get; set; }
    public bool SearchSubdirectories { get; set; }
}

// ── Data-Type Field Models ───────────────────────────────────────────────────

public class TypeFieldInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
}

// ── CSV Stream Models ────────────────────────────────────────────────────────

public class CsvReadResult
{
    public string FilePath { get; set; } = "";
    public int LineCount { get; set; }
    public List<string> Lines { get; set; } = new();
}

// ── Engine / Station Models ──────────────────────────────────────────────────

public class EnginePaths
{
    public string BinDirectory { get; set; } = "";
    public string ConfigDirectory { get; set; } = "";
    public string TestStandDirectory { get; set; } = "";
    public string VersionString { get; set; } = "";
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public string StationId { get; set; } = "";
    public string ComputerName { get; set; } = "";
}

public class StationOptionsInfo
{
    public bool TracingEnabled { get; set; }
    public bool BreakpointsEnabled { get; set; }
    public bool DisableResults { get; set; }
    public bool AlwaysGotoCleanupOnFailure { get; set; }
    public bool BreakOnRte { get; set; }
    public string StationId { get; set; } = "";
    public string ProcessModelPath { get; set; } = "";
}

public class ExpressionCheckResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = "";
}

// ── Undo/Redo Models ─────────────────────────────────────────────────────────

public class UndoItemInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class UndoStackInfo
{
    public bool CanUndo { get; set; }
    public bool CanRedo { get; set; }
    public int NumUndoItems { get; set; }
    public int NumRedoItems { get; set; }
    public string? FilePath { get; set; }
    public List<UndoItemInfo> UndoItems { get; set; } = new();
    public List<UndoItemInfo> RedoItems { get; set; } = new();
}

// ── Sequence File Comparison Models ─────────────────────────────────────────

public class SequenceFileDiff
{
    public string File1 { get; set; } = "";
    public string File2 { get; set; } = "";
    public int TotalDifferences { get; set; }
    public List<string> SequencesOnlyInFile1 { get; set; } = new();
    public List<string> SequencesOnlyInFile2 { get; set; } = new();
    public List<SequenceDiff> ModifiedSequences { get; set; } = new();
}

public class SequenceDiff
{
    public string SequenceName { get; set; } = "";
    public List<StepDiff> StepDiffs { get; set; } = new();
    public List<string> LocalVariableDiffs { get; set; } = new();
    public List<string> ParameterDiffs { get; set; } = new();
    public List<string> PropertyDiffs { get; set; } = new();
}

public class StepDiff
{
    public string DiffType { get; set; } = ""; // Added, Removed, Modified
    public string StepName { get; set; } = "";
    public string StepGroup { get; set; } = "";
    public string StepType { get; set; } = "";
    public List<string> ChangedProperties { get; set; } = new();
}

// ── Sync Manager Models ──────────────────────────────────────────────────────

public class SyncObjectInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public Dictionary<string, object> Properties { get; set; } = new();
}

// ── Advanced Adapter Models ──────────────────────────────────────────────────

public class AdapterDetailInfo
{
    public string KeyName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsConfigurable { get; set; }
    public bool IsSupported { get; set; }
    public bool Hidden { get; set; }
    public bool ShowArgsInStepDescription { get; set; }
    public string IconName { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class StepModuleInfo
{
    public string StepName { get; set; } = "";
    public string AdapterName { get; set; } = "";
    public string AdapterDisplayName { get; set; } = "";
    public Dictionary<string, object> ModuleProperties { get; set; } = new();
}

// ── Search Models ────────────────────────────────────────────────────────────

public class SearchMatch
{
    public string FilePath { get; set; } = "";
    public string SequenceName { get; set; } = "";
    public string StepGroup { get; set; } = "";
    public string StepName { get; set; } = "";
    public string MatchedText { get; set; } = "";
    public string MatchType { get; set; } = "";
    public string PropertyPath { get; set; } = "";
}

public class SearchResult
{
    public int TotalMatches { get; set; }
    public string SearchPattern { get; set; } = "";
    public string SearchIn { get; set; } = "";
    public List<SearchMatch> Matches { get; set; } = new();
}

// ── Native Find/Replace Models (PropertyObject.Search) ───────────────────────

/// <summary>A single match returned by the native TestStand search engine.</summary>
public class FindMatch
{
    public string FilePath { get; set; } = "";
    public string PropertyPath { get; set; } = "";
    public string MatchedText { get; set; } = "";
    public string ValueType { get; set; } = "";
    public bool Replaced { get; set; }
}

/// <summary>Result of a native find or find/replace operation across a file.</summary>
public class FindReplaceResult
{
    public string Pattern { get; set; } = "";
    public string? Replacement { get; set; }
    public int TotalMatches { get; set; }
    public int ReplacedCount { get; set; }
    public string StatusMessage { get; set; } = "";
    public List<FindMatch> Matches { get; set; } = new();
}

// ── Adapter Module Configuration Models ──────────────────────────────────────

/// <summary>Result of configuring a step's code module via a typed adapter tool.</summary>
public class ModuleConfigResult
{
    public string StepName { get; set; } = "";
    public string Adapter { get; set; } = "";
    public Dictionary<string, object> AppliedSettings { get; set; } = new();
}

// ── Thread Models ────────────────────────────────────────────────────────────

public class ThreadInfo
{
    public string ThreadId { get; set; } = "";
    public int ThreadIndex { get; set; }
    public string State { get; set; } = "";
    public string CurrentStepName { get; set; } = "";
    public string CurrentSequenceName { get; set; } = "";
    public string CurrentFilePath { get; set; } = "";
    public int StackDepth { get; set; }
}

public class CallStackFrame
{
    public int Depth { get; set; }
    public string SequenceName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string StepName { get; set; } = "";
    public string StepGroup { get; set; } = "";
}

// ── Sequence Properties Model ────────────────────────────────────────────────

public class SequenceProperties
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public bool GotoCleanupOnFailure { get; set; }
    public bool DisableResults { get; set; }
    public string FailureAction { get; set; } = "";
    public string EntryPointNameExpression { get; set; } = "";
    public bool ShowEntryPointForAllWindows { get; set; }
}

public class StepTemplateInfo
{
    public string Name { get; set; } = "";
    public string StepType { get; set; } = "";
    public string Description { get; set; } = "";
}

// ── Workspace Models ─────────────────────────────────────────────────────────

public class WorkspaceInfo
{
    public string? WorkspacePath { get; set; }
    public List<string> SequenceFiles { get; set; } = new();
}

// ── Watch Expression Models ──────────────────────────────────────────────────

public class WatchExpressionInfo
{
    public int Index { get; set; }
    public string Expression { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Value { get; set; }
    public string? Type { get; set; }
}

// ── Callback Models ──────────────────────────────────────────────────────────

public class CallbackInfo
{
    public string Name { get; set; } = "";
    public string AssignedSequence { get; set; } = "";
}

// ── File Properties Models ───────────────────────────────────────────────────

public class FilePropertiesInfo
{
    public string FilePath { get; set; } = "";
    public string? Comment { get; set; }
    public string? Version { get; set; }
    public string? FileGuid { get; set; }
    public bool IsModified { get; set; }
    public int NumSequences { get; set; }
}

// ── Array Variable Models ────────────────────────────────────────────────────

public class ArrayElementInfo
{
    public int Index { get; set; }
    public object? Value { get; set; }
    public string Type { get; set; } = "";
}

// ── Module Parameter Models ──────────────────────────────────────────────────

public class ModuleParameterInfo
{
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public string Type { get; set; } = "";
    public string Direction { get; set; } = "";
    public string DataType { get; set; } = "";
}
