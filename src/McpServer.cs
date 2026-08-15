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
    private readonly Queue<string> _commandHistory = new();
    private int _panelLinesWritten;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Creates the MCP server with its tool registry, providers and logger.</summary>
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

    /// <summary>Runs the stdio JSON-RPC read/dispatch/write loop until cancelled or EOF.</summary>
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

            JsonRpcResponse? response;
            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOpts);
                if (request == null)
                {
                    response = ErrorResponse(null, -32700, "Parse error");
                }
                else if (request.Id == null)
                {
                    // A JSON-RPC NOTIFICATION (no id) must never be answered — not with a
                    // result, not with an error. Answering one is a protocol violation that
                    // strict clients reject at handshake time (every connection sends
                    // notifications/initialized).
                    HandleNotification(request);
                    continue;
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

            // Belt and braces: never write a bare "null" line onto the wire.
            if (response == null) continue;

            var responseJson = JsonSerializer.Serialize(response, _jsonOpts);
            _logger.LogDebug("<< {Json}", responseJson);
            await stdout.WriteLineAsync(responseJson);
        }

        _logger.LogInformation("TestStand MCP Server stopped.");
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    /// <summary>
    /// Consumes a JSON-RPC notification (no id). Notifications are acknowledged by silence:
    /// this method may act on one, but the caller writes nothing back.
    /// </summary>
    private void HandleNotification(JsonRpcRequest req)
    {
        switch (req.Method)
        {
            case "notifications/initialized":
            case "initialized":                 // pre-2025 spelling, still seen in the wild
                _logger.LogInformation("Client completed initialization.");
                break;
            case "notifications/cancelled":
                _logger.LogDebug("Client cancelled a request.");
                break;
            default:
                _logger.LogDebug("Ignoring notification {Method}", req.Method);
                break;
        }
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req)
    {
        try
        {
            object? result = req.Method switch
            {
                "initialize"         => HandleInitialize(req),
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
        var requested = req.Params is { } p &&
                        p.ValueKind == JsonValueKind.Object &&
                        p.TryGetProperty("protocolVersion", out var pv) &&
                        pv.ValueKind == JsonValueKind.String
            ? pv.GetString()
            : null;

        var negotiated = McpProtocol.Negotiate(requested);

        _logger.LogInformation("Client initialized. Protocol requested: {Requested}, negotiated: {Negotiated}",
            requested ?? "(none)", negotiated);
        DrawCommandPanel();
        return new InitializeResult
        {
            ProtocolVersion = negotiated,
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

        _panelLinesWritten = 11; // 1 header line + 10 slots
    }
}

// ── Custom Exception ──────────────────────────────────────────────────────────

/// <summary>An MCP/JSON-RPC error carrying a numeric error code.</summary>
public class McpException : Exception
{
    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; }
    /// <summary>Creates an exception with the given JSON-RPC code and message.</summary>
    public McpException(int code, string message) : base(message) { Code = code; }
}
