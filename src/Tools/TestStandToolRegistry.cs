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
            "Connect to the NI TestStand engine. Must be called before any other tool. " +
            "The engine itself is always the one TestStand has registered as ACTIVE (activation goes " +
            "through the TestStand.Engine ProgID; switching versions is NI's version-selector's job). " +
            "Use engine_path only as an escape hatch when the NI helper tools must come from a " +
            "specific installation.",
            s => s.AddOptional("engine_path", "string",
                "Optional override pinning WHICH TestStand installation's helper tools (FileDiffer.exe " +
                "for diff_sequence_files, AnalyzerApp.exe for analyze_sequence_file) are launched. " +
                @"Accepts the engine DLL (…\Bin\teapi.dll), the Bin directory, or the install root " +
                @"(…\National Instruments\TestStand 2026). Leave empty to resolve automatically " +
                "(engine Bin -> %TESTSTANDBIN% -> COM registration -> newest install). A path that " +
                "does not exist is rejected with an error. Does NOT select which engine is loaded."),
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
            "When a step's concrete type is unclear, default to 'SequenceCall' (may stay unlinked) " +
            "rather than 'Statement'. " +
            "Adapters: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'Sequence', 'None' " +
            "(default). Use adapter='Sequence' to make a NON-SequenceCall step (e.g. an 'Action') call " +
            "a subsequence — then configure it with configure_sequence_call_module. The step KEEPS its " +
            "type (an Action stays an Action) but calls the subsequence via the Sequence Adapter.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_type", "string", "Step type. Use 'NI_Flow_If'/'NI_Flow_Else'/'NI_Flow_End' for branching, never 'Goto'/'Label'.")
                .AddRequired("step_name", "string", "Name for the new step")
                .AddOptional("index", "integer", "Insert position (default: append at end)", -1)
                .AddOptional("adapter", "string", "Adapter name: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'Sequence', 'None' (default). 'Sequence' on an Action step = an Action that calls a subsequence."),
            InsertStepAsync);

        Register("insert_steps_bulk",
            "Insert MANY steps into ONE sequence in a single call — far more efficient than " +
            "calling insert_step repeatedly (the file is saved only ONCE for the whole batch). " +
            "Steps are appended in array order, so list them top-to-bottom as they should appear. " +
            "Each step may optionally carry its own comment, expression and SequenceCall target, " +
            "collapsing what used to be ~4 calls per step into one. Use this to build a whole " +
            "sequence (or a complete If/Else/loop block) at once. Same step-type and adapter rules " +
            "as insert_step: use 'NI_Flow_If'/'NI_Flow_Else'/'NI_Flow_End' for branching, never Goto/Label. " +
            "For a flow BRANCH step (If/ElseIf/While/DoWhile/For/Select/Case) a plain 'expression' (default/" +
            "'Statement' type) is written to the branch CONDITION (ConditionExpr/ItemExpr) so it actually " +
            "branches — pass expression_type 'Pre'/'Post'/'Status' only to override that. A counted " +
            "NI_Flow_For can be declared in ONE step: 'expression' is its loop-continue test, plus optional " +
            "'init_expr' (InitializationExpr) and 'increment_expr' (IncrementExpr), e.g. init_expr 'Locals.i = 0', " +
            "expression 'Locals.i < 10', increment_expr 'Locals.i += 1'. An NI_Flow_ForEach takes 'array_expr' " +
            "(the collection) + 'element_expr' (the per-element variable). An NI_Flow_Case takes 'is_default' " +
            "to mark the default branch. When a step's " +
            "concrete type is unclear, default to 'SequenceCall' (may stay unlinked) rather than 'Statement'.",
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
                            "Adapter: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'Sequence', 'None' (default). " +
                            "'Sequence' on a non-SequenceCall step (e.g. 'Action') makes it call a subsequence via the Sequence Adapter.")
                        .AddOptional("comment", "string", "Step comment/description (kept short)")
                        .AddOptional("expression", "string",
                            "Step expression (e.g. an NI_Flow_If condition). For NI_Flow_For it is the loop-continue test (ConditionExpr), e.g. 'Locals.i < 10'.")
                        .AddOptional("expression_type", "string",
                            "Where to store it: 'Statement' (default) -> Post Expression (primary for Statement steps); 'Pre' -> before the step; 'Post' -> after the step; 'Status' -> status expression.")
                        .AddOptional("init_expr", "string",
                            "NI_Flow_For only: loop initialization expression (InitializationExpr), e.g. 'Locals.i = 0'. Ignored for other step types.")
                        .AddOptional("increment_expr", "string",
                            "NI_Flow_For only: loop increment expression (IncrementExpr), e.g. 'Locals.i += 1'. Ignored for other step types.")
                        .AddOptional("array_expr", "string",
                            "NI_Flow_ForEach only: the collection to iterate (ArrayExpr), e.g. 'Locals.Items'. Ignored for other step types.")
                        .AddOptional("element_expr", "string",
                            "NI_Flow_ForEach only: the per-element variable (ArrayElementExpr), e.g. 'Locals.Item'. Ignored for other step types.")
                        .AddOptional("is_default", "boolean",
                            "NI_Flow_Case only: mark this case as the default branch (IsDefault=true). Ignored for other step types.")
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
            "blocks (openers ↔ End), ElseIf/Else inside If, each Case closed by its OWN End " +
            "inside a Select (a single End for the whole Select nests the cases), Break/Continue " +
            "inside a loop, no Goto/Label, unique step names, known step types, that every " +
            "Locals.X referenced in a condition is declared in 'locals', and — when 'parameters' " +
            "is supplied — that every Parameters.X referenced is declared there too " +
            "(E_UNDECLARED_PARAM). Tip: when a step's concrete type is unclear, default to " +
            "'SequenceCall' (may stay unlinked) rather than 'Statement'.",
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
                    required: false)
                .AddArray("parameters",
                    "Planned sequence parameters (names only). Supply this to also validate " +
                    "Parameters.X references in conditions/expressions (E_UNDECLARED_PARAM). " +
                    "Omit to skip the parameter check (locals-only, historical behaviour).",
                    item => item
                        .AddRequired("name", "string", "Parameter name"),
                    required: false),
            ValidateSequencePlanAsync);

        Register("audit_sequence_references",
            "POST-BUILD reference audit — reads the expressions ACTUALLY stored on a built " +
            "sequence (ConditionExpr/ItemExpr/PreExpression/PostExpression/StatusExpression) and " +
            "reports every Locals.X / Parameters.X / FileGlobals.X reference that is NOT declared " +
            "in that sequence's locals/parameters (or the file globals). Unlike validate_sequence_plan " +
            "— which only checks Locals refs present in the build PLAN and never sees conditions " +
            "written afterwards via set_flow_condition — this reads the REAL sequence, so it catches " +
            "dangling parameter/local refs introduced by EITHER path (e.g. an If condition referencing " +
            "Parameters.X where X was never declared). Returns {valid, issueCount, issues[], stats{}}; " +
            "each issue carries code E_UNDECLARED_LOCAL / E_UNDECLARED_PARAM / E_UNDECLARED_FILEGLOBAL " +
            "plus the sequence, step, property and expression. Omit sequence_name to audit EVERY " +
            "sequence in the file. Advisory and read-only — it reports, it never modifies. References " +
            "under other scopes (StationGlobals, RunState.*, Step.*, etc.) are intentionally not audited.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Sequence to audit. Omit to audit every sequence in the file."),
            AuditSequenceReferencesAsync);

        Register("insert_local_variable",
            "Insert a new local variable into a sequence. data_type accepts the builtins " +
            "'string'/'number'/'boolean'/'container', 'reference' (an Object Reference, default " +
            "Nothing) OR the name of a custom data type / enum defined in the file (e.g. " +
            "'MyEnum') — anything that isn't a builtin is treated as a named type. " +
            "To create an ARRAY local (required before get_array_variable/set_array_element/" +
            "resize_array_variable can be used), append '[]' to the type (e.g. 'number[]') or " +
            "prefix 'array:' (e.g. 'array:string').",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Name of the local variable")
                .AddRequired("data_type", "string",
                    "Data type: 'string', 'number', 'boolean', 'container', 'reference', or the " +
                    "name of a custom/enum type in the file. Append '[]' (or prefix 'array:') " +
                    "for an array, e.g. 'number[]'.")
                .AddOptional("default_value", "string", "Optional default value"),
            InsertLocalVariableAsync);

        Register("set_local_variable_comment",
            "Set the comment/description of a local variable in a sequence. variable_name may be a " +
            "dotted path to a nested container member (e.g. 'MyCont.Field') to comment that member.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("variable_name", "string", "Local variable name, or a dotted path to a nested member")
                .AddRequired("comment", "string", "Comment text to set"),
            SetLocalVariableCommentAsync);

        Register("set_parameter_comment",
            "Set the comment/description of a SEQUENCE PARAMETER (or a nested member via a dotted " +
            "path, e.g. 'MyParam.Field'). This is the only tool that reaches a Parameter's comment — " +
            "set_local_variable_comment only touches Locals.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("parameter_name", "string", "Parameter name, or a dotted path to a nested member")
                .AddRequired("comment", "string", "Comment text to set"),
            SetParameterCommentAsync);

        Register("set_file_global_comment",
            "Set the comment/description of a FILE GLOBAL (or a nested container member via a dotted " +
            "path, e.g. 'MyCont.Field'). The FileGlobals counterpart of set_local_variable_comment " +
            "(which is Locals-only) and set_parameter_comment; writes to the file globals' " +
            "authored-defaults container (the same one set_file_global writes values to).",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("variable_name", "string", "File global name, or a dotted path to a nested member")
                .AddRequired("comment", "string", "Comment text to set"),
            SetFileGlobalCommentAsync);

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

        Register("set_step_property",
            "Set ANY property on a step by a dotted path RELATIVE TO THE STEP, then read it back. " +
            "This is the generic step-property writer — the only tool that reaches a step's own " +
            "properties: set_property_value/set_property resolve against FileGlobals/StationGlobals/" +
            "Locals (never a step), and the configure_*_module tools only touch the adapter module. " +
            "Essential for custom / None-adapter step types whose config lives in step properties, " +
            "e.g. NI_LV_RunVIAsynchronously ('Run VI Asynchronously'): set 'VIModule.ViCall.VIPath' " +
            "(the VI), 'RemoteHost'/'PortNumber'/'Timeout', 'VIModule.ViCall.ShowFrnPnl'. " +
            "Unlike configure_labview_module it does NOT change the step's adapter. For an " +
            "Expression-typed property (e.g. RemoteHost) pass the expression TEXT (a quoted literal " +
            "like '\"192.168.0.9\"', or an expression like 'Locals.Host'). value_type is " +
            "auto-detected (number / true|false / string) when omitted. The property path must " +
            "already exist on the step (this does not create new subproperties).",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("property_path", "string",
                    "Property path relative to the step (e.g. 'VIModule.ViCall.VIPath', 'PortNumber').")
                .AddRequired("value", "string", "The value to write, as text.")
                .AddOptional("value_type", "string",
                    "How to interpret 'value'. Omit to auto-detect (number / true|false / string).",
                    null, new[] { "number", "boolean", "string" })
                .AddOptional("unescape", "boolean",
                    "Decode \\r \\n \\t \\\\ \\uXXXX escape sequences in 'value' before writing " +
                    "(default false) — the only way to write bare control characters (e.g. CR in " +
                    "a VIDescription) through an MCP string parameter.", false)
                .AddOptional("save", "boolean", "Save the file after writing (default true).", true),
            SetStepPropertyAsync);

        Register("create_step_property",
            "CREATE a new subproperty on a step by a dotted path — the creation counterpart to " +
            "set_step_property, which requires the path to exist. Value types: 'number', " +
            "'boolean', 'string', 'container', 'reference' (Object Reference), or 'named_type' " +
            "with type_name for a typed container (e.g. 'SequenceArgument' for a SequenceCall " +
            "actual-argument entry, 'ErrorDialogOptions', 'NI_CustomResult'). Special value_type " +
            "'array_elements' resizes an EXISTING array property to num_elements — new elements " +
            "are instantiated with the array's ELEMENT type, which is the only way to author " +
            "typed array entries like a LabVIEW connector-pane prototype (TS.SData.ViCall.Parms) " +
            "or TS.AdditionalResultsHints. Idempotent: an existing path is left in place and only " +
            "the optional 'value' is applied. Examples: (1) Result.TimeoutOccurred as boolean; " +
            "(2) TS.SData.ActualArgs.SetPoint as named_type/SequenceArgument, then set its Expr " +
            "via set_step_property; (3) TS.SData.ViCall.Parms with array_elements/5, then fill " +
            "each Parms[i].Label/ArgVal via set_step_property. value_type 'enum' creates the member " +
            "as an instance of a named enum typedef (+type_name = the enum, e.g. 'Color'; value = its " +
            "ordinal number or symbolic name) so it gets its real enum type, not an anonymous container.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("property_path", "string",
                    "Property path relative to the step to create (e.g. 'Result.TimeoutOccurred', " +
                    "'TS.ErrorDialogOptions'). For 'array_elements' this addresses the existing array.")
                .AddRequired("value_type", "string",
                    "Type to create: number/boolean/string/container/reference, 'named_type' " +
                    "(+type_name), 'enum' (+type_name), or 'array_elements' (+num_elements).",
                    new[] { "number", "boolean", "string", "container", "reference",
                            "named_type", "enum", "array_elements" })
                .AddOptional("type_name", "string",
                    "TestStand type name for value_type='named_type' (e.g. 'SequenceArgument', " +
                    "'Error', 'ErrorDialogOptions', 'NI_CustomResult') or the enum type for 'enum'.")
                .AddOptional("num_elements", "number",
                    "Element count for value_type='array_elements' (SetNumElements).")
                .AddOptional("value", "string",
                    "Optional scalar value to assign after creation (number/boolean/string, or for " +
                    "'enum' the ordinal number or symbolic name).")
                .AddOptional("unescape", "boolean",
                    "Decode \\r \\n \\t \\\\ \\uXXXX escapes in 'value' (default false).", false)
                .AddOptional("save", "boolean", "Save the file after writing (default true).", true),
            CreateStepPropertyAsync);

        Register("set_step_property_flags",
            "Set the raw PropFlags bitfield of a step property (PropertyObject.SetFlags) — " +
            "e.g. 0x4 PassByReference, 0x2000 NotSerializedIfDefault-style markers like the " +
            "0x200000 flag TestStand puts on module containers. Returns the read-back flags. " +
            "Use get_property_tree (node.flags) to inspect current values first.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("property_path", "string",
                    "Property path relative to the step (empty string = the step itself).")
                .AddRequired("flags", "number", "The PropFlags bitfield value to set.")
                .AddOptional("save", "boolean", "Save the file after writing (default true).", true),
            SetStepPropertyFlagsAsync);

        Register("rename_step_property",
            "Set the NAME of a step property (PropertyObject.Name). Essential for named ARRAY " +
            "ELEMENTS: the entries of a LabVIEW connector-pane prototype (TS.SData.ViCall.Parms) " +
            "carry the parameter label as their ELEMENT NAME — the editor and FileDiffer display " +
            "'[i] Name' and PAIR array elements by it. create_step_property(array_elements) " +
            "creates elements UNNAMED, so set each element's name afterwards (e.g. path " +
            "'TS.SData.ViCall.Parms[0]', new_name 'error in (no error)'). get_property_tree " +
            "reports existing element names via the node's 'elementName' field.",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("property_path", "string",
                    "Property path relative to the step (e.g. 'TS.SData.ViCall.Parms[0]').")
                .AddRequired("new_name", "string", "The name to assign (PropertyObject.Name).")
                .AddOptional("save", "boolean", "Save the file after writing (default true).", true),
            RenameStepPropertyAsync);

        Register("run_sequence_analyzer",
            "Run the NI TestStand Sequence Analyzer and return a HUMAN-READABLE TEXT summary, " +
            "grouped by severity by default (like the editor's Analysis Results 'Group By' pane). " +
            "Use group_by='rule' to group by rule, or group_by='none' for a flat sorted list. " +
            "This is the quick text variant with NO severity filter; for STRUCTURED JSON (typed " +
            "messages, severity counts, optional groups) and a min_severity filter use " +
            "analyze_sequence_file — the same underlying analyzer. " +
            "COLD/LabVIEW NOTE: analyzing a file with LabVIEW .lvlibp steps on a cold module cache " +
            "can exceed the ~60s MCP transport timeout (-32001) because the analyzer loads each " +
            "code module. Set async=true to get an immediate 'jobId' + status='running' and poll " +
            "get_analysis_status(job_id) for the (structured) result.",
            s => s
                .AddRequired("file_path", "string", "Absolute path to the sequence file to analyze")
                .AddOptional("group_by", "string",
                    "How to group the output: 'severity' (default), 'rule', or 'none' for a flat list.",
                    "severity", new[] { "severity", "rule", "none" })
                .AddOptional("async", "boolean",
                    "Run asynchronously: return a 'jobId' immediately and poll get_analysis_status " +
                    "(which returns the structured result) instead of waiting inline. Use for files " +
                    "with LabVIEW .lvlibp steps on a cold cache. Default false.", false),
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
            "Get a GLOBAL variable's value by lookup string. SCOPE IS LIMITED: only " +
            "'StationGlobals.X' (engine globals) and 'FileGlobals.X' (the FIRST loaded sequence " +
            "file) resolve; a bare name is treated as a StationGlobal. It does NOT reach a " +
            "sequence's Locals or a live RunState — for Locals use get_local_variables, for a " +
            "specific file's globals use get_file_globals (takes an explicit file_path, so it is " +
            "unambiguous when several files are loaded), and for a running thread's scope use the " +
            "thread-context tools (evaluate_in_thread_context / get_runtime_variable).",
            s => s.AddRequired("lookup_string", "string",
                "Global lookup string: 'StationGlobals.X', 'FileGlobals.X', or a bare StationGlobal name."),
            GetPropertyAsync);

        Register("set_property",
            "Set a GLOBAL variable's value by lookup string. SAME LIMITED SCOPE as get_property: " +
            "only 'StationGlobals.X' and 'FileGlobals.X' (the FIRST loaded file) resolve; a bare " +
            "name is a StationGlobal. It does NOT reach a sequence's Locals, a step, or RunState. " +
            "To set a Local use set_local_variable; a specific file's global use set_file_global " +
            "(explicit file_path); create-with-a-type use set_property_value; a step's own property " +
            "use set_step_property.",
            s => s
                .AddRequired("lookup_string", "string",
                    "Global lookup string: 'StationGlobals.X', 'FileGlobals.X', or a bare StationGlobal name.")
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
            "('truncated'=true marks cut-offs). For just ONE property's immediate value and direct " +
            "subproperties (single level) use get_property_object instead.",
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
            "Insert a new FileGlobal variable into a sequence file. data_type accepts the " +
            "builtins 'string'/'number'/'boolean'/'container', 'reference' (an Object " +
            "Reference, default Nothing), OR the name of a custom data type / enum defined in " +
            "the file — same contract as insert_local_variable. To create an ARRAY file " +
            "global (required before the array tools can operate on it), append '[]' to the " +
            "type (e.g. 'number[]') or prefix 'array:' (e.g. 'array:string').",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("variable_name", "string", "Name of the new FileGlobal variable")
                .AddRequired("data_type", "string", "Data type: 'string', 'number', 'boolean', " +
                    "'container', 'reference' (Object Reference), or a named custom type. " +
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
            "Get the structured OVERVIEW of a single step — the same shape as one entry from " +
            "get_steps (name, type, group, enabled, module info, comment). For the flat key/value " +
            "of the step's expressions and run/pass/fail/loop/comparison settings use " +
            "get_step_properties; for arbitrary NESTED subproperty paths use " +
            "get_property_tree(root='SequenceFile').",
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
            "Get a flat key/value map of a step's configuration: Name, StepType, Enabled, " +
            "Pre/Post/Status expressions, Description, Comment, ModuleExpression, RunMode, " +
            "Pass/FailAction, LoopType and ComparisonType (keys absent when the step type lacks " +
            "them). For the structured step overview use get_step; for arbitrary nested subproperty " +
            "paths (the full property bag) use get_property_tree(root='SequenceFile').",
            s => s
                .AddRequired("sequence_file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_name", "string", "Name of the step"),
            GetStepPropertiesAsync);

        // Reports
        Register("generate_report",
            "Return report METADATA for a completed execution (execution id, requested path, " +
            "format). NOTE: it does NOT itself write the report file — to actually save a report " +
            "to disk use save_report.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID to report on")
                .AddRequired("output_path", "string", "File path where the report will be saved")
                .AddOptional("format", "string",
                    "Report format: 'HTML', 'XML', 'TXT', 'ATML'", "HTML",
                    new[] { "HTML", "XML", "TXT", "ATML" }),
            GenerateReportAsync);

        Register("get_report_text",
            "Get the report as TEXT for a (possibly still running) execution — returns the " +
            "execution's ReportText property, so it works mid-run. For the COMPLETE report body " +
            "of a finished execution use get_full_report (Report.All); to write a report file use " +
            "save_report.",
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
            "data types defined in that file; otherwise returns engine-level data types. " +
            "NOTE: for a file this lists only file-ROOT subproperty data types — it does NOT see " +
            "LabVIEW-cluster container typedefs (which live in the file's TypeUsageList). Use " +
            "list_file_typedefs for those.",
            s => s.AddOptional("sequence_file_path", "string",
                "Optional path to a sequence file to read data types from"),
            GetDataTypesAsync);

        Register("list_file_typedefs",
            "List every custom data TYPE embedded in a file's TypeUsageList — INCLUDING the " +
            "LabVIEW-cluster container typedefs that get_data_types cannot see (those are stored in " +
            "the TypeUsageList, not as file-root subproperties). Each entry has the type name, a " +
            "coarse kind, and whether it is attached to the file. Use together with copy_typedefs to " +
            "reproduce such types in a rebuilt file.",
            s => s.AddRequired("file_path", "string", "Path to the sequence file"),
            GetFileTypeDefsAsync);

        Register("copy_typedefs",
            "Copy custom data type definitions from one sequence file into another and attach them so " +
            "they persist embedded. This is the ONLY way to reproduce LabVIEW-cluster typedefs in a " +
            "rebuilt file: they carry GUIDs/structure that cannot be recreated field-by-field with " +
            "create_data_type, so a tool-only rebuild must copy them from the original. Pass explicit " +
            "'type_names' (reliable) or omit to copy EVERY embedded type. Types already present in the " +
            "destination are left untouched. Returns the names actually copied. Removes the previous " +
            "need to physically copy the whole .seq just to keep its data types.",
            s => s
                .AddRequired("source_file_path", "string", "File to copy type definitions FROM (e.g. the original)")
                .AddRequired("dest_file_path", "string", "File to copy the type definitions INTO (the rebuild)")
                .AddOptional("type_names", "array", "Specific type names to copy; omit to copy all embedded types.")
                .AddOptional("save", "boolean", "Save the destination file after copying (default true).", true),
            CopyTypeDefsAsync);

        Register("copy_file_attributes",
            "Copy the file-level name/value ATTRIBUTES from one sequence file into another. TestStand " +
            "attributes are a SEPARATE namespace from subproperties (reached via the file-root " +
            "PropertyObjectFile's Attributes), invisible to get_property_tree and not reproduced by " +
            "copy_typedefs / copy_step_module. Copies whatever the ENGINE exposes on the loaded file; " +
            "each subtree is flag-preservingly cloned before it is attached. Pass explicit top-level " +
            "'attribute_names' or omit to copy all. Returns {copiedCount, copied[], warnings[]}. " +
            "IMPORTANT LIMITATION: the Sequence Analyzer's ignored-message list (NI.Analyzer." +
            "IgnoredMessages, shown by FileDiffer under 'File Properties > Attributes > NI') is NOT " +
            "loaded into the in-memory object by the TestStand engine API at all — only FileDiffer's " +
            "raw disk reader sees it — so this tool CANNOT read or reproduce it, and a rebuild will " +
            "retain that one 'Attributes' diff. That residual is cosmetic (a stale analyzer artifact) " +
            "and is expected; see diff_sequence_files.",
            s => s
                .AddRequired("source_file_path", "string", "File to copy attributes FROM (e.g. the original)")
                .AddRequired("dest_file_path", "string", "File to copy the attributes INTO (the rebuild)")
                .AddOptional("attribute_names", "array", "Top-level attribute names to copy; omit to copy all.")
                .AddOptional("save", "boolean", "Save the destination file after copying (default true).", true),
            CopyFileAttributesAsync);

        Register("copy_file_globals",
            "Copy the FILE GLOBAL variables (the FileGlobalDefaults container) from one sequence file " +
            "into another via a flag-preserving deep clone — each global with its exact type, value, " +
            "comment, PropFlags and nested container/enum members (enum ordinals, Object References and " +
            "typed container members are all preserved, including the FileDiffer's [val]/{val} " +
            "explicit-vs-default distinction). File globals are NOT part of any sequence, so " +
            "duplicate_sequence and copy_step_module do NOT carry them — this is the reliable way to " +
            "reproduce them in a 1:1 rebuild. Run copy_typedefs FIRST so any custom types the globals " +
            "reference exist in the destination. Pass explicit top-level 'global_names' to copy only " +
            "those, or omit to copy every file global from the source. Returns {copiedCount, copied[], " +
            "warnings[]}.",
            s => s
                .AddRequired("source_file_path", "string", "File to copy file globals FROM (e.g. the original)")
                .AddRequired("dest_file_path", "string", "File to copy the file globals INTO (the rebuild)")
                .AddOptional("global_names", "array", "Top-level file-global names to copy; omit to copy all.")
                .AddOptional("save", "boolean", "Save the destination file after copying (default true).", true),
            CopyFileGlobalsAsync);

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
            "fail to validate. " +
            "Expression-language gotchas: '==' is CASE-INSENSITIVE and does NOT trim ('\"A\"==\"a\"' " +
            "is True); there is NO implicit string->bool cast (Val(\"True\")==0), so compare " +
            "explicitly (e.g. Locals.S == \"True\"); StrComp is case-SENSITIVE.",
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
            "Configure the loop settings for a step. loop_type: 'NoLoop', 'For'/'FixedNumLoops', " +
            "'PassFailCount', or 'Custom' (a condition-driven loop). A CUSTOM loop is fully described " +
            "by its four expressions — pass 'init_expr' (LoopInitialize, e.g. 'RunState.LoopIndex = 0'), " +
            "'while_expr' (LoopWhile / loop-continue test), 'inc_expr' (LoopIncrement) and 'status_expr' " +
            "(LoopStatus, e.g. 'RunState.LoopNumPassed / RunState.LoopNumIterations < 1 ? \"Failed\" : \"Passed\"'). " +
            "'While'/'Condition' are accepted aliases for 'Custom'.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("loop_type", "string",
                    "Loop type: 'NoLoop', 'For'/'FixedNumLoops', 'PassFailCount', or 'Custom'")
                .AddOptional("init_expr", "string", "Initialiser expression (For loop / Custom LoopInitialize)")
                .AddOptional("while_expr", "string", "While/condition expression (Custom LoopWhile)")
                .AddOptional("inc_expr", "string", "Increment expression (For loop / Custom LoopIncrement)")
                .AddOptional("status_expr", "string", "Custom-loop status expression (LoopStatus)"),
            SetStepLoopAsync);

        Register("set_flow_condition",
            "Set the flow-control CONDITION of a branch step — the dedicated property the engine " +
            "actually evaluates to branch (NOT Pre/Post/Status). Writes ConditionExpr for " +
            "NI_Flow_If / NI_Flow_ElseIf / NI_Flow_While / NI_Flow_DoWhile (the boolean condition), " +
            "and ItemExpr for NI_Flow_Select (the switch expression) and NI_Flow_Case (the case " +
            "value(s), e.g. \"A\", \"B\" or {Enums.X.A, Enums.X.B}). For a default Case pass " +
            "is_default=true (condition may be empty). NOTE: the bulk-insert / set_step_expression " +
            "'expression' field does NOT set this — it writes the Post Expression, which would " +
            "evaluate-and-discard without branching; this tool also clears such a duplicate Post " +
            "Expression automatically. REJECTS a non-branch step (e.g. NI_Flow_End): a condition " +
            "there has no effect — a DoWhile's loop condition belongs on the NI_Flow_DoWhile opener, " +
            "not its End. Note '==' is case-insensitive and there is no string->bool cast.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the flow step (If/ElseIf/While/DoWhile/Select/Case)")
                .AddRequired("condition", "string",
                    "Condition expression: ConditionExpr for If/ElseIf/While/DoWhile; ItemExpr (switch " +
                    "for Select, case value(s) for Case). May be empty only for a default Case.")
                .AddOptional("is_default", "boolean",
                    "NI_Flow_Case only: mark this as the default case (IsDefault=true)."),
            SetFlowConditionAsync);

        Register("configure_for_loop",
            "Configure a counted NI_Flow_For loop in ONE call — writes its three dedicated step " +
            "properties InitializationExpr / ConditionExpr / IncrementExpr (the parts a For loop " +
            "evaluates; NOT Pre/Post/Status, and NOT the generic step LoopType). Two ways to use it: " +
            "(1) convenience — pass 'count' (and optional 'index_var', default 'Locals.i') to generate " +
            "the standard loop 'index_var = 0' / 'index_var < count' / 'index_var += 1'; " +
            "(2) explicit — pass any of 'init_expr' / 'condition_expr' / 'increment_expr' (these OVERRIDE " +
            "the generated ones). REJECTS a non-For step. NOTE: it does NOT create the index variable — " +
            "declare it with insert_local_variable (type number). Alternatively a For loop can be built " +
            "inline via insert_steps_bulk (expression + init_expr + increment_expr).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NI_Flow_For step")
                .AddOptional("count", "number",
                    "Iteration count for the convenience form: generates 'index_var = 0' / 'index_var < count' / 'index_var += 1'.")
                .AddOptional("index_var", "string",
                    "Loop index variable for the convenience form (default 'Locals.i'). Must be declared separately (type number).")
                .AddOptional("init_expr", "string", "Explicit InitializationExpr (overrides the generated one).")
                .AddOptional("condition_expr", "string", "Explicit ConditionExpr / loop-continue test (overrides the generated one).")
                .AddOptional("increment_expr", "string", "Explicit IncrementExpr (overrides the generated one).")
                .AddOptional("save", "boolean", "Save the file after configuring (default true).", true),
            ConfigureForLoopAsync);

        Register("configure_foreach_loop",
            "Configure an NI_Flow_ForEach loop in ONE call — writes its dedicated step properties " +
            "ArrayExpr (the collection to iterate) and ArrayElementExpr (the per-element variable), " +
            "plus optional OffsetExpr / SubscriptExpr. A ForEach with an empty ArrayExpr never iterates, " +
            "so 'array_expr' is the essential field (like a For loop's condition). REJECTS a non-ForEach " +
            "step. NOTE: it does NOT create the element variable — declare it with insert_local_variable " +
            "(type matching the array element type). Alternatively a ForEach can be built inline via " +
            "insert_steps_bulk (array_expr + element_expr).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NI_Flow_ForEach step")
                .AddOptional("array_expr", "string", "The collection to iterate (ArrayExpr), e.g. 'Locals.Items'.")
                .AddOptional("element_expr", "string", "The per-element variable (ArrayElementExpr), e.g. 'Locals.Item'. Must be declared separately.")
                .AddOptional("offset_expr", "string", "Optional start offset into the collection (OffsetExpr).")
                .AddOptional("subscript_expr", "string", "Optional subscript/index variable (SubscriptExpr).")
                .AddOptional("save", "boolean", "Save the file after configuring (default true).", true),
            ConfigureForEachLoopAsync);

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
            "TestStand REJECTS it on an individual step. Use one of (1)-(4) for a per-step setting. " +
            "This tool now rejects value 5 up front with a clear error.",
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
            "Change the adapter (LabVIEW, CVI, C++/DLL, .NET, Python, ActiveX/COM, Sequence, None, etc.) " +
            "of a step. Changing the ADAPTER keeps the step's TYPE: set it to 'Sequence' on an 'Action' " +
            "step to make that Action call a subsequence (then configure_sequence_call_module sets the " +
            "target — it works on any Sequence-Adapter step, not only 'SequenceCall' steps).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("new_adapter", "string",
                    "Adapter: 'LabVIEW', 'CVI', 'C++/DLL', 'DotNet', 'Python', 'ActiveX', 'Sequence', 'None' " +
                    "(friendly name or exact key name, e.g. 'Automation Adapter' for ActiveX, " +
                    "'DLL Flexible Prototype Adapter' for C++/DLL, 'Sequence Adapter' for Sequence)"),
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
            "Write the report of an execution to a FILE on disk (this is the tool that actually " +
            "persists a report — it calls the execution's Report.Save in the given format). The " +
            "execution must still be in memory (a stale/unknown id errors). Prefer this over " +
            "generate_report, which only returns report metadata and does not write the file.",
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
            "Get the COMPLETE report body for a completed execution (the execution's Report.All). " +
            "Fuller than get_report_text (which returns the lighter ReportText and also works while " +
            "the execution is still running). To write a report to a file use save_report. The " +
            "execution must still be in memory.",
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
            "Returns true if a redo was performed, false if nothing to redo. " +
            "NOTE: like undo, edits made through the headless MCP tools are NOT auto-recorded onto " +
            "the undo/redo stack, so this normally has nothing to redo after such edits.",
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
            "Compare two TestStand sequence files. mode='native' (DEFAULT) runs NI's native FileDiffer " +
            "for an AUTHORITATIVE, field-level diff (identical to diff_sequence_files) — use this to " +
            "VERIFY a rebuild/clone. mode='structural' runs a fast, in-process COARSE comparison that " +
            "only sees sequence/step NAMES, a few step properties (RunMode, Pre/Post/Status, Comment, " +
            "adapter) and the presence of locals/parameters; it does NOT see ActualArgs, container " +
            "members, parameter defaults/comments or threading, so its result carries fidelity/note " +
            "fields and a TotalDifferences of 0 does NOT prove the files are identical. Prefer 'native' " +
            "unless you explicitly want the quick structural overview.",
            s => s
                .AddRequired("file_path_1", "string", "Path to the first sequence file")
                .AddRequired("file_path_2", "string", "Path to the second sequence file")
                .AddOptional("mode", "string",
                    "'native' (default, authoritative FileDiffer) or 'structural' (fast, coarse in-process).",
                    "native", new[] { "native", "structural" }),
            CompareSequenceFilesAsync);

        Register("diff_sequence_files",
            "THE canonical way to VERIFY a rebuild/clone: runs NI TestStand's NATIVE FileDiffer on two " +
            "sequence files and returns its detailed, classified diff — exactly what the Sequence " +
            "Editor's Diff/Merge view shows. (compare_sequence_files mode='native' is the SAME diff; " +
            "prefer THIS tool for verification. Use compare_sequence_files only for its fast, coarse " +
            "'structural' mode.) Returns per-file tallies (changes/insertions/deletions) plus a flat " +
            "list of differences, each with a change type (Insert, Delete, ValueChange, Conflict, " +
            "Moved), the property-tree path, and the value in each file. " +
            "READING THE VALUES: a value in braces {val} means a TYPE-DEFAULT (the property was NOT " +
            "explicitly set) while brackets [val] means an EXPLICITLY-SET value — so an enum/member " +
            "already at its type default should be LEFT UNSET when rebuilding (setting it flips {val}→" +
            "[val] and creates a spurious diff). " +
            "KNOWN IRREDUCIBLE RESIDUAL: a 'File Properties > Attributes' Delete (e.g. NI > Analyzer > " +
            "IgnoredMessages) cannot be closed by any rebuild — the TestStand engine API does not load " +
            "these file attributes into the in-memory object (only FileDiffer's raw reader sees them), " +
            "so no tool can read or reproduce them. Treat such an Attributes-only diff as a functional " +
            "match (identical=false is expected in that case).",
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

        // ── Live Thread-Context Inspection (runtime debugging) ─────────────────
        // These read/write the LIVE SequenceContext (== ThisContext) and RunState of a running or
        // paused thread at a chosen call-stack frame — the runtime values the Sequence Editor's
        // Variables/Watch pane shows. This is the ONLY way to see them: get_property_tree and
        // evaluate_expression resolve against engine Globals and never reach the thread scope.
        // A thread must be executing inside a sequence (running or paused on a step).

        Register("inspect_thread_context",
            "Dump the LIVE variable/property tree of a running or paused thread's call-stack " +
            "frame (the runtime values, NOT the static file defaults). 'scope' selects the " +
            "sub-tree: 'runstate' (default — the execution cursor: StepIndex, NextStepIndex, " +
            "StepGroup, LoopIndex, SequenceError, flags …), 'locals', 'parameters', 'step' " +
            "(the current step's live properties incl. its Result), 'sequence', or 'full'/" +
            "'thiscontext' (everything — large). Use 'lookup_string' to descend to a sub-path " +
            "(e.g. 'SequenceError' or 'Result.Status'), 'call_stack_index' to inspect a caller " +
            "frame (0 = current/innermost). Only works while the thread is executing inside a " +
            "sequence. Complements get_property_tree, which cannot see the live thread scope.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID (from start_execution)")
                .AddOptional("thread_id", "string",
                    "Thread ID or index (from get_execution_threads). Default: first thread.")
                .AddOptional("call_stack_index", "integer",
                    "Call-stack frame: 0 = current/innermost, higher = toward the entry point. Default 0.", 0)
                .AddOptional("scope", "string",
                    "Sub-tree to dump. Default 'runstate'.", "runstate",
                    new[] { "runstate", "locals", "parameters", "step", "sequence", "full", "thiscontext" })
                .AddOptional("lookup_string", "string",
                    "Optional sub-path within the scope (e.g. 'SequenceError', 'Result.Status').")
                .AddOptional("max_depth", "integer",
                    "Max recursion depth. Default 3 (RunState nests recursively — raise cautiously).", 3)
                .AddOptional("include_hidden", "boolean",
                    "Include hidden (TS.*/internal) properties. Default false for a clean debug view.", false)
                .AddOptional("max_array_elements", "integer",
                    "Max array elements expanded per array; 0 = unlimited. Default 50.", 50),
            InspectThreadContextAsync);

        Register("evaluate_in_thread_context",
            "Evaluate a TestStand expression in the LIVE context of a running/paused thread frame " +
            "— the scope where 'Locals.X', 'Parameters.X', 'RunState.X', 'Step.X', 'FileGlobals.X' " +
            "resolve to their RUNTIME values (e.g. 'Locals.Counter + 1', 'RunState.NextStepIndex', " +
            "'Str(Locals.Message)'). This is exactly the scope evaluate_expression CANNOT reach " +
            "(that one only sees Station/FileGlobals). Read-only evaluation; to change a value use " +
            "set_runtime_variable. Requires the thread to be executing inside a sequence.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("expression", "string",
                    "TestStand expression, evaluated with the frame's ThisContext as root.")
                .AddOptional("thread_id", "string", "Thread ID or index. Default: first thread.")
                .AddOptional("call_stack_index", "integer",
                    "Call-stack frame (0 = current/innermost). Default 0.", 0),
            EvaluateInThreadContextAsync);

        Register("get_runtime_variable",
            "Read ONE live variable/property by path from a running/paused thread frame, with its " +
            "value and type. Path is relative to ThisContext, e.g. 'Locals.Counter', " +
            "'Parameters.SerialNumber', 'RunState.NextStepIndex', 'RunState.SequenceError.Msg'. " +
            "Returns the RUNTIME value (get_local_variables returns the static file default instead). " +
            "For a whole sub-tree use inspect_thread_context; for a computed expression use " +
            "evaluate_in_thread_context.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("property_path", "string",
                    "Path relative to ThisContext (e.g. 'Locals.Counter', 'RunState.NextStepIndex').")
                .AddOptional("thread_id", "string", "Thread ID or index. Default: first thread.")
                .AddOptional("call_stack_index", "integer",
                    "Call-stack frame (0 = current/innermost). Default 0.", 0),
            GetRuntimeVariableAsync);

        Register("set_runtime_variable",
            "Write ONE live variable/property by path in a running/paused thread frame, then read " +
            "it back. Path is relative to ThisContext. Powerful debug actions: set " +
            "'RunState.NextStepIndex' to redirect execution (the 'Set Next Step' action), patch a " +
            "'Locals.X'/'Parameters.X' value before resuming, or clear 'RunState.SequenceError'/" +
            "'RunState.GotoCleanup'. Only meaningful while the thread is PAUSED (or parked); writing " +
            "a freely-running thread races with it. 'value_type' is auto-detected when omitted.",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddRequired("property_path", "string",
                    "Path relative to ThisContext (e.g. 'Locals.Counter', 'RunState.NextStepIndex').")
                .AddRequired("value", "string", "The value to write, as text.")
                .AddOptional("thread_id", "string", "Thread ID or index. Default: first thread.")
                .AddOptional("call_stack_index", "integer",
                    "Call-stack frame (0 = current/innermost). Default 0.", 0)
                .AddOptional("value_type", "string",
                    "How to interpret 'value'. Omit to auto-detect (number / true|false / string).",
                    null, new[] { "number", "boolean", "string" }),
            SetRuntimeVariableAsync);

        Register("get_runstate_summary",
            "Get a curated FLAT snapshot of the most-used RunState fields for a running/paused " +
            "thread frame — the 'where am I / what is the state' one-shot: current step/sequence/" +
            "file, StepGroup, StepIndex, NextStepIndex, PreviousStepIndex, CallStackDepth, " +
            "LoopIndex, NumStepsExecuted, the SequenceFailed/GotoCleanup/ErrorReported flags and " +
            "SequenceError (code/msg/occurred). Convenience over inspect_thread_context(scope=runstate).",
            s => s
                .AddRequired("execution_id", "string", "Execution ID")
                .AddOptional("thread_id", "string", "Thread ID or index. Default: first thread.")
                .AddOptional("call_stack_index", "integer",
                    "Call-stack frame (0 = current/innermost). Default 0.", 0),
            GetRunStateSummaryAsync);

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
            "Deep-clone a whole sequence — EVERY step, code module, local, parameter, the sequence " +
            "Comment and all settings (RunMode/RecordResults/failure+cleanup options) — within the same " +
            "file or into a DIFFERENT file (target_file_path). This is a flag-preserving copy (not a " +
            "name-only shell), so it is the FASTEST and most faithful primitive for a 1:1 cross-file " +
            "rebuild: it replaces the per-step insert_steps_bulk + copy_step_module dance with ONE call " +
            "per sequence. For a cross-file clone the referenced data types must already exist in the " +
            "target (run copy_typedefs FIRST) — the clone carries type references by GUID. " +
            "FULL 1:1 REBUILD RECIPE: (1) create_sequence_file; (2) copy_typedefs (all types); " +
            "(3) duplicate_sequence source→target for each sequence, in source order, keeping the same " +
            "name; (4) delete_sequence the default 'MainSequence'; (5) copy_file_globals (globals are " +
            "not part of any sequence); (6) copy_file_attributes + set_file_properties (comment/" +
            "version); (7) save_sequence_file, then verify with diff_sequence_files.",
            s => s
                .AddRequired("source_file_path", "string", "Path to the source sequence file")
                .AddRequired("source_sequence_name", "string", "Name of the sequence to copy")
                .AddRequired("new_sequence_name", "string", "Name for the new duplicate sequence")
                .AddOptional("target_file_path", "string",
                    "Path to the target sequence file (default: same as source file). For a different " +
                    "file, run copy_typedefs first so the clone's type references resolve."),
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
            "Inspect ONE property (local variable or file global) at a SINGLE level: its value " +
            "type, scalar value, named type, and — for containers/structs — its IMMEDIATE " +
            "subproperties with their types and values. For a full RECURSIVE walk of a whole tree " +
            "(StationGlobals / FileGlobals / the entire SequenceFile) use get_property_tree.",
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
            "Targets a sequence's local variable (with sequence_name) or a file global (without). " +
            "It resolves against Locals / FileGlobals ONLY — it NEVER reaches a step's own property; " +
            "use set_step_property (a dotted path relative to the step) for that. Use THIS tool when " +
            "you need to CREATE the property and/or fix its type (incl. 'container' or 'enum'); to " +
            "merely set the value of an EXISTING variable, set_local_variable / set_file_global are " +
            "simpler. " +
            "value_type 'named_type' (+ type_name) creates the member as a FULL instance of a " +
            "file-defined type — a container (e.g. 'TFW_DB_TestCasesLimits', 'VisionSensorSbsi_Reply_Payload', " +
            "'Error') materialises with ALL its fields (like the editor), so afterwards you only set the " +
            "fields that differ from the default; a named leaf gets its real type too. This is the ONLY " +
            "way a nested container member gets its named type instead of an anonymous 'Container' " +
            "(which otherwise causes analyzer 'Expected <Type>, found Container' errors). " +
            "value_type 'enum' (+ type_name) creates/sets an enum-typed member; pass its value as " +
            "'ordinal' (numeric, preferred) or 'value' (ordinal number OR symbolic name).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddOptional("sequence_name", "string",
                    "Name of the sequence. Omit to target a file global.")
                .AddRequired("property_name", "string",
                    "Property name or dotted lookup path (e.g. 'MyContainer.Sub')")
                .AddRequired("value_type", "string",
                    "Value type to create/set",
                    new[] { "number", "boolean", "string", "container", "named_type", "enum" })
                .AddOptional("value", "string",
                    "Value to assign (omitted for 'container'/'named_type'; for 'enum' the ordinal or symbolic name)")
                .AddOptional("type_name", "string",
                    "For value_type 'named_type' or 'enum': the file-defined type name (e.g. " +
                    "'TFW_DB_TestCasesLimits', 'Error', 'Color'). Required when creating.")
                .AddOptional("ordinal", "integer",
                    "For value_type 'enum': the numeric enum value (preferred over 'value')."),
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

        Register("set_property_node",
            "Create/set a property-tree node — and optionally its PropFlags — under ANY scope root: " +
            "Parameters / Locals / FileGlobals / StationGlobals / SequenceFile. This is the " +
            "scope-generic write counterpart of get_property_tree: set_property_value reaches only " +
            "Locals/FileGlobals and set_step_property_flags is step-only, so a sequence's " +
            "Parameters.* nodes (and nested submembers everywhere) had no writer/flag-setter. " +
            "lookup_string is a dotted path relative to the scope root (e.g. " +
            "'MDC_cmd.Request.Cmd.CmdEnum'); missing intermediate containers are created " +
            "automatically (create_missing_parents, default true) as anonymous Containers. " +
            "value_type: number/string/boolean/container/reference create a plain node; " +
            "'named_type' (+type_name) instantiates a FULL typed instance (fields materialise, like " +
            "the editor) so a container member gets its real type instead of an anonymous Container; " +
            "'enum' (+type_name) creates/sets an enum-typed member (value via 'ordinal' preferred, or " +
            "'value' = ordinal number OR symbolic name); 'array_elements' (+num_elements) sizes a " +
            "typed array. 'flags' sets raw PropFlags on the node (OR semantics — e.g. 0x84 = 0x04 " +
            "PassByReference + 0x80). A value is written only when supplied, so a flags-only call " +
            "leaves the value untouched. For StationGlobals the change commits to the station .ini " +
            "(file_path is unused); every other scope saves the sequence file. Returns the read-back " +
            "node {valueType, value, typeName, flags}.",
            s => s
                .AddRequired("file_path", "string",
                    "Path to the .seq file (unused for scope='StationGlobals').")
                .AddRequired("scope", "string", "The scope root the node lives under.",
                    new[] { "Parameters", "Locals", "FileGlobals", "StationGlobals", "SequenceFile" })
                .AddOptional("sequence_name", "string",
                    "Owning sequence — required for scope 'Parameters' or 'Locals'.")
                .AddRequired("lookup_string", "string",
                    "Dotted path to the node relative to the scope root (e.g. 'MDC_cmd.Request.Cmd').")
                .AddRequired("value_type", "string", "The kind of node to create/set.",
                    new[] { "number", "string", "boolean", "container", "reference",
                            "named_type", "enum", "array_elements" })
                .AddOptional("type_name", "string",
                    "For 'named_type'/'enum' (and a named 'array_elements' element type): the " +
                    "file-defined type name (e.g. 'MDC_com_CmdGeneric', 'CmdEnum').")
                .AddOptional("value", "string",
                    "Scalar value to assign (for 'enum' the ordinal number OR the symbolic name). " +
                    "Omit for container/named_type or a flags-only edit.")
                .AddOptional("ordinal", "integer",
                    "For value_type 'enum': the numeric enum value (preferred over 'value').")
                .AddOptional("num_elements", "integer",
                    "For value_type 'array_elements': the number of elements to size the array to.")
                .AddOptional("flags", "integer",
                    "PropFlags bitfield to OR onto the node (e.g. 132 / 0x84). Omit to leave flags unchanged.")
                .AddOptional("create_missing_parents", "boolean",
                    "Auto-create missing intermediate containers along lookup_string (default true).", true)
                .AddOptional("save", "boolean", "Save the file after the edit (default true).", true),
            SetPropertyNodeAsync);

        Register("delete_property_node",
            "Delete a property-tree node under ANY scope root — Parameters / Locals / FileGlobals / " +
            "StationGlobals / SequenceFile — addressed by a dotted lookup_string. Subsumes the " +
            "missing delete_sequence_parameter: pass scope='Parameters' + a top-level parameter name " +
            "to remove a whole parameter (and its structure), or a nested path (e.g. " +
            "'MDC_cmd.Request.Cmd') to surgically remove a single submember. The scope-generic " +
            "counterpart of delete_sub_property (which reaches only Locals/FileGlobals). StationGlobals " +
            "commit to the station .ini (file_path unused); every other scope saves the sequence file.",
            s => s
                .AddRequired("file_path", "string",
                    "Path to the .seq file (unused for scope='StationGlobals').")
                .AddRequired("scope", "string", "The scope root the node lives under.",
                    new[] { "Parameters", "Locals", "FileGlobals", "StationGlobals", "SequenceFile" })
                .AddOptional("sequence_name", "string",
                    "Owning sequence — required for scope 'Parameters' or 'Locals'.")
                .AddRequired("lookup_string", "string",
                    "Dotted path to the node to delete (top-level name OR nested submember).")
                .AddOptional("save", "boolean", "Save the file after the delete (default true).", true),
            DeletePropertyNodeAsync);

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
            "Get all parameters/arguments configured for a step's module. Reads, in order: the " +
            "LabVIEW connector-pane bindings (TS.SData.ViCall.Parms — cluster members flattened " +
            "as 'parent.child', value = the ArgVal binding expression), the step-root VIModule " +
            "of utility steps (NI_LV_RunVIAsynchronously), SequenceCall actual arguments " +
            "(TS.SData.ActualArgs — value = the Expr binding; null when the default is used), " +
            "and finally the legacy flat Module.Parameters container (DLL/.NET/Python). " +
            "Returns a list of {name, value, type, direction, dataType}.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step"),
            GetModuleParametersAsync);

        Register("set_module_parameter",
            "Set a single module parameter/argument binding on a step. LabVIEW steps: matches a " +
            "connector-pane parameter by its Label ('parent.child' descends into a cluster, e.g. " +
            "'error out.status') and writes its ArgVal expression, clearing UseDefaultValues. " +
            "SequenceCall steps: binds TS.SData.ActualArgs.<name>.Expr and clears that argument's " +
            "UseDef. When the argument entry is missing, the callee prototype is loaded first (engine " +
            "'Load Prototype') so EVERY parameter becomes a correctly-typed SequenceArgument (right " +
            "ParamType/ParamRepresentation/Flags) with unbound args left at UseDef=True — only if the " +
            "target cannot be resolved (headless/missing file) is a bare entry created on demand. Pass " +
            "an empty value to revert to 'use default'. Falls back to the legacy flat Module.Parameters " +
            "container for other adapters. step_name accepts the same selectors as set_step_* for " +
            "duplicate-named steps: 'Name#N' (the Nth 1-based occurrence) or '@idx:N' (the 0-based " +
            "index within the group).",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string",
                    "Name of the step, or a selector: 'Name#N' (Nth occurrence) / '@idx:N' (0-based group index).")
                .AddRequired("parameter_name", "string",
                    "Parameter name: VI connector-pane Label ('parent.child' for cluster members) " +
                    "or SequenceCall argument name.")
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

        Register("configure_wait",
            "Configure an NI_Wait step's wait TARGET beyond a plain time interval. wait_mode: " +
            "'time' (expression = seconds), 'thread' (expression = a thread reference, e.g. " +
            "'FileGlobals.ErrorHandlerThread' — wait until that thread ends) or 'execution'. Sets " +
            "WaitForTarget + the matching expression, clears the 'specify by sequence call' flags a " +
            "fresh NI_Wait carries, and optionally sets the timeout (timeout_expr / timeout_enabled / " +
            "error_on_timeout). Use this for thread/execution waits; set_wait_time only does 'time'.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NI_Wait step")
                .AddRequired("wait_mode", "string", "'time', 'thread', or 'execution'")
                .AddOptional("expression", "string",
                    "The target expression: seconds (time), thread reference (thread), or execution reference (execution).")
                .AddOptional("timeout_expr", "string", "Timeout in seconds (TimeoutExpr).")
                .AddOptional("timeout_enabled", "boolean", "Enable the timeout (TimeoutEnabled).")
                .AddOptional("error_on_timeout", "boolean", "Raise an error if the timeout elapses (ErrorOnTimeout)."),
            ConfigureWaitAsync);

        Register("configure_run_vi_async",
            "Configure an NI_LV_RunVIAsynchronously ('Run VI Asynchronously') step in ONE call. This " +
            "step type needs a special layout that a plain insert does NOT produce and that a plain " +
            "adapter switch CORRUPTS (it turns the step into an Action and drops the VIModule): the " +
            "async launch is driven by a Sequence-adapter SeqCallStepAdditions module (calls " +
            "'MainSequence' in a new thread) while the actual VI lives in the step-own VIModule.ViCall. " +
            "This tool builds the SeqCallStepAdditions module by retyping the container, applies the " +
            "async-launch defaults (SFPathExpr/SeqNameExpr/SpecifyByExpr/UsePrototype/ThreadOpt/" +
            "AutoWaitAsync/CustomThreadAffinity), stores the VI (vi_path + optional namespace) and sets " +
            "the module marker flag. Insert the step first (insert_steps_bulk/insert_step with step_type " +
            "'NI_LV_RunVIAsynchronously'), then call this. The connector-pane prototype (ViCall.Parms/" +
            "VIDescription) still won't materialise headless — that's an accepted LabVIEW residual.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the NI_LV_RunVIAsynchronously step")
                .AddRequired("vi_path", "string", "Path to the VI to launch (VIModule.ViCall.VIPath)")
                .AddOptional("namespace", "string", "VI namespace/library (VIModule.ViCall.Namespace), e.g. 'MyLib.lvlibp'")
                .AddOptional("thread_option", "number", "Multithreading option (SData.ThreadOpt): 1 = new thread (default), 3 = new execution.")
                .AddOptional("thread_ref_expr", "string", "Expression to store the launched thread reference (SData.AsyncThreadExpr).")
                .AddOptional("auto_wait", "boolean", "Wait for the async VI at the end of the current sequence (SData.AutoWaitAsync, default true).")
                .AddOptional("sequence_name_expr", "string", "Async-launch sequence name expression (SData.SeqNameExpr, default '\"MainSequence\"').")
                .AddOptional("sequence_file_expr", "string", "Async-launch file-path expression (SData.SFPathExpr, default 'Evaluate(Step.SequenceFileExpr)').")
                .AddOptional("save", "boolean", "Save the file after configuring (default true).", true),
            ConfigureRunViAsyncAsync);

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
                    "Write the users file to disk (default true). Set false to only modify in memory. " +
                    "For test / experimental users pass persist:false — it edits only the in-memory " +
                    "users file and never touches users.ini on disk.", true),
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
            "Switches the step to the .NET adapter if needed. After the member is set, the method " +
            "prototype is loaded (editor 'Load Prototype') so the step's parameter interface is " +
            "populated — the loaded parameters are returned in the result's 'parameters' list.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("assembly_path", "string", "Path to the .NET assembly (DLL)")
                .AddRequired("class_name", "string", "Fully-qualified class name")
                .AddRequired("method_name", "string", "Name of the method to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true)
                .AddOptional("load_prototype", "boolean",
                    "Load the method prototype afterwards to populate the parameter interface " +
                    "(default true). Set false to configure now and load later (e.g. assembly not " +
                    "available yet); then call load_module_prototype once it is reachable.", true),
            ConfigureDotNetModuleAsync);

        Register("configure_dll_module",
            "Configure a step's C/DLL code module: DLL path and function name (C/CVI adapter). " +
            "After the function is set, the prototype is loaded (editor 'Load Prototype') so the " +
            "parameter interface is populated — returned in the result's 'parameters' list.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("dll_path", "string", "Path to the DLL")
                .AddRequired("function_name", "string", "Exported function name to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true)
                .AddOptional("load_prototype", "boolean",
                    "Load the function prototype afterwards to populate the parameter interface " +
                    "(default true). Set false to configure now and load later; then call " +
                    "load_module_prototype once the DLL/prototype is reachable.", true),
            ConfigureDllModuleAsync);

        Register("configure_labview_module",
            "Configure a step's LabVIEW code module: the VI path (LabVIEW adapter). After the VI " +
            "path is set, the VI's connector pane is loaded (editor 'Load Prototype') so the " +
            "parameter interface is populated — returned in the result's 'parameters' list (this " +
            "needs the VI to be loadable: LabVIEW available, not an unloadable .lvlibp headless). " +
            "Do NOT use on a None-adapter LabVIEW UTILITY step (e.g. NI_LV_RunVIAsynchronously, " +
            "'Run VI Asynchronously') — its VI config lives in the step's own properties and " +
            "switching to the LabVIEW adapter corrupts it; this tool now REFUSES such steps. Use " +
            "set_step_property instead (e.g. property_path 'VIModule.ViCall.VIPath').",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("vi_path", "string", "Path to the VI")
                .AddOptional("save", "boolean", "Save the file (default true)", true)
                .AddOptional("load_prototype", "boolean",
                    "Load the VI connector pane afterwards to populate the parameter interface " +
                    "(default true). Set false to set the VI path now and load later (e.g. the VI " +
                    "is not available yet); then call load_module_prototype once it is reachable.", true),
            ConfigureLabViewModuleAsync);

        Register("configure_python_module",
            "Configure a step's Python code module: module path and function name (Python adapter). " +
            "After the function is set, the prototype is loaded (editor 'Load Prototype') so the " +
            "parameter interface is populated — returned in the result's 'parameters' list.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("module_path", "string", "Path to the Python module (.py)")
                .AddRequired("function_name", "string", "Name of the function to call")
                .AddOptional("save", "boolean", "Save the file (default true)", true)
                .AddOptional("load_prototype", "boolean",
                    "Load the function prototype afterwards to populate the parameter interface " +
                    "(default true). Set false to configure now and load later; then call " +
                    "load_module_prototype once the module/function is reachable.", true),
            ConfigurePythonModuleAsync);

        Register("configure_sequence_call_module",
            "Configure a step's SequenceCall module: target sequence and (optional) target file. Works " +
            "on ANY step that uses the Sequence Adapter — not only 'SequenceCall' steps: e.g. an 'Action' " +
            "step switched to the Sequence adapter (change_step_adapter/insert adapter='Sequence') calls " +
            "a subsequence while staying an Action step. " +
            "Prefer this typed tool over set_sequence_call_target for new code. After the target is set, " +
            "the callee prototype is loaded (engine 'Load Prototype'): when the target resolves, " +
            "TS.SData.ActualArgs is populated with one correctly-typed SequenceArgument per callee " +
            "parameter (right ParamType/ParamRepresentation/Flags, unbound args at UseDef=True) and the " +
            "cached Prototype container is filled. Unresolvable targets (placeholder/missing file) skip " +
            "this silently. For an ASYNCHRONOUS call (run the subsequence in a new thread/execution and " +
            "keep a handle), also pass execution_mode='NewThread' (or 'NewExecution'), thread_ref_expr " +
            "(where to store the thread/execution reference, e.g. 'FileGlobals.ErrorHandlerThread') and " +
            "auto_wait=false — these set SData.ThreadOpt / AsyncThreadExpr / AutoWaitAsync.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step")
                .AddRequired("target_sequence_name", "string", "Name of the target sequence")
                .AddOptional("target_sequence_file", "string",
                    "Target sequence file (empty = current file). Stored as a relative path.", "")
                .AddOptional("save", "boolean", "Save the file (default true)", true)
                .AddOptional("execution_mode", "string",
                    "Threading: 'UseCurrentThread' (default), 'NewThread' (async, new thread) or 'NewExecution'.")
                .AddOptional("thread_ref_expr", "string",
                    "Expression to store the new thread/execution reference (SData.AsyncThreadExpr), e.g. 'FileGlobals.ErrorHandlerThread'.")
                .AddOptional("auto_wait", "boolean",
                    "Wait for the async subsequence at the end of the current sequence (SData.AutoWaitAsync).")
                .AddOptional("load_prototype", "boolean",
                    "Load the callee's parameter list into ActualArgs afterwards (default true). Set " +
                    "false to set the target now and load later (e.g. the target sequence does not " +
                    "exist yet); then call load_module_prototype once it is reachable.", true),
            ConfigureSequenceCallModuleAsync);

        Register("load_module_prototype",
            "Load (refresh) a step's code-module prototype — the programmatic equivalent of the " +
            "Sequence Editor's 'Load Prototype' action. It reconciles the step's parameter interface " +
            "against its CURRENT target and returns the resulting parameters so they become visible. " +
            "Adapter-agnostic: LabVIEW VI connector pane, DLL/CVI & .NET & ActiveX function prototype, " +
            "SequenceCall callee argument list. Two main uses: (1) after configuring a module with " +
            "load_prototype=false, run this once the target is reachable; (2) RE-SYNC a caller after " +
            "the target's own interface changed — e.g. you edited a subsequence's Parameters, or a " +
            "DLL/VI/ActiveX signature changed — so the caller's arguments match again. ORDER MATTERS: " +
            "the target must already be set and reachable (SequenceCall target loaded/on the search " +
            "path; VI loadable — LabVIEW available, not an unloadable .lvlibp headless), otherwise " +
            "nothing is updated and 'prototypeLoaded' is false with an explanatory 'note'. Does NOT " +
            "change the step's adapter. Non-destructive: existing bindings are matched by name and " +
            "preserved. Read-only alternative that does not reload: get_module_parameters. " +
            "LABVIEW: a LabVIEW load attaches to/starts LabVIEW (the same slow work the editor's " +
            "'Reload Prototype' does), so by default it runs ASYNCHRONOUSLY — this call returns " +
            "immediately with a 'jobId' and status='running'; poll get_prototype_load_status(job_id) " +
            "until status='completed'. This avoids the ~60s MCP transport timeout (-32001). The LabVIEW " +
            "load runs IN-PROCESS by default so it attaches to the SAME running LabVIEW the editor uses " +
            "(a fresh isolated worker has no such attachment and fails with an immediate MOD_NOT_FOUND). " +
            "The load is routed to the LabVIEW ExecServer (the running LabVIEW ADE via ActiveX — what " +
            "the editor uses), which avoids the AutoDetect→Run-Time (lvrt.dll) delay-load that faults " +
            "headless (0xC06D007E). Because ActiveX works cross-process, this runs by default in a " +
            "crash-safe ISOLATED WORKER (isolate=true) that can bind LabVIEW too — crash-safety AND a " +
            "real load together; a native fault there returns workerOutcome='crashed'/'timeout' without " +
            "taking the server down. isolate=false runs in-process (also ExecServer-routed, but a " +
            "native fault is NOT contained). async=false runs inline and waits. For a genuinely " +
            "unloadable .lvlibp, copy_step_module is the headless fallback. Non-LabVIEW adapters always " +
            "run fast, in-process, synchronously. Result carries executionMode ('in-process'|'worker'), " +
            "workerOutcome, jobId and status.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file")
                .AddRequired("sequence_name", "string", "Name of the sequence containing the step")
                .AddRequired("step_group", "string", "Step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("step_name", "string", "Name of the step whose prototype to load")
                .AddOptional("save", "boolean", "Save the file (default true).", true)
                .AddOptional("async", "boolean", "Run asynchronously and return a jobId to poll with " +
                    "get_prototype_load_status (default TRUE for LabVIEW, false otherwise). Set false " +
                    "to wait inline for the result (may hit the ~60s transport timeout for a slow load).")
                .AddOptional("isolate", "boolean", "LabVIEW only: run in a crash-safe isolated worker " +
                    "process (default TRUE — the worker binds the running LabVIEW via ActiveX AND " +
                    "contains a native crash). false runs in-process (not crash-contained).")
                .AddOptional("labview_server", "string", "LabVIEW server routing before the load: " +
                    "'deferred' (default) = running LabVIEW ADE via ActiveX, launched on first use " +
                    "(matches the editor; avoids the lvrt.dll delay-load); 'exec' = same but connect " +
                    "immediately; 'rte' = legacy Run-Time (AutoDetect, may fault headless); 'auto' = " +
                    "leave the adapter's configured server unchanged.",
                    "deferred", new[] { "deferred", "exec", "rte", "auto" })
                .AddOptional("timeout_seconds", "integer", "Worker/async watchdog timeout in seconds " +
                    "(default 120; min 5). LabVIEW startup can be slow — raise if a real load needs more.", 120),
            LoadModulePrototypeAsync);

        Register("get_prototype_load_status",
            "Poll an ASYNC LabVIEW prototype-load job started by load_module_prototype (async mode). " +
            "Returns the same shape as load_module_prototype plus 'status': 'running' (not finished — " +
            "poll again after a short wait), 'completed' (the result fields prototypeLoaded/parameters/" +
            "note are final), or 'error' (the job itself faulted; see note). Unknown/expired job_id → " +
            "error. Finished jobs are retained ~10 minutes.",
            s => s
                .AddRequired("job_id", "string", "The jobId returned by load_module_prototype (async)."),
            GetPrototypeLoadStatusAsync);

        Register("copy_step_module",
            "Deep-copy a step's whole code-module subtree from a SOURCE step onto a TARGET step, " +
            "WITHOUT loading LabVIEW. Copies TS.SData (a SequenceCall's ActualArgs, a RunVIAsync's " +
            "SeqCallStepAdditions incl. its Parameter0..3, an adapter module) PLUS the step-own " +
            "VIModule with its cached ViCall metadata (Namespace, VI Description, Connector-Pane " +
            "Checksum and the Parms connector-pane bindings), and aligns the target's adapter. It " +
            "ALSO clones the AUTHORED step-config subtrees a fresh insert does not fully instantiate " +
            "— the result-logging hints (TS.AdditionalResultsHints / TS.CustomResults), error-dialog " +
            "options (TS.ErrorDialogOptions, e.g. the NI_Wait/DQMH pattern) and the NI_Wait timeout " +
            "flag (Result.TimeoutOccurred), each copied only when present on the source — so it also " +
            "reproduces a NON-adapter step (e.g. an NI_Wait) faithfully, not just LabVIEW/DLL/.NET/" +
            "SequenceCall modules. Every subtree is CLONED (flag-preserving) before it is attached, so " +
            "an attached source object no longer trips 'already has a parent object'. This " +
            "is the reliable way to reproduce a LabVIEW step whose VI lives in a packed library " +
            "(.lvlibp) that cannot load headless (Load Prototype fails, so the connector pane can't " +
            "be regenerated) — the cached metadata is copied verbatim from a source .seq instead. " +
            "Run copy_typedefs FIRST so the module types exist in the target file. Returns " +
            "{sourceStep, targetStep, adapter, copiedPaths[], warnings[]}.",
            s => s
                .AddRequired("source_file_path", "string", "Path to the SOURCE sequence file")
                .AddRequired("source_sequence_name", "string", "Source sequence name")
                .AddRequired("source_step_group", "string", "Source step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("source_step_name", "string", "Source step name (selectors 'Name#N'/'@idx:N' allowed)")
                .AddRequired("target_file_path", "string", "Path to the TARGET sequence file")
                .AddRequired("target_sequence_name", "string", "Target sequence name")
                .AddRequired("target_step_group", "string", "Target step group: 'Setup', 'Main', or 'Cleanup'")
                .AddRequired("target_step_name", "string", "Target step name (selectors 'Name#N'/'@idx:N' allowed)")
                .AddOptional("save", "boolean", "Save the target file (default true)", true),
            CopyStepModuleAsync);

        // ── Sequence Analyzer (detailed) ───────────────────────────────────────

        Register("analyze_sequence_file",
            "Run the TestStand Sequence Analyzer on a file and return STRUCTURED JSON: typed " +
            "messages with severity counts. Filter by minimum severity, and optionally group the " +
            "results (by severity or rule) like the editor's Analysis Results 'Group By' pane. The " +
            "flat 'messages' list and counts are always present; grouping adds a 'groups' array. " +
            "Prefer this for programmatic use; for a quick human-readable TEXT summary (no filter) " +
            "use run_sequence_analyzer — the same underlying analyzer. " +
            "COLD/LabVIEW NOTE: the analyzer's 'module is loadable' rule LOADS every step's code " +
            "module; for a VI in a packed library (.lvlibp) that cold load can take well over a " +
            "minute and blow the ~60s MCP transport timeout (-32001). Set async=true to run the " +
            "analysis in the background and get an immediate 'jobId' + status='running'; then poll " +
            "get_analysis_status(job_id) until status='completed' (same result shape). The analysis " +
            "already runs in a separate AnalyzerApp.exe process, so a native .lvlibp fault ends the " +
            "job with status='error' and never takes the server down.",
            s => s
                .AddRequired("file_path", "string", "Path to the sequence file to analyze")
                .AddOptional("min_severity", "string",
                    "Minimum severity to include: 'Information' (default), 'Warning', or 'Error'",
                    "Information", new[] { "Information", "Warning", "Error" })
                .AddOptional("group_by", "string",
                    "Group the returned messages: 'severity' (default), 'rule', or 'none' for a " +
                    "flat list only. Grouped results populate the 'groups' array.",
                    "severity", new[] { "severity", "rule", "none" })
                .AddOptional("async", "boolean",
                    "Run asynchronously: return immediately with a 'jobId' + status='running' and " +
                    "poll get_analysis_status(job_id) for the final result. Use this for files with " +
                    "LabVIEW .lvlibp steps on a cold module cache to avoid the ~60s transport timeout. " +
                    "Default false (waits inline — fine for fast/structural analyses).", false),
            AnalyzeSequenceFileAsync);

        Register("get_analysis_status",
            "Poll an ASYNC Sequence-Analyzer job started by analyze_sequence_file (async=true) or " +
            "run_sequence_analyzer (async=true). Returns the SAME structured shape as " +
            "analyze_sequence_file (filePath, totalMessages, errorCount/warningCount/informationCount, " +
            "messages[], optional groups[]) plus 'status': 'running' (not finished — poll again after " +
            "a short wait), 'completed' (the message/count fields are final) or 'error' (the analysis " +
            "itself faulted; see 'note'). Unknown/expired job_id → error. Finished jobs are retained " +
            "~10 minutes.",
            s => s
                .AddRequired("job_id", "string",
                    "The jobId returned by analyze_sequence_file / run_sequence_analyzer (async)."),
            GetAnalysisStatusAsync);

        // ── Output & UI Messages ───────────────────────────────────────────────

        Register("post_output_message",
            "Post a message to the TestStand engine output-message list (visible in the " +
            "sequence editor's Output pane). Works HEADLESS (unlike post_ui_message / " +
            "add_report_section, which need a live execution) — the message appears in the " +
            "engine output list right away.",
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
        var msg = $"Step '{stepName}' ({stepType}) inserted into sequence '{sequenceName}' [{stepGroup}]";
        if (InputGuards.IsWaitStep(stepType))
            msg += ". NOTE: an NI_Wait does not wait until a time is set — call set_wait_time for it.";
        return Ok(msg);
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
                InitExpr           = el.GetStringOrNull("init_expr"),
                IncrementExpr      = el.GetStringOrNull("increment_expr"),
                ArrayExpr          = el.GetStringOrNull("array_expr"),
                ElementExpr        = el.GetStringOrNull("element_expr"),
                IsDefault          = el.TryGetProperty("is_default", out var isDef)
                                     && isDef.ValueKind is JsonValueKind.True or JsonValueKind.False
                                     ? isDef.GetBoolean() : (bool?)null,
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

        // Parameters are OPTIONAL: only when 'parameters' is present does the validator enforce
        // Parameters.X references (E_UNDECLARED_PARAM). Omitting it keeps the historical locals-only
        // behaviour so existing callers are unaffected.
        List<string>? paramNames = null;
        if (args!.Value.TryGetProperty("parameters", out var paramsEl) &&
            paramsEl.ValueKind == JsonValueKind.Array)
        {
            paramNames = new List<string>();
            foreach (var el in paramsEl.EnumerateArray())
            {
                var n = el.GetStringOrNull("name");
                if (!string.IsNullOrWhiteSpace(n)) paramNames.Add(n!);
            }
        }

        var result = SequencePlanValidator.Validate(sequenceName, planSteps, localNames, paramNames);
        return Task.FromResult(OkJson(result));
    }

    private async Task<CallToolResult> AuditSequenceReferencesAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetStringOrNull("sequence_name");
        var data   = await _ts.ReadReferenceAuditDataAsync(filePath, sequenceName);
        var result = ReferenceAuditor.Audit(data);
        return OkJson(result);
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

    private async Task<CallToolResult> SetParameterCommentAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var paramName    = args!.Value.GetRequiredString("parameter_name");
        var comment      = args!.Value.GetRequiredString("comment");
        await _ts.SetParameterCommentAsync(filePath, sequenceName, paramName, comment);
        return Ok($"Comment set on parameter '{paramName}' in sequence '{sequenceName}'");
    }

    private async Task<CallToolResult> SetFileGlobalCommentAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("sequence_file_path");
        var varName  = args!.Value.GetRequiredString("variable_name");
        var comment  = args!.Value.GetRequiredString("comment");
        await _ts.SetFileGlobalCommentAsync(filePath, varName, comment);
        var msg  = $"Comment set on file global '{varName}'.";
        var warn = InputGuards.DescribeLatin1Loss(comment, "comment");
        return Ok(warn == null ? msg : msg + " " + warn);
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

        // Async: hand back a running job handle right away (same job/poll infra as
        // analyze_sequence_file) so a slow cold .lvlibp analysis never trips the transport timeout.
        // The polled result is the structured AnalyzerResult (via get_analysis_status).
        if (args!.Value.GetBoolOrDefault("async", false))
            return OkJson(await _ts.RunSequenceAnalyzerDetailedAsync(filePath, "Information", groupBy, async: true));

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

    private async Task<CallToolResult> SetStepPropertyAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("sequence_file_path");
        var seqName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup = args!.Value.GetRequiredString("step_group");
        var stepName  = args!.Value.GetRequiredString("step_name");
        var path      = args!.Value.GetRequiredString("property_path");
        var value     = args!.Value.GetRequiredString("value");
        var valueType = args?.GetStringOrNull("value_type");
        var unescape  = args?.GetBoolOrDefault("unescape", false) ?? false;
        var save      = args?.GetBoolOrDefault("save", true) ?? true;
        var info = await _ts.SetStepPropertyAsync(filePath, seqName, stepGroup, stepName,
            path, value, valueType, save, unescape);
        return OkJson(info);
    }

    private async Task<CallToolResult> CreateStepPropertyAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("sequence_file_path");
        var seqName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup = args!.Value.GetRequiredString("step_group");
        var stepName  = args!.Value.GetRequiredString("step_name");
        var path      = args!.Value.GetRequiredString("property_path");
        var valueType = args!.Value.GetRequiredString("value_type");
        var typeName  = args?.GetStringOrNull("type_name");
        int? numEl    = null;
        if (args!.Value.TryGetProperty("num_elements", out var ne) &&
            ne.ValueKind == JsonValueKind.Number)
            numEl = ne.GetInt32();
        var value     = args?.GetStringOrNull("value");
        var unescape  = args?.GetBoolOrDefault("unescape", false) ?? false;
        var save      = args?.GetBoolOrDefault("save", true) ?? true;
        var info = await _ts.CreateStepPropertyAsync(filePath, seqName, stepGroup, stepName,
            path, valueType, typeName, numEl, value, unescape, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> SetStepPropertyFlagsAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("sequence_file_path");
        var seqName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup = args!.Value.GetRequiredString("step_group");
        var stepName  = args!.Value.GetRequiredString("step_name");
        var path      = args!.Value.GetStringOrDefault("property_path", "");
        var flags     = args!.Value.GetIntOrDefault("flags", 0);
        var save      = args?.GetBoolOrDefault("save", true) ?? true;
        var info = await _ts.SetStepPropertyFlagsAsync(filePath, seqName, stepGroup, stepName,
            path, flags, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> RenameStepPropertyAsync(JsonElement? args)
    {
        var filePath  = args!.Value.GetRequiredString("sequence_file_path");
        var seqName   = args!.Value.GetRequiredString("sequence_name");
        var stepGroup = args!.Value.GetRequiredString("step_group");
        var stepName  = args!.Value.GetRequiredString("step_name");
        var path      = args!.Value.GetRequiredString("property_path");
        var newName   = args!.Value.GetRequiredString("new_name");
        var save      = args?.GetBoolOrDefault("save", true) ?? true;
        var info = await _ts.RenameStepPropertyAsync(filePath, seqName, stepGroup, stepName,
            path, newName, save);
        return OkJson(info);
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

    private async Task<CallToolResult> GetFileTypeDefsAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var types = await _ts.GetFileTypeDefsAsync(filePath);
        return OkJson(types);
    }

    private async Task<CallToolResult> CopyTypeDefsAsync(JsonElement? args)
    {
        var sourceFile = args!.Value.GetRequiredString("source_file_path");
        var destFile   = args!.Value.GetRequiredString("dest_file_path");
        List<string>? names = null;
        if (args!.Value.TryGetProperty("type_names", out var tn) && tn.ValueKind == JsonValueKind.Array)
            names = tn.EnumerateArray()
                      .Where(e => e.ValueKind == JsonValueKind.String)
                      .Select(e => e.GetString()!)
                      .ToList();
        var save    = args!.Value.GetBoolOrDefault("save", true);
        var copied  = await _ts.CopyTypeDefsAsync(sourceFile, destFile, names, save);
        return OkJson(new { copiedCount = copied.Count, copied });
    }

    private async Task<CallToolResult> CopyFileAttributesAsync(JsonElement? args)
    {
        var sourceFile = args!.Value.GetRequiredString("source_file_path");
        var destFile   = args!.Value.GetRequiredString("dest_file_path");
        List<string>? names = null;
        if (args!.Value.TryGetProperty("attribute_names", out var an) && an.ValueKind == JsonValueKind.Array)
            names = an.EnumerateArray()
                      .Where(e => e.ValueKind == JsonValueKind.String)
                      .Select(e => e.GetString()!)
                      .ToList();
        var save   = args!.Value.GetBoolOrDefault("save", true);
        var result = await _ts.CopyFileAttributesAsync(sourceFile, destFile, names, save);
        return OkJson(result);
    }

    private async Task<CallToolResult> CopyFileGlobalsAsync(JsonElement? args)
    {
        var sourceFile = args!.Value.GetRequiredString("source_file_path");
        var destFile   = args!.Value.GetRequiredString("dest_file_path");
        List<string>? names = null;
        if (args!.Value.TryGetProperty("global_names", out var gn) && gn.ValueKind == JsonValueKind.Array)
            names = gn.EnumerateArray()
                      .Where(e => e.ValueKind == JsonValueKind.String)
                      .Select(e => e.GetString()!)
                      .ToList();
        var save   = args!.Value.GetBoolOrDefault("save", true);
        var result = await _ts.CopyFileGlobalsAsync(sourceFile, destFile, names, save);
        return OkJson(result);
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
        var typeName  = args!.Value.GetStringOrNull("type_name");
        int? ordinal  = args!.Value.TryGetProperty("ordinal", out var ordEl)
                        && ordEl.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? ordEl.GetInt32() : (int?)null;
        await _ts.SetPropertyValueAsync(filePath, seqName, propName, valueType, value, typeName, ordinal);
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

    private async Task<CallToolResult> SetPropertyNodeAsync(JsonElement? args)
    {
        var filePath   = args!.Value.GetRequiredString("file_path");
        var scope      = args!.Value.GetRequiredString("scope");
        var seqName    = args!.Value.GetStringOrNull("sequence_name");
        var lookup     = args!.Value.GetRequiredString("lookup_string");
        var valueType  = args!.Value.GetRequiredString("value_type");
        var typeName   = args!.Value.GetStringOrNull("type_name");
        var value      = args!.Value.GetStringOrNull("value");
        int? ordinal   = args!.Value.TryGetProperty("ordinal", out var ordEl)
                         && ordEl.ValueKind == JsonValueKind.Number ? ordEl.GetInt32() : (int?)null;
        int? numEl     = args!.Value.TryGetProperty("num_elements", out var neEl)
                         && neEl.ValueKind == JsonValueKind.Number ? neEl.GetInt32() : (int?)null;
        int? flags     = args!.Value.TryGetProperty("flags", out var flEl)
                         && flEl.ValueKind == JsonValueKind.Number ? flEl.GetInt32() : (int?)null;
        var createPar  = args?.GetBoolOrDefault("create_missing_parents", true) ?? true;
        var save       = args?.GetBoolOrDefault("save", true) ?? true;
        var info = await _ts.SetPropertyNodeAsync(filePath, scope, seqName, lookup, valueType,
            typeName, value, ordinal, numEl, flags, createPar, save);
        return OkJson(info);
    }

    private async Task<CallToolResult> DeletePropertyNodeAsync(JsonElement? args)
    {
        var filePath = args!.Value.GetRequiredString("file_path");
        var scope    = args!.Value.GetRequiredString("scope");
        var seqName  = args!.Value.GetStringOrNull("sequence_name");
        var lookup   = args!.Value.GetRequiredString("lookup_string");
        var save     = args?.GetBoolOrDefault("save", true) ?? true;
        await _ts.DeletePropertyNodeAsync(filePath, scope, seqName, lookup, save);
        return Ok($"Deleted node '{lookup}' from {scope}" +
                  (seqName is null ? "." : $" of sequence '{seqName}'."));
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
        var msg = $"Properties updated for sequence '{sequenceName}'.";
        var warn = InputGuards.DescribeLatin1Loss(current.Description, "description");
        return Ok(warn == null ? msg : msg + " " + warn);
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
        var msg = $"Comment set on step '{stepName}' via [{method}].";
        var warn = InputGuards.DescribeLatin1Loss(comment, "comment");
        return Ok(warn == null ? msg : msg + " " + warn);
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
        var statusExpr   = args!.Value.GetStringOrNull("status_expr");
        await _ts.SetStepLoopAsync(filePath, sequenceName, stepGroup, stepName,
            loopType, initExpr, whileExpr, incExpr, statusExpr);
        return Ok($"Loop settings of step '{stepName}' updated to '{loopType}'.");
    }

    private async Task<CallToolResult> SetFlowConditionAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var condition    = args!.Value.GetRequiredString("condition");
        bool? isDefault  = args!.Value.TryGetProperty("is_default", out var d)
                           && d.ValueKind != JsonValueKind.Null
            ? d.GetBoolean()
            : (bool?)null;
        await _ts.SetFlowConditionAsync(filePath, sequenceName, stepGroup, stepName, condition, isDefault);
        return Ok($"Flow condition set on step '{stepName}'.");
    }

    private async Task<CallToolResult> ConfigureForLoopAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        int? count = args!.Value.TryGetProperty("count", out var c)
                     && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cv)
            ? cv : (int?)null;
        var indexVar     = args!.Value.GetStringOrNull("index_var");
        var initExpr     = args!.Value.GetStringOrNull("init_expr");
        var condExpr     = args!.Value.GetStringOrNull("condition_expr");
        var incExpr      = args!.Value.GetStringOrNull("increment_expr");
        var save         = args!.Value.GetBoolOrDefault("save", true);
        var result = await _ts.ConfigureForLoopAsync(filePath, sequenceName, stepGroup, stepName,
            count, indexVar, initExpr, condExpr, incExpr, save);
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigureForEachLoopAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var arrayExpr    = args!.Value.GetStringOrNull("array_expr");
        var elementExpr  = args!.Value.GetStringOrNull("element_expr");
        var offsetExpr   = args!.Value.GetStringOrNull("offset_expr");
        var subExpr      = args!.Value.GetStringOrNull("subscript_expr");
        var save         = args!.Value.GetBoolOrDefault("save", true);
        var result = await _ts.ConfigureForEachLoopAsync(filePath, sequenceName, stepGroup, stepName,
            arrayExpr, elementExpr, offsetExpr, subExpr, save);
        return OkJson(result);
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
        var mode  = (args?.GetStringOrDefault("mode", "native") ?? "native").Trim().ToLowerInvariant();

        // DEFAULT 'native': delegate to the authoritative FileDiffer so a "looks clean" result is
        // trustworthy. 'structural': the fast coarse in-process comparison (self-labelled via its
        // Fidelity/Note fields) for a quick overview.
        if (mode == "structural")
        {
            var diff = await _ts.CompareSequenceFilesAsync(path1, path2);
            return OkJson(diff);
        }
        var report = await _ts.DiffSequenceFilesAsync(path1, path2);
        return OkJson(report);
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
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetBoolOrDefault("load_prototype", true));
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
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetBoolOrDefault("load_prototype", true));
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
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetBoolOrDefault("load_prototype", true));
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
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetBoolOrDefault("load_prototype", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> ConfigureSequenceCallModuleAsync(JsonElement? args)
    {
        bool? autoWait = args!.Value.TryGetProperty("auto_wait", out var aw) && aw.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? aw.GetBoolean() : (bool?)null;
        var result = await _ts.ConfigureSequenceCallModuleAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetRequiredString("target_sequence_name"),
            args!.Value.GetStringOrDefault("target_sequence_file", ""),
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetStringOrNull("execution_mode"),
            args!.Value.GetStringOrNull("thread_ref_expr"),
            autoWait,
            args!.Value.GetBoolOrDefault("load_prototype", true));
        return OkJson(result);
    }

    private async Task<CallToolResult> LoadModulePrototypeAsync(JsonElement? args)
    {
        var result = await _ts.LoadModulePrototypeAsync(
            args!.Value.GetRequiredString("file_path"),
            args!.Value.GetRequiredString("sequence_name"),
            args!.Value.GetRequiredString("step_group"),
            args!.Value.GetRequiredString("step_name"),
            args!.Value.GetBoolOrDefault("save", true),
            args!.Value.GetBoolOrNull("isolate"),
            args!.Value.GetIntOrDefault("timeout_seconds", 120),
            args!.Value.GetBoolOrNull("async"),
            args!.Value.GetStringOrNull("labview_server"));
        return OkJson(result);
    }

    private async Task<CallToolResult> GetPrototypeLoadStatusAsync(JsonElement? args)
    {
        var result = await _ts.GetPrototypeLoadStatusAsync(
            args!.Value.GetRequiredString("job_id"));
        return OkJson(result);
    }

    private async Task<CallToolResult> CopyStepModuleAsync(JsonElement? args)
    {
        var result = await _ts.CopyStepModuleAsync(
            args!.Value.GetRequiredString("source_file_path"),
            args!.Value.GetRequiredString("source_sequence_name"),
            args!.Value.GetRequiredString("source_step_group"),
            args!.Value.GetRequiredString("source_step_name"),
            args!.Value.GetRequiredString("target_file_path"),
            args!.Value.GetRequiredString("target_sequence_name"),
            args!.Value.GetRequiredString("target_step_group"),
            args!.Value.GetRequiredString("target_step_name"),
            args!.Value.GetBoolOrDefault("save", true));
        return OkJson(result);
    }

    // ── Sequence Analyzer Handler ─────────────────────────────────────────────

    private async Task<CallToolResult> AnalyzeSequenceFileAsync(JsonElement? args)
    {
        var filePath    = args!.Value.GetRequiredString("file_path");
        var minSeverity = args!.Value.GetStringOrDefault("min_severity", "Information");
        var groupBy     = args!.Value.GetStringOrDefault("group_by", "severity");
        var async       = args!.Value.GetBoolOrDefault("async", false);
        var result      = await _ts.RunSequenceAnalyzerDetailedAsync(filePath, minSeverity, groupBy, async);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetAnalysisStatusAsync(JsonElement? args)
    {
        var result = await _ts.GetAnalysisStatusAsync(
            args!.Value.GetRequiredString("job_id"));
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

    // ── Live Thread-Context Inspection Handlers ───────────────────────────────

    private async Task<CallToolResult> InspectThreadContextAsync(JsonElement? args)
    {
        var id        = args!.Value.GetRequiredString("execution_id");
        var threadId  = args?.GetStringOrNull("thread_id");
        var frame     = args?.GetIntOrDefault("call_stack_index", 0) ?? 0;
        var scope     = args?.GetStringOrDefault("scope", "runstate") ?? "runstate";
        var lookup    = args?.GetStringOrNull("lookup_string");
        var maxDepth  = args?.GetIntOrDefault("max_depth", 3) ?? 3;
        var hidden    = args?.GetBoolOrDefault("include_hidden", false) ?? false;
        var maxArrEl  = args?.GetIntOrDefault("max_array_elements", 50) ?? 50;
        var tree      = await _ts.InspectThreadContextAsync(id, threadId, frame, scope, lookup,
            maxDepth, hidden, maxArrEl);
        return OkJson(tree);
    }

    private async Task<CallToolResult> EvaluateInThreadContextAsync(JsonElement? args)
    {
        var id         = args!.Value.GetRequiredString("execution_id");
        var expression = args!.Value.GetRequiredString("expression");
        var threadId   = args?.GetStringOrNull("thread_id");
        var frame      = args?.GetIntOrDefault("call_stack_index", 0) ?? 0;
        var result     = await _ts.EvaluateInThreadContextAsync(id, threadId, frame, expression);
        return OkJson(result);
    }

    private async Task<CallToolResult> GetRuntimeVariableAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var path     = args!.Value.GetRequiredString("property_path");
        var threadId = args?.GetStringOrNull("thread_id");
        var frame    = args?.GetIntOrDefault("call_stack_index", 0) ?? 0;
        var info     = await _ts.GetRuntimeVariableAsync(id, threadId, frame, path);
        return OkJson(info);
    }

    private async Task<CallToolResult> SetRuntimeVariableAsync(JsonElement? args)
    {
        var id        = args!.Value.GetRequiredString("execution_id");
        var path      = args!.Value.GetRequiredString("property_path");
        var value     = args!.Value.GetRequiredString("value");
        var threadId  = args?.GetStringOrNull("thread_id");
        var frame     = args?.GetIntOrDefault("call_stack_index", 0) ?? 0;
        var valueType = args?.GetStringOrNull("value_type");
        var info      = await _ts.SetRuntimeVariableAsync(id, threadId, frame, path, value, valueType);
        return OkJson(info);
    }

    private async Task<CallToolResult> GetRunStateSummaryAsync(JsonElement? args)
    {
        var id       = args!.Value.GetRequiredString("execution_id");
        var threadId = args?.GetStringOrNull("thread_id");
        var frame    = args?.GetIntOrDefault("call_stack_index", 0) ?? 0;
        var summary  = await _ts.GetRunStateSummaryAsync(id, threadId, frame);
        return OkJson(summary);
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
        var msg = $"File properties updated for: {filePath}";
        var warn = InputGuards.DescribeLatin1Loss(comment, "comment");
        return Ok(warn == null ? msg : msg + " " + warn);
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

    private async Task<CallToolResult> ConfigureWaitAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var waitMode     = args!.Value.GetRequiredString("wait_mode");
        var expression   = args!.Value.GetStringOrNull("expression");
        var timeoutExpr  = args!.Value.GetStringOrNull("timeout_expr");
        bool? timeoutEnabled = args!.Value.TryGetProperty("timeout_enabled", out var te) && te.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? te.GetBoolean() : (bool?)null;
        bool? errorOnTimeout = args!.Value.TryGetProperty("error_on_timeout", out var eo) && eo.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? eo.GetBoolean() : (bool?)null;
        await _ts.ConfigureWaitAsync(filePath, sequenceName, stepGroup, stepName, waitMode,
            expression, timeoutExpr, timeoutEnabled, errorOnTimeout);
        return Ok($"NI_Wait step '{stepName}' configured (mode: {waitMode}).");
    }

    private async Task<CallToolResult> ConfigureRunViAsyncAsync(JsonElement? args)
    {
        var filePath     = args!.Value.GetRequiredString("file_path");
        var sequenceName = args!.Value.GetRequiredString("sequence_name");
        var stepGroup    = args!.Value.GetRequiredString("step_group");
        var stepName     = args!.Value.GetRequiredString("step_name");
        var viPath       = args!.Value.GetRequiredString("vi_path");
        var viNamespace  = args!.Value.GetStringOrNull("namespace");
        int threadOption = args!.Value.TryGetProperty("thread_option", out var to)
                           && to.ValueKind == JsonValueKind.Number && to.TryGetInt32(out var tov) ? tov : 1;
        var threadRefExpr = args!.Value.GetStringOrNull("thread_ref_expr");
        var autoWait      = args!.Value.GetBoolOrDefault("auto_wait", true);
        var seqNameExpr   = args!.Value.GetStringOrDefault("sequence_name_expr", "\"MainSequence\"");
        var seqFileExpr   = args!.Value.GetStringOrDefault("sequence_file_expr", "Evaluate(Step.SequenceFileExpr)");
        var save          = args!.Value.GetBoolOrDefault("save", true);
        var result = await _ts.ConfigureRunViAsyncAsync(filePath, sequenceName, stepGroup, stepName,
            viPath, viNamespace, threadOption, threadRefExpr, autoWait, seqNameExpr, seqFileExpr, save);
        return OkJson(result);
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
