using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestStandMCP.Models;

// ── JSON-RPC 2.0 Base ────────────────────────────────────────────────────────

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
    [JsonPropertyName("id")]      public JsonElement? Id { get; set; }
    [JsonPropertyName("method")]  public string Method { get; set; } = "";
    [JsonPropertyName("params")]  public JsonElement? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
    [JsonPropertyName("id")]      public JsonElement? Id { get; set; }
    [JsonPropertyName("result")]  public object? Result { get; set; }
    [JsonPropertyName("error")]   public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")]    public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")]    public object? Data { get; set; }
}

// ── MCP Protocol Types ───────────────────────────────────────────────────────

public class McpServerInfo
{
    [JsonPropertyName("name")]    public string Name { get; set; } = "TestStand MCP Server";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
}

public class McpCapabilities
{
    [JsonPropertyName("tools")]     public ToolsCapability? Tools { get; set; }
    [JsonPropertyName("resources")] public ResourcesCapability? Resources { get; set; }
    [JsonPropertyName("prompts")]   public PromptsCapability? Prompts { get; set; }
    [JsonPropertyName("logging")]   public LoggingCapability? Logging { get; set; }
}

public class ToolsCapability
{
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; } = false;
}

public class ResourcesCapability
{
    [JsonPropertyName("subscribe")]   public bool Subscribe { get; set; } = false;
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; } = false;
}

public class PromptsCapability
{
    [JsonPropertyName("listChanged")] public bool ListChanged { get; set; } = false;
}

public class LoggingCapability { }

public class InitializeResult
{
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "2024-11-05";
    [JsonPropertyName("capabilities")]    public McpCapabilities Capabilities { get; set; } = new();
    [JsonPropertyName("serverInfo")]      public McpServerInfo ServerInfo { get; set; } = new();
}

// ── Tool Definitions ─────────────────────────────────────────────────────────

public class McpTool
{
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("inputSchema")] public JsonElement InputSchema { get; set; }
}

public class ListToolsResult
{
    [JsonPropertyName("tools")] public List<McpTool> Tools { get; set; } = new();
}

public class CallToolRequest
{
    [JsonPropertyName("name")]      public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public JsonElement? Arguments { get; set; }
}

public class ToolContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class CallToolResult
{
    [JsonPropertyName("content")]  public List<ToolContent> Content { get; set; } = new();
    [JsonPropertyName("isError")]  public bool IsError { get; set; } = false;
}

// ── Resource Definitions ─────────────────────────────────────────────────────

public class McpResource
{
    [JsonPropertyName("uri")]         public string Uri { get; set; } = "";
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("mimeType")]    public string? MimeType { get; set; }
}

public class ListResourcesResult
{
    [JsonPropertyName("resources")] public List<McpResource> Resources { get; set; } = new();
}

public class ReadResourceRequest
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
}

public class ResourceContent
{
    [JsonPropertyName("uri")]      public string Uri { get; set; } = "";
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("text")]     public string? Text { get; set; }
}

public class ReadResourceResult
{
    [JsonPropertyName("contents")] public List<ResourceContent> Contents { get; set; } = new();
}

// ── Prompt Definitions ───────────────────────────────────────────────────────

public class McpPrompt
{
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("arguments")]   public List<PromptArgument>? Arguments { get; set; }
}

public class PromptArgument
{
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("required")]    public bool Required { get; set; }
}

public class ListPromptsResult
{
    [JsonPropertyName("prompts")] public List<McpPrompt> Prompts { get; set; } = new();
}

public class GetPromptRequest
{
    [JsonPropertyName("name")]      public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public Dictionary<string, string>? Arguments { get; set; }
}

public class PromptMessage
{
    [JsonPropertyName("role")]    public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public PromptContent Content { get; set; } = new();
}

public class PromptContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class GetPromptResult
{
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("messages")]    public List<PromptMessage> Messages { get; set; } = new();
}
