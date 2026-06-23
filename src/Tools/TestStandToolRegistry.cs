using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TestStandMCP.Models;
using TestStandMCP.Services;
using Microsoft.Extensions.Logging;

namespace TestStandMCP.Tools;

/// <summary>Registers and dispatches all TestStand MCP tools.</summary>
public class TestStandToolRegistry
{
    private readonly ITestStandService _ts;
    private readonly ISequenceEditorService _seqEditor;
    private readonly ILogger<TestStandToolRegistry> _logger;
    private readonly Dictionary<string, McpTool> _tools = new();
    private readonly Dictionary<string, Func<JsonElement?, Task<CallToolResult>>> _handlers = new();
    private readonly IReadOnlyList<McpTool> _toolList;

    /// <summary>Creates the registry and registers all tools.</summary>
    public TestStandToolRegistry(ITestStandService ts, ISequenceEditorService seqEditor,
        ILogger<TestStandToolRegistry> logger)
    {
        _ts        = ts;
        _seqEditor = seqEditor;
        _logger    = logger;
        RegisterAll();
        // Tools are registered once in the constructor — snapshot the list so repeated
        // tools/list calls don't re-allocate it.
        _toolList = _tools.Values.ToList();
    }

    /// <summary>Returns all registered tools.</summary>
    public IReadOnlyList<McpTool> GetTools() => _toolList;

    /// <summary>Dispatches a <c>tools/call</c> request to the registered handler.</summary>
    public async Task<CallToolResult> CallToolAsync(string name, JsonElement? arguments)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            return Error($"Unknown tool: {name}");
        try
        {
            return await handler(arguments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Name} threw an exception", name);
            return Error($"Tool execution failed: {ex.Message}");
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────

    private void RegisterAll()
    {
        // Engine
        Register("connect_engine",
            "Connect to the NI TestStand engine. Must be called before any other tool.",
            s => s.AddOptional("engine_path", "string",
                "Optional path to TestStand engine DLL. Leave empty for default installation."),
            ConnectEngineAsync);

        Register("disconnect_engine",
            "Disconnect from the TestStand engine and release all resources.",
            s => { },
            DisconnectEngineAsync);

        Register("get_station_info",
            "Get information about the TestStand station: version, loaded files, active executions.",
            s => { },
            GetStationInfoAsync);

        // Sequence Files
        Register("open_sequence_file",
            "Open a TestStand sequence file (.seq) and return its structure.",
            s => s.AddRequired("file_path", "string",
                "Absolute path to the .seq sequence file"),
            OpenSequenceFileAsync);

        Register("close_sequence_file",
            "Close a loaded sequence file and release its resources.",
            s => s.AddRequired("file_path", "string", "Path to the sequence file to close"),
            CloseSequenceFileAsync);

        Register("get_loaded_sequence_files",
            "List all sequence files currently loaded in the TestStand engine. " +
            "Default 'summary' returns only file paths and sequence names/counts " +
            "(lightweight). Use detail='full' for the complete structure incl. steps, " +
            "locals and globals (large) — prefer get_sequence/get_steps for details.",
            s => s.AddOptional("detail", "string",
                "Level of detail: 'summary' (default, names + counts only) or 'full' " +
                "(complete recursive structure)."),
            GetLoadedSequenceFilesAsync);

        Register("get_sequence",
            "Get the detailed structure of a specific sequence including its steps and locals.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence to retrieve"),
            GetSequenceAsync);

        Register("save_sequence_file",
            "Save changes to an open sequence file back to disk.",
            s => s.AddRequired("file_path", "string", "Path to the sequence file to save"),
            SaveSequenceFileAsync);

        Register("create_sequence_file",
            "Create a new empty sequence file at the given path.",
            s => s.AddRequired("file_path", "string", "Absolute path for the new sequence file"),
            CreateSequenceFileAsync);

        Register("insert_sequence",
            "Insert a new named sequence into an open sequence file and save it.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the new sequence to insert"),
            InsertSequenceAsync);

        Register("insert_step",
            "Insert a new step into a sequence. " +
            "PREFERRED step types for conditional branching (if/else): " +
            "'NI_Flow_If' (condition), 'NI_Flow_ElseIf', 'NI_Flow_Else', 'NI_Flow_End' — " +
            "ALWAYS use these for if/else logic. NEVER use Goto/Label for branching. " +
            "Loop step types: 'NI_Flow_While', 'NI_Flow_DoWhile', 'NI_Flow_For', 'NI_Flow_ForEach', " +
            "'NI_Flow_SweepLoop', 'NI_Flow_StreamLoop', 'NI_Flow_End'. " +
            "Other step types: 'Statement', 'NumericLimitTest', 'StringValueTest', 'PassFailTest', " +
            "'MessagePopup', 'CallExecutable', 'SequenceCall', 'Action'. " +
            "FORBIDDEN unless exceptional: 'Goto', 'Label' — legacy only, not for if/else or loops. " +
            "Adapters: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'None' (default).",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_type", "string", "Step type. Use 'NI_Flow_If'/'NI_Flow_Else'/'NI_Flow_End' for branching, never 'Goto'/'Label'.")
                .AddRequired("step_name", "string", "Name for the new step")
                .AddOptional("index", "integer", "Insert position (default: append at end)", -1)
                .AddOptional("adapter", "string", "Adapter name: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'None' (default)"),
            InsertStepAsync);

