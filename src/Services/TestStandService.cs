using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using TestStandMCP.Models;
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
    /// <summary>Inserts a new local variable into the specified sequence.</summary>
    Task InsertLocalVariableAsync(string filePath, string sequenceName,
        string variableName, string dataType, string? defaultValue = null);
    /// <summary>Sets the comment (description) on a local variable in the specified sequence.</summary>
    Task SetLocalVariableCommentAsync(string filePath, string sequenceName,
        string variableName, string comment);
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
    /// <summary>Sets the typed value of a property within a sequence file or sequence.</summary>
    Task SetPropertyValueAsync(string filePath, string? sequenceName,
        string propertyName, string valueType, string? value);
    /// <summary>Deletes a sub-property from a file global or sequence local variable container.</summary>
    Task DeleteSubPropertyAsync(string filePath, string? sequenceName,
        string propertyName);
    /// <summary>Returns all file-global variables defined in the given sequence file.</summary>
    Task<List<VariableInfo>> GetFileGlobalsAsync(string sequenceFilePath);
    /// <summary>Returns all station global variables for the connected engine.</summary>
    Task<List<VariableInfo>> GetStationGlobalsAsync();
    /// <summary>Recursively walks a property object (StationGlobals or a sequence file's
    /// FileGlobals, optionally descending to a sub-path) into a <see cref="PropertyNode"/>
    /// tree. Hidden subproperties are included by default and annotated via
    /// <see cref="PropertyNode.IsHidden"/>; arrays and containers are expanded.</summary>
    Task<PropertyNode> GetPropertyTreeAsync(string root, string? filePath, string? lookupString,
        int maxDepth, bool includeHidden, int maxArrayElements);
    /// <summary>Sets the value of a file-global variable in the given sequence file.</summary>
    Task SetFileGlobalAsync(string sequenceFilePath, string variableName, object value);
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

    // Sequence Analyzer
    /// <summary>Runs the TestStand Sequence Analyzer on the given file and returns any messages.</summary>
    Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath);

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
    Task InsertSequenceParameterAsync(string filePath, string sequenceName, string paramName,
        string dataType, string direction = "Input", string? defaultValue = null,
        bool? passByReference = null);
    /// <summary>Deletes the specified local variable from the given sequence.</summary>
    Task DeleteLocalVariableAsync(string filePath, string sequenceName, string variableName);
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
    /// <summary>Configures the loop settings for the specified step.</summary>
    Task SetStepLoopAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string loopType, string? initExpr = null,
        string? whileExpr = null, string? incExpr = null);
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
    /// <summary>Configures a step to call a .NET method by specifying the assembly, class, and method.</summary>
    Task<ModuleConfigResult> ConfigureDotNetModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string assemblyPath,
        string className, string methodName, bool save = true);
    /// <summary>Configures a step to call a native DLL function.</summary>
    Task<ModuleConfigResult> ConfigureDllModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string dllPath,
        string functionName, bool save = true);
    /// <summary>Configures a step to call a LabVIEW VI.</summary>
    Task<ModuleConfigResult> ConfigureLabViewModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string viPath,
        bool save = true);
    /// <summary>Configures a step to call a Python function.</summary>
    Task<ModuleConfigResult> ConfigurePythonModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string modulePath,
        string functionName, bool save = true);
    /// <summary>Configures a SequenceCall step to call the specified target sequence.</summary>
    Task<ModuleConfigResult> ConfigureSequenceCallModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        string targetSequenceName, string targetSequenceFile = "", bool save = true);

    // ── Sequence Analyzer (detailed) ─────────────────────────────────────────
    /// <summary>Runs the Sequence Analyzer and returns a detailed result filtered by minimum
    /// severity and optionally grouped (by "severity", "rule", or "none" for a flat list).</summary>
    Task<AnalyzerResult> RunSequenceAnalyzerDetailedAsync(string filePath,
        string minSeverity = "Information", string groupBy = "severity");

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
    private dynamic? _engineMgr;      // EngineManager
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

        _engineThread = new Thread(() => EngineThreadProc(enginePath))
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
    /// </summary>
    private void EngineThreadProc(string? enginePath)
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
                if (string.IsNullOrWhiteSpace(spec.Name) || string.IsNullOrWhiteSpace(spec.StepType))
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

                // Optional comment
                if (!string.IsNullOrEmpty(spec.Comment))
                {
                    bool ok = false;
                    try { ((dynamic)step).Comment = spec.Comment; ok = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set step comment via Comment property."); }
                    if (!ok) try { ((NiStep)(object)step).AsPropertyObject().Comment = spec.Comment; ok = true; } catch (Exception ex) { _logger.LogDebug(ex, "Failed to set step comment via AsPropertyObject().Comment."); }
                    if (ok) result.CommentsSet++;
                    else    result.Warnings.Add($"Comment not set on '{spec.Name}'.");
                }

                // Optional expression
                if (!string.IsNullOrEmpty(spec.Expression))
                {
                    try
                    {
                        switch ((spec.ExpressionType ?? "Statement").ToLowerInvariant())
                        {
                            case "pre":    step.PreExpression    = spec.Expression; break;
                            case "post":   step.PostExpression   = spec.Expression; break;
                            case "status": step.StatusExpression = spec.Expression; break;
                            default:
                                // Statement steps: the primary expression home is the Post Expression.
                                step.PostExpression = spec.Expression;
                                break;
                        }
                        result.ExpressionsSet++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Expression not set on '{spec.Name}': {ex.Message}");
                    }
                }

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
                    int runState = GetExecutionRunState((object)exec);
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
        string propertyName, string valueType, string? value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            var root = ResolveValueContainer(sf, sequenceName);

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
                parent.NewSubProperty(leaf, (NiPropValueTypes)MapPropValueType(valueType),
                    false, "", 0);
            }

            switch (valueType.ToLowerInvariant())
            {
                case "container":
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
                    root.SetValNumber(propertyName, 0, double.Parse(value ?? "0",
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default: // string
                    root.SetValString(propertyName, 0, value ?? "");
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
        string? lookupString, int maxDepth, bool includeHidden, int maxArrayElements)
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

            // Optionally descend to a sub-path before walking.
            if (!string.IsNullOrWhiteSpace(lookupString))
            {
                start     = (NiPropertyObject)(object)start.GetPropertyObject(lookupString, 0);
                rootLabel = lookupString!;
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
                    children.Add(BuildPropertyNode(elem, $"[{i}]", depth + 1, maxDepth,
                        includeHidden, maxArrayElements, ref budget));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Array element {Index} of '{Name}' failed.", i, name); }
            }
            if (cap < numElem) node.Truncated = true;
            node.Children = children;
            return node;
        }

        // Scalar leaf — read the value and infer its kind (mirrors TryGetValue's order).
        try { node.Value = po.GetValNumber("", 0);  node.ValueType = "Number";  return node; } catch { }
        try { node.Value = po.GetValBoolean("", 0); node.ValueType = "Boolean"; return node; } catch { }
        try { node.Value = po.GetValString("", 0);  node.ValueType = "String";  return node; } catch { }
        node.ValueType = "Empty";
        return node;
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

            // Infer property type from value
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

            if (!PropertyExists(fg, variableName))
                fg.NewSubProperty(variableName, (NiPropValueTypes)propType, false, "", 0);

            SetPropertyValueByType(fg, variableName, value?.ToString() ?? "", propType);
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
            string rawType = dataType.ToLowerInvariant().Trim();
            bool   isArray = rawType.EndsWith("[]") || rawType.StartsWith("array:");
            string baseDataType = isArray
                ? rawType.Replace("[]", "").Replace("array:", "").Trim()
                : rawType;
            // PropValType: String=1, Boolean=2, Number=3
            int propType = baseDataType switch
            {
                "number" or "double" or "float" or "int" or "integer" => 3,
                "boolean" or "bool"                                   => 2,
                _                                                     => 1
            };
            var fg2 = GetFileGlobals(sf);
            fg2.NewSubProperty(variableName, (NiPropValueTypes)propType, isArray, "", 0);
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
        string variableName, string dataType, string? defaultValue = null)
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
                default:        propType = 4; typeNameParam = baseDataType; break;
            }

            // NewSubProperty(lookupString, valueType, asArray, typeName, options)
            seq.Locals.NewSubProperty(variableName, (NiPropValueTypes)propType, isArray, typeNameParam, 0);

            if (defaultValue != null)
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
            var prop = seq.Locals.GetPropertyObject(variableName, 0);
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

            dynamic step = seq.GetStepByName(stepName, (NiStepGroups)sgValue);

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

            dynamic step    = seq.GetStepByName(stepName, (NiStepGroups)sgValue);

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

            dynamic step = seq.GetStepByName(stepName, (NiStepGroups)sgValue);

            // Access Module via dynamic COM dispatch so VIPath persists.
            dynamic lvModule = step.Module;
            lvModule.VIPath = modulePath;

            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    /// <inheritdoc/>
    public async Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath)
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
            var (binDir, publicDir, productVersion) = ResolveAnalyzerLocations();
            return RunAnalysisViaApp(filePath, binDir, publicDir, productVersion, Log, Flush);
        });
    }

    private static List<AnalyzerMessage> RunAnalysisViaApp(
        string filePath,
        string binDir,
        string publicDir,
        string productVersion,
        Action<string> Log,
        Action Flush)
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
            throw new InvalidOperationException($"AnalyzerApp.exe not found at: {analyzerExe}");

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
        // psi.Environment is pre-seeded with this process's environment. Values are derived from the
        // OS (NOT GetEnvironmentVariable, which returns null when a variable is missing in *this*
        // process) and set/overwritten. ProgramFiles(x86) is mandatory; the rest harden common
        // lvrt/Windows lookups. (UseShellExecute=false above is required for psi.Environment to apply.)
        void Ensure(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) psi.Environment[key] = value;
        }
        Ensure("ProgramFiles(x86)",       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Ensure("ProgramFiles",            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Ensure("ProgramData",             Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("ALLUSERSPROFILE",         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("CommonProgramFiles",      Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        Ensure("CommonProgramFiles(x86)", Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        Ensure("ComSpec",                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        Ensure("TMP",                     Path.GetTempPath());
        Ensure("TEMP",                    Path.GetTempPath());
        Ensure("NUMBER_OF_PROCESSORS",    Environment.ProcessorCount.ToString());

        Log($"Child env normalized — ProgramFiles(x86)=" +
            (psi.Environment.TryGetValue("ProgramFiles(x86)", out var pf86) && !string.IsNullOrEmpty(pf86)
                ? pf86 : "(MISSING!)"));

        Log($"Launching: {analyzerExe} {psi.Arguments}");
        Flush();

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start AnalyzerApp.exe process.");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        bool exited   = proc.WaitForExit(120_000); // 2 min timeout

        if (!exited)
        {
            try { proc.Kill(); } catch (Exception) { /* best-effort: kill timed-out AnalyzerApp.exe process — intentionally ignored */ }
            throw new InvalidOperationException("AnalyzerApp.exe timed out after 120 seconds.");
        }

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
    /// Resolves the TestStand <c>Bin</c> directory, the TestStand <c>Public</c> directory and the
    /// product-version string for the *currently connected* engine, so the Sequence Analyzer always
    /// runs the AnalyzerApp.exe matching the running TestStand — never a hard-coded release. Falls
    /// back to the TESTSTANDBIN / TESTSTANDPUBLIC environment variables, then to a newest-first scan
    /// of the National Instruments install root.
    /// </summary>
    private (string BinDir, string PublicDir, string ProductVersion) ResolveAnalyzerLocations()
    {
        string binDir = "";
        string publicDir = "";
        string productVersion = "";

        // 1. Ask the connected engine — this is the exact running version.
        if (_engine != null)
        {
            binDir = GetEngineProperty<string>("BinDirectory") ?? "";
            productVersion = GetEngineProperty<string>("VersionString") ?? "";
            try { publicDir = (string)((dynamic)_engine!).GetTestStandPath((object)4); } // 4 = TestStandPublic
            catch (Exception ex) { _logger.LogDebug(ex, "Engine GetTestStandPath(TestStandPublic) failed."); }
        }

        // 2. Environment variables exported by the TestStand installer.
        if (string.IsNullOrEmpty(binDir))
            binDir = Environment.GetEnvironmentVariable("TESTSTANDBIN") ?? "";
        if (string.IsNullOrEmpty(publicDir))
            publicDir = Environment.GetEnvironmentVariable("TESTSTANDPUBLIC") ?? "";

        // 3. COM registration of the Engine coclass — points at the actively registered engine's Bin.
        if (string.IsNullOrEmpty(binDir) || !File.Exists(Path.Combine(binDir, "AnalyzerApp.exe")))
        {
            var fromReg = FindTestStandBinFromRegistry();
            if (fromReg != null) binDir = fromReg;
        }

        // 4. Last resort: newest installed TestStand whose Bin holds AnalyzerApp.exe.
        if (string.IsNullOrEmpty(binDir) || !File.Exists(Path.Combine(binDir, "AnalyzerApp.exe")))
        {
            var found = FindNewestTestStandBin();
            if (found != null) binDir = found;
        }

        return (binDir, publicDir, productVersion);
    }

    /// <summary>
    /// Scans the standard National Instruments install roots for the newest installed TestStand
    /// whose <c>Bin</c> directory contains AnalyzerApp.exe. Returns null when none is found.
    /// </summary>
    private static string? FindNewestTestStandBin()
    {
        foreach (var pf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var niDir = Path.Combine(pf, "National Instruments");
            if (!Directory.Exists(niDir)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(niDir, "TestStand*")
                             .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var bin = Path.Combine(dir, "Bin");
                    if (File.Exists(Path.Combine(bin, "AnalyzerApp.exe"))) return bin;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Install root not enumerable on this station — try the next one.
            }
        }
        return null;
    }

    /// <summary>
    /// Reads the registered TestStand engine's Bin directory from the COM registration of the
    /// Engine coclass (CLSID <c>{B2794EF6-C0B6-11D0-939C-0020AF68E893}</c>). Its
    /// <c>InprocServer32</c> default value is the full path to the engine DLL, whose directory is
    /// the TestStand Bin folder. Uses the 32-bit registry view because the TestStand engine is a
    /// 32-bit (x86) COM server. Returns null when the key is missing or AnalyzerApp.exe is absent.
    /// </summary>
    private static string? FindTestStandBinFromRegistry()
    {
        const string engineInprocKey =
            @"CLSID\{B2794EF6-C0B6-11D0-939C-0020AF68E893}\InprocServer32";
        try
        {
            using var hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32);
            using var key = hkcr.OpenSubKey(engineInprocKey);
            // (Default) value = full path to the engine DLL; strip any stray surrounding quotes.
            var dllPath = (key?.GetValue(null) as string)?.Trim().Trim('"');
            if (string.IsNullOrEmpty(dllPath)) return null;

            var bin = Path.GetDirectoryName(dllPath);
            if (!string.IsNullOrEmpty(bin) && File.Exists(Path.Combine(bin, "AnalyzerApp.exe")))
                return bin;
        }
        catch (Exception)
        {
            // Registry not readable / key absent on this station — fall through to the directory scan.
        }
        return null;
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
            var srcSf = _loadedSequenceFiles.TryGetValue(sourceFilePath, out var cachedSrc)
                ? cachedSrc
                : _engine!.GetSequenceFileEx(sourceFilePath, 0, (NiConflictHandler)4);

            string destPath = targetFilePath ?? sourceFilePath;
            var dstSf = string.Equals(destPath, sourceFilePath, StringComparison.OrdinalIgnoreCase)
                ? srcSf
                : (_loadedSequenceFiles.TryGetValue(destPath, out var cachedDst)
                    ? cachedDst
                    : _engine!.GetSequenceFileEx(destPath, 0, (NiConflictHandler)4));

            // Get source sequence
            dynamic srcSeq = srcSf.GetSequenceByName(sourceSequenceName);

            // Create a new sequence from the source using CopySequence if available,
            // or fall back to creating a new one and copying properties manually.
            dynamic newSeq;
            try
            {
                // Try CopySequence API (TestStand 2016+)
                newSeq = ((dynamic)srcSf).CopySequence(srcSeq);
            }
            catch
            {
                newSeq = _engine!.NewSequence();
            }

            newSeq.Name = newSequenceName;
            dstSf.InsertSequence(newSeq);

            if (!string.IsNullOrEmpty(destPath))
                dstSf.Save(destPath);
            if (!_loadedSequenceFiles.ContainsKey(destPath))
                _loadedSequenceFiles[destPath] = dstSf;

            return newSequenceName;
        });
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (_engine == null)
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
                        _comFlags, null, propObj, new object[] { "", i, 0 });

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
        if (value is double d)
            propBlock.SetValNumber(name, 0, d);
        else if (value is bool b)
            propBlock.SetValBoolean(name, 0, b);
        else
            propBlock.SetValString(name, 0, value?.ToString() ?? "");
    }

    private object? TryGetValue(dynamic prop)
    {
        try { return (double)prop.GetValNumber("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as number."); }
        try { return (bool)prop.GetValBoolean("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as boolean."); }
        try { return (string)prop.GetValString("", 0); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to read property value as string."); }
        return null;
    }

    private T? GetEngineProperty<T>(string propName)
    {
        try
        {
            return (T)((object)_engine!).GetType().InvokeMember(
                propName, _comFlags, null, _engine, null);
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
                ComputerName      = GetEngineProperty<string>("ComputerName") ?? Environment.MachineName
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
        string? defaultValue = null, bool? passByReference = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            int propType = MapDataType(dataType);
            seq.Parameters.NewSubProperty(paramName, (object)propType, false, "", 0);

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

            if (defaultValue != null)
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

                    try { name = (string)iType.InvokeMember("Name", _comFlags, null, item, null); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read template Name at index {Index}.", i); }

                    // StepType: step.StepType is an object; get its Name property
                    try
                    {
                        var stObj = iType.InvokeMember("StepType", _comFlags, null, item, null);
                        if (stObj != null)
                            stepType = stObj.GetType().InvokeMember("Name", _comFlags, null, stObj, null)?.ToString() ?? "";
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read StepType for template '{Name}'.", name); }

                    try { desc = (string)iType.InvokeMember("Description", _comFlags, null, item, null); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to read Description for template '{Name}'.", name); }
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
        string? initExpr = null, string? whileExpr = null, string? incExpr = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);
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
        string className, string methodName, bool save = true)
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
            });

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureDllModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string dllPath,
        string functionName, bool save = true)
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
            });

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureLabViewModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string viPath,
        bool save = true)
        => ConfigureModuleAsync(filePath, sequenceName, stepGroup, stepName, "LabVIEW", save,
            mod =>
            {
                var applied = new Dictionary<string, object>();
                if (TrySetModuleProp(mod, "VIPath", viPath) ||
                    TrySetModuleProp(mod, "ModulePath", viPath))
                    applied["viPath"] = viPath;
                return applied;
            });

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigurePythonModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string modulePath,
        string functionName, bool save = true)
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
            });

    /// <inheritdoc/>
    public Task<ModuleConfigResult> ConfigureSequenceCallModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName,
        string targetSequenceName, string targetSequenceFile = "", bool save = true)
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
                return applied;
            });

    /// <summary>
    /// Shared driver for the typed adapter-configuration tools: resolves the step,
    /// switches its adapter (when needed), applies the adapter-specific settings via
    /// the supplied callback, and saves the file.
    /// </summary>
    private async Task<ModuleConfigResult> ConfigureModuleAsync(string filePath,
        string sequenceName, string stepGroup, string stepName, string adapterKey,
        bool save, Func<dynamic, Dictionary<string, object>> apply)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            // Ensure the step uses the requested adapter before configuring its module.
            string resolvedKey = ResolveAdapterKeyName(adapterKey);
            string currentKey  = TryGetString(step, "AdapterKeyName");
            if (!string.Equals(currentKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                try { step.ChangeAdapter((object)resolvedKey); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to change step adapter to '{Adapter}'.", resolvedKey); }
            }

            dynamic mod = step.Module;
            var applied = apply(mod);

            if (save)
            {
                SaveSequenceFileWithRetry((NiSequenceFile)(object)sf, filePath);
                _loadedSequenceFiles[filePath] = sf;
            }

            return new ModuleConfigResult
            {
                StepName       = stepName,
                Adapter        = resolvedKey,
                AppliedSettings = applied
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

    // ── Sequence Analyzer (detailed) ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnalyzerResult> RunSequenceAnalyzerDetailedAsync(string filePath,
        string minSeverity = "Information", string groupBy = "severity")
    {
        var messages = await RunSequenceAnalyzerAsync(filePath);

        int Rank(string s) => s switch
        {
            "Error" => 3, "Warning" => 2, "Information" => 1, _ => 0
        };
        int threshold = Rank(minSeverity);
        var filtered = messages.Where(m => Rank(m.Severity) >= threshold).ToList();

        bool grouped = AnalyzerGrouping.IsGrouped(groupBy);

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
                                   : new List<AnalyzerMessageGroup>()
        };
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
                    string fname = (string)tObj.GetType().InvokeMember(
                        "GetNthSubPropertyName", _comFlags, null, tObj, new object[] { "", i, 0 });
                    dynamic fp = tObj.GetType().InvokeMember(
                        "GetNthSubProperty", _comFlags, null, tObj, new object[] { "", i, 0 });
                    result.Add(new TypeFieldInfo { Name = fname, DataType = TryGetString(fp, "TypeName") });
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
        NiStep     step = seq.GetStepByName(stepName, (NiStepGroups)ParseStepGroup(stepGroup));
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
        NiStep     step = seq.GetStepByName(stepName, (NiStepGroups)ParseStepGroup(stepGroup));
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

    private static int MapDataType(string dataType) => dataType.ToLowerInvariant() switch
    {
        "string"  => 1,
        "boolean" => 2,
        "bool"    => 2,
        "number"  => 3,
        "double"  => 3,
        "float"   => 3,
        "int"     => 3,
        "integer" => 3,
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
                propBlock.SetValString(name, 0, value);
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
                        _comFlags, null, propObj, new object[] { "", i, 0 });
                    var pi = new ParameterInfo
                    {
                        Name     = (string)prop.Name,
                        DataType = TryGetString(prop, "TypeName")
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

            // FileDiffer.exe ships in the connected engine's Bin directory — never hard-code a release.
            var (binDir, _, _) = ResolveAnalyzerLocations();
            string differExe = !string.IsNullOrEmpty(binDir)
                ? Path.Combine(binDir, "FileDiffer.exe")
                : "FileDiffer.exe";
            if (!System.IO.File.Exists(differExe))
                throw new InvalidOperationException($"FileDiffer.exe not found at: {differExe}");

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
    /// Normalises a child <see cref="System.Diagnostics.ProcessStartInfo"/> environment so 32-bit NI
    /// tools (AnalyzerApp.exe, FileDiffer.exe) and the LabVIEW RTE they may load find the system
    /// variables they require — notably ProgramFiles(x86), whose absence crashes lvrt.dll with
    /// 0xC0000409. The MCP host can inherit a heavily reduced environment, so these are set from the
    /// OS regardless of this process's own environment. Requires UseShellExecute=false.
    /// </summary>
    private static void ApplyTestStandToolChildEnv(System.Diagnostics.ProcessStartInfo psi)
    {
        void Ensure(string key, string? value) { if (!string.IsNullOrEmpty(value)) psi.Environment[key] = value; }
        Ensure("ProgramFiles(x86)",       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Ensure("ProgramFiles",            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Ensure("ProgramData",             Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("ALLUSERSPROFILE",         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Ensure("CommonProgramFiles",      Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        Ensure("CommonProgramFiles(x86)", Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        Ensure("ComSpec",                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        Ensure("TMP",                     Path.GetTempPath());
        Ensure("TEMP",                    Path.GetTempPath());
        Ensure("NUMBER_OF_PROCESSORS",    Environment.ProcessorCount.ToString());
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
                        new object[] { "", i, 0 });
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);

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
                                    new object[] { "", vi, 0 });
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
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            var result = new List<ModuleParameterInfo>();

            try
            {
                NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();
                dynamic moduleParams;
                try
                {
                    moduleParams = stepPo.GetPropertyObject("TS.Module.Parameters", 0);
                }
                catch
                {
                    moduleParams = stepPo.GetPropertyObject("Module.Parameters", 0);
                }

                var mpObj  = (object)moduleParams;
                var mpType = mpObj.GetType();
                int count  = Convert.ToInt32(mpType.InvokeMember(
                    "GetNumSubProperties", _comFlags, null, mpObj, new object[] { "" }));

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        // GetNthSubPropertyName returns the name; then GetPropertyObject gets the value
                        string paramName = (string)mpType.InvokeMember(
                            "GetNthSubPropertyName", _comFlags, null, mpObj,
                            new object[] { "", i, 0 });
                        dynamic param = mpType.InvokeMember(
                            "GetPropertyObject", _comFlags, null, mpObj,
                            new object[] { paramName, 0 });

                        var pi = new ModuleParameterInfo
                        {
                            Name     = paramName,
                            DataType = TryGetString(param, "TypeName"),
                        };

                        try
                        {
                            var pObj   = (object)param;
                            int flags2 = Convert.ToInt32(pObj.GetType().InvokeMember(
                                "GetFlags", _comFlags, null, pObj, new object[] { "", 0 }));
                            pi.Direction = (flags2 & 4) != 0 ? "InOut"
                                         : (flags2 & 2) != 0 ? "Output"
                                         : "Input";
                        }
                        catch { pi.Direction = "Input"; }

                        try { pi.Value = (string)param.GetValString("", (object)0); }
                        catch
                        {
                            try { pi.Value = ((double)param.GetValNumber("", (object)0)).ToString(); }
                            catch
                            {
                                try { pi.Value = ((bool)param.GetValBoolean("", (object)0)).ToString(); }
                                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module parameter value as boolean."); }
                            }
                        }

                        result.Add(pi);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to read module parameter at index {Index}.", i); }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to enumerate module parameters for step '{Step}'.", stepName); }

            return result;
        });
    }

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
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            NiPropertyObject stepPo = ((NiStep)(object)step).AsPropertyObject();

            string[] paramPaths = {
                $"TS.Module.Parameters.{parameterName}",
                $"Module.Parameters.{parameterName}"
            };

            bool set = false;
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            NiStep step = seq.GetStepByName(stepName, (NiStepGroups)sgVal);
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
            dynamic step = seq.GetStepByName(stepName, (object)sgVal);

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
                                    null, s2Obj, null);
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
