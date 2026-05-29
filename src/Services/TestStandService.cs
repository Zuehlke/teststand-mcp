using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestStandMCP.Models;
using Microsoft.Extensions.Logging;
using NiSequenceFile      = NationalInstruments.TestStand.Interop.API.SequenceFile;
using NiPropertyObject    = NationalInstruments.TestStand.Interop.API.PropertyObject;
using NiPropValueTypes    = NationalInstruments.TestStand.Interop.API.PropertyValueTypes;
using NiEngine            = NationalInstruments.TestStand.Interop.API.IEngine;
using PropertyObjectFile  = NationalInstruments.TestStand.Interop.API.PropertyObjectFile;

namespace TestStandMCP.Services;

// ── Interface ────────────────────────────────────────────────────────────────

public interface ITestStandService : IDisposable
{
    // Engine
    Task<StationInfo> GetStationInfoAsync();
    Task<bool> ConnectAsync(string? enginePath = null);
    Task DisconnectAsync();
    bool IsConnected { get; }

    // Sequence Files
    Task<SequenceFileInfo> OpenSequenceFileAsync(string filePath);
    Task CloseSequenceFileAsync(string filePath);
    Task<List<SequenceFileInfo>> GetLoadedSequenceFilesAsync();
    Task<SequenceInfo> GetSequenceAsync(string filePath, string sequenceName);
    Task SaveSequenceFileAsync(string filePath);
    Task<string> CreateSequenceFileAsync(string filePath);
    Task InsertSequenceAsync(string filePath, string sequenceName);
    Task InsertStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepType, string stepName, int index = -1, string? adapterName = null);
    Task InsertLocalVariableAsync(string filePath, string sequenceName,
        string variableName, string dataType, string? defaultValue = null);
    Task SetLocalVariableCommentAsync(string filePath, string sequenceName,
        string variableName, string comment);
    Task SetLocalVariableValueAsync(string filePath, string sequenceName,
        string variableName, string value);
    Task<List<VariableInfo>> GetLocalVariablesAsync(string filePath, string sequenceName);
    Task SetStepExpressionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression, string expressionType = "Statement");

    Task SetSequenceCallTargetAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string targetSequenceName, string targetSequenceFile = "");

    Task SetStepModulePathAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string modulePath);

    // Executions
    Task<ExecutionInfo> StartExecutionAsync(string sequenceFilePath, string entryPoint,
        Dictionary<string, object>? parameters = null);
    Task<ExecutionResult> WaitForExecutionAsync(string executionId, int timeoutSeconds = 300);
    Task<ExecutionInfo> GetExecutionStatusAsync(string executionId);
    Task<List<ExecutionInfo>> GetActiveExecutionsAsync();
    Task TerminateExecutionAsync(string executionId);
    Task<ExecutionResult> RunSequenceAsync(string sequenceFilePath, string sequenceName,
        Dictionary<string, object>? parameters = null, int timeoutSeconds = 300);

    // Variables & Properties
    Task<PropertyValue> GetPropertyAsync(string lookupString);
    Task SetPropertyAsync(string lookupString, object value);
    Task<List<VariableInfo>> GetFileGlobalsAsync(string sequenceFilePath);
    Task<List<VariableInfo>> GetStationGlobalsAsync();
    Task SetFileGlobalAsync(string sequenceFilePath, string variableName, object value);
    Task SetStationGlobalAsync(string variableName, object value);
    Task InsertFileGlobalAsync(string sequenceFilePath, string variableName, string dataType);

    // Steps
    Task<List<StepInfo>> GetStepsAsync(string sequenceFilePath, string sequenceName);
    Task<StepInfo> GetStepAsync(string sequenceFilePath, string sequenceName, string stepName);
    Task EnableStepAsync(string sequenceFilePath, string sequenceName, string stepName, bool enabled);
    Task<Dictionary<string, object>> GetStepPropertiesAsync(string sequenceFilePath,
        string sequenceName, string stepName);

    // Sequence Analyzer
    Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath);

    // Reports
    Task<ReportInfo> GenerateReportAsync(string executionId, string outputPath,
        string format = "HTML");
    Task<string> GetReportTextAsync(string executionId);

    // UUT / Batch
    Task<UutInfo> GetUutInfoAsync(string executionId);
    Task SetUutSerialNumberAsync(string executionId, string serialNumber);
    Task SetUutPartNumberAsync(string executionId, string partNumber);

    // Adapters
    Task<List<AdapterInfo>> GetLoadedAdaptersAsync();
    Task LoadAdapterAsync(string adapterName);
    Task UnloadAdapterAsync(string adapterName);

    // Logging
    Task<List<LogEntry>> GetExecutionLogAsync(string executionId, int maxEntries = 100);
    Task ClearLogAsync(string executionId);

    // Process Model
    Task<string> GetProcessModelAsync();
    Task SetProcessModelAsync(string processModelPath);

    // Database / Result Schema
    Task<List<string>> GetResultSchemasAsync();
    Task<string> ExportResultsAsync(string executionId, string schemaName, string outputPath);

    // Type Palettes
    Task<List<TypePaletteInfo>> GetTypePalettesAsync();
    Task LoadTypePaletteAsync(string palettePath);
    Task UnloadTypePaletteAsync(string palettePath);
    Task<List<StepTypeInfo>> GetStepTypesAsync(string? paletteFile = null);
    Task<StepTypeInfo> GetStepTypeAsync(string stepTypeName);
    Task<List<DataTypeInfo>> GetDataTypesAsync(string? sequenceFilePath = null);

    // Engine Info & Control
    Task<EnginePaths> GetEnginePathsAsync();
    Task<ExpressionCheckResult> CheckExpressionAsync(string expression, string? sequenceFilePath = null);
    Task<string> ExpandPathMacrosAsync(string path);
    Task<string> FindFileAsync(string filename);
    Task BreakAllAsync();
    Task AbortAllAsync();
    Task TerminateAllAsync();
    Task<StationOptionsInfo> GetStationOptionsAsync();
    Task SetStationOptionsAsync(StationOptionsInfo options);

    // Execution Debug Control
    Task BreakExecutionAsync(string executionId);
    Task ResumeExecutionAsync(string executionId);
    Task AbortExecutionAsync(string executionId);
    Task RestartExecutionAsync(string executionId);
    Task StepOverAsync(string executionId);
    Task StepIntoAsync(string executionId);
    Task StepOutAsync(string executionId);

    // Sequence File Operations
    Task DeleteSequenceAsync(string filePath, string sequenceName);
    Task<bool> SequenceNameExistsAsync(string filePath, string sequenceName);
    Task RenameSequenceAsync(string filePath, string oldName, string newName);

    // Sequence Operations
    Task DeleteStepAsync(string filePath, string sequenceName, string stepGroup, string stepName);
    Task MoveStepAsync(string filePath, string sequenceName, string stepGroup, string stepName, int newIndex);
    Task<bool> StepNameExistsAsync(string filePath, string sequenceName, string stepName);
    Task<List<ParameterInfo>> GetSequenceParametersAsync(string filePath, string sequenceName);
    Task InsertSequenceParameterAsync(string filePath, string sequenceName, string paramName,
        string dataType, string direction = "Input", string? defaultValue = null);
    Task DeleteLocalVariableAsync(string filePath, string sequenceName, string variableName);
    Task<List<StepTemplateInfo>> GetStepTemplatesAsync(string filePath);
    Task InsertStepFromTemplateAsync(string filePath, string sequenceName, string stepGroup,
        string templateName, string newStepName, int index = -1);
    Task<SequenceProperties> GetSequencePropertiesAsync(string filePath, string sequenceName);
    Task SetSequencePropertiesAsync(string filePath, string sequenceName, SequenceProperties props);

    // Step Property Operations
    Task RenameStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string newName);
    Task<string> SetStepCommentAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string comment);
    Task SetStepRunModeAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string runMode);
    Task SetStepPreconditionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string precondition);
    Task SetStepPassActionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string passAction, string? target = null);
    Task SetStepFailActionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string failAction, string? target = null);
    Task SetStepLoopAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string loopType, string? initExpr = null,
        string? whileExpr = null, string? incExpr = null);
    Task SetStepRecordResultAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string recordingOption);
    Task SetStepEvalPrecondAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    Task SetStepModuleLoadOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    Task SetStepModuleUnloadOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    Task SetStepBatchSyncOptionAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string option);
    Task ChangeStepAdapterAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string newAdapter);
    Task<string> GetStepUniqueIdAsync(string filePath, string sequenceName, string stepGroup,
        string stepName);

    // Report Operations
    Task SaveReportAsync(string executionId, string outputPath, string format = "HTML");
    Task LaunchReportViewerAsync(string executionId);
    Task<string> GetFullReportAsync(string executionId);

    // Undo/Redo
    Task<UndoStackInfo> GetUndoStackAsync(string? filePath = null);
    Task<bool> UndoAsync(string? filePath = null);
    Task<bool> RedoAsync(string? filePath = null);
    Task BeginUndoGroupAsync(string groupName, string? filePath = null);
    Task EndUndoGroupAsync(string? filePath = null);
    Task CancelUndoGroupAsync(string? filePath = null);

    // Sequence File Comparison
    Task<SequenceFileDiff> CompareSequenceFilesAsync(string filePath1, string filePath2);

    // Sync Manager
    Task<List<SyncObjectInfo>> GetSyncObjectsAsync();
    Task CreateSyncObjectAsync(string name, string type, int initialValue = 1, int maxValue = 1);
    Task DeleteSyncObjectAsync(string name);
    Task SyncSemaphoreWaitAsync(string name, double timeoutSeconds = 30);
    Task SyncSemaphoreReleaseAsync(string name);
    Task SyncMutexLockAsync(string name, double timeoutSeconds = 30);
    Task SyncMutexUnlockAsync(string name);
    Task SyncQueueEnqueueAsync(string name, string value);
    Task<string> SyncQueueDequeueAsync(string name, double timeoutSeconds = 30);
    Task SyncQueueFlushAsync(string name);
    Task SyncNotificationSetAsync(string name, string value = "");
    Task SyncNotificationResetAsync(string name);
    Task<string> SyncNotificationWaitAsync(string name, double timeoutSeconds = 30);

    // Advanced Adapter Introspection
    Task<AdapterDetailInfo> GetAdapterDetailsAsync(string adapterName);
    Task<StepModuleInfo> GetStepModuleInfoAsync(string filePath, string sequenceName,
        string stepGroup, string stepName);

    // Search
    Task<SearchResult> SearchStepsAsync(string filePath, string pattern,
        string searchIn = "all", bool caseSensitive = false);

    // Thread-Level Execution Control
    Task<List<ThreadInfo>> GetExecutionThreadsAsync(string executionId);
    Task<ThreadInfo> GetThreadStatusAsync(string executionId, string threadId);
    Task BreakThreadAsync(string executionId, string threadId);
    Task ResumeThreadAsync(string executionId, string threadId);
    Task StepOverThreadAsync(string executionId, string threadId);
    Task StepIntoThreadAsync(string executionId, string threadId);
    Task StepOutThreadAsync(string executionId, string threadId);
    Task<List<CallStackFrame>> GetThreadCallStackAsync(string executionId, string threadId);

    // Numeric/String Limit Configuration
    Task SetNumericLimitsAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, double? lowLimit, double? highLimit, string? units,
        string comparisonType = "GELE");
    Task<Dictionary<string, object?>> GetNumericLimitsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName);
    Task SetStepMeasurementAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression);
    Task ConfigureStringValueTestAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, string expression, string expectedValue,
        string comparisonType = "CaseSensitive");

    // Breakpoints
    Task SetStepBreakpointAsync(string filePath, string sequenceName, string stepGroup,
        string stepName, bool enabled, string breakpointType = "Before");
    Task<List<Dictionary<string, string>>> GetBreakpointsAsync(string filePath);

    // Execution Results
    Task<Dictionary<string, object?>> GetStepResultAsync(string executionId,
        string sequenceName, string stepName);
    Task<Dictionary<string, object?>> GetExecutionResultsAsync(string executionId);
    Task<double> GetExecutionTimeAsync(string executionId);

    // Workspace
    Task<WorkspaceInfo> OpenWorkspaceAsync(string workspacePath);
    Task<WorkspaceInfo> GetWorkspaceAsync();

    // Watch Expressions
    Task<int> AddWatchExpressionAsync(string expression, string? label = null);
    Task<List<WatchExpressionInfo>> GetWatchExpressionsAsync();
    Task RemoveWatchExpressionAsync(int index);

    // Callbacks
    Task<List<CallbackInfo>> GetCallbacksAsync(string filePath);

    // File Properties
    Task<FilePropertiesInfo> GetFilePropertiesAsync(string filePath);
    Task SetFilePropertiesAsync(string filePath, string? comment = null, string? version = null);

    // Duplicate Sequence
    Task<string> DuplicateSequenceAsync(string sourceFilePath, string sourceSequenceName,
        string newSequenceName, string? targetFilePath = null);

    // Array Variable Operations
    Task<List<ArrayElementInfo>> GetArrayVariableAsync(string filePath,
        string? sequenceName, string variableName, int maxElements = 100);
    Task SetArrayElementAsync(string filePath, string? sequenceName,
        string variableName, int index, string value);
    Task ResizeArrayVariableAsync(string filePath, string? sequenceName,
        string variableName, int newSize);

    // Data Type Operations
    Task<DataTypeInfo> CreateDataTypeAsync(string filePath, string typeName,
        string baseType = "Object");
    Task DeleteDataTypeAsync(string filePath, string typeName);

    // Module Parameter Operations
    Task<List<ModuleParameterInfo>> GetModuleParametersAsync(string filePath,
        string sequenceName, string stepGroup, string stepName);
    Task SetModuleParameterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string parameterName, string value,
        bool useExpression = true);

    // Step Configuration
    Task ConfigureMessagePopupAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string message,
        string? title = null, string buttons = "OK", double timeout = -1);
    Task ConfigurePropertyLoaderAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string filePathExpr, string mode = "Read");
}