        Register("insert_steps_bulk",
            "Insert MANY steps into ONE sequence in a single call — far more efficient than " +
            "calling insert_step repeatedly (the file is saved only ONCE for the whole batch). " +
            "Steps are appended in array order, so list them top-to-bottom as they should appear. " +
            "Each step may optionally carry its own comment, expression and SequenceCall target, " +
            "collapsing what used to be ~4 calls per step into one. Use this to build a whole " +
            "sequence (or a complete If/Else/loop block) at once. Same step-type and adapter rules " +
            "as insert_step: use 'NI_Flow_If'/'NI_Flow_Else'/'NI_Flow_End' for branching, never Goto/Label.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence to build")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddArray("steps",
                    "Ordered list of steps to append. Each item: {step_name, step_type} required; " +
                    "optional adapter, comment, expression, expression_type, " +
                    "target_sequence_name, target_sequence_file.",
                    item => item
                        .AddRequired("step_name", "string", "Name for the step")
                        .AddRequired("step_type", "string",
                            "Step type, e.g. 'SequenceCall', 'NI_Flow_If', 'NI_Flow_End', 'NI_Wait', 'Statement'.")
                        .AddOptional("adapter", "string",
                            "Adapter: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'None' (default)")
                        .AddOptional("comment", "string", "Step comment/description (kept short)")
                        .AddOptional("expression", "string",
                            "Step expression (e.g. an NI_Flow_If condition)")
                        .AddOptional("expression_type", "string",
                            "Where to store it: 'Statement' (default) -> Post Expression (primary for Statement steps); 'Pre' -> before the step; 'Post' -> after the step; 'Status' -> status expression.")
                        .AddOptional("target_sequence_name", "string",
                            "For SequenceCall steps: name of the sequence to call")
                        .AddOptional("target_sequence_file", "string",
                            "Target sequence file (empty/omitted = same/current file)"))
                .AddOptional("save", "boolean",
                    "Save the file after the batch (default true). Set false to chain several bulk calls.", true),
            InsertStepsBulkAsync);

        Register("validate_sequence_plan",
            "Validate a sequence BUILD-PLAN before writing it to TestStand (Phase-3 gate). " +
            "Deterministic, engine-independent structural check — run it on the exact same " +
            "'steps' array you will pass to insert_steps_bulk, plus the planned local-variable " +
            "names. Returns {valid, errorCount, warningCount, errors[], warnings[], stats{}}. " +
            "ONLY proceed to build when valid==true (errors block the build; warnings are " +
            "advisory — e.g. unlinked SequenceCall placeholders). Checks: balanced NI_Flow_* " +
            "blocks (openers ↔ End), ElseIf/Else inside If, Case inside Select, Break/Continue " +
            "inside a loop, no Goto/Label, unique step names, known step types, and that every " +
            "Locals.X referenced in a condition is declared in 'locals'.",
            s => s
                .AddRequired("sequence_name", "string", "Name of the sequence the plan builds")
                .AddArray("steps",
                    "The ordered build-plan steps — identical shape to insert_steps_bulk: " +
                    "{step_name, step_type} required; optional expression, target_sequence_name, " +
                    "target_sequence_file, comment.",
                    item => item
                        .AddRequired("step_name", "string", "Name for the step")
                        .AddRequired("step_type", "string", "Step type, e.g. 'SequenceCall', 'NI_Flow_If'.")
                        .AddOptional("expression", "string", "Condition/expression (e.g. an NI_Flow_If condition)")
                        .AddOptional("target_sequence_name", "string", "For SequenceCall: target sequence name")
                        .AddOptional("target_sequence_file", "string", "For SequenceCall: target file")
                        .AddOptional("comment", "string", "Step comment"))
                .AddArray("locals",
                    "Planned local variables (names only are required for the reference check).",
                    item => item
                        .AddRequired("name", "string", "Local variable name"),
                    required: false),
            ValidateSequencePlanAsync);

        Register("insert_local_variable",
            "Insert a new local variable into a sequence. data_type accepts the builtins " +
            "'string'/'number'/'boolean' OR the name of a custom data type / enum defined in the " +
            "file (e.g. 'MyEnum') — anything that isn't a builtin is treated as a named type. " +
            "To create an ARRAY local (required before get_array_variable/set_array_element/" +
            "resize_array_variable can be used), append '[]' to the type (e.g. 'number[]') or " +
            "prefix 'array:' (e.g. 'array:string').",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Name of the local variable")
                .AddRequired("data_type", "string",
                    "Data type: 'string', 'number', 'boolean', or the name of a custom/enum type " +
                    "in the file. Append '[]' (or prefix 'array:') for an array, e.g. 'number[]'.")
                .AddOptional("default_value", "string", "Optional default value"),
            InsertLocalVariableAsync);

        Register("set_local_variable_comment",
            "Set the comment/description of a local variable in a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Name of the local variable")
                .AddRequired("comment", "string", "Comment text to set"),
            SetLocalVariableCommentAsync);

        Register("get_local_variables",
            "Get all local variables of a sequence with their names, types and current values.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence"),
            GetLocalVariablesAsync);

        Register("set_local_variable",
            "Set the value of a local variable in a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Name of the local variable")
                .AddRequired("value", "string", "Value to set"),
            SetLocalVariableAsync);

        Register("set_step_expression",
            "Set the expression of a step. For a Statement step the expression's primary home is the Post Expression — the default 'Statement' (or unspecified) type writes there. Use 'Pre' for an expression that must run BEFORE the step executes, 'Post' for AFTER, 'Status' for the status expression.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("expression", "string", "Expression to set")
                .AddOptional("expression_type", "string",
                    "Where to store it: 'Statement' (default) -> the step's Post Expression (primary slot for Statement steps, runs after the step); 'Pre' -> runs BEFORE the step; 'Post' -> runs AFTER the step; 'Status' -> the status expression."),
            SetStepExpressionAsync);

        Register("set_sequence_call_target",
            "Set the target sequence (and optionally sequence file) for a Sequence Call step.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the Sequence Call step")
                .AddRequired("target_sequence_name", "string", "Name of the sequence to call")
                .AddOptional("target_sequence_file", "string",
                    "Path to the target sequence file (empty = same file)"),
            SetSequenceCallTargetAsync);

        Register("set_step_module_path",
            "Set the module path (e.g. VI path for LabVIEW, DLL for CVI) of a step.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("module_path", "string", "Path to the module file (e.g. C:\\path\\to\\test.vi)"),
            SetStepModulePathAsync);

        Register("run_sequence_analyzer",
            "Run the NI TestStand Sequence Analyzer on a sequence file and return all messages, " +
            "grouped by severity by default (like the editor's Analysis Results 'Group By' pane). " +
            "Use group_by='rule' to group by rule, or group_by='none' for a flat sorted list.",
            s => s
                .AddRequired("file_path", "string", "Absolute path to the sequence file to analyze")
                .AddOptional("group_by", "string",
                    "How to group the output: 'severity' (default), 'rule', or 'none' for a flat list.",
                    "severity", new[] { "severity", "rule", "none" }),
            RunSequenceAnalyzerAsync);

        // Executions
        Register("start_execution",
            "Start a TestStand execution. Returns an execution ID for polling. A client sequence " +
            "name (e.g. 'MainSequence') runs that sequence directly; a station process-model entry " +
            "point ('Single Pass' or 'Test UUTs', spaces/casing optional) runs the client THROUGH " +
            "the process model — which is what populates step results and generates the report. " +
            "HEADLESS NOTE: 'Test UUTs' pauses waiting for the UUT serial-number dialog (no UI to " +
            "answer it) — use 'Single Pass' for unattended runs.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("entry_point", "string",
                    "A sequence name in the file (e.g. 'MainSequence') → runs directly; or a process-" +
                    "model entry point 'Single Pass' / 'Test UUTs' (spaces/casing optional) → runs " +
                    "the client through the process model.")
                .AddOptional("parameters", "object",
                    "Optional key-value parameters passed to the execution"),
            StartExecutionAsync);

        Register("wait_for_execution",
            "Wait for a running execution to complete and return the full result.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID returned by start_execution")
                .AddOptional("timeout_seconds", "integer",
                    "Maximum seconds to wait (default: 300)", 300),
            WaitForExecutionAsync);

        Register("get_execution_status",
            "Get the current status of a running or recently completed execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID to query"),
            GetExecutionStatusAsync);

        Register("get_active_executions",
            "List all currently active (running or paused) executions.",
            s => { },
            GetActiveExecutionsAsync);

        Register("terminate_execution",
            "Forcefully terminate a running execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID to terminate"),
            TerminateExecutionAsync);

        Register("run_sequence",
            "Run a sequence synchronously and return the complete result. Convenience wrapper around " +
            "start_execution + wait_for_execution. Also accepts a process-model entry point ('Single " +
            "Pass' runs the client through the model and returns step results; 'Test UUTs' pauses " +
            "headless and will hit the wait timeout).",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence (or process-model entry point) to run")
                .AddOptional("parameters", "object", "Optional execution parameters")
                .AddOptional("timeout_seconds", "integer", "Timeout in seconds (default: 300)", 300),
            RunSequenceAsync);

        // Variables & Properties
        Register("get_property",
            "Get the value of a TestStand property using its lookup string " +
            "(e.g. 'Locals.Counter', 'RunState.Root.UUT.SerialNumber').",
            s => s.AddRequired("lookup_string", "string",
                "TestStand property lookup string"),
            GetPropertyAsync);

        Register("set_property",
            "Set the value of a TestStand property using its lookup string.",
            s => s
                .AddRequired("lookup_string", "string", "TestStand property lookup string")
                .AddRequired("value", "string",
                    "Value to set (numbers, booleans, and strings all accepted as strings)"),
            SetPropertyAsync);

        Register("get_file_globals",
            "Get all FileGlobal variables defined in a sequence file.",
            s => s.AddRequired("sequence_file_path", "string", "Path to the sequence file"),
            GetFileGlobalsAsync);

        Register("get_station_globals",
            "Get all StationGlobal variables from the TestStand engine.",
            s => { },
            GetStationGlobalsAsync);

        Register("get_property_tree",
            "Export a TestStand property tree RECURSIVELY as a nested structure. Each node " +
            "carries name, type, valueType ('Container'/'Array'/'Number'/'Boolean'/'String'/" +
            "'Empty'), scalar value (leaves), hidden flags (isHidden), array info and children. " +
            "Walks both container members and array elements. Hidden subproperties are INCLUDED " +
            "by default and annotated via 'isHidden' (set include_hidden=false to omit them). " +
            "Roots: 'StationGlobals' (engine.Globals, default), 'FileGlobals' (a sequence file's " +
            "file globals) or 'SequenceFile' (the WHOLE file as a property tree via " +
            "AsPropertyObject — every sequence, step and parameter, the richest/largest tree). " +
            "'FileGlobals' and 'SequenceFile' require file_path. Use lookup_string to start at a " +
            "sub-path. Bounded by max_depth, max_array_elements and an internal node budget " +
            "('truncated'=true marks cut-offs).",
            s => s
                .AddOptional("root", "string",
                    "Root property object to dump: 'StationGlobals' (default), 'FileGlobals' or " +
                    "'SequenceFile'.",
                    "StationGlobals",
                    new[] { "StationGlobals", "FileGlobals", "SequenceFile" })
                .AddOptional("file_path", "string",
                    "Path to the sequence file (required when root='FileGlobals' or 'SequenceFile').")
                .AddOptional("lookup_string", "string",
                    "Optional sub-path to start at within the root (e.g. 'MyContainer.Sub').")
                .AddOptional("max_depth", "integer",
                    "Maximum recursion depth (default 25).", 25)
                .AddOptional("include_hidden", "boolean",
                    "Include hidden subproperties (PropFlags_Hidden). Default true.", true)
                .AddOptional("max_array_elements", "integer",
                    "Max array elements expanded per array; 0 = unlimited (default 500).", 500),
            GetPropertyTreeAsync);

        Register("insert_file_global",
            "Insert a new FileGlobal variable into a sequence file. To create an ARRAY file " +
            "global (required before the array tools can operate on it), append '[]' to the " +
            "type (e.g. 'number[]') or prefix 'array:' (e.g. 'array:string').",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("variable_name", "string", "Name of the new FileGlobal variable")
                .AddRequired("data_type", "string", "Data type: 'string', 'number', 'boolean'. " +
                    "Append '[]' (or prefix 'array:') for an array, e.g. 'number[]', 'array:string'."),
            InsertFileGlobalAsync);

        Register("set_file_global",
            "Set the value of a FileGlobal variable in a sequence file.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("variable_name", "string", "Name of the FileGlobal variable")
                .AddRequired("value", "string", "New value"),
            SetFileGlobalAsync);

        Register("set_station_global",
            "Set the value of a StationGlobal variable. The change is committed to StationGlobals.ini " +
            "on disk so it persists across engine restarts (creates the global if it does not exist).",
            s => s
                .AddRequired("variable_name", "string", "Name of the StationGlobal variable")
                .AddRequired("value", "string", "New value"),
            SetStationGlobalAsync);

        Register("delete_station_global",
            "Delete a StationGlobal variable and commit the removal to StationGlobals.ini on disk so it " +
            "persists (no-op if the global does not exist). Prefer this over an evaluate_expression " +
            "'DeleteSubProperty', which only edits the in-memory copy and is lost on the next restart.",
            s => s
                .AddRequired("variable_name", "string", "Name of the StationGlobal variable to delete"),
            DeleteStationGlobalAsync);

        // Steps
        Register("get_steps",
            "Get all steps of a sequence (Setup, Main and Cleanup groups). " +
            "Compact output: fields are OMITTED when they hold their default — " +
            "absent 'enabled' means the step is enabled (only 'enabled:false' is " +
            "written, for skipped steps); absent 'stepGroup' means the Main group " +
            "('setup'/'cleanup' written explicitly); 'subSteps'/'properties' are " +
            "absent when empty.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence"),
            GetStepsAsync);

        Register("get_step",
            "Get detailed information about a single step.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepAsync);

        Register("enable_step",
            "Enable or disable a step in a sequence.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("enabled", "boolean", "True to enable, false to disable"),
            EnableStepAsync);

        Register("get_step_properties",
            "Get all properties of a specific step.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepPropertiesAsync);

        // Reports
        Register("generate_report",
            "Generate a test report for a completed execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID to report on")
                .AddRequired("output_path", "string", "File path where the report will be saved")
                .AddOptional("format", "string",
                    "Report format: 'HTML', 'XML', 'TXT', 'ATML'", "HTML",
                    new[] { "HTML", "XML", "TXT", "ATML" }),
            GenerateReportAsync);

        Register("get_report_text",
            "Get the text content of the report for a (possibly still running) execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            GetReportTextAsync);

        // UUT
        Register("get_uut_info",
            "Get Unit Under Test (UUT) information for an execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            GetUutInfoAsync);

        Register("set_uut_serial_number",
            "Set the serial number of the UUT for a running execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("serial_number", "string", "UUT serial number"),
            SetUutSerialNumberAsync);

        Register("set_uut_part_number",
            "Set the part number of the UUT for a running execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("part_number", "string", "UUT part number"),
            SetUutPartNumberAsync);

        // Adapters
        Register("get_loaded_adapters",
            "List all step type adapters currently loaded in TestStand.",
            s => { },
            GetLoadedAdaptersAsync);

        Register("load_adapter",
            "Load a step type adapter by name (e.g. 'CVI', 'LabWindows', 'LabVIEW', '.NET').",
            s => s.AddRequired("adapter_name", "string",
                "Name of the adapter to load"),
            LoadAdapterAsync);

        Register("unload_adapter",
            "Unload a step type adapter by name.",
            s => s.AddRequired("adapter_name", "string",
                "Name of the adapter to unload"),
            UnloadAdapterAsync);

        // Logging
        Register("get_execution_log",
            "Retrieve log entries for a specific execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddOptional("max_entries", "integer",
                    "Maximum number of log entries to return (default: 100)", 100),
            GetExecutionLogAsync);

        Register("clear_execution_log",
            "Clear the log entries for a specific execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            ClearExecutionLogAsync);

        // Process Model
        Register("get_process_model",
            "Get the path of the current TestStand process model sequence file.",
            s => { },
            GetProcessModelAsync);

        Register("set_process_model",
            "Change the active process model sequence file.",
            s => s.AddRequired("process_model_path", "string",
                "Absolute path to the process model .seq file"),
            SetProcessModelAsync);

        // Result Schemas / DB
        Register("get_result_schemas",
            "List all configured result database schemas.",
            s => { },
            GetResultSchemasAsync);

        Register("export_results",
            "Export test results using a named result schema.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("schema_name", "string", "Name of the result schema to use")
                .AddRequired("output_path", "string", "Output file path"),
            ExportResultsAsync);

        // Type Palettes
        Register("get_type_palettes",
            "List all currently loaded TestStand type palette files with their step type names.",
            s => { },
            GetTypePalettesAsync);

        Register("load_type_palette",
            "Load a TestStand type palette file (.ini) into the engine.",
            s => s.AddRequired("palette_path", "string",
                "Absolute path to the type palette file (.ini)"),
            LoadTypePaletteAsync);

        Register("unload_type_palette",
            "Unload a TestStand type palette file from the engine.",
            s => s.AddRequired("palette_path", "string",
                "Absolute path to the type palette file to unload"),
            UnloadTypePaletteAsync);

        Register("get_step_types",
            "List all available step types from loaded type palettes. " +
            "Optionally filter by palette file path.",
            s => s.AddOptional("palette_file", "string",
                "Optional path to a palette file to filter results"),
            GetStepTypesAsync);

        Register("get_step_type",
            "Get detailed information about a specific step type by name.",
            s => s.AddRequired("step_type_name", "string", "Name of the step type"),
            GetStepTypeAsync);

        Register("get_data_types",
            "List all custom data types. Optionally pass a sequence file path to get " +
            "data types defined in that file; otherwise returns engine-level data types.",
            s => s.AddOptional("sequence_file_path", "string",
                "Optional path to a sequence file to read data types from"),
            GetDataTypesAsync);

        // Sequence Editor
        Register("launch_sequence_editor",
            "Launch the NI TestStand Sequence Editor (SeqEdit.exe). " +
            "If the editor is already running, connects to the existing instance.",
            s => s.AddOptional("seqedit_path", "string",
                "Optional explicit path to SeqEdit.exe. If omitted, the tool searches " +
                "standard NI installation directories and environment variables."),
            LaunchSequenceEditorAsync);

        Register("get_editor_status",
            "Get the current status of the TestStand Sequence Editor process " +
            "(running, PID, window title).",
            s => { },
            GetEditorStatusAsync);

        Register("open_file_in_editor",
            "Open a sequence file (.seq) in the TestStand Sequence Editor GUI for " +
            "visual inspection and interactive use. Launches the editor if not running.",
            s => s.AddRequired("file_path", "string",
                "Absolute path to the .seq file to open in the editor"),
            OpenFileInEditorAsync);

        Register("run_in_editor",
            "Open a sequence file in the Sequence Editor and start execution with the " +
            "specified entry point. The execution is shown in the editor GUI.",
            s => s
                .AddRequired("sequence_file_path", "string",
                    "Absolute path to the .seq file")
                .AddRequired("entry_point", "string",
                    "Entry point: 'Test UUTs', 'Single Pass', or a custom sequence name"),
            RunInEditorAsync);

        Register("close_sequence_editor",
            "Close the TestStand Sequence Editor application.",
            s => s.AddOptional("force", "boolean",
                "If true, forcefully terminates the editor process. " +
                "Default: false (graceful close via window message)", false),
            CloseSequenceEditorAsync);

        // Engine Info & Control
        Register("get_engine_paths",
            "Get TestStand engine directory paths, version, and station identification.",
            s => { },
            GetEnginePathsAsync);

        Register("check_expression",
            "Validate a TestStand expression for syntax correctness. " +
            "NOTE: the engine's CheckExprSyntax needs a LOADED sequence file as context — " +
            "in practice pass 'sequence_file_path' pointing at an already created/open file " +
            "(create_sequence_file first). Without a loaded file even a valid expression can " +
            "fail to validate.",
            s => s
                .AddRequired("expression", "string", "TestStand expression to validate")
                .AddOptional("sequence_file_path", "string",
                    "Path to a loaded sequence file used as evaluation context. " +
                    "Effectively required — the file must already be open/created."),
            CheckExpressionAsync);

        Register("evaluate_expression",
            "Evaluate a TestStand expression and return its computed value (not just a syntax " +
            "check). The expression is evaluated in the StationGlobals context by default, or in " +
            "a sequence file's FileGlobals context when 'sequence_file_path' is given. It can " +
            "reference variables in that context by name plus literals, operators and built-in " +
            "expression functions (e.g. 'Str(123) + \"V\"', 'MyGlobal * 2').",
            s => s
                .AddRequired("expression", "string", "TestStand expression to evaluate")
                .AddOptional("sequence_file_path", "string",
                    "Optional sequence file path — evaluate in its FileGlobals context"),
            EvaluateExpressionAsync);

        Register("list_expression_reference",
            "Look up TestStand expression-language building blocks — operators, constants and " +
            "built-in functions — instead of guessing or trial-and-error. Returns a categorised, " +
            "searchable catalogue (name, signature, description, example) mirroring the Expression " +
            "Browser's Operators / Constants / Functions groups. Function entries are confirmed to " +
            "exist live in the engine, and key gotchas are baked into the notes (e.g. Round's 2nd " +
            "arg is a rounding MODE, not decimal places; there is no Floor/Ceil/Mod — use % and " +
            "Str; '^' is bitwise XOR, use Pow for powers). Pure static reference — needs no engine " +
            "connection. Use it before writing any expression for set_step_expression, " +
            "evaluate_expression, NI_Flow_If conditions, etc.",
            s => s
                .AddOptional("kind", "string",
                    "Filter by group: 'operator', 'constant' or 'function' (singular or plural, " +
                    "case-insensitive). Omit for all groups.")
                .AddOptional("category", "string",
                    "Filter by category, e.g. 'Arithmetic', 'Bitwise', 'Comparison', 'Logical', " +
                    "'Numeric', 'String', 'Conversion', 'Array'. Omit for all categories.")
                .AddOptional("search", "string",
                    "Case-insensitive substring matched against name, signature, category, " +
                    "description and note (e.g. 'round', 'array', 'shift')."),
            ListExpressionReferenceAsync);

        Register("expand_path_macros",
            "Expand TestStand path macros (e.g. <TestStand>) in a path string.",
            s => s.AddRequired("path", "string", "Path string containing macros to expand"),
            ExpandPathMacrosAsync);

        Register("find_file",
            "Search for a file using the TestStand file search path.",
            s => s.AddRequired("filename", "string", "Filename to search for"),
            FindFileAsync);

        Register("break_all",
            "Break (pause) all active TestStand executions.",
            s => { },
            BreakAllAsync);

        Register("abort_all",
            "Abort all active TestStand executions.",
            s => { },
            AbortAllAsync);

        Register("terminate_all",
            "Terminate all active TestStand executions immediately.",
            s => { },
            TerminateAllAsync);

        Register("get_station_options",
            "Get the current TestStand station options (tracing, breakpoints, process model, etc.).",
            s => { },
            GetStationOptionsAsync);

        Register("set_station_options",
            "Set TestStand station options.",
            s => s
                .AddOptional("tracing_enabled", "boolean", "Enable or disable execution tracing")
                .AddOptional("breakpoints_enabled", "boolean", "Enable or disable breakpoints")
                .AddOptional("disable_results", "boolean", "Disable result collection")
                .AddOptional("always_goto_cleanup_on_failure", "boolean",
                    "Always go to Cleanup on failure")
                .AddOptional("break_on_rte", "boolean", "Break on run-time error"),
            SetStationOptionsAsync);

        // Execution Debug Control
        Register("break_execution",
            "Pause a running TestStand execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID to pause"),
            BreakExecutionAsync);

        Register("resume_execution",
            "Resume a paused TestStand execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID to resume"),
            ResumeExecutionAsync);

        Register("abort_execution",
            "Abort a running TestStand execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID to abort"),
            AbortExecutionAsync);

        Register("restart_execution",
            "Restart a TestStand execution from the beginning.",
            s => s.AddRequired("execution_id", "string", "Execution ID to restart"),
            RestartExecutionAsync);

        Register("step_over",
            "Execute one step and pause at the next step (step over sub-sequences).",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            StepOverAsync);

        Register("step_into",
            "Execute one step and step into sub-sequences if applicable.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            StepIntoAsync);

        Register("step_out",
            "Execute until the current sequence returns, then pause.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            StepOutAsync);

        // Sequence File Operations
        Register("delete_sequence",
            "Delete a sequence from a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence to delete"),
            DeleteSequenceAsync);

        Register("sequence_name_exists",
            "Check whether a sequence with the given name exists in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Sequence name to check"),
            SequenceNameExistsAsync);

        Register("rename_sequence",
            "Rename a sequence in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("old_name", "string", "Current sequence name")
                .AddRequired("new_name", "string", "New sequence name"),
            RenameSequenceAsync);

        // Sequence Operations
        Register("delete_step",
            "Delete a step from a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step to delete"),
            DeleteStepAsync);

        Register("move_step",
            "Move a step to a new position within its step group.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step to move")
                .AddRequired("new_index", "integer", "Target zero-based index"),
            MoveStepAsync);

        Register("step_name_exists",
            "Check whether a step with the given name exists in a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_name", "string", "Step name to check"),
            StepNameExistsAsync);

        Register("get_sequence_parameters",
            "Get all parameters defined for a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence"),
            GetSequenceParametersAsync);

        Register("insert_sequence_parameter",
            "Add a parameter to a sequence. Parameters are passed BY VALUE by default; set " +
            "pass_by_reference=true to pass BY REFERENCE so the called sequence can write back to " +
            "the caller's variable. In TestStand this toggles the PropFlags_PassByReference flag.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("param_name", "string", "Parameter name")
                .AddRequired("data_type", "string",
                    "Data type: 'string', 'number', 'boolean'")
                .AddOptional("pass_by_reference", "boolean",
                    "Pass BY REFERENCE (true) or BY VALUE (false, default). By reference lets the called " +
                    "sequence modify the caller's variable; by value passes a copy. Takes precedence over 'direction'.")
                .AddOptional("direction", "string",
                    "Legacy pass-mode selector — prefer pass_by_reference. 'Input'/'Output' → by value; " +
                    "'InOut' (aliases 'byref'/'passbyreference') → by reference. Ignored when pass_by_reference is set.")
                .AddOptional("default_value", "string", "Optional default value"),
            InsertSequenceParameterAsync);

        Register("delete_local_variable",
            "Delete a local variable from a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Name of the local variable to delete"),
            DeleteLocalVariableAsync);

        Register("get_step_templates",
            "List all step templates defined in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file"),
            GetStepTemplatesAsync);

        Register("insert_step_from_template",
            "Insert a step from a sequence file's step template into a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("template_name", "string", "Name of the step template")
                .AddRequired("new_step_name", "string", "Name for the inserted step")
                .AddOptional("index", "number", "Insert position (default: append at end)"),
            InsertStepFromTemplateAsync);

        Register("get_sequence_properties",
            "Get properties of a sequence (failure action, cleanup settings, etc.).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence"),
            GetSequencePropertiesAsync);

        Register("set_sequence_properties",
            "Set properties of a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddOptional("description", "string", "Description/comment for the sequence")
                .AddOptional("goto_cleanup_on_failure", "boolean",
                    "Go to Cleanup group when a step fails")
                .AddOptional("disable_results", "boolean",
                    "Disable result collection for this sequence")
                .AddOptional("failure_action", "string",
                    "Action on failure: 'Continue', 'Terminate', 'Abort'"),
            SetSequencePropertiesAsync);

        // Step Property Operations
        Register("rename_step",
            "Rename a step in a sequence.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Current step name")
                .AddRequired("new_name", "string", "New step name"),
            RenameStepAsync);

        Register("set_step_comment",
            "Set the comment/description text on a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("comment", "string", "Comment text"),
            SetStepCommentAsync);

        Register("set_step_run_mode",
            "Set the run mode of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("run_mode", "string",
                    "Run mode: 'Normal', 'Skip', 'ForcedPass', 'ForcedFail'"),
            SetStepRunModeAsync);

        Register("set_step_precondition",
            "Set the precondition expression of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("precondition", "string", "Precondition expression"),
            SetStepPreconditionAsync);

        Register("set_step_pass_action",
            "Set the post-execution pass action of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("pass_action", "string",
                    "Action: 'NextStep', 'Break', 'Terminate', 'GoToStep'")
                .AddOptional("target", "string", "Target step name for 'GoToStep' action"),
            SetStepPassActionAsync);

        Register("set_step_fail_action",
            "Set the post-execution fail action of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("fail_action", "string",
                    "Action: 'NextStep', 'Break', 'Terminate', 'GoToStep'")
                .AddOptional("target", "string", "Target step name for 'GoToStep' action"),
            SetStepFailActionAsync);

        Register("set_step_loop",
            "Configure the loop settings for a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("loop_type", "string",
                    "Loop type: 'NoLoop', 'While', 'For', 'Condition'")
                .AddOptional("init_expr", "string", "Initialiser expression (For loop)")
                .AddOptional("while_expr", "string", "While/condition expression")
                .AddOptional("inc_expr", "string", "Increment expression (For loop)"),
            SetStepLoopAsync);

        Register("set_step_record_result",
            "Set result recording mode for a step. " +
            "Options: 'Disabled' (0), 'Enabled' (1), 'EnabledOverride' (2 = Enabled and override sequence setting).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("record_result", "string",
                    "Recording mode: 'Disabled', 'Enabled', or 'EnabledOverride' (overrides sequence-level disable)"),
            SetStepRecordResultAsync);

        Register("set_step_eval_precond",
            "Set the 'Evaluate Precondition for Interactive Execution' option of a step. " +
            "Options: 'UseStationOption' (0), 'EvaluatePrecond' (1), 'NoEvaluatePrecond' (2).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("option", "string",
                    "EvalPrecond option: 'UseStationOption', 'EvaluatePrecond', or 'NoEvaluatePrecond'"),
            SetStepEvalPrecondAsync);

        Register("set_step_module_load_option",
            "Set the module load option of a step. " +
            "Options: 'PreloadWhenOpened' (1), 'PreloadWhenExecuted' (2), 'DynamicLoad' (3), 'UseStepLoadOption' (4).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("option", "string",
                    "Load option: 'PreloadWhenOpened', 'PreloadWhenExecuted', 'DynamicLoad', or 'UseStepLoadOption'"),
            SetStepModuleLoadOptionAsync);

        Register("set_step_module_unload_option",
            "Set the module unload option of a step. " +
            "Options: 'OnPreconditionFailure' (1), 'AfterStepExecution' (2), 'AfterSequenceExecution' (3), 'WithSequenceFile' (4), 'UseStepUnloadOption' (5). " +
            "CAVEAT: 'UseStepUnloadOption' (5) is only valid at the sequence-file / model level — " +
            "TestStand REJECTS it on an individual step. Use one of (1)-(4) for a per-step setting.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("option", "string",
                    "Unload option: 'OnPreconditionFailure', 'AfterStepExecution', 'AfterSequenceExecution', 'WithSequenceFile', or 'UseStepUnloadOption'"),
            SetStepModuleUnloadOptionAsync);

        Register("set_step_batch_sync_option",
            "Set the batch synchronization option of a step. " +
            "Options: 'UseSeqFileSetting' (0), 'UseModelSetting' (1), 'NoSync' (2), 'Serial' (3), 'Parallel' (4), 'OneThreadOnly' (5).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("option", "string",
                    "Batch sync option: 'UseSeqFileSetting', 'UseModelSetting', 'NoSync', 'Serial', 'Parallel', or 'OneThreadOnly'"),
            SetStepBatchSyncOptionAsync);

        Register("change_step_adapter",
            "Change the adapter (LabVIEW, CVI, C++/DLL, .NET, Python, ActiveX/COM, None, etc.) of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("new_adapter", "string",
                    "Adapter: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'None' " +
                    "(friendly name or exact key name, e.g. 'Automation Adapter' for ActiveX, " +
                    "'DLL Flexible Prototype Adapter' for C++/DLL)"),
            ChangeStepAdapterAsync);

        Register("get_step_unique_id",
            "Get the unique persistent ID of a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepUniqueIdAsync);

        // Report Operations
        Register("save_report",
            "Save the report of a completed execution to a file.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("output_path", "string", "File path to save the report to")
                .AddOptional("format", "string",
                    "Report format: 'HTML' (default), 'XML', 'TXT'", "HTML"),
            SaveReportAsync);

        Register("launch_report_viewer",
            "Open the report viewer for a completed execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            LaunchReportViewerAsync);

        Register("get_full_report",
            "Get the full text content of the report for a completed execution.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            GetFullReportAsync);

        // ── Undo/Redo Stack ───────────────────────────────────────────────────

        Register("get_undo_stack",
            "Get the current undo/redo stack status: whether undo/redo is available, " +
            "and the list of undo and redo items with their names. " +
            "Pass file_path for a file-level undo stack, omit for the engine-level stack. " +
            "NOTE: the headless Engine API does NOT auto-record edits made via these MCP tools — " +
            "automatic undo recording is a Sequence Editor feature. On a freshly created file " +
            "CanUndo is false and there is nothing to undo. Do not rely on undo to revert MCP edits.",
            s => s.AddOptional("file_path", "string",
                "Optional sequence file path for a file-level undo stack"),
            GetUndoStackAsync);

        Register("undo",
            "Undo the last operation on the undo stack. " +
            "Returns true if an undo was performed, false if nothing to undo. " +
            "NOTE: edits made through the headless MCP tools are NOT auto-recorded onto the " +
            "undo stack, so this will normally return false right after such edits. To revert " +
            "an MCP edit, perform the inverse operation explicitly.",
            s => s.AddOptional("file_path", "string",
                "Optional sequence file path for a file-level undo"),
            UndoAsync);

        Register("redo",
            "Redo the last undone operation. " +
            "Returns true if a redo was performed, false if nothing to redo.",
            s => s.AddOptional("file_path", "string",
                "Optional sequence file path for a file-level redo"),
            RedoAsync);

        Register("begin_undo_group",
            "Begin an undo group. All subsequent operations will be grouped into a single " +
            "undo item with the given name. Must be followed by end_undo_group.",
            s => s
                .AddRequired("group_name", "string",
                    "Name for the undo group (shown in the undo history)")
                .AddOptional("file_path", "string",
                    "Optional sequence file path for a file-level undo group"),
            BeginUndoGroupAsync);

        Register("end_undo_group",
            "End the current undo group, committing all grouped operations as a single undo item.",
            s => s.AddOptional("file_path", "string",
                "Optional sequence file path for a file-level undo group"),
            EndUndoGroupAsync);

        Register("cancel_undo_group",
            "Cancel the current undo group and undo all operations that were part of it.",
            s => s.AddOptional("file_path", "string",
                "Optional sequence file path"),
            CancelUndoGroupAsync);

        // ── Sequence File Comparison ──────────────────────────────────────────

        Register("compare_sequence_files",
            "Compare two TestStand sequence files and return a structured diff: " +
            "sequences only in file1, sequences only in file2, and for sequences present " +
            "in both files: added/removed/modified steps, changed local variables, " +
            "parameters, and sequence properties.",
            s => s
                .AddRequired("file_path_1", "string", "Path to the first sequence file")
                .AddRequired("file_path_2", "string", "Path to the second sequence file"),
            CompareSequenceFilesAsync);

        Register("diff_sequence_files",
            "Run NI TestStand's NATIVE FileDiffer on two sequence files and return its detailed, " +
            "classified diff — exactly what the Sequence Editor's Diff/Merge view shows. Returns " +
            "per-file tallies (changes/insertions/deletions) plus a flat list of differences, each " +
            "with a change type (Insert, Delete, ValueChange, Conflict, Moved), the property-tree " +
            "path, and the value in each file. More detailed than compare_sequence_files (which is " +
            "a lighter, in-process structural comparison).",
            s => s
                .AddRequired("file_path_1", "string", "Path to the first (base) sequence file")
                .AddRequired("file_path_2", "string", "Path to the second sequence file to diff against file 1"),
            DiffSequenceFilesAsync);

        // ── Sync Manager ─────────────────────────────────────────────────────

        Register("get_sync_objects",
            "List all TestStand synchronization objects (Semaphore, Mutex, Queue, Notification, Rendezvous).",
            s => { },
            GetSyncObjectsAsync);

        Register("create_sync_object",
            "Create a TestStand synchronization object. " +
            "Types: 'Semaphore', 'Mutex', 'Queue', 'Notification', 'Rendezvous'. " +
            "initial_value: start count for Semaphore / num threads for Rendezvous. " +
            "max_value: max count for Semaphore / max queue size for Queue. " +
            "NOTE: a headless engine has no execution context and may not expose a SyncManager — " +
            "an InvalidOperationException ('SyncManager unavailable') is then EXPECTED, not a bug. " +
            "Sync objects are normally created/used from within a running execution.",
            s => s
                .AddRequired("name", "string", "Unique name for the sync object")
                .AddRequired("type", "string",
                    "Type: 'Semaphore', 'Mutex', 'Queue', 'Notification', 'Rendezvous'")
                .AddOptional("initial_value", "integer",
                    "Initial count (Semaphore: initial count, Rendezvous: num threads)", 1)
                .AddOptional("max_value", "integer",
                    "Max count (Semaphore: max count, Queue: max size)", 1),
            CreateSyncObjectAsync);

        Register("delete_sync_object",
            "Delete a TestStand synchronization object by name.",
            s => s.AddRequired("name", "string", "Name of the sync object to delete"),
            DeleteSyncObjectAsync);

        Register("sync_semaphore_wait",
            "Wait to acquire a semaphore. Blocks until acquired or timeout.",
            s => s
                .AddRequired("name", "string", "Semaphore name")
                .AddOptional("timeout_seconds", "number", "Timeout in seconds (-1 = infinite)", 30),
            SyncSemaphoreWaitAsync);

        Register("sync_semaphore_release",
            "Release (signal) a semaphore, incrementing its count.",
            s => s.AddRequired("name", "string", "Semaphore name"),
            SyncSemaphoreReleaseAsync);

        Register("sync_mutex_lock",
            "Acquire (lock) a mutex. Blocks until acquired or timeout.",
            s => s
                .AddRequired("name", "string", "Mutex name")
                .AddOptional("timeout_seconds", "number", "Timeout in seconds (-1 = infinite)", 30),
            SyncMutexLockAsync);

        Register("sync_mutex_unlock",
            "Release (unlock) a mutex.",
            s => s.AddRequired("name", "string", "Mutex name"),
            SyncMutexUnlockAsync);

        Register("sync_queue_enqueue",
            "Enqueue a string value into a TestStand Queue sync object.",
            s => s
                .AddRequired("name", "string", "Queue name")
                .AddRequired("value", "string", "Value to enqueue"),
            SyncQueueEnqueueAsync);

        Register("sync_queue_dequeue",
            "Dequeue a value from a TestStand Queue sync object. Blocks until available or timeout.",
            s => s
                .AddRequired("name", "string", "Queue name")
                .AddOptional("timeout_seconds", "number", "Timeout in seconds (-1 = infinite)", 30),
            SyncQueueDequeueAsync);

        Register("sync_queue_flush",
            "Flush all elements from a Queue sync object.",
            s => s.AddRequired("name", "string", "Queue name"),
            SyncQueueFlushAsync);

        Register("sync_notification_set",
            "Set a Notification sync object, waking all waiting threads.",
            s => s
                .AddRequired("name", "string", "Notification name")
                .AddOptional("value", "string", "Optional string value to pass to waiters", ""),
            SyncNotificationSetAsync);

        Register("sync_notification_reset",
            "Reset a Notification sync object to the non-signalled state.",
            s => s.AddRequired("name", "string", "Notification name"),
            SyncNotificationResetAsync);

        Register("sync_notification_wait",
            "Wait for a Notification sync object to be set. Returns the notification value.",
            s => s
                .AddRequired("name", "string", "Notification name")
                .AddOptional("timeout_seconds", "number", "Timeout in seconds (-1 = infinite)", 30),
            SyncNotificationWaitAsync);

        // ── Advanced Adapter Introspection ────────────────────────────────────

        Register("get_adapter_details",
            "Get detailed information about a specific TestStand adapter including " +
            "configuration flags, icon name, and version properties.",
            s => s.AddRequired("adapter_name", "string",
                "Adapter key or display name (e.g. 'G Std Prototype Adapter', 'DotNet Adapter')"),
            GetAdapterDetailsAsync);

        Register("get_step_module_info",
            "Get the complete module configuration for a step: VI path, function name, " +
            "DLL path, .NET class/method, Python module, or sequence call target.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepModuleInfoAsync);

        // ── Search ────────────────────────────────────────────────────────────

        Register("search_steps",
            "Search for steps in a sequence file by pattern. " +
            "search_in options: 'all', 'name', 'type', 'expression', 'comment', 'variables'. " +
            "Returns all matching steps with their location and matched text.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("pattern", "string", "Search pattern (substring match)")
                .AddOptional("search_in", "string",
                    "Where to search: 'all' (default), 'name', 'type', 'expression', " +
                    "'comment', 'variables'")
                .AddOptional("case_sensitive", "boolean", "Case-sensitive search (default: false)", false),
            SearchStepsAsync);

        // ── Thread-Level Execution Control ────────────────────────────────────

        Register("get_execution_threads",
            "Get all threads of a running or paused execution with their current state " +
            "and position in the sequence.",
            s => s.AddRequired("execution_id", "string", "Execution ID"),
            GetExecutionThreadsAsync);

        Register("get_thread_status",
            "Get the detailed status of a specific thread within an execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string",
                    "Thread ID or thread index (from get_execution_threads)"),
            GetThreadStatusAsync);

        Register("break_thread",
            "Break (pause) a specific thread within an execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            BreakThreadAsync);

        Register("resume_thread",
            "Resume a paused thread within an execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            ResumeThreadAsync);

        Register("step_over_thread",
            "Execute one step on a specific thread and pause at the next step " +
            "(does not step into sub-sequences).",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            StepOverThreadAsync);

        Register("step_into_thread",
            "Execute one step on a specific thread and step into sub-sequences.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            StepIntoThreadAsync);

        Register("step_out_thread",
            "Execute until the current sequence on a specific thread returns, then pause.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            StepOutThreadAsync);

        Register("get_thread_call_stack",
            "Get the full call stack of a specific thread, showing all nested sequence calls " +
            "from the current step up to the entry point.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("thread_id", "string", "Thread ID or thread index"),
            GetThreadCallStackAsync);

        // ── Workspace ─────────────────────────────────────────────────────────

        Register("open_workspace",
            "Open a TestStand workspace file (.tsw) and return its contents.",
            s => s.AddRequired("workspace_path", "string",
                "Absolute path to the .tsw workspace file"),
            OpenWorkspaceAsync);

        Register("get_workspace",
            "Get the currently open TestStand workspace and the list of sequence files it contains.",
            s => { },
            GetWorkspaceAsync);

        // ── Watch Expressions ──────────────────────────────────────────────────

        Register("add_watch_expression",
            "Add a watch expression to monitor during debugging. Returns the index of the new entry.",
            s => s
                .AddRequired("expression", "string", "TestStand expression to watch (e.g. 'Locals.Counter')")
                .AddOptional("label", "string", "Display label for the watch expression"),
            AddWatchExpressionAsync);

        Register("get_watch_expressions",
            "Get all current watch expressions and their latest evaluated values.",
            s => { },
            GetWatchExpressionsAsync);

        Register("remove_watch_expression",
            "Remove a watch expression by its index.",
            s => s.AddRequired("index", "integer", "Zero-based index of the watch expression to remove"),
            RemoveWatchExpressionAsync);

        // ── Callbacks ─────────────────────────────────────────────────────────

        Register("get_callbacks",
            "Get all callback sequences defined in a sequence file (based on the process model).",
            s => s.AddRequired("file_path", "string", "Path to the sequence file"),
            GetCallbacksAsync);

        Register("add_callback_override",
            "Add an override of a model/engine callback (e.g. 'PreUUT', 'PostUUT') to a sequence " +
            "file — same as the editor's 'Sequence File Callbacks → Add'. With copy_default_steps " +
            "true the model's default steps are copied in (e.g. the 'Call DoPreUUT' dialog step, " +
            "which you can then set_step_run_mode to 'Skip' to run headless). The override lives " +
            "only in this file, so the station process model stays unchanged.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("callback_name", "string", "Callback to override, e.g. 'PreUUT' / 'PostUUT'")
                .AddOptional("copy_default_steps", "boolean",
                    "Copy the model's default steps into the override (default true)", true),
            AddCallbackOverrideAsync);

        // ── File Properties ───────────────────────────────────────────────────

        Register("get_file_properties",
            "Get metadata and properties of a sequence file: comment, version, GUID, modification state, and sequence count.",
            s => s.AddRequired("file_path", "string", "Path to the sequence file"),
            GetFilePropertiesAsync);

        Register("set_file_properties",
            "Set metadata of a sequence file. Provide at least one of: comment, version.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("comment", "string", "File comment / description to set")
                .AddOptional("version", "string", "Version string to set (e.g. '1.2.3')"),
            SetFilePropertiesAsync);

        // ── Duplicate Sequence ─────────────────────────────────────────────────

        Register("duplicate_sequence",
            "Duplicate a sequence within the same file or to a different file. " +
            "Creates a new sequence with the given name containing a copy of the source sequence.",
            s => s
                .AddRequired("source_file_path", "string", "Path to the source sequence file")
                .AddRequired("source_sequence_name", "string", "Name of the sequence to copy")
                .AddRequired("new_sequence_name", "string", "Name for the new duplicate sequence")
                .AddOptional("target_file_path", "string",
                    "Path to the target sequence file (default: same as source file)"),
            DuplicateSequenceAsync);

        // ── Array Variable Operations ──────────────────────────────────────────

        Register("get_array_variable",
            "Read elements of an array local variable or file global. " +
            "Returns an array of {index, value, type} objects. The variable must already be an " +
            "array — create one with insert_local_variable/insert_file_global using a 'number[]' " +
            "(or 'array:number') data type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence containing the local variable. " +
                    "Omit to read a file global instead.")
                .AddRequired("variable_name", "string", "Name of the array variable")
                .AddOptional("max_elements", "integer",
                    "Maximum number of elements to return (default: 100)", 100),
            GetArrayVariableAsync);

        Register("set_array_element",
            "Set one element of an array local variable or file global. The variable must " +
            "already be an array (see insert_local_variable/insert_file_global with a 'number[]' " +
            "type); grow it first with resize_array_variable if the index is out of range.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence. Omit to target a file global.")
                .AddRequired("variable_name", "string", "Name of the array variable")
                .AddRequired("index", "integer", "Zero-based index of the element to set")
                .AddRequired("value", "string", "New value for the element"),
            SetArrayElementAsync);

        Register("resize_array_variable",
            "Resize an array local variable or file global to a new number of elements. The " +
            "variable must already be an array (create one via insert_local_variable/" +
            "insert_file_global with a 'number[]' or 'array:number' data type) — this does not " +
            "convert a scalar into an array.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence. Omit to target a file global.")
                .AddRequired("variable_name", "string", "Name of the array variable")
                .AddRequired("new_size", "integer", "New number of elements"),
            ResizeArrayVariableAsync);

        Register("get_property_object",
            "Inspect a property (local variable or file global) in a structured way: its value " +
            "type, scalar value, named type, and — for containers/structs — its immediate " +
            "subproperties with their types and values.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence containing the local variable. Omit for a file global.")
                .AddRequired("property_name", "string",
                    "Property name or dotted lookup path (e.g. 'MyContainer.Sub')"),
            GetPropertyObjectAsync);

        Register("set_property_value",
            "Set a property value with an explicit type, creating the property if it does not " +
            "exist yet. value_type 'container' creates an empty container/struct (no value). " +
            "Targets a sequence's local variable (with sequence_name) or a file global (without).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence. Omit to target a file global.")
                .AddRequired("property_name", "string",
                    "Property name or dotted lookup path (e.g. 'MyContainer.Sub')")
                .AddRequired("value_type", "string",
                    "Value type to create/set",
                    new[] { "number", "boolean", "string", "container" })
                .AddOptional("value", "string",
                    "Value to assign (omitted/ignored for 'container')"),
            SetPropertyValueAsync);

        Register("delete_sub_property",
            "Delete a subproperty (local variable or file global) by name or dotted lookup path.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence. Omit to target a file global.")
                .AddRequired("property_name", "string",
                    "Property name or dotted lookup path to delete"),
            DeleteSubPropertyAsync);

        // ── Data Type Operations ───────────────────────────────────────────────

        Register("create_data_type",
            "Create a new custom data type (TypeDef) in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("type_name", "string", "Name of the new data type")
                .AddOptional("base_type", "string",
                    "Base type name (default: 'Object')", "Object"),
            CreateDataTypeAsync);

        Register("delete_data_type",
            "Delete a custom data type from a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("type_name", "string", "Name of the data type to delete"),
            DeleteDataTypeAsync);

        // ── Enumeration Data Types ─────────────────────────────────────────────
        // An enum is a data type with named numeric constants (name → value). It is stored in
        // the sequence file alongside other custom data types (visible via get_data_types,
        // removable via delete_data_type or delete_enum). Values are populated/replaced via the
        // engine's UpdateEnumerators mechanism; auto-values are assigned when 'value' is omitted.

        Register("create_enum",
            "Create a new enumeration data type (named numeric constants) in a sequence file. " +
            "Each value is {name, value?}; when 'value' is omitted it is auto-assigned C-style " +
            "(previous + 1, starting at 0). Pass an empty 'values' list to create an empty enum " +
            "and add constants later with add_enum_value.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the new enum data type")
                .AddArray("values",
                    "Initial enumerators. Each item: {name (required), value (optional number)}.",
                    item => item
                        .AddRequired("name", "string", "Enumerator name (label)")
                        .AddOptional("value", "number", "Numeric value (auto-assigned if omitted)"),
                    required: false)
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            CreateEnumAsync);

        Register("get_enum_values",
            "List the constants (name → numeric value) of an enumeration data type, in order.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type"),
            GetEnumValuesAsync);

        Register("set_enum_values",
            "Replace the ENTIRE constant list of an enum data type. Each value is {name, value?} " +
            "(value auto-assigned C-style when omitted). Any existing constant not in the list is removed.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type")
                .AddArray("values",
                    "The full new enumerator list. Each item: {name (required), value (optional number)}.",
                    item => item
                        .AddRequired("name", "string", "Enumerator name (label)")
                        .AddOptional("value", "number", "Numeric value (auto-assigned if omitted)"))
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            SetEnumValuesAsync);

        Register("add_enum_value",
            "Add a single constant to an enum data type. When 'value' is omitted it defaults to " +
            "the current maximum value + 1 (or 0 for an empty enum).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type")
                .AddRequired("value_name", "string", "Name of the new enumerator")
                .AddOptional("value", "number", "Numeric value (auto-assigned if omitted)")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            AddEnumValueAsync);

        Register("remove_enum_value",
            "Remove a single constant (by name) from an enum data type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type")
                .AddRequired("value_name", "string", "Name of the enumerator to remove")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            RemoveEnumValueAsync);

        Register("rename_enum_value",
            "Rename a single constant of an enum data type (uses OldEnumeratorName so the rename " +
            "maps cleanly to the existing enumerator). Optionally change its numeric value too.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type")
                .AddRequired("old_name", "string", "Current enumerator name")
                .AddRequired("new_name", "string", "New enumerator name")
                .AddOptional("value", "number", "New numeric value (kept unchanged if omitted)")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            RenameEnumValueAsync);

        Register("delete_enum",
            "Delete an enumeration data type from a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("enum_name", "string", "Name of the enum data type to delete")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            DeleteEnumAsync);

        // ── Module Parameter Operations ────────────────────────────────────────

        Register("get_module_parameters",
            "Get all parameters/arguments configured for a step's module (VI, DLL, .NET, Python). " +
            "Returns a list of {name, value, type, direction, dataType}.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step"),
            GetModuleParametersAsync);

        Register("set_module_parameter",
            "Set a single module parameter value on a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("parameter_name", "string", "Name of the parameter to set")
                .AddRequired("value", "string", "Value or expression to assign")
                .AddOptional("use_expression", "boolean",
                    "If true (default), assigns as an expression. If false, sets as a literal value.",
                    true),
            SetModuleParameterAsync);

        // ── Step Configuration ─────────────────────────────────────────────────

        Register("configure_message_popup",
            "Configure a MessagePopup step: message text, title, button style, and optional timeout.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the MessagePopup step")
                .AddRequired("message", "string", "Message text to display")
                .AddOptional("title", "string", "Title bar text (optional)")
                .AddOptional("buttons", "string",
                    "Button style: 'OK' (default), 'OKCancel', 'YesNo', 'YesNoCancel'",
                    "OK")
                .AddOptional("timeout", "number",
                    "Timeout in seconds. -1 means no timeout (default: -1).", -1),
            ConfigureMessagePopupAsync);

        Register("configure_property_loader",
            "Configure a PropertyLoader step: file path expression and read/write mode.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the PropertyLoader step")
                .AddRequired("file_path_expr", "string",
                    "Expression for the properties file path (e.g. '\"C:\\\\config.ini\"')")
                .AddOptional("mode", "string",
                    "Mode: 'Read' (default) or 'Write'", "Read"),
            ConfigurePropertyLoaderAsync);

        // ── Numeric / String Limit Configuration ──────────────────────────────

        Register("set_numeric_limits",
            "Set limits on a NumericLimitTest step: low/high limit, units, and comparison type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NumericLimitTest step")
                .AddOptional("low_limit", "number", "Low (minimum) limit value")
                .AddOptional("high_limit", "number", "High (maximum) limit value")
                .AddOptional("units", "string", "Units label (e.g. 'V', 'A', 'ms')")
                .AddOptional("comparison_type", "string",
                    "Comparison: 'GELE' (default, low<=x<=high), 'GE' (x>=low), 'LE' (x<=high), 'EQ' (x==value), 'NE' (x!=value), 'GT' (x>low), 'LT' (x<high)",
                    "GELE"),
            SetNumericLimitsAsync);

        Register("get_numeric_limits",
            "Get the current limits of a NumericLimitTest step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NumericLimitTest step"),
            GetNumericLimitsAsync);

        Register("set_step_measurement",
            "Set the measurement expression on a NumericLimitTest step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NumericLimitTest step")
                .AddRequired("expression", "string",
                    "Measurement expression (e.g. 'Locals.Value', 'Step.TS.NumericLimitTest.Measurement.Expression')"),
            SetStepMeasurementAsync);

        Register("set_wait_time",
            "Configure an NI_Wait step to wait a fixed time interval. Sets the wait mode to 'time' " +
            "and the time expression (in seconds). A freshly inserted NI_Wait has no time set and " +
            "does not actually wait until this is configured.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NI_Wait step")
                .AddRequired("time_expression", "string",
                    "Seconds to wait — a literal number ('2.5') or any expression evaluating to seconds."),
            SetWaitTimeAsync);

        Register("configure_string_value_test",
            "Configure a StringValueTest step: set the expression, expected value, and comparison type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the StringValueTest step")
                .AddRequired("expression", "string", "Expression that produces the string value to test")
                .AddRequired("expected_value", "string", "Expected string value")
                .AddOptional("comparison_type", "string",
                    "Comparison: 'CaseSensitive' (default), 'CaseInsensitive', 'Ignore'",
                    "CaseSensitive"),
            ConfigureStringValueTestAsync);

        // ── Breakpoints ────────────────────────────────────────────────────────

        Register("set_step_breakpoint",
            "Set or clear a breakpoint on a step.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("enabled", "boolean", "True to enable the breakpoint, false to clear it")
                .AddOptional("breakpoint_type", "string",
                    "Breakpoint type: 'Before' (default), 'After', 'Both'", "Before"),
            SetStepBreakpointAsync);

        Register("get_breakpoints",
            "List all steps with breakpoints enabled in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file"),
            GetBreakpointsAsync);

        // ── Execution Results ──────────────────────────────────────────────────

        Register("get_step_result",
            "Get the result of a specific step from a completed or running execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("sequence_name", "string", "Name of the sequence containing the step")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepResultAsync);

        Register("get_execution_results",
            "Get all step results for a completed or running execution as structured JSON.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID"),
            GetExecutionResultsAsync);

        Register("get_execution_time",
            "Get the elapsed execution time in seconds for a running or completed execution.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID"),
            GetExecutionTimeAsync);

        // ── User & Privilege Management ────────────────────────────────────────

        Register("get_users",
            "List all users defined in the TestStand users file (login name, full name).",
            s => { },
            GetUsersAsync);

        Register("get_current_user",
            "Get the currently logged-in TestStand user.",
            s => { },
            GetCurrentUserAsync);

        Register("user_name_exists",
            "Check whether a user with the given login name exists.",
            s => s.AddRequired("login_name", "string", "Login name to check"),
            UserNameExistsAsync);

        Register("create_user",
            "Create a new TestStand user and add it to the users file. Pass 'profile' to grant a "
            + "privilege level: the new user is seeded from the named user profile (e.g. 'Administrator'). "
            + "Use get_user_profiles to list valid profile names. Omit/empty = minimal default privileges.",
            s => s
                .AddRequired("login_name", "string", "Unique login name for the new user")
                .AddOptional("full_name", "string", "Full display name", "")
                .AddOptional("password", "string", "Initial password (stored scrambled)", "")
                .AddOptional("profile", "string",
                    "User profile to seed privileges from (e.g. 'Administrator', 'Developer', "
                    + "'Technician', 'Operator'). Empty = minimal default privileges.", "")
                .AddOptional("persist", "boolean",
                    "Write the users file to disk (default true). Set false to only modify in memory.", true),
            CreateUserAsync);

        Register("delete_user",
            "Delete a user from the TestStand users file by login name.",
            s => s
                .AddRequired("login_name", "string", "Login name of the user to delete")
                .AddOptional("persist", "boolean", "Write the users file to disk (default true)", true),
            DeleteUserAsync);

        Register("set_user_password",
            "Set (reset) the password of an existing user.",
            s => s
                .AddRequired("login_name", "string", "Login name of the user")
                .AddRequired("password", "string", "New password (stored scrambled)")
                .AddOptional("persist", "boolean", "Write the users file to disk (default true)", true),
            SetUserPasswordAsync);

        Register("get_user_privileges",
            "List the enabled privilege paths for a user.",
            s => s.AddRequired("login_name", "string", "Login name of the user"),
            GetUserPrivilegesAsync);

        Register("check_user_privilege",
            "Check whether a user has a specific privilege (e.g. 'OperatorInterface.Run').",
            s => s
                .AddRequired("login_name", "string", "Login name of the user")
                .AddRequired("privilege", "string", "Privilege lookup string to test"),
            CheckUserPrivilegeAsync);

        Register("get_user_profiles",
            "List the available user profiles (privilege templates such as Administrator, Developer, "
            + "Technician, Operator) defined in the users file. Use one of these names as the 'profile' "
            + "argument of create_user.",
            s => { },
            GetUserProfilesAsync);

        // ── Native Find / Replace ──────────────────────────────────────────────

        Register("find_in_file",
            "Search a sequence file using the native TestStand search engine. Returns the " +
            "matches with their full property paths. Supports regex, whole-word and case options.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("pattern", "string", "Text or regular expression to search for")
                .AddOptional("match_case", "boolean", "Case-sensitive search (default false)", false)
                .AddOptional("whole_word", "boolean", "Match whole words only (default false)", false)
                .AddOptional("regex", "boolean", "Treat pattern as a regular expression (default false)", false)
                .AddOptional("elements", "string",
                    "What to search: 'all' (default), 'name', 'comment', or 'values'", "all",
                    new[] { "all", "name", "comment", "values" })
                .AddOptional("max_results", "integer", "Maximum matches to return (default 500)", 500),
            FindInFileAsync);

        Register("replace_in_file",
            "Find and replace text across a sequence file using the native TestStand search " +
            "engine, then save the file. Returns the number of replacements made.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("pattern", "string", "Text or regular expression to search for")
                .AddRequired("replacement", "string", "Replacement text")
                .AddOptional("match_case", "boolean", "Case-sensitive search (default false)", false)
                .AddOptional("whole_word", "boolean", "Match whole words only (default false)", false)
                .AddOptional("regex", "boolean", "Treat pattern as a regular expression (default false)", false)
                .AddOptional("elements", "string",
                    "What to search: 'all' (default), 'name', 'comment', or 'values'", "all",
                    new[] { "all", "name", "comment", "values" })
                .AddOptional("save", "boolean", "Save the file after replacing (default true)", true),
            ReplaceInFileAsync);

        // ── Typed Adapter / Code-Module Configuration ──────────────────────────

        Register("configure_dotnet_module",
            "Configure a step's .NET code module: assembly, class and method. " +
            "Switches the step to the .NET adapter if needed.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("assembly_path", "string", "Path to the .NET assembly (DLL)")
                .AddRequired("class_name", "string", "Fully-qualified class name")
                .AddRequired("method_name", "string", "Name of the method to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            ConfigureDotNetModuleAsync);

        Register("configure_dll_module",
            "Configure a step's C/DLL code module: DLL path and function name (C/CVI adapter).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("dll_path", "string", "Path to the DLL")
                .AddRequired("function_name", "string", "Exported function name to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            ConfigureDllModuleAsync);

        Register("configure_labview_module",
            "Configure a step's LabVIEW code module: the VI path (LabVIEW adapter).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("vi_path", "string", "Path to the VI")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            ConfigureLabViewModuleAsync);

        Register("configure_python_module",
            "Configure a step's Python code module: module path and function name (Python adapter).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("module_path", "string", "Path to the Python module (.py)")
                .AddRequired("function_name", "string", "Name of the function to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            ConfigurePythonModuleAsync);

        Register("configure_sequence_call_module",
            "Configure a step's SequenceCall module: target sequence and (optional) target file. " +
            "Prefer this typed tool over set_sequence_call_target for new code.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("target_sequence_name", "string", "Name of the target sequence")
                .AddOptional("target_sequence_file", "string",
                    "Target sequence file (empty = current file). Stored as a relative path.", "")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            ConfigureSequenceCallModuleAsync);

        // ── Sequence Analyzer (detailed) ───────────────────────────────────────

        Register("analyze_sequence_file",
            "Run the TestStand Sequence Analyzer on a file and return typed messages with " +
            "severity counts. Filter by minimum severity, and optionally group the results " +
            "(by severity or rule) like the editor's Analysis Results 'Group By' pane. The flat " +
            "'messages' list and counts are always present; grouping adds a 'groups' array.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file to analyze")
                .AddOptional("min_severity", "string",
                    "Minimum severity to include: 'Information' (default), 'Warning', or 'Error'",
                    "Information", new[] { "Information", "Warning", "Error" })
                .AddOptional("group_by", "string",
                    "Group the returned messages: 'severity' (default), 'rule', or 'none' for a " +
                    "flat list only. Grouped results populate the 'groups' array.",
                    "severity", new[] { "severity", "rule", "none" }),
            AnalyzeSequenceFileAsync);

        // ── Output & UI Messages ───────────────────────────────────────────────

        Register("post_output_message",
            "Post a message to the TestStand engine output-message list (visible in the " +
            "sequence editor's Output pane).",
            s => s
                .AddRequired("message", "string", "Message text")
                .AddOptional("category", "string", "Optional category/grouping label", "")
                .AddOptional("severity", "string", "Severity: 'Information' (default), 'Warning', 'Error'",
                    "Information", new[] { "Information", "Warning", "Error" }),
            PostOutputMessageAsync);

        Register("get_output_messages",
            "List the messages currently in the engine output-message list.",
            s => s.AddOptional("max_messages", "integer", "Maximum messages to return (default 200)", 200),
            GetOutputMessagesAsync);

        Register("clear_output_messages",
            "Clear all messages from the engine output-message list.",
            s => { },
            ClearOutputMessagesAsync);

        Register("post_ui_message",
            "Post a UI message to a running execution's main thread (for custom operator " +
            "interfaces). Requires an active execution_id — only meaningful during a LIVE run. " +
            "An unknown/stale execution_id raises a clear error (KeyNotFoundException).",
            s => s
                .AddRequired("execution_id", "string", "ID of the target execution")
                .AddRequired("message_code", "string",
                    "UIMessageCodes constant (e.g. 'UserMessageBase') or full 'UIMsg_*' name")
                .AddOptional("numeric_data", "number", "Optional numeric payload", 0)
                .AddOptional("string_data", "string", "Optional string payload", ""),
            PostUiMessageAsync);

        // ── Search Directories ──────────────────────────────────────────────────

        Register("get_search_directories",
            "List the TestStand engine search directories (used to resolve relative file paths).",
            s => { },
            GetSearchDirectoriesAsync);

        Register("add_search_directory",
            "Add a directory to the TestStand engine search-directory list.",
            s => s
                .AddRequired("path", "string", "Absolute directory path to add")
                .AddOptional("index", "integer", "Insertion index; -1 appends at the end (default)", -1)
                .AddOptional("search_subdirectories", "boolean",
                    "Include subdirectories in the search (default true)", true),
            AddSearchDirectoryAsync);

        Register("remove_search_directory",
            "Remove a directory from the TestStand engine search-directory list by path.",
            s => s.AddRequired("path", "string", "Directory path to remove"),
            RemoveSearchDirectoryAsync);

        // ── Data-Type Field Editing ─────────────────────────────────────────────

        Register("add_data_type_field",
            "Add a field (subproperty) to a custom data type in a sequence file.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("type_name", "string", "Name of the custom data type")
                .AddRequired("field_name", "string", "Name of the new field")
                .AddRequired("field_type", "string",
                    "Field type: 'Number', 'String', 'Boolean', or the name of another custom type")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            AddDataTypeFieldAsync);

        Register("get_data_type_fields",
            "List the fields (subproperties) of a custom data type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("type_name", "string", "Name of the custom data type"),
            GetDataTypeFieldsAsync);

        Register("remove_data_type_field",
            "Remove a field (subproperty) from a custom data type.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("type_name", "string", "Name of the custom data type")
                .AddRequired("field_name", "string", "Name of the field to remove")
                .AddOptional("save", "boolean", "Save the file (default true)", true),
            RemoveDataTypeFieldAsync);

        // ── CSV Record Streams ──────────────────────────────────────────────────

        Register("write_csv_lines",
            "Write lines to a CSV file using the TestStand CSV output record stream.",
            s => s
                .AddRequired("file_path", "string", "Absolute path to the CSV file")
                .AddArray("lines", "Lines to write (each becomes one CSV row)",
                    item => item.AddRequired("value", "string", "A single CSV line")),
            WriteCsvLinesAsync);

        Register("read_csv_lines",
            "Read lines from a CSV file using the TestStand CSV input record stream.",
            s => s
                .AddRequired("file_path", "string", "Absolute path to the CSV file")
                .AddOptional("max_lines", "integer", "Maximum lines to read (default 1000)", 1000),
            ReadCsvLinesAsync);

        // ── Result Logging / Batch / Interactive / Report Sections ──────────────

        Register("create_result_log",
            "Create a TestStand ResultLog helper object (logging used by process models). " +
            "Headless this confirms the object can be created.",
            s => s
                .AddOptional("file_path", "string", "Associated file path (optional)", "")
                .AddOptional("format", "string", "Log format hint (default 'ASCII')", "ASCII"),
            CreateResultLogAsync);

        Register("create_batch_sync_object",
            "Create a Batch synchronization object. Note: batch sync is normally provided by " +
            "the batch process model and may not be available as a standalone object. " +
            "A NotSupportedException (no standalone batch sync) or InvalidOperationException " +
            "(no SyncManager headless) is EXPECTED in a headless engine — not a bug.",
            s => s.AddRequired("name", "string", "Name for the batch sync object"),
            CreateBatchSyncObjectAsync);

        Register("run_steps_interactively",
            "Set up interactive execution of selected steps (NewInteractiveArgs). Full " +
            "interactive runs require an active editor context.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddArray("step_names", "Names of the steps to run interactively",
                    item => item.AddRequired("value", "string", "A step name"))
                .AddOptional("timeout_seconds", "integer", "Timeout in seconds (default 60)", 60),
            RunStepsInteractivelyAsync);

        Register("add_report_section",
            "Append a custom section to a running/completed execution's report. " +
            "Requires a real execution_id — an unknown/stale id raises a clear error " +
            "(KeyNotFoundException). Only meaningful once an execution exists.",
            s => s
                .AddRequired("execution_id", "string", "ID of the target execution")
                .AddRequired("title", "string", "Section title")
                .AddOptional("body", "string", "Section body text", ""),
            AddReportSectionAsync);
    }

    private void Register(string name, string description,
        Action<SchemaObject> schema,
        Func<JsonElement?, Task<CallToolResult>> handler)
    {
        _tools[name]   = new McpTool
        {
            Name        = name,
            Description = description,
            InputSchema = SchemaBuilder.Build(schema)
        };
        _handlers[name] = handler;
    }

    // ── Handler Implementations ───────────────────────────────────────────────

    private async Task<CallToolResult> ConnectEngineAsync(JsonElement? args)
    {
        var path   = args?.GetStringOrNull("engine_path");
        var result = await _ts.ConnectAsync(path);
        return result
            ? Ok("Successfully connected to NI TestStand engine.")
            : Error("Failed to connect to TestStand engine. Ensure NI TestStand is installed.");
    }

    private async Task<CallToolResult> DisconnectEngineAsync(JsonElement? _)
    {
        await _ts.DisconnectAsync();
        return Ok("Disconnected from TestStand engine.");
    }

    private async Task<CallToolResult> GetStationInfoAsync(JsonElement? _)
    {
        var info = await _ts.GetStationInfoAsync();
        return OkJson(info);
    }

    private async Task<CallToolResult> OpenSequenceFileAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("file_path");
        var info = await _ts.OpenSequenceFileAsync(path);
        return OkJson(info);
    }

    private async Task<CallToolResult> CloseSequenceFileAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("file_path");
        await _ts.CloseSequenceFileAsync(path);
        return Ok($"Sequence file closed: {path}");
    }

    private async Task<CallToolResult> GetLoadedSequenceFilesAsync(JsonElement? args)
    {
        var detail = args.HasValue
            ? args.Value.GetStringOrDefault("detail", "summary")
            : "summary";

        if (detail.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            var files = await _ts.GetLoadedSequenceFilesAsync();
            return OkJson(files);
        }

        var summary = await _ts.GetLoadedSequenceFilesSummaryAsync();
        return OkJson(summary);
    }

    private async Task<CallToolResult> GetSequenceAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("file_path");
        var name = args!.Value.GetRequiredString("sequence_name");
        var seq  = await _ts.GetSequenceAsync(path, name);
        return OkJson(seq);
    }

    private async Task<CallToolResult> SaveSequenceFileAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("file_path");
        await _ts.SaveSequenceFileAsync(path);
        return Ok($"Sequence file saved: {path}");
    }

    private async Task<CallToolResult> CreateSequenceFileAsync(JsonElement? args)
    {
        var path   = args!.Value.GetRequiredString("file_path");
        var result = await _ts.CreateSequenceFileAsync(path);
        return Ok($"New sequence file created: {result}");
    }

    private async Task<CallToolResult> InsertSequenceAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        await _ts.InsertSequenceAsync(filePath, sequenceName);
        return Ok($"Sequence '{sequenceName}' inserted into {filePath}");
    }

    private async Task<CallToolResult> InsertStepAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("sequence_file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepType     = args!.Value.GetRequiredString("step_type");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var index        = args!.Value.GetIntOrDefault("index", -1);
        var adapter      = args!.Value.GetStringOrDefault("adapter", "");
        await _ts.InsertStepAsync(filePath, sequenceName, stepGroup, stepType, stepName, index, adapter);
        return Ok($"Step '{stepName}' ({stepType}) inserted into sequence '{sequenceName}' [{stepGroup}]");
    }

    private async Task<CallToolResult> InsertStepsBulkAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("sequence_file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var save         = args!.Value.GetBoolOrDefault("save", true);

        if (!args!.Value.TryGetProperty("steps", out var stepsEl) ||
            stepsEl.ValueKind != JsonValueKind.Array)
            return Error("Argument 'steps' must be a non-empty array of step objects.");

        var specs = new List<BulkStepSpec>();
        foreach (var el in stepsEl.EnumerateArray())
        {
            specs.Add(new BulkStepSpec
            {
                Name               = el.GetStringOrDefault("step_name", ""),
                StepType           = el.GetStringOrDefault("step_type", ""),
                Adapter            = el.GetStringOrNull("adapter"),
                Comment            = el.GetStringOrNull("comment"),
                Expression         = el.GetStringOrNull("expression"),
                ExpressionType     = el.GetStringOrNull("expression_type"),
                TargetSequenceName = el.GetStringOrNull("target_sequence_name"),
                TargetSequenceFile = el.GetStringOrNull("target_sequence_file")
            });
        }

        if (specs.Count == 0)
            return Error("Argument 'steps' must contain at least one step object.");

        var result = await _ts.InsertStepsBulkAsync(filePath, sequenceName, stepGroup, specs, save);
        return OkJson(result);
    }

    // Phase-3 gate: deterministic, engine-free validation of a build plan.
    private Task<CallToolResult> ValidateSequencePlanAsync(JsonElement? args)
    {
        var sequenceName = args!.Value.GetStringOrDefault("sequence_name", "");

        if (!args!.Value.TryGetProperty("steps", out var stepsEl) ||
            stepsEl.ValueKind != JsonValueKind.Array)
            return Task.FromResult(Error("Argument 'steps' must be an array of step objects."));

        var planSteps = new List<PlanStepInput>();
        foreach (var el in stepsEl.EnumerateArray())
        {
            planSteps.Add(new PlanStepInput
            {
                Name               = el.GetStringOrDefault("step_name", ""),
                StepType           = el.GetStringOrDefault("step_type", ""),
                Expression         = el.GetStringOrNull("expression"),
                TargetSequenceName = el.GetStringOrNull("target_sequence_name"),
                TargetSequenceFile = el.GetStringOrNull("target_sequence_file"),
                Comment            = el.GetStringOrNull("comment")
            });
        }

        var localNames = new List<string>();
        if (args!.Value.TryGetProperty("locals", out var localsEl) &&
            localsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in localsEl.EnumerateArray())
            {
                var n = el.GetStringOrNull("name");
                if (!string.IsNullOrWhiteSpace(n)) localNames.Add(n!);
            }
        }

        var result = SequencePlanValidator.Validate(sequenceName, planSteps, localNames);
        return Task.FromResult(OkJson(result));
    }

    private async Task<CallToolResult> InsertLocalVariableAsync(JsonElement? args)
    {
        var filePath      = args!.Value.GetRequiredString("file_path");
        var sequenceName  = args!.Value.GetRequiredString("sequence_name");
        var variableName  = args!.Value.GetRequiredString("variable_name");
        var dataType      = args!.Value.GetRequiredString("data_type");
        var defaultValue  = args!.Value.GetStringOrNull("default_value");
        await _ts.InsertLocalVariableAsync(filePath, sequenceName, variableName, dataType, defaultValue);
        return Ok($"Local variable '{variableName}' ({dataType}) added to sequence '{sequenceName}'");
    }

    private async Task<CallToolResult> SetLocalVariableCommentAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        var comment      = args!.Value.GetRequiredString("comment");
        await _ts.SetLocalVariableCommentAsync(filePath, sequenceName, varName, comment);
        return Ok($"Comment set on variable '{varName}' in sequence '{sequenceName}'");
    }

    private async Task<CallToolResult> GetLocalVariablesAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var vars = await _ts.GetLocalVariablesAsync(filePath, sequenceName);
        return OkJson(vars);
    }

    private async Task<CallToolResult> SetLocalVariableAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        var value        = args!.Value.GetRequiredString("value");
        await _ts.SetLocalVariableValueAsync(filePath, sequenceName, varName, value);
        return Ok($"Variable '{varName}' set to '{value}' in sequence '{sequenceName}'");
    }

    private async Task<CallToolResult> RunSequenceAnalyzerAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var groupBy  = args!.Value.GetStringOrDefault("group_by", "severity");
        var messages = await _ts.RunSequenceAnalyzerAsync(filePath);
        if (messages.Count == 0)
            return Ok("Sequence Analyzer found no issues.");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Sequence Analyzer found {messages.Count} message(s):\n");

        if (AnalyzerGrouping.IsGrouped(groupBy))
        {
            // The grouped field is shown in the header, so omit it from each line.
            bool byRule = groupBy.Trim().ToLowerInvariant() == "rule";
            foreach (var g in AnalyzerGrouping.Group(messages, groupBy))
            {
                sb.AppendLine($"{g.Key} ({g.Count}):");
                foreach (var m in g.Messages)
                    sb.AppendLine(byRule ? $"  [{m.Severity}] {m.Text}"
                                         : $"  [{m.RuleId}] {m.Text}");
                sb.AppendLine();
            }
        }
        else
        {
            foreach (var m in messages)
                sb.AppendLine($"[{m.Severity}] {m.RuleId}: {m.Text}");
        }
        return Ok(sb.ToString().TrimEnd());
    }

    private async Task<CallToolResult> SetStepModulePathAsync(JsonElement? args)
    {
        var filePath   = args!.Value.GetRequiredString("sequence_file_path");
        var seqName    = args!.Value.GetRequiredString("sequence_name");
        var stepGroup  = args!.Value.GetRequiredString("step_group");
        var stepName   = args!.Value.GetRequiredString("step_name");
        var modulePath = args!.Value.GetRequiredString("module_path");
        await _ts.SetStepModulePathAsync(filePath, seqName, stepGroup, stepName, modulePath);
        return Ok($"Module path for step '{stepName}' set to '{modulePath}'");
    }

    private async Task<CallToolResult> SetSequenceCallTargetAsync(JsonElement? args)
    {
        var filePath           = args!.Value.GetRequiredString("sequence_file_path");
        var seqName            = args!.Value.GetRequiredString("sequence_name");
        var stepGroup          = args!.Value.GetRequiredString("step_group");
        var stepName           = args!.Value.GetRequiredString("step_name");
        var targetSeqName      = args!.Value.GetRequiredString("target_sequence_name");
        var targetSeqFile      = args!.Value.GetStringOrDefault("target_sequence_file", "");
        await _ts.SetSequenceCallTargetAsync(filePath, seqName, stepGroup, stepName, targetSeqName, targetSeqFile);
        return Ok($"Sequence Call '{stepName}' target set to sequence '{targetSeqName}'" +
                  (string.IsNullOrEmpty(targetSeqFile) ? " (same file)" : $" in '{targetSeqFile}'"));
    }

    private async Task<CallToolResult> SetStepExpressionAsync(JsonElement? args)
    {
        var filePath    = args!.Value.GetRequiredString("sequence_file_path");
        var seqName     = args!.Value.GetRequiredString("sequence_name");
        var stepGroup   = args!.Value.GetRequiredString("step_group");
        var stepName    = args!.Value.GetRequiredString("step_name");
        var expression  = args!.Value.GetRequiredString("expression");
        var exprType    = args!.Value.GetStringOrDefault("expression_type", "Statement");
        await _ts.SetStepExpressionAsync(filePath, seqName, stepGroup, stepName, expression, exprType);
        return Ok($"Expression set on step '{stepName}' [{exprType}]");
    }

    private async Task<CallToolResult> StartExecutionAsync(JsonElement? args)
    {
        var path       = args!.Value.GetRequiredString("sequence_file_path");
        var entryPoint = args!.Value.GetRequiredString("entry_point");
        var parameters = args!.Value.GetDictionaryOrNull("parameters");
        var info = await _ts.StartExecutionAsync(path, entryPoint, parameters);
        return OkJson(info);
    }

    private async Task<CallToolResult> WaitForExecutionAsync(JsonElement? args)
    {
        var id      = args!.Value.GetRequiredString("execution_id");
        var timeout = args!.Value.GetIntOrDefault("timeout_seconds", 300);
        var result  = await _ts.WaitForExecutionAsync(id, timeout);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetExecutionStatusAsync(JsonElement? args)
    {
        var id   = args!.Value.GetRequiredString("execution_id");
        var info = await _ts.GetExecutionStatusAsync(id);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetActiveExecutionsAsync(JsonElement? _)
    {
        var execs = await _ts.GetActiveExecutionsAsync();
        return OkJson(execs);
    }

    private async Task<CallToolResult> TerminateExecutionAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.TerminateExecutionAsync(id);
        return Ok($"Execution {id} terminated.");
    }

    private async Task<CallToolResult> RunSequenceAsync(JsonElement? args)
    {
        var path    = args!.Value.GetRequiredString("sequence_file_path");
        var seq     = args!.Value.GetRequiredString("sequence_name");
        var parms   = args!.Value.GetDictionaryOrNull("parameters");
        var timeout = args!.Value.GetIntOrDefault("timeout_seconds", 300);
        var result  = await _ts.RunSequenceAsync(path, seq, parms, timeout);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetPropertyAsync(JsonElement? args)
    {
        var lookup = args!.Value.GetRequiredString("lookup_string");
        var prop   = await _ts.GetPropertyAsync(lookup);
        return OkJson(prop);
    }

    private async Task<CallToolResult> SetPropertyAsync(JsonElement? args)
    {
        var lookup = args!.Value.GetRequiredString("lookup_string");
        var value  = args!.Value.GetRequiredString("value");
        await _ts.SetPropertyAsync(lookup, value);
        return Ok($"Property '{lookup}' set to '{value}'.");
    }

    private async Task<CallToolResult> GetFileGlobalsAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("sequence_file_path");
        var vars = await _ts.GetFileGlobalsAsync(path);
        return OkJson(vars);
    }

    private async Task<CallToolResult> GetStationGlobalsAsync(JsonElement? _)
    {
        var vars = await _ts.GetStationGlobalsAsync();
        return OkJson(vars);
    }

    private async Task<CallToolResult> GetPropertyTreeAsync(JsonElement? args)
    {
        var root       = args?.GetStringOrDefault("root", "StationGlobals") ?? "StationGlobals";
        var filePath   = args?.GetStringOrNull("file_path");
        var lookup     = args?.GetStringOrNull("lookup_string");
        var maxDepth   = args?.GetIntOrDefault("max_depth", 25) ?? 25;
        var hidden     = args?.GetBoolOrDefault("include_hidden", true) ?? true;
        var maxArrayEl = args?.GetIntOrDefault("max_array_elements", 500) ?? 500;
        var tree = await _ts.GetPropertyTreeAsync(root, filePath, lookup, maxDepth, hidden, maxArrayEl);
        return OkJson(tree);
    }

    private async Task<CallToolResult> InsertFileGlobalAsync(JsonElement? args)
    {
        var path     = args!.Value.GetRequiredString("sequence_file_path");
        var name     = args!.Value.GetRequiredString("variable_name");
        var dataType = args!.Value.GetRequiredString("data_type");
        await _ts.InsertFileGlobalAsync(path, name, dataType);
        return Ok($"FileGlobal '{name}' ({dataType}) inserted into '{path}'.");
    }

    private async Task<CallToolResult> SetFileGlobalAsync(JsonElement? args)
    {
        var path  = args!.Value.GetRequiredString("sequence_file_path");
        var name  = args!.Value.GetRequiredString("variable_name");
        var value = args!.Value.GetRequiredString("value");
        await _ts.SetFileGlobalAsync(path, name, value);
        return Ok($"FileGlobal '{name}' set to '{value}'.");
    }

    private async Task<CallToolResult> SetStationGlobalAsync(JsonElement? args)
    {
        var name  = args!.Value.GetRequiredString("variable_name");
        var value = args!.Value.GetRequiredString("value");
        await _ts.SetStationGlobalAsync(name, value);
        return Ok($"StationGlobal '{name}' set to '{value}'.");
    }

    private async Task<CallToolResult> DeleteStationGlobalAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("variable_name");
        await _ts.DeleteStationGlobalAsync(name);
        return Ok($"StationGlobal '{name}' deleted and committed to disk.");
    }

    private async Task<CallToolResult> GetStepsAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("sequence_file_path");
        var seq  = args!.Value.GetRequiredString("sequence_name");
        var steps = await _ts.GetStepsAsync(path, seq);
        return OkJson(steps);
    }

    private async Task<CallToolResult> GetStepAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("sequence_file_path");
        var seq  = args!.Value.GetRequiredString("sequence_name");
        var step = args!.Value.GetRequiredString("step_name");
        var info = await _ts.GetStepAsync(path, seq, step);
        return OkJson(info);
    }

    private async Task<CallToolResult> EnableStepAsync(JsonElement? args)
    {
        var path    = args!.Value.GetRequiredString("sequence_file_path");
        var seq     = args!.Value.GetRequiredString("sequence_name");
        var step    = args!.Value.GetRequiredString("step_name");
        var enabled = args!.Value.GetBoolOrDefault("enabled", true);
        await _ts.EnableStepAsync(path, seq, step, enabled);
        return Ok($"Step '{step}' {(enabled ? "enabled" : "disabled")}.");
    }

    private async Task<CallToolResult> GetStepPropertiesAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("sequence_file_path");
        var seq  = args!.Value.GetRequiredString("sequence_name");
        var step = args!.Value.GetRequiredString("step_name");
        var props = await _ts.GetStepPropertiesAsync(path, seq, step);
        return OkJson(props);
    }

    private async Task<CallToolResult> GenerateReportAsync(JsonElement? args)
    {
        var id     = args!.Value.GetRequiredString("execution_id");
        var output = args!.Value.GetRequiredString("output_path");
        var format = args!.Value.GetStringOrDefault("format", "HTML");
        var info   = await _ts.GenerateReportAsync(id, output, format);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetReportTextAsync(JsonElement? args)
    {
        var id   = args!.Value.GetRequiredString("execution_id");
        var text = await _ts.GetReportTextAsync(id);
        return Ok(text);
    }

    private async Task<CallToolResult> GetUutInfoAsync(JsonElement? args)
    {
        var id  = args!.Value.GetRequiredString("execution_id");
        var uut = await _ts.GetUutInfoAsync(id);
        return OkJson(uut);
    }

    private async Task<CallToolResult> SetUutSerialNumberAsync(JsonElement? args)
    {
        var id  = args!.Value.GetRequiredString("execution_id");
        var sn  = args!.Value.GetRequiredString("serial_number");
        await _ts.SetUutSerialNumberAsync(id, sn);
        return Ok($"UUT serial number set to '{sn}' for execution {id}.");
    }

    private async Task<CallToolResult> SetUutPartNumberAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        var pn = args!.Value.GetRequiredString("part_number");
        await _ts.SetUutPartNumberAsync(id, pn);
        return Ok($"UUT part number set to '{pn}' for execution {id}.");
    }

    private async Task<CallToolResult> GetLoadedAdaptersAsync(JsonElement? _)
    {
        var adapters = await _ts.GetLoadedAdaptersAsync();
        return OkJson(adapters);
    }

    private async Task<CallToolResult> LoadAdapterAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("adapter_name");
        await _ts.LoadAdapterAsync(name);
        return Ok($"Adapter '{name}' loaded.");
    }

    private async Task<CallToolResult> UnloadAdapterAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("adapter_name");
        await _ts.UnloadAdapterAsync(name);
        return Ok($"Adapter '{name}' unloaded.");
    }

    private async Task<CallToolResult> GetExecutionLogAsync(JsonElement? args)
    {
        var id   = args!.Value.GetRequiredString("execution_id");
        var max  = args!.Value.GetIntOrDefault("max_entries", 100);
        var log  = await _ts.GetExecutionLogAsync(id, max);
        return OkJson(log);
    }

    private async Task<CallToolResult> ClearExecutionLogAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.ClearLogAsync(id);
        return Ok($"Log cleared for execution {id}.");
    }

    private async Task<CallToolResult> GetProcessModelAsync(JsonElement? _)
    {
        var model = await _ts.GetProcessModelAsync();
        return Ok($"Current process model: {model}");
    }

    private async Task<CallToolResult> SetProcessModelAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("process_model_path");
        await _ts.SetProcessModelAsync(path);
        return Ok($"Process model set to: {path}");
    }

    private async Task<CallToolResult> GetResultSchemasAsync(JsonElement? _)
    {
        var schemas = await _ts.GetResultSchemasAsync();
        return OkJson(schemas);
    }

    private async Task<CallToolResult> ExportResultsAsync(JsonElement? args)
    {
        var id     = args!.Value.GetRequiredString("execution_id");
        var schema = args!.Value.GetRequiredString("schema_name");
        var output = args!.Value.GetRequiredString("output_path");
        var path   = await _ts.ExportResultsAsync(id, schema, output);
        return Ok($"Results exported to: {path}");
    }

    // ── Type Palette Handlers ─────────────────────────────────────────────────

    private async Task<CallToolResult> GetTypePalettesAsync(JsonElement? _)
    {
        var palettes = await _ts.GetTypePalettesAsync();
        return OkJson(palettes);
    }

    private async Task<CallToolResult> LoadTypePaletteAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("palette_path");
        await _ts.LoadTypePaletteAsync(path);
        return Ok($"Type palette loaded: {path}");
    }

    private async Task<CallToolResult> UnloadTypePaletteAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("palette_path");
        await _ts.UnloadTypePaletteAsync(path);
        return Ok($"Type palette unloaded: {path}");
    }

    private async Task<CallToolResult> GetStepTypesAsync(JsonElement? args)
    {
        var paletteFile = args?.GetStringOrNull("palette_file");
        var types = await _ts.GetStepTypesAsync(paletteFile);
        return OkJson(types);
    }

    private async Task<CallToolResult> GetStepTypeAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("step_type_name");
        var info = await _ts.GetStepTypeAsync(name);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetDataTypesAsync(JsonElement? args)
    {
        var seqFile = args?.GetStringOrNull("sequence_file_path");
        var types = await _ts.GetDataTypesAsync(seqFile);
        return OkJson(types);
    }

    // ── Sequence Editor Handlers ──────────────────────────────────────────────

    private async Task<CallToolResult> LaunchSequenceEditorAsync(JsonElement? args)
    {
        var path = args?.GetStringOrNull("seqedit_path");
        var result = await _seqEditor.LaunchAsync(path);
        if (result)
        {
            var status = await _seqEditor.GetStatusAsync();
            return OkJson(status);
        }
        return Error("Failed to launch Sequence Editor. Ensure NI TestStand is installed.");
    }

    private async Task<CallToolResult> GetEditorStatusAsync(JsonElement? _)
    {
        var status = await _seqEditor.GetStatusAsync();
        return OkJson(status);
    }

    private async Task<CallToolResult> OpenFileInEditorAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("file_path");
        await _seqEditor.OpenFileAsync(path);
        return Ok($"Opened sequence file in Sequence Editor: {path}");
    }

    private async Task<CallToolResult> RunInEditorAsync(JsonElement? args)
    {
        var path  = args!.Value.GetRequiredString("sequence_file_path");
        var entry = args!.Value.GetRequiredString("entry_point");
        var result = await _seqEditor.RunSequenceAsync(path, entry);
        return Ok(result);
    }

    private async Task<CallToolResult> CloseSequenceEditorAsync(JsonElement? args)
    {
        var force = args.HasValue ? args.Value.GetBoolOrDefault("force", false) : false;
        await _seqEditor.CloseEditorAsync(force);
        return Ok(force
            ? "Sequence Editor forcefully terminated."
            : "Close request sent to Sequence Editor.");
    }

    // ── Engine Info & Control Handlers ───────────────────────────────────────

    private async Task<CallToolResult> GetEnginePathsAsync(JsonElement? _)
    {
        var paths = await _ts.GetEnginePathsAsync();
        return OkJson(paths);
    }

    private async Task<CallToolResult> CheckExpressionAsync(JsonElement? args)
    {
        var expression = args!.Value.GetRequiredString("expression");
        var seqFile    = args!.Value.GetStringOrNull("sequence_file_path");
        var result     = await _ts.CheckExpressionAsync(expression, seqFile);
        return OkJson(result);
    }

    private async Task<CallToolResult> EvaluateExpressionAsync(JsonElement? args)
    {
        var expression = args!.Value.GetRequiredString("expression");
        var seqFile    = args!.Value.GetStringOrNull("sequence_file_path");
        var result     = await _ts.EvaluateExpressionAsync(expression, seqFile);
        return OkJson(result);
    }

    // Pure static catalogue lookup — no engine touched, so this never awaits the service.
    private Task<CallToolResult> ListExpressionReferenceAsync(JsonElement? args)
    {
        var kind     = args?.GetStringOrNull("kind");
        var category = args?.GetStringOrNull("category");
        var search   = args?.GetStringOrNull("search");
        var entries  = ExpressionReference.Query(kind, category, search);
        return Task.FromResult(OkJson(new
        {
            count      = entries.Count,
            kinds      = ExpressionReference.Kinds,
            categories = ExpressionReference.Categories(kind),
            entries
        }));
    }

    private async Task<CallToolResult> GetPropertyObjectAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var seqName  = args!.Value.GetStringOrNull("sequence_name");
        var propName = args!.Value.GetRequiredString("property_name");
        var result   = await _ts.GetPropertyObjectAsync(filePath, seqName, propName);
        return OkJson(result);
    }

    private async Task<CallToolResult> SetPropertyValueAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("file_path");
        var seqName   = args!.Value.GetStringOrNull("sequence_name");
        var propName  = args!.Value.GetRequiredString("property_name");
        var valueType = args!.Value.GetRequiredString("value_type");
        var value     = args!.Value.GetStringOrNull("value");
        await _ts.SetPropertyValueAsync(filePath, seqName, propName, valueType, value);
        return Ok($"Property '{propName}' ({valueType}) set in " +
                  $"{(seqName is null ? "FileGlobals" : $"sequence '{seqName}'")}.");
    }

    private async Task<CallToolResult> DeleteSubPropertyAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var seqName  = args!.Value.GetStringOrNull("sequence_name");
        var propName = args!.Value.GetRequiredString("property_name");
        await _ts.DeleteSubPropertyAsync(filePath, seqName, propName);
        return Ok($"Deleted property '{propName}'.");
    }

    private async Task<CallToolResult> ExpandPathMacrosAsync(JsonElement? args)
    {
        var path     = args!.Value.GetRequiredString("path");
        var expanded = await _ts.ExpandPathMacrosAsync(path);
        return Ok(expanded);
    }

    private async Task<CallToolResult> FindFileAsync(JsonElement? args)
    {
        var filename = args!.Value.GetRequiredString("filename");
        var found    = await _ts.FindFileAsync(filename);
        return Ok(found);
    }

    private async Task<CallToolResult> BreakAllAsync(JsonElement? _)
    {
        await _ts.BreakAllAsync();
        return Ok("Break signal sent to all active executions.");
    }

    private async Task<CallToolResult> AbortAllAsync(JsonElement? _)
    {
        await _ts.AbortAllAsync();
        return Ok("Abort signal sent to all active executions.");
    }

    private async Task<CallToolResult> TerminateAllAsync(JsonElement? _)
    {
        await _ts.TerminateAllAsync();
        return Ok("Terminate signal sent to all active executions.");
    }

    private async Task<CallToolResult> GetStationOptionsAsync(JsonElement? _)
    {
        var opts = await _ts.GetStationOptionsAsync();
        return OkJson(opts);
    }

    private async Task<CallToolResult> SetStationOptionsAsync(JsonElement? args)
    {
        var current = await _ts.GetStationOptionsAsync();
        if (args.HasValue)
        {
            var a = args.Value;
            if (a.TryGetProperty("tracing_enabled", out var t))
                current.TracingEnabled = t.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("breakpoints_enabled", out var b))
                current.BreakpointsEnabled = b.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("disable_results", out var d))
                current.DisableResults = d.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("always_goto_cleanup_on_failure", out var g))
                current.AlwaysGotoCleanupOnFailure = g.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("break_on_rte", out var r))
                current.BreakOnRte = r.ValueKind == JsonValueKind.True;
        }
        await _ts.SetStationOptionsAsync(current);
        return Ok("Station options updated.");
    }

    // ── Execution Debug Control Handlers ─────────────────────────────────────

    private async Task<CallToolResult> BreakExecutionAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.BreakExecutionAsync(id);
        return Ok($"Execution {id} paused.");
    }

    private async Task<CallToolResult> ResumeExecutionAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.ResumeExecutionAsync(id);
        return Ok($"Execution {id} resumed.");
    }

    private async Task<CallToolResult> AbortExecutionAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.AbortExecutionAsync(id);
        return Ok($"Execution {id} aborted.");
    }

    private async Task<CallToolResult> RestartExecutionAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.RestartExecutionAsync(id);
        return Ok($"Execution {id} restarted.");
    }

    private async Task<CallToolResult> StepOverAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.StepOverAsync(id);
        return Ok($"Step Over executed on execution {id}.");
    }

    private async Task<CallToolResult> StepIntoAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.StepIntoAsync(id);
        return Ok($"Step Into executed on execution {id}.");
    }

    private async Task<CallToolResult> StepOutAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.StepOutAsync(id);
        return Ok($"Step Out executed on execution {id}.");
    }

    // ── Sequence File Operation Handlers ─────────────────────────────────────

    private async Task<CallToolResult> DeleteSequenceAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        await _ts.DeleteSequenceAsync(filePath, sequenceName);
        return Ok($"Sequence '{sequenceName}' deleted from {filePath}.");
    }

    private async Task<CallToolResult> SequenceNameExistsAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var exists       = await _ts.SequenceNameExistsAsync(filePath, sequenceName);
        return Ok(exists ? $"Sequence '{sequenceName}' exists." : $"Sequence '{sequenceName}' does not exist.");
    }

    private async Task<CallToolResult> RenameSequenceAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var oldName  = args!.Value.GetRequiredString("old_name");
        var newName  = args!.Value.GetRequiredString("new_name");
        await _ts.RenameSequenceAsync(filePath, oldName, newName);
        return Ok($"Sequence '{oldName}' renamed to '{newName}'.");
    }

    // ── Sequence Operation Handlers ───────────────────────────────────────────

    private async Task<CallToolResult> DeleteStepAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        await _ts.DeleteStepAsync(filePath, sequenceName, stepGroup, stepName);
        return Ok($"Step '{stepName}' deleted from [{stepGroup}] in sequence '{sequenceName}'.");
    }

    private async Task<CallToolResult> MoveStepAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var newIndex     = args!.Value.GetIntOrDefault("new_index", 0);
        await _ts.MoveStepAsync(filePath, sequenceName, stepGroup, stepName, newIndex);
        return Ok($"Step '{stepName}' moved to index {newIndex}.");
    }

    private async Task<CallToolResult> StepNameExistsAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var exists       = await _ts.StepNameExistsAsync(filePath, sequenceName, stepName);
        return Ok(exists ? $"Step '{stepName}' exists." : $"Step '{stepName}' does not exist.");
    }

    private async Task<CallToolResult> GetSequenceParametersAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var parameters   = await _ts.GetSequenceParametersAsync(filePath, sequenceName);
        return OkJson(parameters);
    }

    private async Task<CallToolResult> InsertSequenceParameterAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var paramName    = args!.Value.GetRequiredString("param_name");
        var dataType     = args!.Value.GetRequiredString("data_type");
        var direction    = args!.Value.GetStringOrDefault("direction", "Input");
        var defValue     = args!.Value.GetStringOrNull("default_value");
        var passByRef    = args!.Value.GetBoolOrNull("pass_by_reference");
        await _ts.InsertSequenceParameterAsync(filePath, sequenceName, paramName, dataType, direction, defValue, passByRef);

        bool effectiveByRef = passByRef ??
            direction.ToLowerInvariant() is "inout" or "inputoutput" or "passbyreference" or "byref";
        var passMode = effectiveByRef ? "by reference" : "by value";
        return Ok($"Parameter '{paramName}' ({dataType}, {passMode}) added to sequence '{sequenceName}'.");
    }

    private async Task<CallToolResult> DeleteLocalVariableAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        await _ts.DeleteLocalVariableAsync(filePath, sequenceName, varName);
        return Ok($"Local variable '{varName}' deleted from sequence '{sequenceName}'.");
    }

    private async Task<CallToolResult> GetStepTemplatesAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("file_path");
        var templates = await _ts.GetStepTemplatesAsync(filePath);
        return OkJson(templates);
    }

    private async Task<CallToolResult> InsertStepFromTemplateAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var templateName = args!.Value.GetRequiredString("template_name");
        var newStepName  = args!.Value.GetRequiredString("new_step_name");
        var index        = args!.Value.GetIntOrDefault("index", -1);
        await _ts.InsertStepFromTemplateAsync(filePath, sequenceName, stepGroup, templateName, newStepName, index);
        return Ok($"Step '{newStepName}' inserted from template '{templateName}' into sequence '{sequenceName}' [{stepGroup}].");
    }

    private async Task<CallToolResult> GetSequencePropertiesAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var props        = await _ts.GetSequencePropertiesAsync(filePath, sequenceName);
        return OkJson(props);
    }

    private async Task<CallToolResult> SetSequencePropertiesAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var current      = await _ts.GetSequencePropertiesAsync(filePath, sequenceName);
        if (args.HasValue)
        {
            var a = args.Value;
            if (a.TryGetProperty("description", out var desc))
                current.Description = desc.GetString() ?? current.Description;
            if (a.TryGetProperty("goto_cleanup_on_failure", out var g))
                current.GotoCleanupOnFailure = g.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("disable_results", out var d))
                current.DisableResults = d.ValueKind == JsonValueKind.True;
            if (a.TryGetProperty("failure_action", out var f))
                current.FailureAction = f.GetString() ?? current.FailureAction;
        }
        await _ts.SetSequencePropertiesAsync(filePath, sequenceName, current);
        return Ok($"Properties updated for sequence '{sequenceName}'.");
    }

    // ── Step Property Operation Handlers ─────────────────────────────────────

    private async Task<CallToolResult> RenameStepAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var newName      = args!.Value.GetRequiredString("new_name");
        await _ts.RenameStepAsync(filePath, sequenceName, stepGroup, stepName, newName);
        return Ok($"Step '{stepName}' renamed to '{newName}'.");
    }

    private async Task<CallToolResult> SetStepCommentAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var comment      = args!.Value.GetRequiredString("comment");
        var method = await _ts.SetStepCommentAsync(filePath, sequenceName, stepGroup, stepName, comment);
        return Ok($"Comment set on step '{stepName}' via [{method}].");
    }

    private async Task<CallToolResult> SetStepRunModeAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var runMode      = args!.Value.GetRequiredString("run_mode");
        await _ts.SetStepRunModeAsync(filePath, sequenceName, stepGroup, stepName, runMode);
        return Ok($"Run mode of step '{stepName}' set to '{runMode}'.");
    }

    private async Task<CallToolResult> SetStepPreconditionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var precondition = args!.Value.GetRequiredString("precondition");
        await _ts.SetStepPreconditionAsync(filePath, sequenceName, stepGroup, stepName, precondition);
        return Ok($"Precondition of step '{stepName}' set.");
    }

    private async Task<CallToolResult> SetStepPassActionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var passAction   = args!.Value.GetRequiredString("pass_action");
        var target       = args!.Value.GetStringOrNull("target");
        await _ts.SetStepPassActionAsync(filePath, sequenceName, stepGroup, stepName, passAction, target);
        return Ok($"Pass action of step '{stepName}' set to '{passAction}'.");
    }

    private async Task<CallToolResult> SetStepFailActionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var failAction   = args!.Value.GetRequiredString("fail_action");
        var target       = args!.Value.GetStringOrNull("target");
        await _ts.SetStepFailActionAsync(filePath, sequenceName, stepGroup, stepName, failAction, target);
        return Ok($"Fail action of step '{stepName}' set to '{failAction}'.");
    }

    private async Task<CallToolResult> SetStepLoopAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var loopType     = args!.Value.GetRequiredString("loop_type");
        var initExpr     = args!.Value.GetStringOrNull("init_expr");
        var whileExpr    = args!.Value.GetStringOrNull("while_expr");
        var incExpr      = args!.Value.GetStringOrNull("inc_expr");
        await _ts.SetStepLoopAsync(filePath, sequenceName, stepGroup, stepName,
            loopType, initExpr, whileExpr, incExpr);
        return Ok($"Loop settings of step '{stepName}' updated to '{loopType}'.");
    }

    private async Task<CallToolResult> SetStepRecordResultAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");

        // Accept both boolean (legacy schema cached by client) and string (new schema)
        // boolean true  → "EnabledOverride" (2 = Enabled and override sequence setting)
        // boolean false → "Disabled"        (0)
        // string        → passed through as-is
        string recordingOption;
        if (!args!.Value.TryGetProperty("record_result", out var elem))
            return Error("Required argument 'record_result' is missing.");
        if (elem.ValueKind == JsonValueKind.True)
            recordingOption = "EnabledOverride";
        else if (elem.ValueKind == JsonValueKind.False)
            recordingOption = "Disabled";
        else
            recordingOption = elem.GetString() ?? "Enabled";

        await _ts.SetStepRecordResultAsync(filePath, sequenceName, stepGroup, stepName, recordingOption);
        return Ok($"Record result for step '{stepName}' set to '{recordingOption}'.");
    }

    private async Task<CallToolResult> SetStepEvalPrecondAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var option       = args!.Value.GetRequiredString("option");
        await _ts.SetStepEvalPrecondAsync(filePath, sequenceName, stepGroup, stepName, option);
        return Ok($"EvalPrecondForInteractiveExecution of step '{stepName}' set to '{option}'.");
    }

    private async Task<CallToolResult> SetStepModuleLoadOptionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var option       = args!.Value.GetRequiredString("option");
        await _ts.SetStepModuleLoadOptionAsync(filePath, sequenceName, stepGroup, stepName, option);
        return Ok($"ModuleLoadOption of step '{stepName}' set to '{option}'.");
    }

    private async Task<CallToolResult> SetStepModuleUnloadOptionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var option       = args!.Value.GetRequiredString("option");
        await _ts.SetStepModuleUnloadOptionAsync(filePath, sequenceName, stepGroup, stepName, option);
        return Ok($"ModuleUnloadOption of step '{stepName}' set to '{option}'.");
    }

    private async Task<CallToolResult> SetStepBatchSyncOptionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var option       = args!.Value.GetRequiredString("option");
        await _ts.SetStepBatchSyncOptionAsync(filePath, sequenceName, stepGroup, stepName, option);
        return Ok($"BatchSyncOption of step '{stepName}' set to '{option}'.");
    }

    private async Task<CallToolResult> ChangeStepAdapterAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var newAdapter   = args!.Value.GetRequiredString("new_adapter");
        await _ts.ChangeStepAdapterAsync(filePath, sequenceName, stepGroup, stepName, newAdapter);
        return Ok($"Adapter of step '{stepName}' changed to '{newAdapter}'.");
    }

    private async Task<CallToolResult> GetStepUniqueIdAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var uid          = await _ts.GetStepUniqueIdAsync(filePath, sequenceName, stepGroup, stepName);
        return Ok(uid);
    }

    // ── Report Operation Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> SaveReportAsync(JsonElement? args)
    {
        var id         = args!.Value.GetRequiredString("execution_id");
        var outputPath = args!.Value.GetRequiredString("output_path");
        var format     = args!.Value.GetStringOrDefault("format", "HTML");
        await _ts.SaveReportAsync(id, outputPath, format);
        return Ok($"Report saved to '{outputPath}'.");
    }

    private async Task<CallToolResult> LaunchReportViewerAsync(JsonElement? args)
    {
        var id = args!.Value.GetRequiredString("execution_id");
        await _ts.LaunchReportViewerAsync(id);
        return Ok($"Report viewer launched for execution {id}.");
    }

    private async Task<CallToolResult> GetFullReportAsync(JsonElement? args)
    {
        var id   = args!.Value.GetRequiredString("execution_id");
        var text = await _ts.GetFullReportAsync(id);
        return Ok(text);
    }

    // ── Undo/Redo Handlers ────────────────────────────────────────────────────

    private async Task<CallToolResult> GetUndoStackAsync(JsonElement? args)
    {
        var filePath = args?.GetStringOrNull("file_path");
        var info     = await _ts.GetUndoStackAsync(filePath);
        return OkJson(info);
    }

    private async Task<CallToolResult> UndoAsync(JsonElement? args)
    {
        var filePath = args?.GetStringOrNull("file_path");
        var done     = await _ts.UndoAsync(filePath);
        return Ok(done ? "Undo performed successfully." : "Nothing to undo.");
    }

    private async Task<CallToolResult> RedoAsync(JsonElement? args)
    {
        var filePath = args?.GetStringOrNull("file_path");
        var done     = await _ts.RedoAsync(filePath);
        return Ok(done ? "Redo performed successfully." : "Nothing to redo.");
    }

    private async Task<CallToolResult> BeginUndoGroupAsync(JsonElement? args)
    {
        var groupName = args!.Value.GetRequiredString("group_name");
        var filePath  = args!.Value.GetStringOrNull("file_path");
        await _ts.BeginUndoGroupAsync(groupName, filePath);
        return Ok($"Undo group '{groupName}' started.");
    }

    private async Task<CallToolResult> EndUndoGroupAsync(JsonElement? args)
    {
        var filePath = args?.GetStringOrNull("file_path");
        await _ts.EndUndoGroupAsync(filePath);
        return Ok("Undo group committed.");
    }

    private async Task<CallToolResult> CancelUndoGroupAsync(JsonElement? args)
    {
        var filePath = args?.GetStringOrNull("file_path");
        await _ts.CancelUndoGroupAsync(filePath);
        return Ok("Undo group cancelled and operations rolled back.");
    }

    // ── Sequence File Comparison Handler ─────────────────────────────────────

    private async Task<CallToolResult> CompareSequenceFilesAsync(JsonElement? args)
    {
        var path1 = args!.Value.GetRequiredString("file_path_1");
        var path2 = args!.Value.GetRequiredString("file_path_2");
        var diff  = await _ts.CompareSequenceFilesAsync(path1, path2);
        return OkJson(diff);
    }

    private async Task<CallToolResult> DiffSequenceFilesAsync(JsonElement? args)
    {
        var path1 = args!.Value.GetRequiredString("file_path_1");
        var path2 = args!.Value.GetRequiredString("file_path_2");
        var report = await _ts.DiffSequenceFilesAsync(path1, path2);
        return OkJson(report);
    }

    // ── Sync Manager Handlers ─────────────────────────────────────────────────

    private async Task<CallToolResult> GetSyncObjectsAsync(JsonElement? _)
    {
        var objs = await _ts.GetSyncObjectsAsync();
        return OkJson(objs);
    }

    private async Task<CallToolResult> CreateSyncObjectAsync(JsonElement? args)
    {
        var name    = args!.Value.GetRequiredString("name");
        var type    = args!.Value.GetRequiredString("type");
        var initial = args!.Value.GetIntOrDefault("initial_value", 1);
        var max     = args!.Value.GetIntOrDefault("max_value", 1);
        await _ts.CreateSyncObjectAsync(name, type, initial, max);
        return Ok($"{type} sync object '{name}' created (initial={initial}, max={max}).");
    }

    private async Task<CallToolResult> DeleteSyncObjectAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.DeleteSyncObjectAsync(name);
        return Ok($"Sync object '{name}' deleted.");
    }

    private async Task<CallToolResult> SyncSemaphoreWaitAsync(JsonElement? args)
    {
        var name    = args!.Value.GetRequiredString("name");
        var timeout = args!.Value.GetDoubleOrDefault("timeout_seconds", 30);
        await _ts.SyncSemaphoreWaitAsync(name, timeout);
        return Ok($"Semaphore '{name}' acquired.");
    }

    private async Task<CallToolResult> SyncSemaphoreReleaseAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.SyncSemaphoreReleaseAsync(name);
        return Ok($"Semaphore '{name}' released.");
    }

    private async Task<CallToolResult> SyncMutexLockAsync(JsonElement? args)
    {
        var name    = args!.Value.GetRequiredString("name");
        var timeout = args!.Value.GetDoubleOrDefault("timeout_seconds", 30);
        await _ts.SyncMutexLockAsync(name, timeout);
        return Ok($"Mutex '{name}' locked.");
    }

    private async Task<CallToolResult> SyncMutexUnlockAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.SyncMutexUnlockAsync(name);
        return Ok($"Mutex '{name}' unlocked.");
    }

    private async Task<CallToolResult> SyncQueueEnqueueAsync(JsonElement? args)
    {
        var name  = args!.Value.GetRequiredString("name");
        var value = args!.Value.GetRequiredString("value");
        await _ts.SyncQueueEnqueueAsync(name, value);
        return Ok($"Value '{value}' enqueued into queue '{name}'.");
    }

    private async Task<CallToolResult> SyncQueueDequeueAsync(JsonElement? args)
    {
        var name    = args!.Value.GetRequiredString("name");
        var timeout = args!.Value.GetDoubleOrDefault("timeout_seconds", 30);
        var value = await _ts.SyncQueueDequeueAsync(name, timeout);
        return Ok(value);
    }

    private async Task<CallToolResult> SyncQueueFlushAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.SyncQueueFlushAsync(name);
        return Ok($"Queue '{name}' flushed.");
    }

    private async Task<CallToolResult> SyncNotificationSetAsync(JsonElement? args)
    {
        var name  = args!.Value.GetRequiredString("name");
        var value = args!.Value.GetStringOrDefault("value", "");
        await _ts.SyncNotificationSetAsync(name, value);
        return Ok($"Notification '{name}' set{(string.IsNullOrEmpty(value) ? "" : $" with value '{value}'")}.");
    }

    private async Task<CallToolResult> SyncNotificationResetAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.SyncNotificationResetAsync(name);
        return Ok($"Notification '{name}' reset.");
    }

    private async Task<CallToolResult> SyncNotificationWaitAsync(JsonElement? args)
    {
        var name    = args!.Value.GetRequiredString("name");
        var timeout = args!.Value.GetDoubleOrDefault("timeout_seconds", 30);
        var value = await _ts.SyncNotificationWaitAsync(name, timeout);
        return Ok(string.IsNullOrEmpty(value)
            ? $"Notification '{name}' received."
            : $"Notification '{name}' received with value: {value}");
    }

    // ── Advanced Adapter Introspection Handlers ───────────────────────────────

    private async Task<CallToolResult> GetAdapterDetailsAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("adapter_name");
        var info = await _ts.GetAdapterDetailsAsync(name);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetStepModuleInfoAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var seqName  = args!.Value.GetRequiredString("sequence_name");
        var group    = args!.Value.GetRequiredString("step_group");
        var step     = args!.Value.GetRequiredString("step_name");
        var info     = await _ts.GetStepModuleInfoAsync(filePath, seqName, group, step);
        return OkJson(info);
    }

    // ── Search Handlers ───────────────────────────────────────────────────────

    private async Task<CallToolResult> SearchStepsAsync(JsonElement? args)
    {
        var filePath      = args!.Value.GetRequiredString("file_path");
        var pattern       = args!.Value.GetRequiredString("pattern");
        var searchIn      = args!.Value.GetStringOrDefault("search_in", "all");
        var caseSensitive = args!.Value.GetBoolOrDefault("case_sensitive", false);
        var result        = await _ts.SearchStepsAsync(filePath, pattern, searchIn, caseSensitive);
        return OkJson(result);
    }

    // ── User & Privilege Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> GetUsersAsync(JsonElement? _)
        => OkJson(await _ts.GetUsersAsync());

    private async Task<CallToolResult> GetCurrentUserAsync(JsonElement? _)
    {
        var user = await _ts.GetCurrentUserAsync();
        return user == null ? Ok("No user is currently logged in.") : OkJson(user);
    }

    private async Task<CallToolResult> UserNameExistsAsync(JsonElement? args)
    {
        var login  = args!.Value.GetRequiredString("login_name");
        var exists = await _ts.UserNameExistsAsync(login);
        return OkJson(new { loginName = login, exists });
    }

    private async Task<CallToolResult> CreateUserAsync(JsonElement? args)
    {
        var login    = args!.Value.GetRequiredString("login_name");
        var fullName = args!.Value.GetStringOrDefault("full_name", "");
        var password = args!.Value.GetStringOrDefault("password", "");
        var profile  = args!.Value.GetStringOrDefault("profile", "");
        var persist  = args!.Value.GetBoolOrDefault("persist", true);
        var profileName = string.IsNullOrWhiteSpace(profile) ? null : profile;
        await _ts.CreateUserAsync(login, fullName, password, profileName, persist);
        return Ok(profileName == null
            ? $"User '{login}' created."
            : $"User '{login}' created with profile '{profileName}'.");
    }

    private async Task<CallToolResult> DeleteUserAsync(JsonElement? args)
    {
        var login   = args!.Value.GetRequiredString("login_name");
        var persist = args!.Value.GetBoolOrDefault("persist", true);
        await _ts.DeleteUserAsync(login, persist);
        return Ok($"User '{login}' deleted.");
    }

    private async Task<CallToolResult> SetUserPasswordAsync(JsonElement? args)
    {
        var login    = args!.Value.GetRequiredString("login_name");
        var password = args!.Value.GetRequiredString("password");
        var persist  = args!.Value.GetBoolOrDefault("persist", true);
        await _ts.SetUserPasswordAsync(login, password, persist);
        return Ok($"Password updated for user '{login}'.");
    }

    private async Task<CallToolResult> GetUserPrivilegesAsync(JsonElement? args)
    {
        var login = args!.Value.GetRequiredString("login_name");
        return OkJson(new { loginName = login, privileges = await _ts.GetUserPrivilegesAsync(login) });
    }

    private async Task<CallToolResult> CheckUserPrivilegeAsync(JsonElement? args)
    {
        var login     = args!.Value.GetRequiredString("login_name");
        var privilege = args!.Value.GetRequiredString("privilege");
        var has       = await _ts.CheckUserPrivilegeAsync(login, privilege);
        return OkJson(new { loginName = login, privilege, hasPrivilege = has });
    }

    private async Task<CallToolResult> GetUserProfilesAsync(JsonElement? args)
        => OkJson(new { profiles = await _ts.GetUserProfilesAsync() });

    // ── Native Find / Replace Handlers ────────────────────────────────────────

    private async Task<CallToolResult> FindInFileAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("file_path");
        var pattern   = args!.Value.GetRequiredString("pattern");
        var matchCase = args!.Value.GetBoolOrDefault("match_case", false);
        var wholeWord = args!.Value.GetBoolOrDefault("whole_word", false);
        var regex     = args!.Value.GetBoolOrDefault("regex", false);
        var elements  = args!.Value.GetStringOrDefault("elements", "all");
        var maxRes    = args!.Value.GetIntOrDefault("max_results", 500);
        var result    = await _ts.FindInFileAsync(filePath, pattern, matchCase, wholeWord,
            regex, elements, maxRes);
        return OkJson(result);
    }

    private async Task<CallToolResult> ReplaceInFileAsync(JsonElement? args)
    {
        var filePath    = args!.Value.GetRequiredString("file_path");
        var pattern     = args!.Value.GetRequiredString("pattern");
        var replacement = args!.Value.GetRequiredString("replacement");
        var matchCase   = args!.Value.GetBoolOrDefault("match_case", false);
        var wholeWord   = args!.Value.GetBoolOrDefault("whole_word", false);
        var regex       = args!.Value.GetBoolOrDefault("regex", false);
        var elements    = args!.Value.GetStringOrDefault("elements", "all");
        var save        = args!.Value.GetBoolOrDefault("save", true);
        var result      = await _ts.ReplaceInFileAsync(filePath, pattern, replacement,
            matchCase, wholeWord, regex, elements, save);
        return OkJson(result);
    }

    // ── Adapter / Code-Module Configuration Handlers ──────────────────────────

    private async Task<CallToolResult> ConfigureDotNetModuleAsync(JsonElement? args)
    {
        var result = await _ts.ConfigureDotNetModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("assembly_path"),
            args!.Value.GetRequiredString("class_name"),
            args!.Value.GetRequiredString("method_name"),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigureDllModuleAsync(JsonElement? args)
    {
        var result = await _ts.ConfigureDllModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("dll_path"),
            args!.Value.GetRequiredString("function_name"),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigureLabViewModuleAsync(JsonElement? args)
    {
        var result = await _ts.ConfigureLabViewModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("vi_path"),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigurePythonModuleAsync(JsonElement? args)
    {
        var result = await _ts.ConfigurePythonModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("module_path"),
            args!.Value.GetRequiredString("function_name"),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigureSequenceCallModuleAsync(JsonElement? args)
    {
        var result = await _ts.ConfigureSequenceCallModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("target_sequence_name"),
            args!.Value.GetStringOrDefault("target_sequence_file", ""),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    // ── Sequence Analyzer Handler ─────────────────────────────────────────────

    private async Task<CallToolResult> AnalyzeSequenceFileAsync(JsonElement? args)
    {
        var filePath    = args!.Value.GetRequiredString("file_path");
        var minSeverity = args!.Value.GetStringOrDefault("min_severity", "Information");
        var groupBy     = args!.Value.GetStringOrDefault("group_by", "severity");
        var result      = await _ts.RunSequenceAnalyzerDetailedAsync(filePath, minSeverity, groupBy);
        return OkJson(result);
    }

    // ── Output & UI Message Handlers ──────────────────────────────────────────

    private async Task<CallToolResult> PostOutputMessageAsync(JsonElement? args)
    {
        var message  = args!.Value.GetRequiredString("message");
        var category = args!.Value.GetStringOrDefault("category", "");
        var severity = args!.Value.GetStringOrDefault("severity", "Information");
        return OkJson(await _ts.PostOutputMessageAsync(message, category, severity));
    }

    private async Task<CallToolResult> GetOutputMessagesAsync(JsonElement? args)
    {
        var max = args.HasValue ? args.Value.GetIntOrDefault("max_messages", 200) : 200;
        return OkJson(await _ts.GetOutputMessagesAsync(max));
    }

    private async Task<CallToolResult> ClearOutputMessagesAsync(JsonElement? _)
    {
        await _ts.ClearOutputMessagesAsync();
        return Ok("Output messages cleared.");
    }

    private async Task<CallToolResult> PostUiMessageAsync(JsonElement? args)
    {
        var execId  = args!.Value.GetRequiredString("execution_id");
        var code    = args!.Value.GetRequiredString("message_code");
        var numeric = args!.Value.TryGetProperty("numeric_data", out var n) && n.TryGetDouble(out var d) ? d : 0;
        var str     = args!.Value.GetStringOrDefault("string_data", "");
        await _ts.PostUiMessageAsync(execId, code, numeric, str);
        return Ok($"UI message '{code}' posted to execution {execId}.");
    }

    // ── Search Directory Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> GetSearchDirectoriesAsync(JsonElement? _)
        => OkJson(await _ts.GetSearchDirectoriesAsync());

    private async Task<CallToolResult> AddSearchDirectoryAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("path");
        var idx  = args!.Value.GetIntOrDefault("index", -1);
        var sub  = args!.Value.GetBoolOrDefault("search_subdirectories", true);
        await _ts.AddSearchDirectoryAsync(path, idx, sub);
        return Ok($"Search directory added: {path}");
    }

    private async Task<CallToolResult> RemoveSearchDirectoryAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("path");
        await _ts.RemoveSearchDirectoryAsync(path);
        return Ok($"Search directory removed: {path}");
    }

    // ── Data-Type Field Handlers ──────────────────────────────────────────────

    private async Task<CallToolResult> AddDataTypeFieldAsync(JsonElement? args)
    {
        await _ts.AddDataTypeFieldAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("type_name"),
            args!.Value.GetRequiredString("field_name"),
            args!.Value.GetRequiredString("field_type"),
            args!.Value.GetBoolOrDefault("save", true));
        return Ok("Data-type field added.");
    }

    private async Task<CallToolResult> GetDataTypeFieldsAsync(JsonElement? args)
    {
        var fields = await _ts.GetDataTypeFieldsAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("type_name"));
        return OkJson(fields);
    }

    private async Task<CallToolResult> RemoveDataTypeFieldAsync(JsonElement? args)
    {
        await _ts.RemoveDataTypeFieldAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("type_name"),
            args!.Value.GetRequiredString("field_name"),
            args!.Value.GetBoolOrDefault("save", true));
        return Ok("Data-type field removed.");
    }

    // ── CSV Stream Handlers ───────────────────────────────────────────────────

    private async Task<CallToolResult> WriteCsvLinesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var lines    = ExtractStringArray(args!.Value, "lines");
        await _ts.WriteCsvLinesAsync(filePath, lines);
        return Ok($"Wrote {lines.Count} line(s) to {filePath}");
    }

    private async Task<CallToolResult> ReadCsvLinesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var max      = args!.Value.GetIntOrDefault("max_lines", 1000);
        return OkJson(await _ts.ReadCsvLinesAsync(filePath, max));
    }

    // ── Result Log / Batch / Interactive / Report Section Handlers ────────────

    private async Task<CallToolResult> CreateResultLogAsync(JsonElement? args)
    {
        var filePath = args.HasValue ? args.Value.GetStringOrDefault("file_path", "") : "";
        var format   = args.HasValue ? args.Value.GetStringOrDefault("format", "ASCII") : "ASCII";
        return Ok(await _ts.CreateResultLogAsync(filePath, format));
    }

    private async Task<CallToolResult> CreateBatchSyncObjectAsync(JsonElement? args)
    {
        var name = args!.Value.GetRequiredString("name");
        await _ts.CreateBatchSyncObjectAsync(name);
        return Ok($"Batch sync object '{name}' created.");
    }

    private async Task<CallToolResult> RunStepsInteractivelyAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var seqName  = args!.Value.GetRequiredString("sequence_name");
        var group    = args!.Value.GetRequiredString("step_group");
        var steps    = ExtractStringArray(args!.Value, "step_names");
        var timeout  = args!.Value.GetIntOrDefault("timeout_seconds", 60);
        return Ok(await _ts.RunStepsInteractivelyAsync(filePath, seqName, group, steps, timeout));
    }

    private async Task<CallToolResult> AddReportSectionAsync(JsonElement? args)
    {
        var execId = args!.Value.GetRequiredString("execution_id");
        var title  = args!.Value.GetRequiredString("title");
        var body   = args!.Value.GetStringOrDefault("body", "");
        return Ok(await _ts.AddReportSectionAsync(execId, title, body));
    }

    // Extracts a list of strings from an array argument whose items are either bare
    // strings or objects of the form { "value": "..." }.
    private static List<string> ExtractStringArray(JsonElement args, string key)
    {
        var result = new List<string>();
        if (!args.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                result.Add(el.GetString() ?? "");
            else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var v))
                result.Add(v.GetString() ?? "");
        }
        return result;
    }

    // ── Thread-Level Execution Control Handlers ───────────────────────────────

    private async Task<CallToolResult> GetExecutionThreadsAsync(JsonElement? args)
    {
        var id      = args!.Value.GetRequiredString("execution_id");
        var threads = await _ts.GetExecutionThreadsAsync(id);
        return OkJson(threads);
    }

    private async Task<CallToolResult> GetThreadStatusAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        var info     = await _ts.GetThreadStatusAsync(id, threadId);
        return OkJson(info);
    }

    private async Task<CallToolResult> BreakThreadAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        await _ts.BreakThreadAsync(id, threadId);
        return Ok($"Thread {threadId} in execution {id} paused.");
    }

    private async Task<CallToolResult> ResumeThreadAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        await _ts.ResumeThreadAsync(id, threadId);
        return Ok($"Thread {threadId} in execution {id} resumed.");
    }

    private async Task<CallToolResult> StepOverThreadAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        await _ts.StepOverThreadAsync(id, threadId);
        return Ok($"Step Over executed on thread {threadId} in execution {id}.");
    }

    private async Task<CallToolResult> StepIntoThreadAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        await _ts.StepIntoThreadAsync(id, threadId);
        return Ok($"Step Into executed on thread {threadId} in execution {id}.");
    }

    private async Task<CallToolResult> StepOutThreadAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        await _ts.StepOutThreadAsync(id, threadId);
        return Ok($"Step Out executed on thread {threadId} in execution {id}.");
    }

    private async Task<CallToolResult> GetThreadCallStackAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args!.Value.GetRequiredString("thread_id");
        var frames   = await _ts.GetThreadCallStackAsync(id, threadId);
        return OkJson(frames);
    }

    // ── Workspace Handlers ────────────────────────────────────────────────────

    private async Task<CallToolResult> OpenWorkspaceAsync(JsonElement? args)
    {
        var path = args!.Value.GetRequiredString("workspace_path");
        var info = await _ts.OpenWorkspaceAsync(path);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetWorkspaceAsync(JsonElement? _)
    {
        var info = await _ts.GetWorkspaceAsync();
        return OkJson(info);
    }

    // ── Watch Expression Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> AddWatchExpressionAsync(JsonElement? args)
    {
        var expression = args!.Value.GetRequiredString("expression");
        var label      = args!.Value.GetStringOrNull("label");
        var index      = await _ts.AddWatchExpressionAsync(expression, label);
        return Ok($"Watch expression '{expression}' added at index {index}.");
    }

    private async Task<CallToolResult> GetWatchExpressionsAsync(JsonElement? _)
    {
        var watches = await _ts.GetWatchExpressionsAsync();
        return OkJson(watches);
    }

    private async Task<CallToolResult> RemoveWatchExpressionAsync(JsonElement? args)
    {
        var index = args!.Value.GetIntOrDefault("index", -1);
        if (index < 0)
            return Error("Parameter 'index' is required and must be >= 0.");
        await _ts.RemoveWatchExpressionAsync(index);
        return Ok($"Watch expression at index {index} removed.");
    }

    // ── Callbacks Handler ─────────────────────────────────────────────────────

    private async Task<CallToolResult> GetCallbacksAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var callbacks = await _ts.GetCallbacksAsync(filePath);
        return OkJson(callbacks);
    }

    private async Task<CallToolResult> AddCallbackOverrideAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var callbackName = args!.Value.GetRequiredString("callback_name");
        bool copyDefault = !(args!.Value.TryGetProperty("copy_default_steps", out var c)
                             && c.ValueKind == JsonValueKind.False);
        var name = await _ts.AddCallbackOverrideAsync(filePath, callbackName, copyDefault);
        return Ok($"Added callback override '{name}' to the sequence file.");
    }

    // ── File Properties Handlers ──────────────────────────────────────────────

    private async Task<CallToolResult> GetFilePropertiesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var props    = await _ts.GetFilePropertiesAsync(filePath);
        return OkJson(props);
    }

    private async Task<CallToolResult> SetFilePropertiesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var comment  = args!.Value.GetStringOrNull("comment");
        var version  = args!.Value.GetStringOrNull("version");
        if (comment == null && version == null)
            return Error("At least one of 'comment' or 'version' must be provided.");
        await _ts.SetFilePropertiesAsync(filePath, comment, version);
        return Ok($"File properties updated for: {filePath}");
    }

    // ── Duplicate Sequence Handler ────────────────────────────────────────────

    private async Task<CallToolResult> DuplicateSequenceAsync(JsonElement? args)
    {
        var sourceFile     = args!.Value.GetRequiredString("source_file_path");
        var sourceName     = args!.Value.GetRequiredString("source_sequence_name");
        var newName        = args!.Value.GetRequiredString("new_sequence_name");
        var targetFile     = args!.Value.GetStringOrNull("target_file_path");
        var result         = await _ts.DuplicateSequenceAsync(sourceFile, sourceName, newName, targetFile);
        return Ok($"Sequence '{sourceName}' duplicated as '{result}'" +
                  (targetFile != null ? $" in '{targetFile}'" : "."));
    }

    // ── Array Variable Handlers ───────────────────────────────────────────────

    private async Task<CallToolResult> GetArrayVariableAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetStringOrNull("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        var maxElements  = args!.Value.GetIntOrDefault("max_elements", 100);
        var elements     = await _ts.GetArrayVariableAsync(filePath, sequenceName, varName, maxElements);
        return OkJson(elements);
    }

    private async Task<CallToolResult> SetArrayElementAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetStringOrNull("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        var index        = args!.Value.GetIntOrDefault("index", 0);
        var value        = args!.Value.GetRequiredString("value");
        await _ts.SetArrayElementAsync(filePath, sequenceName, varName, index, value);
        return Ok($"Array element [{index}] of '{varName}' set to '{value}'.");
    }

    private async Task<CallToolResult> ResizeArrayVariableAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetStringOrNull("sequence_name");
        var varName      = args!.Value.GetRequiredString("variable_name");
        var newSize      = args!.Value.GetIntOrDefault("new_size", 0);
        await _ts.ResizeArrayVariableAsync(filePath, sequenceName, varName, newSize);
        return Ok($"Array variable '{varName}' resized to {newSize} elements.");
    }

    // ── Data Type Handlers ────────────────────────────────────────────────────

    private async Task<CallToolResult> CreateDataTypeAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var typeName = args!.Value.GetRequiredString("type_name");
        var baseType = args!.Value.GetStringOrDefault("base_type", "Object");
        var info     = await _ts.CreateDataTypeAsync(filePath, typeName, baseType);
        return OkJson(info);
    }

    private async Task<CallToolResult> DeleteDataTypeAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var typeName = args!.Value.GetRequiredString("type_name");
        await _ts.DeleteDataTypeAsync(filePath, typeName);
        return Ok($"Data type '{typeName}' deleted from '{filePath}'.");
    }

    // ── Enum Handlers ─────────────────────────────────────────────────────────

    private async Task<CallToolResult> CreateEnumAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var enumName = args!.Value.GetRequiredString("enum_name");
        var values   = ExtractEnumValues(args!.Value, "values");
        var save     = args!.Value.GetBoolOrDefault("save", true);
        var info     = await _ts.CreateEnumAsync(filePath, enumName, values, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetEnumValuesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var enumName = args!.Value.GetRequiredString("enum_name");
        var info     = await _ts.GetEnumValuesAsync(filePath, enumName);
        return OkJson(info);
    }

    private async Task<CallToolResult> SetEnumValuesAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var enumName = args!.Value.GetRequiredString("enum_name");
        var values   = ExtractEnumValues(args!.Value, "values");
        var save     = args!.Value.GetBoolOrDefault("save", true);
        var info     = await _ts.SetEnumValuesAsync(filePath, enumName, values, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> AddEnumValueAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("file_path");
        var enumName  = args!.Value.GetRequiredString("enum_name");
        var valueName = args!.Value.GetRequiredString("value_name");
        var value     = GetOptionalDouble(args!.Value, "value");
        var save      = args!.Value.GetBoolOrDefault("save", true);
        var info      = await _ts.AddEnumValueAsync(filePath, enumName, valueName, value, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> RemoveEnumValueAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("file_path");
        var enumName  = args!.Value.GetRequiredString("enum_name");
        var valueName = args!.Value.GetRequiredString("value_name");
        var save      = args!.Value.GetBoolOrDefault("save", true);
        var info      = await _ts.RemoveEnumValueAsync(filePath, enumName, valueName, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> RenameEnumValueAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var enumName = args!.Value.GetRequiredString("enum_name");
        var oldName  = args!.Value.GetRequiredString("old_name");
        var newName  = args!.Value.GetRequiredString("new_name");
        var value    = GetOptionalDouble(args!.Value, "value");
        var save     = args!.Value.GetBoolOrDefault("save", true);
        var info     = await _ts.RenameEnumValueAsync(filePath, enumName, oldName, newName, value, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> DeleteEnumAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var enumName = args!.Value.GetRequiredString("enum_name");
        var save     = args!.Value.GetBoolOrDefault("save", true);
        await _ts.DeleteEnumAsync(filePath, enumName, save);
        return Ok($"Enum '{enumName}' deleted from '{filePath}'.");
    }

    // Parses a 'values' array of enum constants — each item an object {name, value?}. When
    // 'value' is omitted, assigns a C-style running value (previous + 1, starting at 0).
    // internal (not private) so the engine-free auto-numbering logic can be unit-tested.
    internal static List<EnumValueInfo> ExtractEnumValues(JsonElement args, string key)
    {
        var result = new List<EnumValueInfo>();
        if (!args.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        double next = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            string name  = el.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            double value = el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble()
                : next;
            result.Add(new EnumValueInfo { Name = name, Value = value });
            next = value + 1;
        }
        return result;
    }

    // Reads an optional numeric argument, returning null when absent or not a JSON number.
    private static double? GetOptionalDouble(JsonElement args, string key) =>
        args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : (double?)null;

    // ── Module Parameter Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> GetModuleParametersAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var parameters   = await _ts.GetModuleParametersAsync(filePath, sequenceName, stepGroup, stepName);
        return OkJson(parameters);
    }

    private async Task<CallToolResult> SetModuleParameterAsync(JsonElement? args)
    {
        var filePath      = args!.Value.GetRequiredString("file_path");
        var sequenceName  = args!.Value.GetRequiredString("sequence_name");
        var stepGroup     = args!.Value.GetRequiredString("step_group");
        var stepName      = args!.Value.GetRequiredString("step_name");
        var paramName     = args!.Value.GetRequiredString("parameter_name");
        var value         = args!.Value.GetRequiredString("value");
        var useExpression = args!.Value.GetBoolOrDefault("use_expression", true);
        await _ts.SetModuleParameterAsync(filePath, sequenceName, stepGroup, stepName,
            paramName, value, useExpression);
        return Ok($"Module parameter '{paramName}' on step '{stepName}' set to '{value}'.");
    }

    // ── Step Configuration Handlers ───────────────────────────────────────────

    private async Task<CallToolResult> ConfigureMessagePopupAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var message      = args!.Value.GetRequiredString("message");
        var title        = args!.Value.GetStringOrNull("title");
        var buttons      = args!.Value.GetStringOrDefault("buttons", "OK");
        double timeout   = -1;
        if (args.HasValue && args.Value.TryGetProperty("timeout", out var t) &&
            t.ValueKind == JsonValueKind.Number)
            timeout = t.GetDouble();
        await _ts.ConfigureMessagePopupAsync(filePath, sequenceName, stepGroup, stepName,
            message, title, buttons, timeout);
        return Ok($"MessagePopup step '{stepName}' configured.");
    }

    private async Task<CallToolResult> ConfigurePropertyLoaderAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var filePathExpr = args!.Value.GetRequiredString("file_path_expr");
        var mode         = args!.Value.GetStringOrDefault("mode", "Read");
        await _ts.ConfigurePropertyLoaderAsync(filePath, sequenceName, stepGroup, stepName,
            filePathExpr, mode);
        return Ok($"PropertyLoader step '{stepName}' configured (mode: {mode}).");
    }

    // ── Numeric / String Limit Handlers ──────────────────────────────────────

    private async Task<CallToolResult> SetNumericLimitsAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var cmpType      = args!.Value.GetStringOrDefault("comparison_type", "GELE");

        double? lowLimit  = null;
        double? highLimit = null;
        if (args!.Value.TryGetProperty("low_limit", out var ll) && ll.ValueKind == JsonValueKind.Number)
            lowLimit = ll.GetDouble();
        if (args!.Value.TryGetProperty("high_limit", out var hl) && hl.ValueKind == JsonValueKind.Number)
            highLimit = hl.GetDouble();

        string? units = args!.Value.GetStringOrNull("units");

        await _ts.SetNumericLimitsAsync(filePath, sequenceName, stepGroup, stepName,
            lowLimit, highLimit, units, cmpType);
        return Ok($"Numeric limits set on step '{stepName}' (comparison: {cmpType}).");
    }

    private async Task<CallToolResult> GetNumericLimitsAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var limits = await _ts.GetNumericLimitsAsync(filePath, sequenceName, stepGroup, stepName);
        return OkJson(limits);
    }

    private async Task<CallToolResult> SetStepMeasurementAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var expression   = args!.Value.GetRequiredString("expression");
        await _ts.SetStepMeasurementAsync(filePath, sequenceName, stepGroup, stepName, expression);
        return Ok($"Measurement expression '{expression}' set on step '{stepName}'.");
    }

    private async Task<CallToolResult> SetWaitTimeAsync(JsonElement? args)
    {
        var filePath       = args!.Value.GetRequiredString("file_path");
        var sequenceName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup      = args!.Value.GetRequiredString("step_group");
        var stepName       = args!.Value.GetRequiredString("step_name");
        var timeExpression = args!.Value.GetRequiredString("time_expression");
        await _ts.SetWaitTimeAsync(filePath, sequenceName, stepGroup, stepName, timeExpression);
        return Ok($"NI_Wait step '{stepName}' set to wait {timeExpression} s.");
    }

    private async Task<CallToolResult> ConfigureStringValueTestAsync(JsonElement? args)
    {
        var filePath       = args!.Value.GetRequiredString("file_path");
        var sequenceName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup      = args!.Value.GetRequiredString("step_group");
        var stepName       = args!.Value.GetRequiredString("step_name");
        var expression     = args!.Value.GetRequiredString("expression");
        var expectedValue  = args!.Value.GetRequiredString("expected_value");
        var cmpType        = args!.Value.GetStringOrDefault("comparison_type", "CaseSensitive");
        await _ts.ConfigureStringValueTestAsync(filePath, sequenceName, stepGroup, stepName,
            expression, expectedValue, cmpType);
        return Ok($"StringValueTest '{stepName}' configured (comparison: {cmpType}).");
    }

    // ── Breakpoint Handlers ───────────────────────────────────────────────────

    private async Task<CallToolResult> SetStepBreakpointAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var enabled      = args!.Value.GetBoolOrDefault("enabled", false);
        var bpType       = args!.Value.GetStringOrDefault("breakpoint_type", "Before");
        await _ts.SetStepBreakpointAsync(filePath, sequenceName, stepGroup, stepName, enabled, bpType);
        return Ok($"Breakpoint {(enabled ? "enabled" : "cleared")} on step '{stepName}' (type: {bpType}).");
    }

    private async Task<CallToolResult> GetBreakpointsAsync(JsonElement? args)
    {
        var filePath    = args!.Value.GetRequiredString("file_path");
        var breakpoints = await _ts.GetBreakpointsAsync(filePath);
        if (breakpoints.Count == 0)
            return Ok("No breakpoints found in the sequence file.");
        return OkJson(breakpoints);
    }

    // ── Execution Result Handlers ─────────────────────────────────────────────

    private async Task<CallToolResult> GetStepResultAsync(JsonElement? args)
    {
        var execId   = args!.Value.GetRequiredString("execution_id");
        var seqName  = args!.Value.GetRequiredString("sequence_name");
        var stepName = args!.Value.GetRequiredString("step_name");
        var result   = await _ts.GetStepResultAsync(execId, seqName, stepName);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetExecutionResultsAsync(JsonElement? args)
    {
        var execId = args!.Value.GetRequiredString("execution_id");
        var result = await _ts.GetExecutionResultsAsync(execId);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetExecutionTimeAsync(JsonElement? args)
    {
        var execId  = args!.Value.GetRequiredString("execution_id");
        var elapsed = await _ts.GetExecutionTimeAsync(execId);
        return Ok($"Elapsed time for execution {execId}: {elapsed:F3} seconds.");
    }

    // ── Response Helpers ──────────────────────────────────────────────────────

    private static CallToolResult Ok(string message) => new()
    {
        Content = new List<ToolContent> { new() { Type = "text", Text = message } }
    };

    // Cached once: a JsonSerializerOptions is expensive to build and caches serialization
    // metadata internally — a fresh instance per response defeats that. OkJson is the hot path.
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static CallToolResult OkJson(object obj) => new()
    {
        Content = new List<ToolContent>
        {
            new() { Type = "text", Text = JsonSerializer.Serialize(obj, _jsonOpts) }
        }
    };

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new List<ToolContent> { new() { Type = "text", Text = message } }
    };
}

