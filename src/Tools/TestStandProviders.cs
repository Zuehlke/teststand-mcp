using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using TestStandMCP.Models;
using TestStandMCP.Services;
using Microsoft.Extensions.Logging;

namespace TestStandMCP.Tools;

// ── Resources ────────────────────────────────────────────────────────────────

/// <summary>Serves read-only MCP resources backed by the TestStand engine.</summary>
public class TestStandResourceProvider
{
    private readonly ITestStandService _ts;
    private readonly ILogger<TestStandResourceProvider> _logger;

    // Cached once: building these options per call defeats System.Text.Json's metadata cache.
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // The resource list is static data — build it once.
    private static readonly ListResourcesResult _resourcesResult = new()
    {
        Resources = new List<McpResource>
        {
            new()
            {
                Uri         = "teststand://station/info",
                Name        = "Station Information",
                Description = "Current TestStand station details, version, and status",
                MimeType    = "application/json"
            },
            new()
            {
                Uri         = "teststand://engine/loaded-files",
                Name        = "Loaded Sequence Files",
                Description = "All sequence files currently open in the TestStand engine",
                MimeType    = "application/json"
            },
            new()
            {
                Uri         = "teststand://engine/active-executions",
                Name        = "Active Executions",
                Description = "Currently running or paused test executions",
                MimeType    = "application/json"
            },
            new()
            {
                Uri         = "teststand://engine/station-globals",
                Name        = "Station Globals",
                Description = "All TestStand station global variables",
                MimeType    = "application/json"
            },
            new()
            {
                Uri         = "teststand://adapters/loaded",
                Name        = "Loaded Adapters",
                Description = "Step type adapters currently loaded in TestStand",
                MimeType    = "application/json"
            },
            new()
            {
                Uri         = "teststand://engine/process-model",
                Name        = "Process Model",
                Description = "The active TestStand process model",
                MimeType    = "text/plain"
            }
        }
    };

    /// <summary>Creates the provider with its engine service and logger.</summary>
    public TestStandResourceProvider(ITestStandService ts,
        ILogger<TestStandResourceProvider> logger)
    {
        _ts     = ts;
        _logger = logger;
    }

    /// <summary>Lists the resources this provider exposes.</summary>
    public Task<ListResourcesResult> ListResourcesAsync() => Task.FromResult(_resourcesResult);

