using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Mcp.Protocol;

public sealed class McpStdioServer : BackgroundService
{
    private readonly McpProtocolHandler _handler;
    private readonly IOptions<OperatorOptions> _options;

    public McpStdioServer(
        McpProtocolHandler handler,
        IOptions<OperatorOptions> options,
        ILogger<McpStdioServer> logger)
    {
        _handler = handler;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.EnableMcpStdio)
        {
            return;
        }

        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();

        while (!stoppingToken.IsCancellationRequested)
        {
            var request = await ReadRequestAsync(input, stoppingToken);
            if (request is null)
            {
                await Task.Delay(50, stoppingToken);
                continue;
            }

            var response = await _handler.HandleAsync(request, stoppingToken);
            if (response is null)
            {
                continue;
            }

            await WriteResponseAsync(output, response, stoppingToken);
        }
    }

    private static async Task<McpRequest?> ReadRequestAsync(Stream input, CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>();
        var one = new byte[1];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await input.ReadAsync(one, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            headerBytes.Add(one[0]);
            var count = headerBytes.Count;
            if (count >= 4 &&
                headerBytes[count - 4] == '\r' &&
                headerBytes[count - 3] == '\n' &&
                headerBytes[count - 2] == '\r' &&
                headerBytes[count - 1] == '\n')
            {
                break;
            }
        }

        var header = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLengthLine = header
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        if (contentLengthLine is null)
        {
            return null;
        }

        var contentLength = int.Parse(contentLengthLine.Split(':', 2)[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var payloadBytes = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = await input.ReadAsync(payloadBytes.AsMemory(offset, contentLength - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var node = JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("Invalid MCP payload.");

        return new McpRequest(
            node["jsonrpc"]?.GetValue<string>(),
            node["id"],
            node["method"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing method."),
            node["params"] as JsonObject);
    }

    private static async Task WriteResponseAsync(Stream output, McpResponse response, CancellationToken cancellationToken)
    {
        var payloadNode = new JsonObject
        {
            ["jsonrpc"] = response.Jsonrpc,
            ["id"] = response.Id?.DeepClone(),
            ["result"] = response.Result?.DeepClone(),
            ["error"] = response.Error?.DeepClone(),
        };

        var payload = payloadNode.ToJsonString(OperatorJson.SerializerOptions);
        var body = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(body, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