// ── JsonElement Extension Helpers ────────────────────────────────────────────

internal static class JsonElementExtensions
{
    public static string GetRequiredString(this JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop))
            return prop.GetString() ?? throw new ArgumentException($"'{key}' is null.");
        throw new ArgumentException($"Required argument '{key}' is missing.");
    }

    public static string? GetStringOrNull(this JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) ? p.GetString() : null;

    public static string GetStringOrDefault(this JsonElement el, string key,
        string defaultValue) =>
        el.TryGetProperty(key, out var p) ? (p.GetString() ?? defaultValue) : defaultValue;

    public static int GetIntOrDefault(this JsonElement el, string key, int defaultValue) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var v) ? v : defaultValue;

    /// <summary>Reads a numeric property, or returns <paramref name="defaultValue"/> when absent/non-numeric.</summary>
    public static double GetDoubleOrDefault(this JsonElement el, string key, double defaultValue) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDouble() : defaultValue;

    public static bool GetBoolOrDefault(this JsonElement el, string key, bool defaultValue)
    {
        if (!el.TryGetProperty(key, out var p)) return defaultValue;
        return p.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            _                   => defaultValue
        };
    }

    /// <summary>Reads a boolean property, or returns null when absent/non-boolean — so callers can
    /// distinguish "not supplied" from an explicit false.</summary>
    public static bool? GetBoolOrNull(this JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            _                   => (bool?)null
        };
    }

    public static Dictionary<string, object>? GetDictionaryOrNull(this JsonElement el,
        string key)
    {
        if (!el.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, object>();
        foreach (var prop in p.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.TryGetDouble(out var d) ? d : 0,
                JsonValueKind.True   => (object)true,
                JsonValueKind.False  => false,
                _                    => prop.Value.GetString() ?? ""
            };
        }
        return dict;
    }
}
