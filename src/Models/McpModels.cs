using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestStandMCP.Models;

// ── JSON-RPC 2.0 Base ────────────────────────────────────────────────────────

/// <summary>A JSON-RPC 2.0 request (or notification when <see cref="Id"/> is null).</summary>
public class JsonRpcRequest
{
    /// <summary>JSON-RPC protocol version; always "2.0".</summary>
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
    /// <summary>Request id echoed back in the response; null for notifications.</summary>
    [JsonPropertyName("id")]      public JsonElement? Id { get; set; }
    /// <summary>The RPC method name to invoke.</summary>
    [JsonPropertyName("method")]  public string Method { get; set; } = "";
    /// <summary>Opaque method parameters, deserialized per method.</summary>
    [JsonPropertyName("params")]  public JsonElement? Params { get; set; }
}

/// <summary>A JSON-RPC 2.0 response carrying either a result or an error.</summary>
public class JsonRpcResponse
{
    /// <summary>JSON-RPC protocol version; always "2.0".</summary>
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
    /// <summary>Id of the request this response corresponds to.</summary>
    [JsonPropertyName("id")]      public JsonElement? Id { get; set; }
    /// <summary>The successful result payload; null when <see cref="Error"/> is set.</summary>
    [JsonPropertyName("result")]  public object? Result { get; set; }
    /// <summary>The error payload; null on success.</summary>
    [JsonPropertyName("error")]   public JsonRpcError? Error { get; set; }
}

/// <summary>A JSON-RPC 2.0 error object.</summary>
public class JsonRpcError
{
    /// <summary>Numeric JSON-RPC error code.</summary>
    [JsonPropertyName("code")]    public int Code { get; set; }
    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    /// <summary>Optional structured error data.</summary>
    [JsonPropertyName("data")]    public object? Data { get; set; }
}

// ── MCP Protocol Types ───────────────────────────────────────────────────────

/// <summary>Identifies the MCP server to the client during initialization.</summary>
public class McpServerInfo
{
    /// <summary>Display name of the server.</summary>
    [JsonPropertyName("name")]    public string Name { get; set; } = "TestStand MCP Server";
    /// <summary>Server version string.</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
}

/// <summary>The capabilities the server advertises to the client.</summary>
public class McpCapabilities
{
    /// <summary>Tool capability descriptor, if tools are supported.</summary>
    [JsonPropertyName("tools")]     public ToolsCapability? Tools { get; set; }
    /// <summary>Resource capability descriptor, if resources are supported.</summary>
    [JsonPropertyName("resources")] public ResourcesCapability? Resources { get; set; }
    /// <summary>Prompt capability descriptor, if prompts are supported.</summary>
    [JsonPropertyName("prompts")]   public PromptsCapability? Prompts { get; set; }
    /// <summary>Logging capability descriptor, if logging is supported.</summary>
    [JsonPropertyName("logging")]   public LoggingCapability? Logging { get; set; }
}

/// <summary>Tool capability flags.</summary>
public class ToolsCapability
{
    /// <summary>Whether the server emits notifications when the tool list changes.</summary>
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; }
}

/// <summary>Resource capability flags.</summary>
public class ResourcesCapability
{
    /// <summary>Whether the client may subscribe to resource updates.</summary>
    [JsonPropertyName("subscribe")]   public bool Subscribe { get; set; }
    /// <summary>Whether the server emits notifications when the resource list changes.</summary>
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; }
}

/// <summary>Prompt capability flags.</summary>
public class PromptsCapability
{
    /// <summary>Whether the server emits notifications when the prompt list changes.</summary>
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; }
}

/// <summary>Marker capability indicating the server supports logging control.</summary>
public class LoggingCapability { }

/// <summary>Result of the MCP <c>initialize</c> handshake.</summary>
public class InitializeResult
{
    /// <summary>MCP protocol version the server implements.</summary>
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "2024-11-05";
    /// <summary>Capabilities advertised by the server.</summary>
    [JsonPropertyName("capabilities")]    public McpCapabilities Capabilities { get; set; } = new();
    /// <summary>Identity of the server.</summary>
    [JsonPropertyName("serverInfo")]      public McpServerInfo ServerInfo { get; set; } = new();
}

// ── Tool Definitions ─────────────────────────────────────────────────────────

/// <summary>Describes a callable MCP tool and its input schema.</summary>
public class McpTool
{
    /// <summary>Unique tool name used in <c>tools/call</c>.</summary>
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    /// <summary>Human-readable description of what the tool does.</summary>
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>JSON Schema describing the tool's arguments.</summary>
    [JsonPropertyName("inputSchema")] public JsonElement InputSchema { get; set; }
}

/// <summary>Result of <c>tools/list</c>.</summary>
public class ListToolsResult
{
    /// <summary>All tools the server exposes.</summary>
    [JsonPropertyName("tools")] public List<McpTool> Tools { get; set; } = new();
}

