using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Services;

internal interface IEdgeDevToolsService
{
    EdgePageTarget? ReadTarget(int devToolsPort, string? preferredUrl);

    EdgeDevToolsEvaluation Evaluate(string webSocketDebuggerUrl, string expression, TimeSpan timeout);

    bool SendCommand(string webSocketDebuggerUrl, string method, object parameters, TimeSpan timeout);

    bool CloseTarget(int devToolsPort, string? targetId);
}

internal sealed class EdgeDevToolsService : IEdgeDevToolsService
{
    private static readonly HttpClient DevToolsHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public EdgePageTarget? ReadTarget(int devToolsPort, string? preferredUrl) =>
        TryReadEdgeTarget(devToolsPort, preferredUrl);

    public EdgeDevToolsEvaluation Evaluate(string webSocketDebuggerUrl, string expression, TimeSpan timeout) =>
        EvaluateRuntime(webSocketDebuggerUrl, expression, timeout);

    public bool SendCommand(string webSocketDebuggerUrl, string method, object parameters, TimeSpan timeout) =>
        SendDevToolsCommand(webSocketDebuggerUrl, method, parameters, timeout);

    public bool CloseTarget(int devToolsPort, string? targetId) =>
        TryCloseEdgeTarget(devToolsPort, targetId);

    public static EdgePageTarget? TryReadEdgeTarget(int devToolsPort, string? preferredUrl)
    {
        try
        {
            var targets = TryReadEdgeTargets(devToolsPort);
            return targets is null ? null : SelectBestEdgeTarget(targets, preferredUrl);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<EdgePageTarget>? TryReadEdgeTargets(int devToolsPort)
    {
        using var response = DevToolsHttpClient.GetAsync($"http://127.0.0.1:{devToolsPort}/json/list").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        return ParseEdgePageTargets(document.RootElement);
    }

    public static IReadOnlyList<EdgePageTarget> ParseEdgePageTargets(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EdgePageTarget>();
        }

        var targets = new List<EdgePageTarget>();
        foreach (var candidate in root.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = candidate.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetId = candidate.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var url = candidate.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var title = candidate.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var webSocketDebuggerUrl = candidate.TryGetProperty("webSocketDebuggerUrl", out var wsElement)
                ? wsElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(webSocketDebuggerUrl))
            {
                continue;
            }

            targets.Add(new EdgePageTarget(targetId, title, url, webSocketDebuggerUrl));
        }

        return targets;
    }

    public static EdgePageTarget? SelectBestEdgeTarget(
        IEnumerable<EdgePageTarget> targets,
        string? preferredUrl)
    {
        EdgePageTarget? best = null;
        foreach (var target in targets)
        {
            if (best is null || ScoreEdgeTarget(target, preferredUrl) > ScoreEdgeTarget(best.Value, preferredUrl))
            {
                best = target;
            }
        }

        return best;
    }

