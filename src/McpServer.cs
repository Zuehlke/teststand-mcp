using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestStandMCP.Models;
using TestStandMCP.Tools;
using Microsoft.Extensions.Logging;

namespace TestStandMCP;

/// <summary>
/// Core MCP server: reads JSON-RPC 2.0 messages from stdin,
/// dispatches to the appropriate handler, and writes responses to stdout.
/// </summary>
public class McpServer
{
    private readonly TestStandToolRegistry _tools;
    private readonly TestStandResourceProvider _resources;
    private readonly TestStandPromptProvider _prompts;
    private readonly ILogger<McpServer> _logger;
    private bool _initialized;
    private readonly Queue<string> _commandHistory = new();
    private int _panelLinesWritten = 0;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public McpServer(
        TestStandToolRegistry tools,
        TestStandResourceProvider resources,
        TestStandPromptProvider prompts,
        ILogger<McpServer> logger)
    {
        _tools     = tools;
        _resources = resources;
        _prompts   = prompts;
        _logger    = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("TestStand MCP Server starting (stdio transport)...");

        // Use UTF-8 without BOM for stdio
        Console.InputEncoding  = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);

        using var stdin  = new StreamReader(Console.OpenStandardInput(),  new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await stdin.ReadLineAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            _logger.LogDebug(">> {Line}", line);

            JsonRpcResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOpts);
                if (request == null)
                {
                    response = ErrorResponse(null, -32700, "Parse error");
                }
                else
                {
                    response = await DispatchAsync(request);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parse error");
                response = ErrorResponse(null, -32700, $"Parse error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing request");
                response = ErrorResponse(null, -32603, $"Internal error: {ex.Message}");
            }

            var responseJson = JsonSerializer.Serialize(response, _jsonOpts);
            _logger.LogDebug("<< {Json}", responseJson);
            await stdout.WriteLineAsync(responseJson);
        }

        _logger.LogInformation("TestStand MCP Server stopped.");
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req)
    {
        try
        {
            object? result = req.Method switch
            {
                "initialize"         => HandleInitialize(req),
                "initialized"        => (object?)null,       // notification, no response needed
                "ping"               => new { },
                "tools/list"         => HandleToolsList(),
                "tools/call"         => await HandleToolCallAsync(req),
                "resources/list"     => await HandleResourcesListAsync(),
                "resources/read"     => await HandleResourceReadAsync(req),
                "prompts/list"       => HandlePromptsList(),
                "prompts/get"        => HandlePromptsGet(req),
                "logging/setLevel"   => new { },
                _                    => throw new McpException(-32601, $"Method not found: {req.Method}")
            };

            // notifications don't need a response
            if (req.Id == null) return null!;

            return new JsonRpcResponse { Id = req.Id, Result = result };
        }
        catch (McpException ex)
        {
            _logger.LogWarning("MCP error {Code}: {Message}", ex.Code, ex.Message);
            return ErrorResponse(req.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling method {Method}", req.Method);
            return ErrorResponse(req.Id, -32603, ex.Message);
        }
    }

    // ── Method Handlers ───────────────────────────────────────────────────────

    private InitializeResult HandleInitialize(JsonRpcRequest req)
    {
        _initialized = true;
        _logger.LogInformation("Client initialized. Protocol: {Method}", req.Method);
        DrawCommandPanel();
        return new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo      = new McpServerInfo
            {
                Name    = "TestStand MCP Server",
                Version = "1.0.0"
            },
            Capabilities = new McpCapabilities
            {
                Tools     = new ToolsCapability     { ListChanged = false },
                Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                Prompts   = new PromptsCapability   { ListChanged = false },
                Logging   = new LoggingCapability()
            }
        };
    }

    private ListToolsResult HandleToolsList()
    {
        return new ListToolsResult { Tools = _tools.GetTools().ToList() };
    }

    private async Task<CallToolResult> HandleToolCallAsync(JsonRpcRequest req)
    {
        var callReq = DeserializeParams<CallToolRequest>(req);
        if (string.IsNullOrEmpty(callReq.Name))
            throw new McpException(-32602, "tools/call requires 'name' parameter.");

        if (_commandHistory.Count >= 10) _commandHistory.Dequeue();
        _commandHistory.Enqueue($"{DateTime.Now:HH:mm:ss}  {callReq.Name}");
        DrawCommandPanel();
        return await _tools.CallToolAsync(callReq.Name, callReq.Arguments);
    }

    private async Task<ListResourcesResult> HandleResourcesListAsync() =>
        await _resources.ListResourcesAsync();

    private async Task<ReadResourceResult> HandleResourceReadAsync(JsonRpcRequest req)
    {
        var readReq = DeserializeParams<ReadResourceRequest>(req);
        if (string.IsNullOrEmpty(readReq.Uri))
            throw new McpException(-32602, "resources/read requires 'uri' parameter.");
        return await _resources.ReadResourceAsync(readReq.Uri);
    }

    private ListPromptsResult HandlePromptsList() => _prompts.ListPrompts();

    private GetPromptResult HandlePromptsGet(JsonRpcRequest req)
    {
        var getReq = DeserializeParams<GetPromptRequest>(req);
        if (string.IsNullOrEmpty(getReq.Name))
            throw new McpException(-32602, "prompts/get requires 'name' parameter.");
        return _prompts.GetPrompt(getReq.Name, getReq.Arguments);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T DeserializeParams<T>(JsonRpcRequest req) where T : new()
    {
        if (req.Params == null) return new T();
        return JsonSerializer.Deserialize<T>(req.Params.Value.GetRawText(), _jsonOpts) ?? new T();
    }

    private static JsonRpcResponse ErrorResponse(JsonElement? id, int code, string message) =>
        new()
        {
            Id    = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };

    private void DrawCommandPanel()
    {
        const string dim = "\x1b[90m";
        const string rst = "\x1b[0m";
        const string cyan = "\x1b[96m";

        if (_panelLinesWritten > 0)
            Console.Error.Write($"\x1b[{_panelLinesWritten}A\x1b[0J");

        Console.Error.WriteLine($"{dim}─── Last 10 commands ──────────────────────────────────{rst}");

        var cmds = _commandHistory.ToArray();
        for (int i = 0; i < 10; i++)
        {
            if (i < cmds.Length)
                Console.Error.WriteLine($"  {cyan}{cmds[i]}{rst}");
            else
                Console.Error.WriteLine();
        }

        _panelLinesWritten = 11; // 1 Headerzeile + 10 Slots
    }
}

// ── Custom Exception ──────────────────────────────────────────────────────────

public class McpException : Exception
{
    public int Code { get; }
    public McpException(int code, string message) : base(message) { Code = code; }
}