/// <summary>Arguments for a <c>tools/call</c> request.</summary>
public class CallToolRequest
{
    /// <summary>Name of the tool to invoke.</summary>
    [JsonPropertyName("name")]      public string Name { get; set; } = "";
    /// <summary>Tool-specific arguments as a JSON object.</summary>
    [JsonPropertyName("arguments")] public JsonElement? Arguments { get; set; }
}

/// <summary>A single content block in a tool result.</summary>
public class ToolContent
{
    /// <summary>Content type; "text" for textual content.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    /// <summary>The textual content payload.</summary>
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

/// <summary>Result of a <c>tools/call</c> invocation.</summary>
public class CallToolResult
{
    /// <summary>Content blocks returned by the tool.</summary>
    [JsonPropertyName("content")]  public List<ToolContent> Content { get; set; } = new();
    /// <summary>True when the tool reported an error.</summary>
    [JsonPropertyName("isError")]  public bool IsError { get; set; }
}

// ── Resource Definitions ─────────────────────────────────────────────────────

/// <summary>Describes an MCP resource the client can read.</summary>
public class McpResource
{
    /// <summary>Unique resource URI.</summary>
    [JsonPropertyName("uri")]         public string Uri { get; set; } = "";
    /// <summary>Display name of the resource.</summary>
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    /// <summary>Optional human-readable description.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>Optional MIME type of the resource content.</summary>
    [JsonPropertyName("mimeType")]    public string? MimeType { get; set; }
}

/// <summary>Result of <c>resources/list</c>.</summary>
public class ListResourcesResult
{
    /// <summary>All resources the server exposes.</summary>
    [JsonPropertyName("resources")] public List<McpResource> Resources { get; set; } = new();
}

/// <summary>Arguments for a <c>resources/read</c> request.</summary>
public class ReadResourceRequest
{
    /// <summary>URI of the resource to read.</summary>
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
}

/// <summary>A single resource content block.</summary>
public class ResourceContent
{
    /// <summary>URI of the resource this content belongs to.</summary>
    [JsonPropertyName("uri")]      public string Uri { get; set; } = "";
    /// <summary>MIME type of the content.</summary>
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    /// <summary>The content payload as text.</summary>
    [JsonPropertyName("text")]     public string? Text { get; set; }
}

/// <summary>Result of <c>resources/read</c>.</summary>
public class ReadResourceResult
{
    /// <summary>Content blocks of the requested resource.</summary>
    [JsonPropertyName("contents")] public List<ResourceContent> Contents { get; set; } = new();
}

// ── Prompt Definitions ───────────────────────────────────────────────────────

/// <summary>Describes an MCP prompt template.</summary>
public class McpPrompt
{
    /// <summary>Unique prompt name.</summary>
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    /// <summary>Optional human-readable description.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>Arguments the prompt accepts, if any.</summary>
    [JsonPropertyName("arguments")]   public List<PromptArgument>? Arguments { get; set; }
}

/// <summary>Declares a single argument accepted by a prompt.</summary>
public class PromptArgument
{
    /// <summary>Argument name.</summary>
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    /// <summary>Optional description of the argument.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>Whether the argument is required.</summary>
    [JsonPropertyName("required")]    public bool Required { get; set; }
}

/// <summary>Result of <c>prompts/list</c>.</summary>
public class ListPromptsResult
{
    /// <summary>All prompts the server exposes.</summary>
    [JsonPropertyName("prompts")] public List<McpPrompt> Prompts { get; set; } = new();
}

/// <summary>Arguments for a <c>prompts/get</c> request.</summary>
public class GetPromptRequest
{
    /// <summary>Name of the prompt to retrieve.</summary>
    [JsonPropertyName("name")]      public string Name { get; set; } = "";
    /// <summary>Values for the prompt's declared arguments.</summary>
    [JsonPropertyName("arguments")] public Dictionary<string, string>? Arguments { get; set; }
}

/// <summary>A single message in a rendered prompt.</summary>
public class PromptMessage
{
    /// <summary>Role of the message author ("user" or "assistant").</summary>
    [JsonPropertyName("role")]    public string Role { get; set; } = "user";
    /// <summary>The message content.</summary>
    [JsonPropertyName("content")] public PromptContent Content { get; set; } = new();
}

/// <summary>Content of a single prompt message.</summary>
public class PromptContent
{
    /// <summary>Content type; "text" for textual content.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    /// <summary>The textual content payload.</summary>
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

/// <summary>Result of <c>prompts/get</c>.</summary>
public class GetPromptResult
{
    /// <summary>Optional description of the rendered prompt.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>The rendered prompt messages.</summary>
    [JsonPropertyName("messages")]    public List<PromptMessage> Messages { get; set; } = new();
}
