using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsOperator.Relay;

namespace WindowsOperator.Relay.Tests;

public sealed class RelayEndpointsTests
{
    private const string Token = "relay-test-token-secret";

    [Fact]
    public async Task AuthenticatedHealthAndCapabilities_AreForwardedWithoutRelayCredentials()
    {
        var upstream = new RecordingUpstream(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var json = RequestPath(request) == "/v1/health"
                ? """{"status":"ok"}"""
                : """{"contractVersion":"1.0.0"}""";
            return JsonResponse(HttpStatusCode.OK, json);
        });
        await using var app = await CreateAppAsync(upstream);
        using var client = app.GetTestClient();
        Authenticate(client);
        client.DefaultRequestHeaders.Add("Cookie", "session=must-not-forward");

        using var health = await client.GetAsync("/v1/health");
        using var capabilities = await client.GetAsync("/v1/capabilities");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", (await ReadJsonAsync(health)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        Assert.Equal(
            "1.0.0",
            (await ReadJsonAsync(capabilities)).GetProperty("contractVersion").GetString());
        Assert.Equal(2, upstream.RequestCount);
    }

    [Fact]
    public async Task MissingOrInvalidAuthentication_IsRejectedBeforeUpstream()
    {
        var upstream = new RecordingUpstream(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        await using var app = await CreateAppAsync(upstream);
        using var client = app.GetTestClient();

        using var missing = await client.GetAsync("/v1/health");
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        using var invalid = await client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(
            "relay_unauthorized",
            (await ReadJsonAsync(missing)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(0, upstream.RequestCount);
    }

    [Fact]
    public async Task CallerMethodAndRouteAllowlist_IsEnforced()
    {
        var upstream = new RecordingUpstream(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        await using var app = await CreateAppAsync(upstream);
        using var client = app.GetTestClient();
        Authenticate(client);

        using var wrongMethod = await client.PostAsync("/v1/health", new StringContent("{}"));
        using var unlisted = await client.GetAsync("/v1/dev/edge/eval");

        Assert.Equal(HttpStatusCode.Forbidden, wrongMethod.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unlisted.StatusCode);
        Assert.Equal(0, upstream.RequestCount);
    }

    [Fact]
    public async Task PerCallerRouteRateLimit_ReturnsRelayOwned429()
    {
        var upstream = new RecordingUpstream(_ => JsonResponse(HttpStatusCode.OK, """{"status":"ok"}"""));
        await using var app = await CreateAppAsync(
            upstream,
            options => Route(options, "GET", "/v1/health").RequestsPerMinute = 1);
        using var client = app.GetTestClient();
        Authenticate(client);

        using var first = await client.GetAsync("/v1/health");
        using var limited = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0"));
        Assert.Equal(
            "relay_rate_limited",
            (await ReadJsonAsync(limited)).GetProperty("code").GetString());
        Assert.Equal(1, upstream.RequestCount);
    }

    [Fact]
    public async Task AuditRedactsAuthorizationAndBodies_WhileOperatorErrorPassesThrough()
    {
        const string bodySecret = "mailbox-body-secret";
        var operatorError =
            $$"""{"code":"mail_unavailable","message":"{{Token}} {{bodySecret}}","remediation":"retry","category":"unavailable","retryable":true,"correlationId":"corr-7"}""";
        var logs = new RecordingLoggerProvider();
        var upstream = new RecordingUpstream(async request =>
        {
            Assert.Equal(
                $$"""{"password":"{{bodySecret}}"}""",
                await request.Content!.ReadAsStringAsync());
            return JsonResponse(HttpStatusCode.ServiceUnavailable, operatorError);
        });
        await using var app = await CreateAppAsync(upstream, loggerProvider: logs);
        using var client = app.GetTestClient();
        Authenticate(client);

        using var response = await client.PostAsync(
            "/v1/mail/messages/search",
            new StringContent(
                $$"""{"password":"{{bodySecret}}"}""",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(operatorError, await response.Content.ReadAsStringAsync());
        var audit = string.Join('\n', logs.Messages);
        Assert.DoesNotContain(Token, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(bodySecret, audit, StringComparison.Ordinal);
        Assert.Contains("mail_unavailable", audit, StringComparison.Ordinal);
        Assert.Contains("corr-7", audit, StringComparison.Ordinal);
        Assert.Contains("/v1/mail/messages/search", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactRoutesStayPrivate_AndRelativeHrefsUsePublicBaseUrl()
    {
        var upstream = new RecordingUpstream(request =>
        {
            if (RequestPath(request) == "/v1/runs/run-1/artifacts")
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    """{"runId":"run-1","artifacts":[{"artifactId":"opaque","href":"/v1/artifacts/opaque","mediaType":"image/png"}]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            }.WithContentType("image/png");
        });
        await using var app = await CreateAppAsync(upstream);
        using var client = app.GetTestClient();

        using var privateResponse = await client.GetAsync("/v1/artifacts/opaque");
        Assert.Equal(HttpStatusCode.Unauthorized, privateResponse.StatusCode);
        Assert.Equal(0, upstream.RequestCount);

        Authenticate(client);
        using var list = await client.GetAsync("/v1/runs/run-1/artifacts");
        var artifact = (await ReadJsonAsync(list))
            .GetProperty("artifacts")[0];
        Assert.Equal(
            "https://relay.example/v1/artifacts/opaque",
            artifact.GetProperty("href").GetString());

        using var download = await client.GetAsync("/v1/artifacts/opaque");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await download.Content.ReadAsByteArrayAsync());
    }

    private static async Task<WebApplication> CreateAppAsync(
        RecordingUpstream upstream,
        Action<RelayOptions>? configure = null,
        RecordingLoggerProvider? loggerProvider = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.Services.AddWindowsOperatorRelay(options =>
        {
            options.UpstreamBaseUrl = "http://127.0.0.1:43117";
            options.PublicBaseUrl = "https://relay.example";
            options.Callers.Add(new RelayCallerOptions
            {
                Id = "consumer-a",
                TokenSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(Token))),
                Routes =
                [
                    new() { Method = "GET", RouteTemplate = "/v1/health", RequestsPerMinute = 10 },
                    new() { Method = "GET", RouteTemplate = "/v1/capabilities", RequestsPerMinute = 10 },
                    new() { Method = "GET", RouteTemplate = "/v1/artifacts/{artifactId}", RequestsPerMinute = 10 },
                    new() { Method = "GET", RouteTemplate = "/v1/runs/{runId}/artifacts", RequestsPerMinute = 10 },
                    new() { Method = "POST", RouteTemplate = "/v1/mail/messages/search", RequestsPerMinute = 10 },
                ],
            });
            configure?.Invoke(options);
        });
        builder.Services.AddSingleton<IRelayUpstream>(upstream);

        var app = builder.Build();
        app.MapWindowsOperatorRelay();
        await app.StartAsync();
        return app;
    }

    private static RelayRouteOptions Route(
        RelayOptions options,
        string method,
        string template) =>
        options.Callers.Single().Routes.Single(route =>
            route.Method == method && route.RouteTemplate == template);

    private static string RequestPath(HttpRequestMessage request) =>
        request.RequestUri!.IsAbsoluteUri
            ? request.RequestUri.AbsolutePath
            : request.RequestUri.OriginalString.Split('?', 2)[0];

    private static void Authenticate(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingUpstream : IRelayUpstream
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public RecordingUpstream(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public RecordingUpstream(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int RequestCount => _requestCount;

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return await _handler(request);
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}

internal static class HttpResponseMessageExtensions
{
    public static HttpResponseMessage WithContentType(
        this HttpResponseMessage response,
        string contentType)
    {
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }
}
