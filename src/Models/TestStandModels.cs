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
    public bool Enabled { get; set; } = true;
    /// <summary>Step group the step belongs to: "Setup", "Main", or "Cleanup".</summary>
    public string StepGroup { get; set; } = "";
    public List<StepInfo> SubSteps { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new();
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
