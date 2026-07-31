using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace WindowsOperator.Relay;

internal sealed class RelayProxy(
    RelayAuthenticator authenticator,
    RelayRateLimiter rateLimiter,
    IRelayUpstream upstream,
    IOptions<RelayOptions> options,
    ILogger<RelayProxy> logger)
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Content-Length",
        "Set-Cookie",
    };

    public async Task HandleAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        RelayIdentity? identity = null;
        RelayRouteMatch? match = null;
        string? operatorCode = null;
        string? operatorCategory = null;
        bool? operatorRetryable = null;
        string? operatorCorrelationId = null;

        try
        {
            if (!authenticator.TryAuthenticate(context.Request, out identity))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteRelayErrorAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "relay_unauthorized",
                    "Bearer authentication failed.",
                    "Supply a valid relay bearer token.");
                return;
            }

            match = RelayAuthenticator.Match(identity!, context.Request);
            if (match is null)
            {
                await WriteRelayErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "relay_route_forbidden",
                    "Caller is not authorized for this method and route.",
                    "Use a route allowed for this relay caller.");
                return;
            }

            if (!rateLimiter.TryAcquire(identity!.CallerId, match.Rule))
            {
                context.Response.Headers.RetryAfter = "60";
                await WriteRelayErrorAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "relay_rate_limited",
                    "Caller rate limit exceeded.",
                    "Retry after the response Retry-After interval.");
                return;
            }

            using var request = CreateUpstreamRequest(context.Request);
            using var response = await upstream.SendAsync(request, context.RequestAborted);
            var body = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
            ReadOperatorAuditFields(
                response,
                body,
                out operatorCode,
                out operatorCategory,
                out operatorRetryable,
                out operatorCorrelationId);

            if (response.IsSuccessStatusCode &&
                response.Content.Headers.ContentType?.MediaType?.EndsWith("/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                body = RewriteArtifactHrefs(body, options.Value.PublicBaseUrl);
            }

            context.Response.StatusCode = (int)response.StatusCode;
            CopyResponseHeaders(response, context.Response);
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TaskCanceledException)
        {
            await WriteRelayErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                "relay_upstream_unavailable",
                "Windows Operator Host could not be reached.",
                "Verify loopback Host health and retry.");
        }
        finally
        {
            logger.LogInformation(
                "RelayRequest caller={CallerId} method={Method} route={RouteTemplate} status={StatusCode} durationMs={DurationMs} operatorCode={OperatorCode} operatorCategory={OperatorCategory} operatorRetryable={OperatorRetryable} operatorCorrelationId={OperatorCorrelationId}",
                identity?.CallerId ?? "unauthenticated",
                context.Request.Method,
                match?.Rule.RouteTemplate ?? "unmatched",
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                operatorCode,
                operatorCategory,
                operatorRetryable,
                operatorCorrelationId);
        }
    }

    private static HttpRequestMessage CreateUpstreamRequest(HttpRequest request)
    {
        var target = $"{request.PathBase}{request.Path}{request.QueryString}";
        var message = new HttpRequestMessage(new HttpMethod(request.Method), target);
        if (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            message.Content = new StreamContent(request.Body);
            if (MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
            {
                message.Content.Headers.ContentType = contentType;
            }
        }

        CopyRequestHeader(request, message, "Accept");
        CopyRequestHeader(request, message, "Accept-Language");
        CopyRequestHeader(request, message, "If-None-Match");
        CopyRequestHeader(request, message, "X-Correlation-Id");
        return message;
    }

    private static void CopyRequestHeader(
        HttpRequest request,
        HttpRequestMessage message,
        string name)
    {
        if (request.Headers.TryGetValue(name, out var values))
        {
            message.Headers.TryAddWithoutValidation(name, values.ToArray());
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                target.Headers[header.Key] = header.Value.ToArray();
            }
        }
        foreach (var header in source.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                target.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }

    private static byte[] RewriteArtifactHrefs(byte[] body, string publicBaseUrl)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        if (document is null)
        {
            return body;
        }

        var changed = RewriteNode(document, publicBaseUrl.TrimEnd('/'));
        return changed
            ? JsonSerializer.SerializeToUtf8Bytes(document)
            : body;
    }

    private static bool RewriteNode(JsonNode node, string publicBaseUrl)
    {
        var changed = false;
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (string.Equals(property.Key, "href", StringComparison.Ordinal) &&
                    property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var href) &&
                    href.StartsWith("/v1/artifacts/", StringComparison.Ordinal))
                {
                    jsonObject[property.Key] = publicBaseUrl + href;
                    changed = true;
                }
                else if (property.Value is not null)
                {
                    changed |= RewriteNode(property.Value, publicBaseUrl);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    changed |= RewriteNode(item, publicBaseUrl);
                }
            }
        }

        return changed;
    }

    private static void ReadOperatorAuditFields(
        HttpResponseMessage response,
        byte[] body,
        out string? code,
        out string? category,
        out bool? retryable,
        out string? correlationId)
    {
        code = null;
        category = null;
        retryable = null;
        correlationId = null;
        if ((int)response.StatusCode < 400 ||
            response.Content.Headers.ContentType?.MediaType?.EndsWith("/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            code = ReadString(root, "code");
            category = ReadString(root, "category");
            correlationId = ReadString(root, "correlationId");
            if (root.TryGetProperty("retryable", out var retryableElement) &&
                retryableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                retryable = retryableElement.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // Upstream bytes still pass through. Audit omits malformed fields.
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Task WriteRelayErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        string remediation)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new
        {
            code,
            message,
            remediation,
            category = statusCode == StatusCodes.Status429TooManyRequests
                || statusCode == StatusCodes.Status502BadGateway
                ? "unavailable"
                : "permission",
            retryable = statusCode is
                StatusCodes.Status429TooManyRequests or
                StatusCodes.Status502BadGateway,
        });
    }
}