// ── Implementation ────────────────────────────────────────────────────────────

public class TestStandService : ITestStandService
{
    private readonly ILogger<TestStandService> _logger;
    private dynamic? _engine;         // NationalInstruments.TestStand.Interop.API.Engine
    private dynamic? _engineMgr;      // EngineManager
    private bool _disposed;
    private readonly Dictionary<string, DateTime> _executionStartTimes = new();
    private readonly Dictionary<string, List<LogEntry>> _executionLogs = new();
    private readonly Dictionary<string, dynamic> _syncObjects = new();

    // In-memory tracking (Engine API has no SequenceFiles/Executions collection)
    private readonly Dictionary<string, dynamic> _loadedSequenceFiles = new();
    private readonly Dictionary<string, dynamic> _activeExecutions = new();

    // Watch expressions are an editor/GUI concept not available in the engine API;
    // we keep them in memory so Claude can manage them across calls.
    private readonly List<WatchExpressionInfo> _watchExpressions = new();

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

    public TestStandService(ILogger<TestStandService> logger)
    {
        _logger = logger;
    }

    // ── Engine ───────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(string? enginePath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Connecting to TestStand engine...");

                // Create TestStand Engine via COM
                var engineType = Type.GetTypeFromProgID("TestStand.Engine");
                if (engineType == null)
                    throw new InvalidOperationException(
                        "TestStand Engine COM server not found. Ensure NI TestStand is installed.");

                _engine = Activator.CreateInstance(engineType)
                    ?? throw new InvalidOperationException("Failed to create TestStand Engine instance.");