    public static StartupTargetPrunePlan PlanStartupTargetPrune(
        IEnumerable<EdgePageTarget> targets,
        string? preferredUrl)
    {
        var pages = targets.ToArray();
        var best = SelectBestEdgeTarget(pages, preferredUrl);
        if (best is null)
        {
            return new StartupTargetPrunePlan(null, Array.Empty<EdgePageTarget>());
        }

        var close = pages
            .Where(target =>
                !string.Equals(target.WebSocketDebuggerUrl, best.Value.WebSocketDebuggerUrl, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(target.TargetId))
            .ToArray();
        return new StartupTargetPrunePlan(best, close);
    }

    public static int ScoreEdgeTarget(EdgePageTarget target, string? preferredUrl)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(target.Url))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(preferredUrl) &&
            string.Equals(target.Url, preferredUrl, StringComparison.OrdinalIgnoreCase))
        {
            score += 400;
        }

        if (Uri.TryCreate(preferredUrl, UriKind.Absolute, out var preferredUri) &&
            Uri.TryCreate(target.Url, UriKind.Absolute, out var targetUri) &&
            string.Equals(preferredUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, "devtools", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "edge", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        return score;
    }

    public static bool TryCloseEdgeTarget(int devToolsPort, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return false;
        }

        try
        {
            using var response = DevToolsHttpClient.GetAsync($"http://127.0.0.1:{devToolsPort}/json/close/{Uri.EscapeDataString(targetId)}").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static T? EvaluateJson<T>(string webSocketDebuggerUrl, string expression)
    {
        var evaluation = EvaluateRuntime(webSocketDebuggerUrl, expression, TimeSpan.FromSeconds(10));
        if (!evaluation.Success)
        {
            return default;
        }

        var raw = evaluation.ValueText ?? evaluation.ValueJson;
        if (typeof(T) == typeof(string))
        {
            return (T)(object)(raw ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, OperatorJson.SerializerOptions);
        }
        catch
        {
            return default;
        }
    }

    public static EdgeDevToolsEvaluation EvaluateRuntime(
        string webSocketDebuggerUrl,
        string expression,
        TimeSpan timeout)
    {
        try
        {
            using var timeoutSource = new CancellationTokenSource(ClampTimeout(timeout));
            using var client = new ClientWebSocket();
            var token = timeoutSource.Token;
            client.ConnectAsync(new Uri(webSocketDebuggerUrl), token).GetAwaiter().GetResult();
            var requestId = Random.Shared.Next(1, int.MaxValue);
            var payload = JsonSerializer.Serialize(
                new
                {
                    id = requestId,
                    method = "Runtime.evaluate",
                    @params = new
                    {
                        expression,
                        returnByValue = true,
                        awaitPromise = true,
                    },
                });
            var requestBytes = Encoding.UTF8.GetBytes(payload);
            client.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Text,
                    true,
                    token)
                .GetAwaiter()
                .GetResult();

            var buffer = new byte[64 * 1024];
            while (true)
            {
                var builder = new ArrayBufferWriter<byte>();
                WebSocketReceiveResult result;
                do
                {
                    result = client.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return EdgeDevToolsEvaluation.Failed("DevTools websocket closed.");
                    }

                    builder.Write(new ReadOnlySpan<byte>(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(builder.WrittenMemory);
                if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                    idElement.GetInt32() != requestId)
                {
                    continue;
                }

                return ParseRuntimeEvaluation(document.RootElement);
            }
        }
        catch (OperationCanceledException)
        {
            return EdgeDevToolsEvaluation.Timeout();
        }
        catch (WebSocketException ex)
        {
            return EdgeDevToolsEvaluation.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return EdgeDevToolsEvaluation.Failed(ex.Message);
        }
    }

    public static bool SendDevToolsCommand(
        string webSocketDebuggerUrl,
        string method,
        object parameters,
        TimeSpan? timeout = null)
    {
        try
        {
            using var timeoutSource = new CancellationTokenSource(ClampTimeout(timeout ?? TimeSpan.FromSeconds(10)));
            using var client = new ClientWebSocket();
            var token = timeoutSource.Token;
            client.ConnectAsync(new Uri(webSocketDebuggerUrl), token).GetAwaiter().GetResult();
            var requestId = Random.Shared.Next(1, int.MaxValue);
            var payload = JsonSerializer.Serialize(new { id = requestId, method, @params = parameters });
            var requestBytes = Encoding.UTF8.GetBytes(payload);
            client.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Text,
                    true,
                    token)
                .GetAwaiter()
                .GetResult();

            var buffer = new byte[64 * 1024];
            while (true)
            {
                var builder = new ArrayBufferWriter<byte>();
                WebSocketReceiveResult result;
                do
                {
                    result = client.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return false;
                    }

                    builder.Write(new ReadOnlySpan<byte>(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(builder.WrittenMemory);
                if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                    idElement.GetInt32() != requestId)
                {
                    continue;
                }

                return !document.RootElement.TryGetProperty("error", out _);
            }
        }
        catch
        {
            return false;
        }
    }

    private static EdgeDevToolsEvaluation ParseRuntimeEvaluation(JsonElement root)
    {
        if (root.TryGetProperty("error", out var errorElement))
        {
            return EdgeDevToolsEvaluation.Failed(ReadMessage(errorElement) ?? "DevTools runtime error.");
        }

        if (!root.TryGetProperty("result", out var resultElement))
        {
            return EdgeDevToolsEvaluation.Failed("DevTools response missing result.");
        }

        if (resultElement.TryGetProperty("exceptionDetails", out var exceptionElement))
        {
            return EdgeDevToolsEvaluation.Failed(ReadException(exceptionElement) ?? "JavaScript execution failed.");
        }

        if (!resultElement.TryGetProperty("result", out var runtimeResult))
        {
            return EdgeDevToolsEvaluation.Failed("DevTools response missing runtime result.");
        }

        string? valueJson = null;
        string? valueText = null;
        if (runtimeResult.TryGetProperty("value", out var valueElement))
        {
            valueJson = valueElement.GetRawText();
            valueText = valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString()
                : valueJson;
        }
        else if (runtimeResult.TryGetProperty("unserializableValue", out var unserializableElement))
        {
            valueText = unserializableElement.GetString();
        }
        else if (runtimeResult.TryGetProperty("description", out var descriptionElement))
        {
            valueText = descriptionElement.GetString();
        }

        return new EdgeDevToolsEvaluation(true, false, valueJson, valueText, null);
    }

    private static string? ReadMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("message", out var messageElement))
        {
            return messageElement.GetString();
        }

        return null;
    }

    private static string? ReadException(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("exception", out var exceptionElement) &&
            exceptionElement.ValueKind == JsonValueKind.Object)
        {
            if (exceptionElement.TryGetProperty("description", out var descriptionElement))
            {
                return descriptionElement.GetString();
            }

            if (exceptionElement.TryGetProperty("value", out var valueElement))
            {
                return valueElement.GetString();
            }
        }

        return element.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
    }

    private static TimeSpan ClampTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return timeout > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : timeout;
    }
}

internal readonly record struct StartupTargetPrunePlan(
    EdgePageTarget? SelectedTarget,
    IReadOnlyList<EdgePageTarget> TargetsToClose);

internal readonly record struct EdgePageTarget(
    string? TargetId,
    string? Title,
    string? Url,
    string WebSocketDebuggerUrl);

internal sealed record EdgeDevToolsEvaluation(
    bool Success,
    bool TimedOut,
    string? ValueJson,
    string? ValueText,
    string? ErrorText)
{
    public static EdgeDevToolsEvaluation Failed(string errorText) =>
        new(false, false, null, null, errorText);

    public static EdgeDevToolsEvaluation Timeout() =>
        new(false, true, null, null, "JavaScript execution timed out.");
}