    /// <summary>Reads the resource identified by <paramref name="uri"/>.</summary>
    public async Task<ReadResourceResult> ReadResourceAsync(string uri)
    {
        try
        {
            return uri switch
            {
                "teststand://station/info"
                    => await ReadStationInfoAsync(uri),
                "teststand://engine/loaded-files"
                    => await ReadLoadedFilesAsync(uri),
                "teststand://engine/active-executions"
                    => await ReadActiveExecutionsAsync(uri),
                "teststand://engine/station-globals"
                    => await ReadStationGlobalsAsync(uri),
                "teststand://adapters/loaded"
                    => await ReadLoadedAdaptersAsync(uri),
                "teststand://engine/process-model"
                    => await ReadProcessModelAsync(uri),
                _   => throw new ArgumentException($"Unknown resource URI: {uri}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read resource: {Uri}", uri);
            throw;
        }
    }

    private async Task<ReadResourceResult> ReadStationInfoAsync(string uri)
    {
        var info = await _ts.GetStationInfoAsync();
        return JsonResult(uri, info);
    }

    private async Task<ReadResourceResult> ReadLoadedFilesAsync(string uri)
    {
        var files = await _ts.GetLoadedSequenceFilesAsync();
        return JsonResult(uri, files);
    }

    private async Task<ReadResourceResult> ReadActiveExecutionsAsync(string uri)
    {
        var execs = await _ts.GetActiveExecutionsAsync();
        return JsonResult(uri, execs);
    }

    private async Task<ReadResourceResult> ReadStationGlobalsAsync(string uri)
    {
        var vars = await _ts.GetStationGlobalsAsync();
        return JsonResult(uri, vars);
    }

    private async Task<ReadResourceResult> ReadLoadedAdaptersAsync(string uri)
    {
        var adapters = await _ts.GetLoadedAdaptersAsync();
        return JsonResult(uri, adapters);
    }

    private async Task<ReadResourceResult> ReadProcessModelAsync(string uri)
    {
        var model = await _ts.GetProcessModelAsync();
        return new ReadResourceResult
        {
            Contents = new List<ResourceContent>
            {
                new() { Uri = uri, MimeType = "text/plain", Text = model }
            }
        };
    }

    private static ReadResourceResult JsonResult(string uri, object obj) => new()
    {
        Contents = new List<ResourceContent>
        {
            new()
            {
                Uri      = uri,
                MimeType = "application/json",
                Text     = JsonSerializer.Serialize(obj, _jsonOpts)
            }
        }
    };
}

// ── Prompts ──────────────────────────────────────────────────────────────────

/// <summary>Serves MCP prompt templates for common TestStand workflows.</summary>
public class TestStandPromptProvider
{
    // Prompt definitions are static data — build the list once.
    private static readonly ListPromptsResult _promptsResult = new()
    {
        Prompts = new List<McpPrompt>
        {
            new()
            {
                Name        = "run_test_sequence",
                Description = "Prompt template for running a test sequence and analyzing results",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "sequence_file", Description = "Path to the .seq file", Required = true },
                    new() { Name = "entry_point",   Description = "Entry point sequence name", Required = false },
                    new() { Name = "serial_number", Description = "UUT serial number",       Required = false }
                }
            },
            new()
            {
                Name        = "analyze_sequence_file",
                Description = "Analyze a TestStand sequence file and summarize its structure",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "sequence_file", Description = "Path to the .seq file", Required = true }
                }
            },
            new()
            {
                Name        = "debug_failed_execution",
                Description = "Help diagnose and debug a failed test execution",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "execution_id", Description = "ID of the failed execution", Required = true }
                }
            },
            new()
            {
                Name        = "create_test_plan",
                Description = "Create a structured test plan from requirements",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "requirements", Description = "Test requirements to implement", Required = true },
                    new() { Name = "dut_type",     Description = "Type of DUT (e.g. PCB, module)",   Required = false }
                }
            },
            new()
            {
                Name        = "review_test_results",
                Description = "Review and summarize test results from a completed execution",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "execution_id", Description = "Completed execution ID", Required = true },
                    new() { Name = "detail_level", Description = "Summary level: 'brief', 'detailed', 'full'",
                            Required = false }
                }
            },
            new()
            {
                Name        = "use_sequence_editor",
                Description = "Launch and interact with the TestStand Sequence Editor for visual test execution and debugging",
                Arguments   = new List<PromptArgument>
                {
                    new() { Name = "sequence_file", Description = "Path to the .seq file to open", Required = false },
                    new() { Name = "entry_point",   Description = "Entry point to run (e.g. 'Test UUTs', 'Single Pass')", Required = false }
                }
            }
        }
    };

    /// <summary>Lists the available prompt templates.</summary>
    public ListPromptsResult ListPrompts() => _promptsResult;

    /// <summary>Renders the named prompt with the given arguments.</summary>
    public GetPromptResult GetPrompt(string name, Dictionary<string, string>? args)
    {
        args ??= new Dictionary<string, string>();
        return name switch
        {
            "run_test_sequence"      => RunTestSequencePrompt(args),
            "analyze_sequence_file"  => AnalyzeSequenceFilePrompt(args),
            "debug_failed_execution" => DebugFailedExecutionPrompt(args),
            "create_test_plan"       => CreateTestPlanPrompt(args),
            "review_test_results"    => ReviewTestResultsPrompt(args),
            "use_sequence_editor"    => UseSequenceEditorPrompt(args),
            _                        => throw new ArgumentException($"Unknown prompt: {name}")
        };
    }

    private static GetPromptResult RunTestSequencePrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("sequence_file",  out var seqFile);
        args.TryGetValue("entry_point",    out var entryPt);
        args.TryGetValue("serial_number",  out var sn);

        var text = $"""
            You are a TestStand automation engineer. Please help execute and analyze a test sequence.

            Steps to follow:
            1. First call `connect_engine` to connect to the TestStand engine.
            2. Open the sequence file: `{seqFile ?? "<sequence_file_path>"}` using `open_sequence_file`.
            3. {(sn != null ? $"Set the UUT serial number to '{sn}' after starting the execution." : "Ask for the UUT serial number if needed.")}
            4. Start the execution with entry point: `{entryPt ?? "SequentialPre"}` using `start_execution`.
            5. {(sn != null ? $"Immediately call `set_uut_serial_number` with serial number '{sn}'." : "")}
            6. Wait for completion using `wait_for_execution`.
            7. Retrieve and summarize the results.
            8. If any steps failed, highlight which steps failed and their error messages.

            Provide a clear summary of: overall pass/fail, test duration, any failed steps with their limits and measured values.
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult AnalyzeSequenceFilePrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("sequence_file", out var seqFile);

        var text = $"""
            You are a TestStand expert. Please analyze the following sequence file and provide a comprehensive summary.

            Sequence file to analyze: `{seqFile ?? "<sequence_file_path>"}`

            Use the following tools in order:
            1. `connect_engine` - Connect to TestStand.
            2. `open_sequence_file` - Open the file.
            3. `get_loaded_sequence_files` - Confirm it is loaded.
            4. For each sequence found, use `get_sequence` to get detailed step information.
            5. `get_file_globals` - List all file global variables.

            In your analysis, cover:
            - Number and names of sequences
            - Types of steps used (Numeric Limit Test, String Value Test, Action, etc.)
            - File global variables and their data types
            - Any potential issues or recommendations for improvement
            - Test coverage estimation
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult DebugFailedExecutionPrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("execution_id", out var execId);

        var text = $"""
            You are a TestStand diagnostic expert. Help me debug a failed test execution.

            Execution ID: `{execId ?? "<execution_id>"}`

            Investigation steps:
            1. `get_execution_status` - Check current state of the execution.
            2. `get_uut_info` - Get UUT details and overall result.
            3. `get_report_text` - Retrieve the full test report.
            4. `get_execution_log` - Check the execution log for errors.

            Based on the information gathered:
            - Identify which step(s) caused the failure
            - Explain the likely root cause
            - Suggest corrective actions
            - Indicate if this is a DUT failure, a test setup issue, or a sequence configuration problem
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult CreateTestPlanPrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("requirements", out var reqs);
        args.TryGetValue("dut_type",     out var dutType);

        var text = $"""
            You are a TestStand test engineer. Create a structured test plan for the following requirements.

            DUT Type: {dutType ?? "Electronic assembly"}
            Requirements:
            {reqs ?? "<paste your test requirements here>"}

            Please provide:
            1. A list of test sequences to create with their purpose
            2. For each sequence: the steps needed with step types (Numeric Limit, String Value, Action, etc.)
            3. Suggested limits and tolerances where applicable
            4. FileGlobal variables to define
            5. The recommended entry point sequence structure (Setup, Main Test, Cleanup)
            6. Estimated test time per sequence

            Format the output as a TestStand-ready test plan document.
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult ReviewTestResultsPrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("execution_id", out var execId);
        args.TryGetValue("detail_level", out var detail);
        detail ??= "detailed";

        var text = $"""
            You are a test results analyst. Please review and summarize the results of test execution `{execId ?? "<execution_id>"}`.

            Detail level requested: {detail}

            Use these tools to gather data:
            1. `get_execution_status` - Get execution state.
            2. `get_uut_info` - Get UUT identification and overall result.
            3. `get_report_text` - Get the full report text.

            {(detail == "brief" ? "Provide a one-paragraph summary with overall pass/fail, total steps, and any critical failures." :
              detail == "full"  ? "Provide the complete analysis including all step results, measurements, limits, margins, and trends." :
              "Provide a structured summary with: overall result, pass/fail counts, failed step details with measured values vs limits, and recommendations.")}
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult UseSequenceEditorPrompt(Dictionary<string, string> args)
    {
        args.TryGetValue("sequence_file", out var seqFile);
        args.TryGetValue("entry_point",   out var entryPt);

        var text = $"""
            You are a TestStand automation engineer. Help the user work with the TestStand Sequence Editor (SeqEdit.exe).

            Steps to follow:
            1. Use `launch_sequence_editor` to start the Sequence Editor if it is not already running.
            2. Check the editor status with `get_editor_status`.
            {(seqFile != null ? $"3. Open the sequence file `{seqFile}` using `open_file_in_editor`." : "3. Ask the user which sequence file to open, then use `open_file_in_editor`.")}
            4. The user can now visually inspect and interact with the sequence in the editor GUI.
            {(entryPt != null ? $"5. Run the sequence using `run_in_editor` with entry point `{entryPt}`." : "5. If the user wants to run, use `run_in_editor` with the appropriate entry point (e.g. 'Test UUTs', 'Single Pass').")}
            6. To close the editor when done, use `close_sequence_editor`.

            Available Sequence Editor tools:
            - `launch_sequence_editor` – Launch or connect to a running editor
            - `get_editor_status` – Check if the editor is running and get process details
            - `open_file_in_editor` – Open a .seq file in the editor GUI
            - `run_in_editor` – Run a sequence with a specific entry point in the editor
            - `close_sequence_editor` – Close the editor (graceful or forced)
            """;

        return SimplePrompt(text);
    }

    private static GetPromptResult SimplePrompt(string text) => new()
    {
        Messages = new List<PromptMessage>
        {
            new()
            {
                Role    = "user",
                Content = new PromptContent { Type = "text", Text = text }
            }
        }
    };
}