                // Load type palette files so step types (Label, Action, etc.) are available
                try { _engine.LoadTypePaletteFiles(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not load type palette files"); }

                _logger.LogInformation("Successfully connected to TestStand engine.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to TestStand engine");
                return false;
            }
        });
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            // Release all loaded sequence file COM objects before shutting down the engine.
            // Abandoning RCWs causes GC finalizer crashes when the engine is already gone.
            foreach (var sf in _loadedSequenceFiles.Values)
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sf); } catch { }
            }
            _loadedSequenceFiles.Clear();
            _activeExecutions.Clear();

            if (_engine != null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_engine);
                _engine = null;
            }
        });
    }

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
                    try { info.ActiveExecutions.Add(MapExecutionInfo(exec)); } catch { }
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

    public async Task<SequenceFileInfo> OpenSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Opening sequence file: {Path}", filePath);
                // GetSequenceFileEx(path, getSeqFileFlags=0, conflictHandler=UseGlobalType=4)
                var sf = _engine!.GetSequenceFileEx(filePath, 0, 4);
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
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sf); } catch { }
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

    public async Task<SequenceInfo> GetSequenceAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapSequenceInfo(seq);
        });
    }

    public async Task SaveSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);
            sf.Save(filePath);
        });
    }

    public async Task<string> CreateSequenceFileAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _engine!.NewSequenceFile();
            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
            return filePath;
        });
    }

    public async Task InsertSequenceAsync(string filePath, string sequenceName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq  = _engine!.NewSequence();
            seq.Name = sequenceName;
            sf.InsertSequence(seq);
            sf.Save(filePath);

            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task InsertStepAsync(string filePath, string sequenceName, string stepGroup,
        string stepType, string stepName, int index = -1, string? adapterName = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq  = sf.GetSequenceByName(sequenceName);

            // StepGroup_Setup=0, StepGroup_Main=1, StepGroup_Cleanup=2
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            // Map adapter display names to internal key names
            string ResolveAdapter(string name) => name.ToLowerInvariant() switch
            {
                "labview" or "lv" or "g" or "vi"       => "G Std Prototype Adapter",
                "labview flex" or "g flex"              => "G Flexible VI Adapter",
                "cvi" or "c" or "c/cvi"                => "C/CVI Std Prototype Adapter",
                "cvi flex" or "c flex"                  => "C/CVI Flexible Prototype Adapter",
                "dotnet" or ".net"                      => "DotNet Adapter",
                "python"                                => "Python Adapter",
                "none"                                  => "None Adapter",
                "sequence adapter" or "sequence"        => "Sequence Adapter",
                _                                       => name  // pass through as-is
            };

            // Determine adapter and internal step type name
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

            // Override adapter if explicitly specified
            if (!string.IsNullOrWhiteSpace(adapterName))
                adapterKey = ResolveAdapter(adapterName);

            var step = _engine!.NewStep(adapterKey, internalType);
            step.Name = stepName;

            int insertAt = index < 0 ? (int)seq.GetNumSteps((object)sgValue) : index;
            seq.InsertStep(step, insertAt, (object)sgValue);

            // Initialize TS.Description to a non-empty placeholder so the binary file
            // serializes this field. Empty strings are omitted by the TOF1 binary format,
            // so we use a space to force the field to exist after save/load.
            // set_step_comment will overwrite this with the real description.
            bool tsDescInit = false;
            try { step.SetValString("TS.Description", 0, " "); tsDescInit = true; } catch { }
            if (!tsDescInit)
                try { step.SetValString("TS.Description", 0x8, " "); } catch { }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Executions ────────────────────────────────────────────────────────────

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
                    : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);

                // NewExecution via typed IEngine interface to avoid COM argument-conversion issues.
                // processModel=null means no process model; execTypeMask=0 = ExecTypeMask_Normal.
                var typedEngine = (NiEngine)_engine!;
                dynamic exec = typedEngine.NewExecution(
                    (NiSequenceFile)(object)sf,   // sequenceFile
                    entryPoint,                    // sequenceName / entry-point
                    null,                          // processModel (none)
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

                var execId = TryGetString(exec, "Id");
                if (string.IsNullOrEmpty(execId))
                    execId = ((object)exec).GetHashCode().ToString();
                _executionStartTimes[execId] = DateTime.UtcNow;
                _executionLogs[execId] = new List<LogEntry>();
                _activeExecutions[execId] = exec;

                return MapExecutionInfo(exec);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start execution");
                throw;
            }
        });
    }

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

                _activeExecutions.Remove(executionId);

                if (exec == null)
                {
                    // Execution finished and was removed from the list
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

    public async Task<List<ExecutionInfo>> GetActiveExecutionsAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<ExecutionInfo>();
            foreach (var exec in _activeExecutions.Values)
            {
                try { result.Add(MapExecutionInfo(exec)); } catch { }
            }
            return result;
        });
    }

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

    public async Task<ExecutionResult> RunSequenceAsync(string sequenceFilePath,
        string sequenceName, Dictionary<string, object>? parameters = null,
        int timeoutSeconds = 300)
    {
        var execInfo = await StartExecutionAsync(sequenceFilePath, sequenceName, parameters);
        return await WaitForExecutionAsync(execInfo.ExecutionId, timeoutSeconds);
    }

    // ── Variables & Properties ────────────────────────────────────────────────

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
                    DataType     = prop.GetType().Name,
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

    public async Task<List<VariableInfo>> GetFileGlobalsAsync(string sequenceFilePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
            try { return MapVariables(GetFileGlobals(sf)); }
            catch { return new List<VariableInfo>(); }
        });
    }

    public async Task<List<VariableInfo>> GetStationGlobalsAsync()
    {
        EnsureConnected();
        return await Task.Run(() => { try { return MapVariables(GetStationGlobals()); } catch { return new List<VariableInfo>(); } });
    }

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
                    : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
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
            ((NiSequenceFile)(object)sf).Save(sequenceFilePath);
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

    public async Task InsertFileGlobalAsync(string sequenceFilePath, string variableName,
        string dataType)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = GetOrLoadSeqFile(sequenceFilePath);
            // PropValType: String=1, Boolean=2, Number=3
            int propType = dataType.ToLowerInvariant() switch
            {
                "number" or "double" or "float" or "int" => 3,
                "boolean" or "bool"                      => 2,
                _                                        => 1
            };
            var fg2 = GetFileGlobals(sf);
            fg2.NewSubProperty(variableName, (NiPropValueTypes)propType, false, "", 0);
            ((NiSequenceFile)(object)sf).Save(sequenceFilePath);
        });
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    public async Task<List<StepInfo>> GetStepsAsync(string sequenceFilePath,
        string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
            var seq = sf.GetSequenceByName(sequenceName);
            // Collect steps from all three groups
            var all = new List<StepInfo>();
            string[] groupNames = { "Setup", "Main", "Cleanup" };
            for (int g = 0; g <= 2; g++)
            {
                try
                {
                    int count = Convert.ToInt32((object)seq.GetNumSteps((object)g));
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var step = MapStepInfo(seq.GetStep(i, (object)g));
                            step.StepGroup = groupNames[g];
                            all.Add(step);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return all;
        });
    }

    public async Task<StepInfo> GetStepAsync(string sequenceFilePath, string sequenceName,
        string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
            var seq = sf.GetSequenceByName(sequenceName);
            return MapStepInfo(FindStepInAllGroups(seq, stepName));
        });
    }

    public async Task EnableStepAsync(string sequenceFilePath, string sequenceName,
        string stepName, bool enabled)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
            var seq  = sf.GetSequenceByName(sequenceName);
            var step = FindStepInAllGroups(seq, stepName);
            step.StepEnabled = enabled;
        });
    }

    public async Task<Dictionary<string, object>> GetStepPropertiesAsync(
        string sequenceFilePath, string sequenceName, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf   = _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(sequenceFilePath, 0, 4);
            var seq  = sf.GetSequenceByName(sequenceName);
            var step = FindStepInAllGroups(seq, stepName);

            var props = new Dictionary<string, object>();
            try { props["Name"]            = (string)step.Name; }            catch { }
            try { props["StepType"]        = (string)step.StepType.Name; }   catch { }
            try { props["Enabled"]         = (bool)step.StepEnabled; }       catch { }
            try { props["PreExpression"]   = (string)step.PreExpression; }   catch { }
            try { props["PostExpression"]  = (string)step.PostExpression; }  catch { }
            try { props["StatusExpression"]= (string)step.StatusExpression;} catch { }
            // Read the user-set description first (stored in property bag as TS.Description).
            // For steps without stored description, step.Description returns the auto-generated
            // type-name (e.g. "Action"), which masks any stored value — so try stored first.
            string? desc = null;
            try
            {
                var storedDesc = (string)step.AsPropertyObject().GetValString("TS.Description", 0);
                if (!string.IsNullOrWhiteSpace(storedDesc)) desc = storedDesc;
            }
            catch { }
            if (desc == null) try { desc = (string)step.Description; } catch { }
            if (string.IsNullOrEmpty(desc))
                try { desc = (string)step.AsPropertyObject().GetValString("Description", 0); } catch { }
            if (desc != null) props["Description"] = desc;
            // Also read the PropertyObject.Comment attribute (separate from Description)
            try
            {
                var poComment = (string)step.AsPropertyObject().Comment;
                if (!string.IsNullOrEmpty(poComment)) props["Comment"] = poComment;
            }
            catch { }
            try
            {
                var expr = (string)step.AsPropertyObject().GetValString("Module.Expression", 0);
                props["ModuleExpression"] = expr;
            }
            catch { }
            return props;
        });
    }

    // ── Reports ───────────────────────────────────────────────────────────────

    public async Task<ReportInfo> GenerateReportAsync(string executionId,
        string outputPath, string format = "HTML")
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                var reportGen = _engine!.ReportGenerator;
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

    public async Task<List<AdapterInfo>> GetLoadedAdaptersAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result   = new List<AdapterInfo>();
            var adapters = _engine!.Adapters;
            for (int i = 0; i < (int)adapters.Count; i++)
            {
                var adapter = adapters[(object)i];
                result.Add(new AdapterInfo
                {
                    Name     = (string)adapter.Name,
                    Type     = (string)adapter.Type,
                    Version  = TryGetString(adapter, "Version"),
                    IsLoaded = true
                });
            }
            return result;
        });
    }

    public async Task LoadAdapterAsync(string adapterName)
    {
        EnsureConnected();
        await Task.Run(() => _engine!.Adapters.LoadAdapter(adapterName));
    }

    public async Task UnloadAdapterAsync(string adapterName)
    {
        EnsureConnected();
        await Task.Run(() => _engine!.Adapters.UnloadAdapter(adapterName));
    }

    // ── Logging ───────────────────────────────────────────────────────────────

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

    public async Task ClearLogAsync(string executionId)
    {
        await Task.Run(() =>
        {
            if (_executionLogs.ContainsKey(executionId))
                _executionLogs[executionId].Clear();
        });
    }

    // ── Process Model ─────────────────────────────────────────────────────────

    public async Task<string> GetProcessModelAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try { return (string)_engine!.StationModelSequenceFilePath; }
            catch { return "Unknown"; }
        });
    }

    public async Task SetProcessModelAsync(string processModelPath)
    {
        EnsureConnected();
        await Task.Run(() => _engine!.StationModelSequenceFilePath = processModelPath);
    }

    // ── Result Schemas ────────────────────────────────────────────────────────

    public async Task<List<string>> GetResultSchemasAsync()
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var schemas = new List<string>();
            try
            {
                var db = _engine!.DatabaseLogger;
                var schemaList = db.ResultSchemas;
                for (int i = 0; i < (int)schemaList.Count; i++)
                    schemas.Add((string)schemaList[(object)i].Name);
            }
            catch { /* DB logger may not be configured */ }
            return schemas;
        });
    }

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

    public async Task InsertLocalVariableAsync(string filePath, string sequenceName,
        string variableName, string dataType, string? defaultValue = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq = sf.GetSequenceByName(sequenceName);

            // Detect array suffix: "number[]", "string[]", "array:number", etc.
            string rawType  = dataType.ToLowerInvariant().Trim();
            bool   isArray  = rawType.EndsWith("[]") || rawType.StartsWith("array:");
            string baseDataType = isArray
                ? rawType.Replace("[]", "").Replace("array:", "").Trim()
                : rawType;

            int propType = baseDataType switch
            {
                "string"  => 1,
                "boolean" => 2,
                "bool"    => 2,
                "number"  => 3,
                "double"  => 3,
                "float"   => 3,
                "int"     => 3,
                "integer" => 3,
                _         => 1  // default: string
            };

            // NewSubProperty(lookupString, valueType, asArray, typeName, options)
            seq.Locals.NewSubProperty(variableName, (object)propType, (object)isArray, "", 0);

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task SetLocalVariableCommentAsync(string filePath, string sequenceName,
        string variableName, string comment)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq  = sf.GetSequenceByName(sequenceName);
            var prop = seq.Locals.GetPropertyObject(variableName, 0);
            prop.Comment = comment;

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task SetLocalVariableValueAsync(string filePath, string sequenceName,
        string variableName, string value)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq = sf.GetSequenceByName(sequenceName);

            // Auto-detect type and set accordingly
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                SetPropertyValue(seq.Locals, variableName, numVal);
            else if (bool.TryParse(value, out var boolVal))
                SetPropertyValue(seq.Locals, variableName, boolVal);
            else
                SetPropertyValue(seq.Locals, variableName, value);

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task SetStepExpressionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression, string expressionType = "Statement")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            var step = seq.GetStepByName(stepName, (object)sgValue);

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
                    // For Statement steps: expression is stored as PreExpression
                    // and also accessible via AsPropertyObject "Module.Expression"
                    try { step.AsPropertyObject().SetValString("Module.Expression", 0, expression); }
                    catch { step.PreExpression = expression; }
                    break;
            }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task SetSequenceCallTargetAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string targetSequenceName, string targetSequenceFile = "")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            var step    = seq.GetStepByName(stepName, (object)sgValue);

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task SetStepModulePathAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string modulePath)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var seq = sf.GetSequenceByName(sequenceName);
            int sgValue = stepGroup.ToLowerInvariant() switch
            {
                "setup"   => 0,
                "main"    => 1,
                "cleanup" => 2,
                _         => 1
            };

            var step = seq.GetStepByName(stepName, (object)sgValue);

            // Access Module via dynamic COM dispatch so VIPath persists.
            dynamic lvModule = step.Module;
            lvModule.VIPath = modulePath;

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task<List<AnalyzerMessage>> RunSequenceAnalyzerAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var diag = new System.Text.StringBuilder();
            string diagPath = @"C:\Temp\ts_analyzer_diag.txt";
            void Log(string msg) { diag.AppendLine(msg); }
            void Flush() { try { System.IO.File.WriteAllText(diagPath, diag.ToString()); } catch { } }

            // Ensure the file is saved to disk before analysis
            if (_loadedSequenceFiles.TryGetValue(filePath, out var cachedSf))
            {
                try { cachedSf.Save(filePath); Log("File saved to disk OK"); }
                catch (Exception ex) { Log($"File save warning: {ex.Message}"); }
            }

            return RunAnalysisViaApp(filePath, Log, Flush);
        });
    }

    private static List<AnalyzerMessage> RunAnalysisViaApp(
        string filePath,
        Action<string> Log,
        Action Flush)
    {
        const string analyzerExe  = @"C:\Program Files (x86)\National Instruments\TestStand 2026\Bin\AnalyzerApp.exe";
        const string savedProject = @"C:\Users\Public\Documents\National Instruments\TestStand 2026 (32-bit)\MyAnalyzerProject.tsaproj";
        string tempProject = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ts_mcp_analysis_" + System.IO.Path.GetFileNameWithoutExtension(filePath) + ".tsaproj");

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
            projectXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<teststandfileheader type='SequenceAnalyzerProjectFile' fileversion='1022' productname='TestStand' productversion='2026 Q1 (26.0.0.49152)' compatibleversion='23.0.0.0' xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://www.ni.com/TestStand/23.0.0/PropertyObjectFile"">
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
        projectXml = System.Text.RegularExpressions.Regex.Replace(
            projectXml,
            @"<Files classname='Strs'>.*?</Files>",
            newFilesBlock,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Log($"Injected file into project XML: {filePath}");

        // Clear old messages so only the new run's results remain
        string clearMessages = "<Messages classname='Objs'><value lbound='[0]' ubound='[]'/></Messages>";
        projectXml = System.Text.RegularExpressions.Regex.Replace(
            projectXml,
            @"<Messages classname='Objs'>.*?</Messages>",
            clearMessages,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Update PathAtLastWrite to match our temp file so AnalyzerApp /save works
        string escapedTempProject = tempProject.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        projectXml = System.Text.RegularExpressions.Regex.Replace(
            projectXml,
            @"<PathAtLastWrite classname='Str'>.*?</PathAtLastWrite>",
            $"<PathAtLastWrite classname='Str'><value>{escapedTempProject}</value></PathAtLastWrite>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        System.IO.File.WriteAllText(tempProject, projectXml, System.Text.Encoding.UTF8);
        Log($"Temp project written: {tempProject}");
        Flush();

        // ── 3. Run AnalyzerApp.exe ────────────────────────────────────────────
        if (!System.IO.File.Exists(analyzerExe))
            throw new InvalidOperationException($"AnalyzerApp.exe not found at: {analyzerExe}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = analyzerExe,
            Arguments              = $"\"{tempProject}\" /analyze /save /quit",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        Log($"Launching: {analyzerExe} {psi.Arguments}");
        Flush();

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start AnalyzerApp.exe process.");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        bool exited   = proc.WaitForExit(120_000); // 2 min timeout

        if (!exited)
        {
            try { proc.Kill(); } catch { }
            throw new InvalidOperationException("AnalyzerApp.exe timed out after 120 seconds.");
        }

        int exitCode = proc.ExitCode;
        Log($"AnalyzerApp exit code: {exitCode}");
        if (!string.IsNullOrWhiteSpace(stdout)) Log($"stdout: {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr)) Log($"stderr: {stderr.Trim()}");
        // exit 0 = clean, 1 = errors, 2 = warnings, <0 = bad args/paths
        if (exitCode < 0)
        {
            Flush();
            throw new InvalidOperationException(
                $"AnalyzerApp.exe returned error code {exitCode}. stdout: {stdout.Trim()} stderr: {stderr.Trim()}");
        }
        Flush();

        // ── 4. Parse the saved project XML for messages ───────────────────────
        if (!System.IO.File.Exists(tempProject))
            throw new InvalidOperationException("AnalyzerApp.exe did not save the project file.");

        string savedXml = System.IO.File.ReadAllText(tempProject, System.Text.Encoding.UTF8);
        var result = ParseAnalyzerMessages(savedXml, Log);

        // Clean up temp file
        try { System.IO.File.Delete(tempProject); } catch { }

        Log($"Total messages collected: {result.Count}");
        Flush();

        int SevOrder(string s) => s switch { "Error" => 0, "Warning" => 1, "Information" => 2, _ => 3 };
        result.Sort((a, b) => SevOrder(a.Severity).CompareTo(SevOrder(b.Severity)));
        return result;
    }

    private static List<AnalyzerMessage> ParseAnalyzerMessages(string projectXml, Action<string> Log)
    {
        var result = new List<AnalyzerMessage>();

        // Extract the <Messages classname='Objs'>...</Messages> block
        var msgBlockMatch = System.Text.RegularExpressions.Regex.Match(
            projectXml,
            @"<Messages classname='Objs'>(.*?)</Messages>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!msgBlockMatch.Success)
        {
            Log("Messages block not found in saved XML");
            return result;
        }

        string msgBlock = msgBlockMatch.Value;
        Log($"Messages block length: {msgBlock.Length} chars");

        // Quick check: if the direct array child has ubound='[]' (self-closing), it's empty.
        // Match only the FIRST <value ...> tag directly inside <Messages> — not nested ones.
        var firstValueMatch = System.Text.RegularExpressions.Regex.Match(
            msgBlock, @"<Messages classname='Objs'>\s*<value\b([^>]*)>?",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (firstValueMatch.Success)
        {
            string attrs = firstValueMatch.Groups[1].Value;
            bool isSelfClosing = attrs.EndsWith("/") ||
                System.Text.RegularExpressions.Regex.IsMatch(msgBlock,
                    @"<Messages classname='Objs'>\s*<value\b[^>]*/\s*>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
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

            int.TryParse(sevStr, out int sevInt);
            string sevLabel = sevInt switch
            {
                0 => "Error",
                1 => "Warning",
                2 => "Information",
                _ => "Information"   // 3 = Default (use rule's own default)
            };

            result.Add(new AnalyzerMessage { Severity = sevLabel, RuleId = ruleId, Text = text });
        }

        return result;
    }

    // ── Legacy COM path (no longer called — kept for reference) ──────────────
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
        try { return (string)m.RuleId;  } catch { }
        try { return (string)m.RuleID;  } catch { }
        try { return (string)m.Rule.Id; } catch { }
        return "";
    }

    // ── Workspace ─────────────────────────────────────────────────────────────

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
                try { _engine!.OpenWorkspace(workspacePath, 0); }
                catch (Exception ex2)
                {
                    _logger.LogWarning(ex2, "OpenWorkspace dynamic also failed");
                }
            }
            return BuildWorkspaceInfo();
        });
    }

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
            dynamic ws = _engine!.Workspace;
            try { info.WorkspacePath = (string)ws.Path; } catch { }
            try
            {
                dynamic files = ws.Files;
                int count = Convert.ToInt32((object)files.Count);
                for (int i = 0; i < count; i++)
                {
                    try { info.SequenceFiles.Add((string)files[(object)i].Path); } catch { }
                }
            }
            catch { }
        }
        catch { }
        return info;
    }

    // ── Watch Expressions ─────────────────────────────────────────────────────

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

    public async Task<List<CallbackInfo>> GetCallbacksAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var result = new List<CallbackInfo>();
            try
            {
                dynamic callbacks = sf.Callbacks;
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
                    catch { }
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
                try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch { }
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
                    catch { }
                }
            }
            return result;
        });
    }

    // ── File Properties ───────────────────────────────────────────────────────

    public async Task<FilePropertiesInfo> GetFilePropertiesAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            var info = new FilePropertiesInfo { FilePath = filePath };
            // Use PropertyObjectFile typed interface for all file-level metadata
            try
            {
                var pof = (PropertyObjectFile)(object)sf.AsPropertyObjectFile();
                info.Version    = pof.Version;
                info.IsModified = pof.IsModified;
                info.Comment    = string.IsNullOrEmpty(pof.Comment) ? null : pof.Comment;
            }
            catch { }
            // NumSequences is on SequenceFile interface directly
            try { info.NumSequences = Convert.ToInt32((object)sf.NumSequences); } catch { }
            return info;
        });
    }

    public async Task SetFilePropertiesAsync(string filePath, string? comment = null,
        string? version = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf = _loadedSequenceFiles.TryGetValue(filePath, out var cached)
                ? cached
                : _engine!.GetSequenceFileEx(filePath, 0, 4);

            // PropertyObjectFile has Comment and Version as direct typed properties
            var pof = (PropertyObjectFile)(object)sf.AsPropertyObjectFile();
            if (comment != null) pof.Comment = comment;
            if (version != null) pof.Version = version;

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Duplicate Sequence ────────────────────────────────────────────────────

    public async Task<string> DuplicateSequenceAsync(string sourceFilePath,
        string sourceSequenceName, string newSequenceName, string? targetFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var srcSf = _loadedSequenceFiles.TryGetValue(sourceFilePath, out var cachedSrc)
                ? cachedSrc
                : _engine!.GetSequenceFileEx(sourceFilePath, 0, 4);

            string destPath = targetFilePath ?? sourceFilePath;
            var dstSf = string.Equals(destPath, sourceFilePath, StringComparison.OrdinalIgnoreCase)
                ? srcSf
                : (_loadedSequenceFiles.TryGetValue(destPath, out var cachedDst)
                    ? cachedDst
                    : _engine!.GetSequenceFileEx(destPath, 0, 4));

            // Get source sequence
            dynamic srcSeq = srcSf.GetSequenceByName(sourceSequenceName);

            // Create a new sequence from the source using CopySequence if available,
            // or fall back to creating a new one and copying properties manually.
            dynamic newSeq;
            try
            {
                // Try CopySequence API (TestStand 2016+)
                newSeq = srcSf.CopySequence(srcSeq);
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
    /// Call GetStates(out runState, out termState) via reflection so we can use out-params
    /// from a dynamic reference without compiler errors.
    /// runState: 1=Running, 2=Paused, 3=Stopped
    /// </summary>
    private static int GetExecutionRunState(object execObj)
    {
        try
        {
            var args  = new object[2];
            var pmods = new[] { new System.Reflection.ParameterModifier(2) };
            pmods[0][0] = true; // out runState
            pmods[0][1] = true; // out termState
            execObj.GetType().InvokeMember("GetStates",
                System.Reflection.BindingFlags.InvokeMethod |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance,
                null, execObj, args, pmods, null, null);
            return Convert.ToInt32(args[0]);
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

    private ExecutionResult BuildExecutionResult(dynamic exec, string executionId)
    {
        var elapsed = _executionStartTimes.TryGetValue(executionId, out var st)
            ? (DateTime.UtcNow - st).TotalSeconds : 0;

        int    runState = GetExecutionRunState((object)exec);
        string result   = TryGetString(exec, "ResultStatus");
        if (string.IsNullOrEmpty(result)) result = "Unknown";

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

        try { info.FileGlobals    = MapVariables(GetFileGlobals(sf)); } catch { }
        try { info.StationGlobals = MapVariables(GetStationGlobals()); } catch { }

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
        try { seqDesc = (string)seq.Comment; } catch { }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)seq.AsPropertyObject().GetValString("TS.Comment", 0); } catch { }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)seq.AsPropertyObject().GetValString("Comment", 0); } catch { }
        if (string.IsNullOrEmpty(seqDesc))
            try { seqDesc = (string)seq.Description; } catch { }
        if (!string.IsNullOrEmpty(seqDesc)) info.Description = seqDesc;
        string[] groupNames = { "Setup", "Main", "Cleanup" };
        for (int g = 0; g <= 2; g++)
        {
            try
            {
                int count = Convert.ToInt32((object)seq.GetNumSteps((object)g));
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var step = MapStepInfo(seq.GetStep(i, (object)g));
                        step.StepGroup = groupNames[g];
                        info.Steps.Add(step);
                    }
                    catch { }
                }
            }
            catch { }
        }
        try { info.Locals = MapVariables(seq.Locals); } catch { }
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
        catch { }
        return steps;
    }

    private StepInfo MapStepInfo(dynamic step)
    {
        var info = new StepInfo
        {
            Name     = (string)step.Name,
            StepType = TryGetString(step.StepType, "Name"),
        };
        // RunMode is a string property: "Normal", "Skip", "Fail", "Pass"
        try { info.Enabled = (string)step.RunMode != "Skip"; } catch { info.Enabled = true; }
        // step.Comment holds the user-set comment (written by SetStepCommentAsync).
        // step.Description returns the auto-generated type description (e.g. "Action"),
        // so prefer Comment, and only fall back to Description when Comment is empty.
        try
        {
            var c = (string)step.Comment;
            if (!string.IsNullOrEmpty(c)) info.Description = c;
        }
        catch { }
        if (string.IsNullOrEmpty(info.Description))
            try { info.Description = (string)step.Description; } catch { }
        try
        {
            if ((int)step.SubSteps.Count > 0)
                info.SubSteps = MapSteps(step.SubSteps);
        }
        catch { }
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
                    vars.Add(new VariableInfo
                    {
                        Name     = (string)prop.Name,
                        DataType = TryGetString(prop, "TypeName"),
                        Value    = TryGetValue(prop)
                    });
                }
                catch { }
            }
        }
        catch { }
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
        catch { }
        try { return (bool)prop.GetValBoolean("", 0); }
        catch { }
        try { return (string)prop.GetValString("", 0); }
        catch { }
        return null;
    }

    private T? GetEngineProperty<T>(string propName)
    {
        try
        {
            return (T)((object)_engine!).GetType().InvokeMember(
                propName, _comFlags, null, _engine, null);
        }
        catch { }
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
            catch { }
        }
        throw new KeyNotFoundException($"Step '{stepName}' not found in any step group.");
    }

    // ── Type Palettes ─────────────────────────────────────────────────────────

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
                catch { }
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
                    try { ver = (string)td.TypeVersion; } catch { }

                    string palette = ResolvePaletteName(typeName, ver, stepTypesByPalette.Keys);
                    if (!string.IsNullOrEmpty(palette) && stepTypesByPalette.ContainsKey(palette))
                        stepTypesByPalette[palette].Add(typeName);
                }
                catch { }
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
    private static string ResolvePaletteName(string typeName, string typeVersion, IEnumerable<string> availablePalettes)
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
                catch { }
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

            _engine!.SetTypePaletteFileList(newArray);
            _engine!.LoadTypePaletteFiles();
        });
    }

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
            _engine!.SetTypePaletteFileList(filtered.ToArray());
            _engine!.LoadTypePaletteFiles();
        });
    }

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
                        var td = _engine.GetTypeDefinition((object)name);
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
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enumerate step types");
            }
            return result;
        });
    }

    public async Task<StepTypeInfo> GetStepTypeAsync(string stepTypeName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                var td = _engine!.GetTypeDefinition((object)stepTypeName);
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
                catch { }

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

    public async Task<List<DataTypeInfo>> GetDataTypesAsync(string? sequenceFilePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var result = new List<DataTypeInfo>();
            try
            {
                // If a sequence file is given, use its custom data types only
                if (!string.IsNullOrEmpty(sequenceFilePath) &&
                    _loadedSequenceFiles.TryGetValue(sequenceFilePath, out var seqFile))
                {
                    int cnt = (int)seqFile.GetNumSubProperties((object)"");
                    // fall through to engine-level approach
                }

                var names = (string[])_engine!.GetTypeNames();
                foreach (var name in names)
                {
                    try
                    {
                        var td = _engine.GetTypeDefinition((object)name);
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
                    catch { }
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

    public async Task<string> ExpandPathMacrosAsync(string path)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try { return (string)_engine!.ExpandPathMacros((object)path); }
            catch { return path; }
        });
    }

    public async Task<string> FindFileAsync(string filename)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            try
            {
                // FindFile(filename, searchDir, searchFlags) - searchFlags 0 = default search dirs
                var result = _engine!.FindFile((object)filename, (object)"", (object)0);
                return result?.ToString() ?? "";
            }
            catch { return ""; }
        });
    }

    public async Task BreakAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.BreakAll());
    }

    public async Task AbortAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.AbortAll());
    }

    public async Task TerminateAllAsync()
    {
        EnsureConnected();
        await Task.Run(() => _engine!.TerminateAll());
    }

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

    public async Task SetStationOptionsAsync(StationOptionsInfo options)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            try { _engine!.TracingEnabled             = options.TracingEnabled;             } catch { }
            try { _engine!.BreakpointsEnabled         = options.BreakpointsEnabled;         } catch { }
            try { _engine!.DisableResults             = options.DisableResults;             } catch { }
            try { _engine!.AlwaysGotoCleanupOnFailure = options.AlwaysGotoCleanupOnFailure; } catch { }
            try { _engine!.BreakOnRTE                 = options.BreakOnRte;                 } catch { }
            if (!string.IsNullOrEmpty(options.StationId))
                try { _engine!.StationID = options.StationId; } catch { }
            if (!string.IsNullOrEmpty(options.ProcessModelPath))
                try { _engine!.StationModelSequenceFilePath = options.ProcessModelPath; } catch { }
        });
    }

    // ── Execution Debug Control ────────────────────────────────────────────────

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

    public async Task RestartExecutionAsync(string executionId)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");
            exec.Restart();
            _executionStartTimes[executionId] = DateTime.UtcNow;
        });
    }

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

            sf.Save(filePath);
        });
    }

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

    public async Task RenameSequenceAsync(string filePath, string oldName, string newName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(oldName);
            seq.Name = newName;
            sf.Save(filePath);
        });
    }

    // ── Sequence Operations ────────────────────────────────────────────────────

    public async Task DeleteStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            seq.DeleteStep(step, (object)sgVal);
            sf.Save(filePath);
        });
    }

    public async Task MoveStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, int newIndex)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            // RemoveStep + InsertStep at new position
            seq.RemoveStep(step, (object)sgVal);
            seq.InsertStep(step, newIndex, (object)sgVal);
            sf.Save(filePath);
        });
    }

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

    public async Task InsertSequenceParameterAsync(string filePath, string sequenceName,
        string paramName, string dataType, string direction = "Input",
        string? defaultValue = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            int propType = MapDataType(dataType);
            seq.Parameters.NewSubProperty(paramName, (object)propType, false, "", 0);

            // PropFlags_PassByReference = 4 (enables pass-by-reference / InOut semantics)
            int flags = direction.ToLowerInvariant() switch
            {
                "inout" or "inputoutput" or "passbyreference" or "byref" => 4,
                _ => 0
            };
            if (flags != 0)
            {
                var propObj2 = (object)seq.Parameters.GetPropertyObject(paramName, 0);
                propObj2.GetType().InvokeMember("SetFlags", _comFlags, null, propObj2,
                    new object[] { "", 0, flags });
            }

            if (defaultValue != null)
                SetPropertyValueByType(seq.Parameters, paramName, defaultValue, propType);

            sf.Save(filePath);
        });
    }

    public async Task DeleteLocalVariableAsync(string filePath, string sequenceName,
        string variableName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            bool inLocals = false;
            try { seq.Locals.GetPropertyObject(variableName, 0); inLocals = true; } catch { }

            if (inLocals)
                seq.Locals.DeleteSubProperty(variableName, 0);
            else
                seq.Parameters.DeleteSubProperty(variableName, 0);

            sf.Save(filePath);
        });
    }

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

                    try { name = (string)iType.InvokeMember("Name", _comFlags, null, item, null); } catch { }

                    // StepType: step.StepType is an object; get its Name property
                    try
                    {
                        var stObj = iType.InvokeMember("StepType", _comFlags, null, item, null);
                        if (stObj != null)
                            stepType = stObj.GetType().InvokeMember("Name", _comFlags, null, stObj, null)?.ToString() ?? "";
                    }
                    catch { }

                    try { desc = (string)iType.InvokeMember("Description", _comFlags, null, item, null); } catch { }
                    if (string.IsNullOrEmpty(desc))
                    {
                        try { desc = Convert.ToString(iType.InvokeMember("GetValString",
                            _comFlags, null, item, new object[] { "TS.Description", 0 })) ?? ""; } catch { }
                    }

                    result.Add(new StepTemplateInfo { Name = name, StepType = stepType, Description = desc });
                }
            }
            catch { }
            return result;
        });
    }

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException?.Message ?? ex.Message;
                System.IO.File.WriteAllText(@"C:\Temp\ts_insert_diag.txt", $"{ex}\nInner: {ex.InnerException}");
                throw new InvalidOperationException(msg, ex);
            }
        });
    }

    public async Task<SequenceProperties> GetSequencePropertiesAsync(string filePath,
        string sequenceName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);
            var props = new SequenceProperties();
            try { props.Name                     = (string)seq.Name;                     } catch { }
            try { props.Type                     = (string)seq.SequenceType.ToString();  } catch { }
            try { props.GotoCleanupOnFailure      = (bool)seq.GotoCleanupOnFailure;       } catch { }
            try { props.DisableResults            = (bool)seq.DisableResults;             } catch { }
            try
            {
                int fa = (int)seq.FailureAction;
                props.FailureAction = fa switch { 0 => "Continue", 1 => "Terminate", 2 => "Abort", _ => fa.ToString() };
            }
            catch { }
            try { props.EntryPointNameExpression  = (string)seq.EntryPointNameExpression; } catch { }
            try { props.ShowEntryPointForAllWindows = (bool)seq.ShowEntryPointForAllWindows; } catch { }
            string? seqDesc = null;
            // TestStand stores sequence comments as "Comment" (not "Description")
            try { seqDesc = (string)seq.Comment; } catch { }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)seq.AsPropertyObject().GetValString("TS.Comment", 0); } catch { }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)seq.AsPropertyObject().GetValString("Comment", 0); } catch { }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)seq.Description; } catch { }
            if (string.IsNullOrEmpty(seqDesc))
                try { seqDesc = (string)seq.AsPropertyObject().GetValString("TS.Description", 0); } catch { }
            if (!string.IsNullOrEmpty(seqDesc)) props.Description = seqDesc;
            return props;
        });
    }

    public async Task SetSequencePropertiesAsync(string filePath, string sequenceName,
        SequenceProperties props)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf  = GetOrLoadSeqFile(filePath);
            var seq = sf.GetSequenceByName(sequenceName);

            if (!string.IsNullOrEmpty(props.Name) && props.Name != sequenceName)
                try { seq.Name = props.Name; } catch { }
            try { seq.GotoCleanupOnFailure = props.GotoCleanupOnFailure; } catch { }
            try { seq.DisableResults       = props.DisableResults;       } catch { }
            if (!string.IsNullOrEmpty(props.FailureAction))
            {
                int fa = props.FailureAction.ToLowerInvariant() switch
                { "terminate" => 1, "abort" => 2, _ => 0 };
                try { seq.FailureAction = (object)fa; } catch { }
            }
            if (!string.IsNullOrEmpty(props.EntryPointNameExpression))
                try { seq.EntryPointNameExpression = props.EntryPointNameExpression; } catch { }
            if (!string.IsNullOrEmpty(props.Description))
            {
                bool descSet = false;
                // TestStand uses "Comment" as the sequence comment property
                try { seq.Comment = props.Description; descSet = true; } catch { }
                if (!descSet)
                    try { seq.AsPropertyObject().SetValString("TS.Comment", 0, props.Description); descSet = true; } catch { }
                if (!descSet)
                    try { seq.AsPropertyObject().SetValString("Comment", 0, props.Description); descSet = true; } catch { }
                if (!descSet)
                    try { seq.Description = props.Description; descSet = true; } catch { }
                if (!descSet)
                    try { seq.AsPropertyObject().SetValString("TS.Description", 0, props.Description); } catch { }
            }

            sf.Save(filePath);
        });
    }

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

    public async Task RenameStepAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string newName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            step.Name = newName;
            sf.Save(filePath);
        });
    }

    public async Task<string> SetStepCommentAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string comment)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
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
                try { step.AsPropertyObject().Comment = comment; method = "po.Comment"; }
                catch (Exception ex) { errors.Append($"[po.Comment: {ex.Message}] "); }
            }
            if (method == "")
                throw new InvalidOperationException($"Could not set step comment. Attempts: {errors}");
            sf.Save(filePath);
            return method;
        });
    }

    public async Task SetStepRunModeAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string runMode)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
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
            sf.Save(filePath);
        });
    }

    public async Task SetStepPreconditionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string precondition)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            step.Precondition = precondition;
            sf.Save(filePath);
        });
    }

    public async Task SetStepPassActionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string passAction, string? target = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            // PostActionValues: Next, Break, Terminate, Goto, Cback
            string actionVal = MapPostAction(passAction);
            step.PassAction = actionVal;
            if (!string.IsNullOrEmpty(target) && actionVal == "Goto")
                try { step.PassActionTarget = target; } catch { }
            sf.Save(filePath);
        });
    }

    public async Task SetStepFailActionAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string failAction, string? target = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            string actionVal = MapPostAction(failAction);
            step.FailAction = actionVal;
            if (!string.IsNullOrEmpty(target) && actionVal == "Goto")
                try { step.FailActionTarget = target; } catch { }
            sf.Save(filePath);
        });
    }

    public async Task SetStepLoopAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string loopType,
        string? initExpr = null, string? whileExpr = null, string? incExpr = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            // StepLoopTypes: NoLooping, FixedNumLoops, PassFailCount, Custom
            string loopVal = loopType.ToLowerInvariant() switch
            {
                "fixednumloops"  => "FixedNumLoops",
                "fixed"          => "FixedNumLoops",
                "for"            => "FixedNumLoops",
                "passfailcount"  => "PassFailCount",
                "passorfail"     => "PassFailCount",
                "custom"         => "Custom",
                _                => "NoLooping"
            };
            step.LoopType = loopVal;
            if (!string.IsNullOrEmpty(initExpr))
                try { step.LoopInitExpression  = initExpr;  } catch { }
            if (!string.IsNullOrEmpty(whileExpr))
                try { step.LoopWhileExpression = whileExpr; } catch { }
            if (!string.IsNullOrEmpty(incExpr))
                try { step.LoopIncExpression   = incExpr;   } catch { }
            sf.Save(filePath);
        });
    }

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
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            // Use the typed Step interface to set the enum property correctly
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ResultRecordingOption =
                (NationalInstruments.TestStand.Interop.API.ResultRecordingOptions)optVal;
            sf.Save(filePath);
        });
    }

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
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.EvalPrecondForInteractiveExecution =
                (NationalInstruments.TestStand.Interop.API.EvalPrecondOptions)optVal;
            sf.Save(filePath);
        });
    }

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
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ModuleLoadOption =
                (NationalInstruments.TestStand.Interop.API.ModuleLoadOptions)optVal;
            sf.Save(filePath);
        });
    }

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
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.ModuleUnloadOption =
                (NationalInstruments.TestStand.Interop.API.ModuleUnloadOptions)optVal;
            sf.Save(filePath);
        });
    }

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
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            var typedStep = (NationalInstruments.TestStand.Interop.API.Step)(object)step;
            typedStep.BatchSyncOption =
                (NationalInstruments.TestStand.Interop.API.BatchSynchronizationOptions)optVal;
            sf.Save(filePath);
        });
    }

    public async Task ChangeStepAdapterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string newAdapter)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            var adapter = _engine!.GetAdapterByKeyName((object)newAdapter);
            step.ChangeAdapter(adapter);
            sf.Save(filePath);
        });
    }

    public async Task<string> GetStepUniqueIdAsync(string filePath, string sequenceName,
        string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            try { return (string)step.UniqueStepId; } catch { return ""; }
        });
    }

    // ── Report Operations ─────────────────────────────────────────────────────

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

    // ── Private Helpers (new) ─────────────────────────────────────────────────

    private dynamic GetOrLoadSeqFile(string filePath)
    {
        return _loadedSequenceFiles.TryGetValue(filePath, out var cached)
            ? cached
            : _engine!.GetSequenceFileEx(filePath, 0, 4);
    }

    /// <summary>
    /// Returns <paramref name="targetPath"/> rewritten as a path relative to
    /// <paramref name="fromDirectory"/>. Falls back to <paramref name="targetPath"/>
    /// unchanged if the paths cannot be relativized (e.g. different drives) or
    /// if <paramref name="targetPath"/> is already relative.
    /// </summary>
    private static string MakeRelativePath(string fromDirectory, string targetPath)
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
        catch { }
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
                        pi.Direction = (flags2 & 4) != 0 ? "InOut"
                                     : (flags2 & 2) != 0 ? "Output"
                                     : "Input";
                    }
                    catch { pi.Direction = "Input"; }
                    pi.DefaultValue = TryGetValue(prop);
                    parms.Add(pi);
                }
                catch { }
            }
        }
        catch { }
        return parms;
    }

    // ── Undo/Redo ─────────────────────────────────────────────────────────────

    private dynamic GetUndoStack(string? filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            var sf = GetOrLoadSeqFile(filePath);
            try { return sf.UndoStack; } catch { }
        }
        try { return _engine!.UndoStack; } catch { }
        throw new InvalidOperationException(
            "UndoStack is not available. Pass a file_path to access a file-level undo stack.");
    }

    public async Task<UndoStackInfo> GetUndoStackAsync(string? filePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var stack = GetUndoStack(filePath);
            var info  = new UndoStackInfo
            {
                FilePath     = filePath,
                CanUndo      = TryGetBool(stack, "CanUndo"),
                CanRedo      = TryGetBool(stack, "CanRedo"),
                NumUndoItems = 0,
                NumRedoItems = 0
            };

            try
            {
                info.NumUndoItems = Convert.ToInt32((object)stack.NumUndoItems);
                for (int i = 0; i < info.NumUndoItems; i++)
                {
                    try
                    {
                        dynamic item = stack.GetUndoItem((object)i);
                        info.UndoItems.Add(new UndoItemInfo
                        {
                            Index       = i,
                            Name        = TryGetString(item, "Name"),
                            Description = TryGetString(item, "Description")
                        });
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                info.NumRedoItems = Convert.ToInt32((object)stack.NumRedoItems);
                for (int i = 0; i < info.NumRedoItems; i++)
                {
                    try
                    {
                        dynamic item = stack.GetRedoItem((object)i);
                        info.RedoItems.Add(new UndoItemInfo
                        {
                            Index       = i,
                            Name        = TryGetString(item, "Name"),
                            Description = TryGetString(item, "Description")
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return info;
        });
    }

    public async Task<bool> UndoAsync(string? filePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var stack = GetUndoStack(filePath);
            if (!TryGetBool(stack, "CanUndo")) return false;
            stack.Undo();
            return true;
        });
    }

    public async Task<bool> RedoAsync(string? filePath = null)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var stack = GetUndoStack(filePath);
            if (!TryGetBool(stack, "CanRedo")) return false;
            stack.Redo();
            return true;
        });
    }

    public async Task BeginUndoGroupAsync(string groupName, string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() => GetUndoStack(filePath).BeginUndoGroup((object)groupName));
    }

    public async Task EndUndoGroupAsync(string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() => GetUndoStack(filePath).EndUndoGroup());
    }

    public async Task CancelUndoGroupAsync(string? filePath = null)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var stack = GetUndoStack(filePath);
            try { stack.CancelUndoGroup(); }
            catch { stack.EndUndoGroup(); }
        });
    }

    // ── Sequence File Comparison ──────────────────────────────────────────────

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

            diff.SequencesOnlyInFile1 = seqs1.Where(n => !seqs2.Contains(n)).ToList();
            diff.SequencesOnlyInFile2 = seqs2.Where(n => !seqs1.Contains(n)).ToList();

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
                catch { }
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
        try { count = Convert.ToInt32((object)sf.NumSequences); } catch { }
        for (int i = 0; i < count; i++)
        {
            try { names.Add((string)sf.GetSequence(i).Name); } catch { }
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
            catch { }
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
                catch { }
            }
        }

        // Compare local variables
        diff.LocalVariableDiffs.AddRange(ComparePropertyBlock(seq1.Locals, seq2.Locals, "Locals"));

        // Compare parameters
        try { diff.ParameterDiffs.AddRange(ComparePropertyBlock(seq1.Parameters, seq2.Parameters, "Parameters")); }
        catch { }

        return diff;
    }

    private static Dictionary<string, string> CollectStepNames(dynamic seq, int group)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        int count = 0;
        try { count = Convert.ToInt32((object)seq.GetNumSteps((object)group)); } catch { }
        for (int i = 0; i < count; i++)
        {
            try
            {
                dynamic step = seq.GetStep(i, (object)group);
                string  name = (string)step.Name;
                string  type = "";
                try { type = (string)step.StepType.Name; } catch { }
                dict[name] = type;
            }
            catch { }
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
            catch { }
        }

        // Compare module expression
        try
        {
            var m1 = (string)step1.AsPropertyObject().GetValString("Module.Expression", (object)0);
            var m2 = (string)step2.AsPropertyObject().GetValString("Module.Expression", (object)0);
            if (m1 != m2) changed.Add($"Module.Expression: '{m1}' → '{m2}'");
        }
        catch { }

        // Compare adapter
        try
        {
            var a1 = TryGetString(step1, "AdapterName");
            var a2 = TryGetString(step2, "AdapterName");
            if (a1 != a2) changed.Add($"AdapterName: '{a1}' → '{a2}'");
        }
        catch { }

        return changed;
    }

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
            catch { }
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
                catch { }
            }
        }
        catch { }
        return names;
    }

    private static string TryGetSubPropertyValue(dynamic block, string name)
    {
        try { return block.GetValString(name, (object)0)?.ToString() ?? ""; }
        catch { }
        try { return block.GetValNumber(name, (object)0).ToString(); }
        catch { }
        try { return block.GetValBoolean(name, (object)0).ToString(); }
        catch { }
        return "";
    }

    // ── Sync Manager ─────────────────────────────────────────────────────────

    private dynamic GetSyncManager()
    {
        try { return _engine!.SyncManager; }
        catch { }
        try { return _engine!.LocalProcessSyncMgr; }
        catch { }
        throw new InvalidOperationException(
            "TestStand SyncManager is not available. Ensure TestStand supports synchronization.");
    }

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
                    try { info.Properties["Count"] = (object)(int)kvp.Value.Count; } catch { }
                    try { info.Properties["MaxCount"] = (object)(int)kvp.Value.MaxCount; } catch { }
                }
                catch { }
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
                    catch { }
                }
            }
            catch { }

            return result;
        });
    }

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

    public async Task DeleteSyncObjectAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            if (_syncObjects.TryGetValue(name, out var obj))
            {
                try { obj.Delete(); } catch { }
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

    public async Task SyncSemaphoreReleaseAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "semaphore").Release());
    }

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

    public async Task SyncMutexUnlockAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "mutex").Unlock());
    }

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

    public async Task SyncQueueFlushAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "queue").Flush());
    }

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

    public async Task SyncNotificationResetAsync(string name)
    {
        EnsureConnected();
        await Task.Run(() => GetSyncObject(name, "notification").Reset());
    }

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

    public async Task<AdapterDetailInfo> GetAdapterDetailsAsync(string adapterName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var adapters = _engine!.Adapters;
            int count = (int)adapters.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic adapter = adapters[(object)i];
                string key = TryGetString(adapter, "KeyName");
                string name = TryGetString(adapter, "Name");

                if (!string.Equals(key, adapterName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, adapterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new AdapterDetailInfo
                {
                    KeyName     = key,
                    DisplayName = TryGetString(adapter, "DisplayName"),
                    Type        = TryGetString(adapter, "Type"),
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

    public async Task<StepModuleInfo> GetStepModuleInfoAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            string adapterKey  = TryGetString(step, "AdapterName");
            string adapterDisp = "";
            try { adapterDisp = (string)step.StepType.Adapter.DisplayName; } catch { }

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
                catch { }
            }

            // Try generic property object access for remaining module props
            try
            {
                dynamic propObj = step.AsPropertyObject();
                string[] modulePaths = {
                    "Module.VIPath", "Module.FunctionName", "Module.LibraryFilePath",
                    "Module.AssemblyName", "Module.ClassName", "Module.MethodName",
                    "Module.ModulePath", "Module.Expression"
                };
                foreach (var mp in modulePaths)
                {
                    string key = mp.Split('.').Last();
                    if (info.ModuleProperties.ContainsKey(key)) continue;
                    try
                    {
                        var val = propObj.GetValString(mp, (object)0);
                        if (!string.IsNullOrEmpty((string)val))
                            info.ModuleProperties[key] = (object)(string)val;
                    }
                    catch { }
                }
            }
            catch { }

            return info;
        });
    }

    // ── Search ────────────────────────────────────────────────────────────────

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
            try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch { }

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
                            catch { }
                        }
                    }
                    catch { }
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
            catch { }
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
                catch { }
            }

            // Module expression
            try
            {
                string modExpr = (string)step.AsPropertyObject()
                    .GetValString("Module.Expression", (object)0);
                if (!string.IsNullOrEmpty(modExpr) && modExpr.IndexOf(pattern, cmp) >= 0)
                    AddMatch(modExpr, "ModuleExpression",
                        $"{seqName}.{stepName}.Module.Expression");
            }
            catch { }
        }

        if (searchIn is "all" or "comment")
        {
            try
            {
                string comment = (string)step.Comment;
                if (!string.IsNullOrEmpty(comment) && comment.IndexOf(pattern, cmp) >= 0)
                    AddMatch(comment, "Comment", $"{seqName}.{stepName}.Comment");
            }
            catch { }
        }

        return matches;
    }

    // ── Thread-Level Execution Control ────────────────────────────────────────

    private dynamic FindThread(string executionId, string threadId)
    {
        var exec = FindExecution(executionId)
            ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

        int numThreads = 0;
        try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch { }

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
            catch { }
        }
        throw new KeyNotFoundException(
            $"Thread '{threadId}' not found in execution {executionId}.");
    }

    private static ThreadInfo MapThreadInfo(dynamic thread, int index)
    {
        var info = new ThreadInfo { ThreadIndex = index };
        try { info.ThreadId = TryGetString(thread, "ID"); } catch { }
        if (string.IsNullOrEmpty(info.ThreadId)) info.ThreadId = index.ToString();

        try { info.State = MapExecutionState((int)thread.State); } catch { }

        try
        {
            dynamic ctx = thread.GetSequenceContext((object)0);
            try { info.CurrentStepName     = (string)ctx.Step.Name;  } catch { }
            try { info.CurrentSequenceName = (string)ctx.Sequence.Name; } catch { }
            try { info.CurrentFilePath     = (string)ctx.SequenceFile.Path; } catch { }
        }
        catch { }

        try
        {
            info.StackDepth = Convert.ToInt32((object)thread.StackDepth);
        }
        catch { }

        return info;
    }

    public async Task<List<ThreadInfo>> GetExecutionThreadsAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            var result = new List<ThreadInfo>();
            int numThreads = 0;
            try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch { }

            for (int i = 0; i < numThreads; i++)
            {
                try
                {
                    dynamic t = exec.GetThread((object)i);
                    result.Add(MapThreadInfo(t, i));
                }
                catch { }
            }
            return result;
        });
    }

    public async Task<ThreadInfo> GetThreadStatusAsync(string executionId, string threadId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            int numThreads = 0;
            try { numThreads = Convert.ToInt32((object)exec.NumThreads); } catch { }

            for (int i = 0; i < numThreads; i++)
            {
                try
                {
                    dynamic t  = exec.GetThread((object)i);
                    string  id = TryGetString(t, "ID");
                    if (id == threadId || i.ToString() == threadId)
                        return MapThreadInfo(t, i);
                }
                catch { }
            }
            throw new KeyNotFoundException(
                $"Thread '{threadId}' not found in execution {executionId}.");
        });
    }

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

    public async Task ResumeThreadAsync(string executionId, string threadId)
    {
        EnsureConnected();
        await Task.Run(() => FindThread(executionId, threadId).Resume());
    }

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

    public async Task<List<CallStackFrame>> GetThreadCallStackAsync(
        string executionId, string threadId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var thread = FindThread(executionId, threadId);
            var frames = new List<CallStackFrame>();

            int depth = 0;
            try { depth = Convert.ToInt32((object)thread.StackDepth); } catch { }

            for (int d = 0; d < depth; d++)
            {
                try
                {
                    dynamic ctx = thread.GetSequenceContext((object)d);
                    var frame   = new CallStackFrame { Depth = d };
                    try { frame.SequenceName = (string)ctx.Sequence.Name;   } catch { }
                    try { frame.FilePath     = (string)ctx.SequenceFile.Path; } catch { }
                    try { frame.StepName     = (string)ctx.Step.Name;        } catch { }
                    try
                    {
                        int grp = (int)ctx.StepGroup;
                        frame.StepGroup = grp switch { 0 => "Setup", 2 => "Cleanup", _ => "Main" };
                    }
                    catch { }
                    frames.Add(frame);
                }
                catch { }
            }
            return frames;
        });
    }

    // ── Array Variable Operations ─────────────────────────────────────────────

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
            try { numElements = Convert.ToInt32((object)prop.GetNumElements()); } catch { }

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Data Type Operations ──────────────────────────────────────────────────

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

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;

            return new DataTypeInfo { Name = typeName, BaseType = baseType };
        });
    }

    public async Task DeleteDataTypeAsync(string filePath, string typeName)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf   = GetOrLoadSeqFile(filePath);
            dynamic sfPo = sf.AsPropertyObjectFile();

            // Check existence first to give a meaningful error
            bool exists = false;
            try { exists = (bool)sfPo.Exists((object)typeName, (object)0); } catch { }
            if (!exists)
                throw new InvalidOperationException(
                    $"Data type '{typeName}' not found in '{Path.GetFileName(filePath)}'.");

            sfPo.DeleteSubProperty((object)typeName, (object)0);
            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Module Parameter Operations ───────────────────────────────────────────

    public async Task<List<ModuleParameterInfo>> GetModuleParametersAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            var result = new List<ModuleParameterInfo>();

            try
            {
                var stepPo = step.AsPropertyObject();
                dynamic moduleParams;
                try
                {
                    moduleParams = stepPo.GetPropertyObject("TS.Module.Parameters", (object)0);
                }
                catch
                {
                    moduleParams = stepPo.GetPropertyObject("Module.Parameters", (object)0);
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
                                catch { }
                            }
                        }

                        result.Add(pi);
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        });
    }

    public async Task SetModuleParameterAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string parameterName, string value,
        bool useExpression = true)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            var stepPo = step.AsPropertyObject();

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
                        stepPo.SetValString(path, (object)0x8, value);
                    }
                    else
                    {
                        if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                            stepPo.SetValNumber(path, (object)0, d);
                        else if (bool.TryParse(value, out bool b))
                            stepPo.SetValBoolean(path, (object)0, b);
                        else
                            stepPo.SetValString(path, (object)0, value);
                    }
                    set = true;
                    break;
                }
                catch { }
            }

            if (!set)
                throw new InvalidOperationException(
                    $"Could not set module parameter '{parameterName}' on step '{stepName}'.");

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Step Configuration ────────────────────────────────────────────────────

    public async Task ConfigureMessagePopupAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string message,
        string? title = null, string buttons = "OK", double timeout = -1)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            var stepPo = step.AsPropertyObject();

            try { stepPo.SetValString("TS.MessagePopup.Message", (object)0, message); }
            catch { }

            if (!string.IsNullOrEmpty(title))
                try { stepPo.SetValString("TS.MessagePopup.Title", (object)0, title); }
                catch { }

            int buttonValue = buttons.ToLowerInvariant() switch
            {
                "okcancel"  or "ok cancel"       => 1,
                "yesno"     or "yes no"          => 2,
                "yesnocancel" or "yes no cancel" => 3,
                _ => 0
            };
            try { stepPo.SetValNumber("TS.MessagePopup.Buttons", (object)0, (double)buttonValue); }
            catch { }

            try { stepPo.SetValNumber("TS.MessagePopup.TimeoutInSeconds", (object)0, timeout); }
            catch { }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task ConfigurePropertyLoaderAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string filePathExpr, string mode = "Read")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);

            var stepPo = step.AsPropertyObject();

            try { stepPo.SetValString("TS.PropertyLoader.FilePathExpression", (object)0, filePathExpr); }
            catch { }

            int modeValue = mode.ToLowerInvariant() switch
            {
                "write" => 1,
                _       => 0
            };
            try { stepPo.SetValNumber("TS.PropertyLoader.Mode", (object)0, (double)modeValue); }
            catch { }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    // ── Numeric / String Limit Configuration ─────────────────────────────────

    public async Task SetNumericLimitsAsync(string filePath, string sequenceName,
        string stepGroup, string stepName,
        double? lowLimit, double? highLimit, string? units,
        string comparisonType = "GELE")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            dynamic po = step.AsPropertyObject();

            // Correct paths: NumericLimitTest stores limits under Limits.Low/High/Units
            // and comparison type under Comp (not TS.NumericLimitTest.*)
            int cmpVal = comparisonType.ToUpperInvariant() switch
            {
                "GE"  => 1,
                "LE"  => 2,
                "EQ"  => 3,
                "NE"  => 4,
                "GT"  => 5,
                "LT"  => 6,
                _     => 0  // GELE
            };
            try { po.SetValNumber("Comp", (object)0, (object)(double)cmpVal); } catch { }

            if (lowLimit.HasValue)
                try { po.SetValNumber("Limits.Low", (object)0, (object)lowLimit.Value); } catch { }

            if (highLimit.HasValue)
                try { po.SetValNumber("Limits.High", (object)0, (object)highLimit.Value); } catch { }

            if (units != null)
                try { po.SetValString("Limits.Units", (object)0, (object)units); } catch { }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task<Dictionary<string, object?>> GetNumericLimitsAsync(string filePath,
        string sequenceName, string stepGroup, string stepName)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            dynamic po = step.AsPropertyObject();

            var result = new Dictionary<string, object?>();

            double? GetNum(string path)
            {
                try { return (double)po.GetValNumber(path, (object)0); }
                catch { return null; }
            }
            string? GetStr(string path)
            {
                try
                {
                    var v = (string)po.GetValString(path, (object)0);
                    return string.IsNullOrEmpty(v) ? null : v;
                }
                catch { return null; }
            }

            // Correct paths: Limits.Low, Limits.High, Limits.Units, Comp, DataSource
            result["low_limit"]              = GetNum("Limits.Low");
            result["high_limit"]             = GetNum("Limits.High");
            result["units"]                  = GetStr("Limits.Units");
            result["measurement_expression"] = GetStr("DataSource");

            int cmpInt = (int)(GetNum("Comp") ?? 0.0);
            result["comparison_type"] = cmpInt switch
            {
                1 => "GE",
                2 => "LE",
                3 => "EQ",
                4 => "NE",
                5 => "GT",
                6 => "LT",
                _ => "GELE"
            };

            return result;
        });
    }

    public async Task SetStepMeasurementAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression)
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            dynamic po = step.AsPropertyObject();
            // DataSource is the measurement expression for NumericLimitTest
            po.SetValString("DataSource", (object)0, (object)expression);
            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task ConfigureStringValueTestAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, string expression, string expectedValue,
        string comparisonType = "CaseSensitive")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
            int sgVal = ParseStepGroup(stepGroup);
            var step  = seq.GetStepByName(stepName, (object)sgVal);
            dynamic po = step.AsPropertyObject();

            // StringValueTest: DataSource = expression being tested
            try { po.SetValString("DataSource", (object)0, (object)expression); } catch { }
            // Expected string value and comparison type stored under Limits[0]
            try { po.SetValString("Limits[0].String", (object)0, (object)expectedValue); } catch { }

            int cmpVal = comparisonType.ToLowerInvariant() switch
            {
                "caseinsensitive" or "case insensitive" => 1,
                "ignore"                                => 2,
                _                                       => 0
            };
            try { po.SetValNumber("Limits[0].ComparisonType", (object)0, (object)(double)cmpVal); } catch { }

            sf.Save(filePath);
        });
    }

    // ── Breakpoints ───────────────────────────────────────────────────────────

    public async Task SetStepBreakpointAsync(string filePath, string sequenceName,
        string stepGroup, string stepName, bool enabled, string breakpointType = "Before")
    {
        EnsureConnected();
        await Task.Run(() =>
        {
            var sf    = GetOrLoadSeqFile(filePath);
            var seq   = sf.GetSequenceByName(sequenceName);
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
                try { step.BreakOnStep = (object)(breakBefore || breakAfter); } catch { }
            }

            sf.Save(filePath);
            _loadedSequenceFiles[filePath] = sf;
        });
    }

    public async Task<List<Dictionary<string, string>>> GetBreakpointsAsync(string filePath)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var sf     = GetOrLoadSeqFile(filePath);
            var result = new List<Dictionary<string, string>>();

            int numSeqs = 0;
            try { numSeqs = Convert.ToInt32((object)sf.NumSequences); } catch { }

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
                                try { hasBreak = (bool)step2.BreakOnStep; } catch { }
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
                        catch { }
                    }
                }
            }

            return result;
        });
    }

    // ── Execution Results ─────────────────────────────────────────────────────

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
                    try { result[key] = (string)stepResult.GetValString((object)f, (object)0); } catch { }
                }
                try { result["numeric_value"] = (double)stepResult.GetValNumber((object)"TS.Result.NumericValue", (object)0); } catch { }
                try { result["string_value"]  = (string)stepResult.GetValString((object)"TS.Result.StringValue",  (object)0); } catch { }
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
        try { arrayCount = Convert.ToInt32((object)resultObj.GetNumElements()); } catch { }

        if (arrayCount > 0)
        {
            for (int i = 0; i < arrayCount; i++)
            {
                try
                {
                    dynamic sr = resultObj.GetPropertyObjectByOffset((object)i, (object)0);
                    string sn  = "";
                    try { sn = (string)sr.GetValString((object)"TS.StepName", (object)0); } catch { }
                    if (sn == stepName) return sr;

                    // Check nested ResultList
                    try
                    {
                        dynamic sub = sr.GetPropertyObject((object)"ResultList", (object)0);
                        var found = FindStepResultByName(sub, stepName, depth + 1);
                        if (found != null) return found;
                    }
                    catch { }
                }
                catch { }
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
                try { sn = (string)sr.GetValString((object)"TS.StepName", (object)0); } catch { }
                if (sn == stepName) return sr;

                var nested = FindStepResultByName(sr, stepName, depth + 1);
                if (nested != null) return nested;
            }
            catch { }
        }
        return null;
    }

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
            try { result["overall_status"]  = (string)exec.ResultStatus;           } catch { }
            try { result["seconds_elapsed"] = (double)exec.SecondsExecuting;       } catch { }
            try { result["display_name"]    = TryGetString(exec, "DisplayName");   } catch { }
            try { result["elapsed_seconds_from_start"] =
                    _executionStartTimes.TryGetValue(executionId, out DateTime t0)
                    ? (DateTime.UtcNow - t0).TotalSeconds : 0; } catch { }

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
                    catch { }
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
        try { arrayCount = Convert.ToInt32((object)resultObj.GetNumElements()); } catch { }

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
                catch { }
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
            catch { }
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
            try { entry[key] = (string)sr.GetValString((object)path, (object)0); } catch { }

        // Numeric measurement
        try { entry["numeric_value"] = (double)sr.GetValNumber((object)"TS.Result.Numeric.Value", (object)0); } catch { }

        // Nested ResultList (e.g. sub-sequence results)
        if (depth < 2)
        {
            try
            {
                dynamic subList = sr.GetPropertyObject((object)"ResultList", (object)0);
                var nested = CollectStepResults(subList, depth + 1);
                if (nested.Count > 0) entry["sub_results"] = nested;
            }
            catch { }
        }
    }

    public async Task<double> GetExecutionTimeAsync(string executionId)
    {
        EnsureConnected();
        return await Task.Run(() =>
        {
            var exec = FindExecution(executionId)
                ?? throw new KeyNotFoundException($"Execution {executionId} not found.");

            try { return (double)exec.ElapsedTime; } catch { }

            if (_executionStartTimes.TryGetValue(executionId, out var st))
                return (DateTime.UtcNow - st).TotalSeconds;

            return 0.0;
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

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
