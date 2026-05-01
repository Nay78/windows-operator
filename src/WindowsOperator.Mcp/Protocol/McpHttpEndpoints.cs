using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Mcp.Protocol;

public static class McpHttpEndpoints
{
    public static IEndpointRouteBuilder MapMcpHttpEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/mcp", async (
            HttpRequest httpRequest,
            McpProtocolHandler handler,
            CancellationToken cancellationToken) =>
        {
            var node = await JsonNode.ParseAsync(httpRequest.Body, cancellationToken: cancellationToken);
            var request = new McpRequest(
                node?["jsonrpc"]?.GetValue<string>(),
                node?["id"],
                node?["method"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing method."),
                node?["params"] as JsonObject);

            var response = await handler.HandleAsync(request, cancellationToken);
            return response is null
                ? Results.NoContent()
                : Results.Json(
                    new JsonObject
                    {
                        ["jsonrpc"] = response.Jsonrpc,
                        ["id"] = response.Id?.DeepClone(),
                        ["result"] = response.Result?.DeepClone(),
                        ["error"] = response.Error?.DeepClone(),
                    },
                    OperatorJson.SerializerOptions);
        });

        return endpoints;
    }
}
