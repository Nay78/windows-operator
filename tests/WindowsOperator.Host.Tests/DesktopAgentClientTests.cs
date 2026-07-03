using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class DesktopAgentClientTests
{
    [Fact]
    public async Task GetHealthAsync_MapsErrorStatusWithoutBodyToOperatorError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.LockedDesktop, failure.Error.Code);
        Assert.Contains("HTTP 500", failure.Error.Details!["detail"]);
    }

    [Fact]
    public async Task GetHealthAsync_PropagatesJsonAgentError()
    {
        var error = OperatorErrors.UnsupportedControl("uia property unsupported");
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(error, options: OperatorJson.SerializerOptions),
        });

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.UnsupportedControl, failure.Error.Code);
        Assert.Equal("uia property unsupported", failure.Error.Details!["detail"]);
    }

    [Fact]
    public async Task GetHealthAsync_MapsEmptySuccessToOperatorError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK));

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.LockedDesktop, failure.Error.Code);
        Assert.Contains("empty response", failure.Error.Details!["detail"]);
    }

    [Fact]
    public async Task GetHealthAsync_MapsInvalidSuccessJsonToOperatorError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json"),
        });

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.LockedDesktop, failure.Error.Code);
        Assert.Contains("invalid or empty response", failure.Error.Details!["detail"]);
    }

    [Fact]
    public async Task CaptureWindowAsync_ForwardsLowercaseFormatQuery()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateScreenshotResult()));
        var client = CreateClient(handler);

        await client.CaptureWindowAsync(123, ScreenshotFormat.Jpeg, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/windows/123/screenshot?format=jpeg", handler.PathAndQuery);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task NavigateEdgeBrowserSessionAsync_EscapesSessionIdAndPostsRequest()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateBrowserSessionResult()));
        var client = CreateClient(handler);

        await client.NavigateEdgeBrowserSessionAsync(
            "session with/slash?#",
            new BrowserEdgeSessionNavigateRequest
            {
                Url = "https://example.test/path?q=1",
                WaitSeconds = 5,
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "/v1/browser/edge/session/session%20with%2Fslash%3F%23/navigate",
            handler.PathAndQuery);
        Assert.Contains("\"url\":\"https://example.test/path?q=1\"", handler.Body);
        Assert.Contains("\"waitSeconds\":5", handler.Body);
    }

    [Fact]
    public async Task GenericSessionEndpoints_EscapeSessionIdAndPostRequests()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateDesktopScreenshotResult("session-shot")));
        var client = CreateClient(handler);

        await client.CaptureSessionScreenshotAsync(
            "session with/slash?#",
            new DesktopScreenshotRequest { Label = "session-shot" },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "/v1/sessions/session%20with%2Fslash%3F%23/screenshot",
            handler.PathAndQuery);
        Assert.Contains("\"label\":\"session-shot\"", handler.Body);
    }

    [Fact]
    public async Task GetSessionAsync_EscapesSessionId()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateWorkbenchSessionResult()));
        var client = CreateClient(handler);

        await client.GetSessionAsync("session with/slash?#", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/sessions/session%20with%2Fslash%3F%23", handler.PathAndQuery);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CleanupSessionAsync_EscapesSessionIdAndPostsNoBody()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateWorkbenchSessionCleanupResult()));
        var client = CreateClient(handler);

        await client.CleanupSessionAsync("session with/slash?#", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/sessions/session%20with%2Fslash%3F%23/cleanup", handler.PathAndQuery);
        Assert.Null(handler.Body);
    }

    private static DesktopAgentClient CreateClient(HttpResponseMessage response) =>
        new(
            new HttpClient(new StaticResponseHandler(response)),
            Options.Create(new DesktopAgentOptions { BaseUrl = "http://127.0.0.1:43119" }));

    private static DesktopAgentClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new DesktopAgentOptions { BaseUrl = "http://127.0.0.1:43119" }));

    private static HttpResponseMessage JsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload, options: OperatorJson.SerializerOptions),
        };

    private static ScreenshotResult CreateScreenshotResult() =>
        new(
            "image/png",
            "aW1hZ2U=",
            1,
            1,
            new WindowBounds(0, 0, 1, 1),
            1,
            DateTimeOffset.UnixEpoch,
            "test",
            1,
            null,
            true);

    private static BrowserEdgeSessionStateResult CreateBrowserSessionResult() =>
        new(
            true,
            "edge-session-run",
            BrowserEdgeProfileMode.Temp,
            true,
            true,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private static DesktopScreenshotResult CreateDesktopScreenshotResult(string label) =>
        new(
            true,
            new WorkbenchArtifactRef(
                $@"Z:\operator-exchange\runs\test\screenshots\{label}.png",
                $"runs/test/screenshots/{label}.png",
                $"/var/lib/windows-server/shared/operator-exchange/runs/test/screenshots/{label}.png",
                "image/png",
                3),
            new WindowRef(
                1,
                2,
                "Window",
                "Class",
                new WindowBounds(0, 0, 1, 1),
                1,
                DateTimeOffset.UnixEpoch,
                true,
                false),
            1,
            1,
            "test",
            DateTimeOffset.UnixEpoch,
            [],
            []);

    private static WorkbenchSessionResult CreateWorkbenchSessionResult() =>
        new(
            true,
            "session",
            "browser.edge",
            true,
            new WorkbenchRunRef(
                "run",
                @"Z:\operator-exchange\runs\run",
                "runs/run",
                "/var/lib/windows-server/shared/operator-exchange/runs/run"),
            [2],
            [1],
            "Window",
            "https://example.com/",
            @"Z:\operator-exchange\runs\run\state.json",
            [],
            [],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private static WorkbenchSessionCleanupResult CreateWorkbenchSessionCleanupResult() =>
        new(
            true,
            "session",
            "browser.edge",
            1,
            1,
            0,
            0,
            1,
            0,
            1,
            0,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticResponseHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }

    private sealed class RecordingResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public RecordingResponseHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responseFactory();
        }
    }
}
