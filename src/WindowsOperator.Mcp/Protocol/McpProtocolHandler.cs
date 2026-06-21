using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WindowsOperator.Core;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Mcp.Protocol;

public sealed class McpProtocolHandler
{
    private readonly ILogger<McpProtocolHandler> _logger;
    private readonly McpToolCatalog _toolCatalog;

    public McpProtocolHandler(McpToolCatalog toolCatalog, ILogger<McpProtocolHandler> logger)
    {
        _toolCatalog = toolCatalog;
        _logger = logger;
    }

    public async Task<McpResponse?> HandleAsync(McpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            JsonObject? result = request.Method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "windows-operator",
                        ["version"] = "0.1.0",
                    },
                },
                "notifications/initialized" => null,
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject
                {
                    ["tools"] = JsonSerializer.SerializeToNode(_toolCatalog.ListTools(), OperatorJson.SerializerOptions),
                },
                "tools/call" => await ExecuteToolCallAsync(request.Params, cancellationToken),
                _ => throw McpProtocolException.MethodNotFound($"Unsupported method '{request.Method}'."),
            };

            return result is null ? null : new McpResponse("2.0", request.Id, result, null);
        }
        catch (McpProtocolException failure)
        {
            return new McpResponse("2.0", request.Id, null, new JsonObject
            {
                ["code"] = failure.Code,
                ["message"] = failure.Message,
            });
        }
        catch (OperatorFailureException failure)
        {
            return new McpResponse("2.0", request.Id, null, new JsonObject
            {
                ["code"] = failure.Error.Code,
                ["message"] = failure.Error.Message,
                ["data"] = JsonSerializer.SerializeToNode(failure.Error, OperatorJson.SerializerOptions),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP request failed: {Method}", request.Method);
            return new McpResponse("2.0", request.Id, null, new JsonObject
            {
                ["code"] = -32603,
                ["message"] = ex.Message,
            });
        }
    }

    private async Task<JsonObject> ExecuteToolCallAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>()
            ?? throw McpProtocolException.InvalidParams("tools/call missing name.");

        var args = parameters["arguments"] as JsonObject;
        var result = await _toolCatalog.ExecuteToolResultAsync(name, args, cancellationToken);

        return new JsonObject
        {
            ["structuredContent"] = result.StructuredContent,
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = result.ContentText,
                },
            },
        };
    }
}
