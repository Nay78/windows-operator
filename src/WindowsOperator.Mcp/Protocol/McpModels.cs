using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WindowsOperator.Mcp.Protocol;

public sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema)
{
    public string Title { get; init; } = Name;

    public JsonObject? OutputSchema { get; init; }

    public JsonObject? Annotations { get; init; }

    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; init; }
}

public sealed record McpToolResult(
    JsonNode? StructuredContent,
    string ContentText);

public sealed record McpRequest(
    string? Jsonrpc,
    JsonNode? Id,
    string Method,
    JsonObject? Params);

public sealed record McpResponse(
    string Jsonrpc,
    JsonNode? Id,
    JsonObject? Result,
    JsonObject? Error);

internal sealed class McpProtocolException : Exception
{
    private McpProtocolException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }

    public static McpProtocolException InvalidParams(string message) => new(-32602, message);

    public static McpProtocolException MethodNotFound(string message) => new(-32601, message);
}
