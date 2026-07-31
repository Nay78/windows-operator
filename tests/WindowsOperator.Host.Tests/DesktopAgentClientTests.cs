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

    [Fact]
    public async Task PowerPointOnlineSessionEndpoints_EscapeSessionId_AndPostExpectedPayloads()
    {
        var handler = new RecordingResponseHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/addin/probe", StringComparison.Ordinal) == true
                ? JsonResponse(CreatePowerPointOnlineAddInProbeResult())
                : JsonResponse(CreatePowerPointOnlineSessionResult()));
        var client = CreateClient(handler);

        await client.StartOnlineSessionAsync(
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://example.sharepoint.com/deck.pptx?web=1",
                SessionId = "ppt with/slash?#",
                Capture = true,
            },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions", handler.PathAndQuery);
        Assert.Contains("\"deckUrl\":\"https://example.sharepoint.com/deck.pptx?web=1\"", handler.Body);
        Assert.Contains("\"sessionId\":\"ppt with/slash?#\"", handler.Body);

        await client.GetOnlineSessionAsync("ppt with/slash?#", CancellationToken.None);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23", handler.PathAndQuery);

        await client.SelectOnlineSlideAsync(
            "ppt with/slash?#",
            new PowerPointOnlineSlideSelectRequest { SlideNumber = 4, Capture = false, WaitSeconds = 0 },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/slides/select", handler.PathAndQuery);
        Assert.Contains("\"slideNumber\":4", handler.Body);

        await client.ProbeOnlineAddInAsync(
            "ppt with/slash?#",
            new PowerPointOnlineAddInProbeRequest
            {
                AddInBaseUrl = "https://localhost:3003",
                Capture = true,
                ActivateIfNeeded = true,
                ActivationTimeoutSeconds = 11,
                HostTimeoutSeconds = 7,
            },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/addin/probe", handler.PathAndQuery);
        Assert.Contains("\"addInBaseUrl\":\"https://localhost:3003\"", handler.Body);
        Assert.Contains("\"activateIfNeeded\":true", handler.Body);
        Assert.Contains("\"activationTimeoutSeconds\":11", handler.Body);
        Assert.Contains("\"hostTimeoutSeconds\":7", handler.Body);

        await client.WaitForOnlineSaveAsync(
            "ppt with/slash?#",
            new PowerPointOnlineSaveWaitRequest { TimeoutSeconds = 9, PollSeconds = 2, Capture = true, Label = "save-wait" },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/save/wait", handler.PathAndQuery);
        Assert.Contains("\"timeoutSeconds\":9", handler.Body);
        Assert.Contains("\"pollSeconds\":2", handler.Body);
        Assert.Contains("\"label\":\"save-wait\"", handler.Body);

        await client.PrepareOnlineTemplateAsync(
            "ppt with/slash?#",
            new PowerPointOnlineTemplateRequest { Capture = true, WaitSeconds = 3, AllowDeckMutation = true, Label = "template-prepare" },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/template/prepare", handler.PathAndQuery);
        Assert.Contains("\"capture\":true", handler.Body);
        Assert.Contains("\"waitSeconds\":3", handler.Body);
        Assert.Contains("\"allowDeckMutation\":true", handler.Body);
        Assert.Contains("\"label\":\"template-prepare\"", handler.Body);

        await client.CleanupOnlineTemplateAsync(
            "ppt with/slash?#",
            new PowerPointOnlineTemplateRequest { Capture = false, WaitSeconds = 4, AllowDeckMutation = true, Label = "template-cleanup" },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/template/cleanup", handler.PathAndQuery);
        Assert.Contains("\"capture\":false", handler.Body);
        Assert.Contains("\"waitSeconds\":4", handler.Body);
        Assert.Contains("\"allowDeckMutation\":true", handler.Body);
        Assert.Contains("\"label\":\"template-cleanup\"", handler.Body);

        await client.RunOnlinePendingJobAsync(
            "ppt with/slash?#",
            new PowerPointOnlineAddInCommandRequest { Capture = true, WaitSeconds = 5, Label = "run-pending-job" },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/addin/run-pending-job", handler.PathAndQuery);
        Assert.Contains("\"capture\":true", handler.Body);
        Assert.Contains("\"waitSeconds\":5", handler.Body);
        Assert.Contains("\"label\":\"run-pending-job\"", handler.Body);

        await client.CaptureOnlineSessionScreenshotAsync(
            "ppt with/slash?#",
            new PowerPointOnlineSessionScreenshotRequest { Label = "ppt-online-shot" },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/screenshot", handler.PathAndQuery);
        Assert.Contains("\"label\":\"ppt-online-shot\"", handler.Body);

        await client.CleanupOnlineSessionAsync("ppt with/slash?#", CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/cleanup", handler.PathAndQuery);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task DevAutomationEndpoints_EscapeSessionId_AndPostExpectedPayloads()
    {
        var handler = new RecordingResponseHandler(() => JsonResponse(CreateDevScriptResult()));
        var client = CreateClient(handler);

        await client.RunPowerPointOnlineScriptAsync(
            "ppt with/slash?#",
            new PowerPointDevScriptRequest
            {
                ScriptId = "ppt.dom.snapshot",
                TimeoutSeconds = 7,
                CaptureScreenshot = true,
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/dev/powerpoint/online/sessions/ppt%20with%2Fslash%3F%23/script", handler.PathAndQuery);
        Assert.Contains("\"scriptId\":\"ppt.dom.snapshot\"", handler.Body);
        Assert.Contains("\"timeoutSeconds\":7", handler.Body);
        Assert.Contains("\"captureScreenshot\":true", handler.Body);

        await client.EvaluateEdgeBrowserSessionAsync(
            "ppt with/slash?#",
            new BrowserEdgeDevEvalRequest
            {
                Source = "document.title",
                AllowUnsafeRawJs = true,
                TimeoutSeconds = 3,
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/dev/browser/edge/sessions/ppt%20with%2Fslash%3F%23/eval", handler.PathAndQuery);
        Assert.Contains("\"source\":\"document.title\"", handler.Body);
        Assert.Contains("\"allowUnsafeRawJs\":true", handler.Body);
        Assert.Contains("\"timeoutSeconds\":3", handler.Body);
    }

    [Fact]
    public async Task PowerAutomateMcpEndpoints_ForwardExpectedPayloads()
    {
        var handler = new RecordingResponseHandler(request =>
            request.Method == HttpMethod.Get
                ? JsonResponse(CreatePowerAutomateMcpStatusResult())
                : request.RequestUri?.AbsolutePath.EndsWith("/start", StringComparison.Ordinal) == true
                    ? JsonResponse(new PowerAutomateMcpStartResult { Success = true, Status = CreatePowerAutomateMcpStatusResult() })
                    : request.RequestUri?.AbsolutePath.EndsWith("/cleanup", StringComparison.Ordinal) == true
                        ? JsonResponse(new PowerAutomateMcpEdgeCleanupResult { Success = true, Alive = false })
                    : request.RequestUri?.AbsolutePath.EndsWith("/flows/read", StringComparison.Ordinal) == true
                        ? JsonResponse(new PowerAutomateMcpFlowReadResult { Success = true, FlowId = "flow-1", EnvId = "env-1" })
                    : request.RequestUri?.AbsolutePath.EndsWith("/flows/update", StringComparison.Ordinal) == true
                        ? JsonResponse(new PowerAutomateMcpFlowUpdateResult { Success = true, Status = PowerAutomateMcpFlowUpdateStatus.Succeeded })
                    : JsonResponse(new PowerAutomateMcpEdgeResult { Success = true, Url = "https://make.powerautomate.com/" }));
        var client = CreateClient(handler);

        await client.GetStatusAsync(CancellationToken.None);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/status", handler.PathAndQuery);
        Assert.Null(handler.Body);

        await client.StartBridgeAsync(
            new PowerAutomateMcpStartRequest
            {
                BridgePort = 17373,
                DryRun = true,
            },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/start", handler.PathAndQuery);
        Assert.Contains("\"bridgePort\":17373", handler.Body);
        Assert.Contains("\"dryRun\":true", handler.Body);

        await client.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                DryRun = true,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/edge", handler.PathAndQuery);
        Assert.DoesNotContain("allowTokenCapture", handler.Body);
        Assert.Contains("\"dryRun\":true", handler.Body);
        Assert.Contains("\"profileMode\":\"temp\"", handler.Body);

        await client.CleanupEdgeAsync(CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/edge/cleanup", handler.PathAndQuery);
        Assert.Null(handler.Body);

        await client.ReadFlowAsync(new PowerAutomateMcpFlowReadRequest { FlowId = "flow-1" }, CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/flows/read", handler.PathAndQuery);
        Assert.Contains("\"flowId\":\"flow-1\"", handler.Body);

        await client.UpdateFlowAsync(
            new PowerAutomateMcpFlowUpdateRequest
            {
                FlowId = "flow-1",
                FlowJson = "{\"connectionReferences\":{},\"definition\":{}}",
                DryRun = true,
            },
            CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/power-automate/mcp/flows/update", handler.PathAndQuery);
        Assert.Contains("connectionReferences", handler.Body);
        Assert.Contains("\"dryRun\":true", handler.Body);
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

    private static PowerPointOnlineSessionResult CreatePowerPointOnlineSessionResult() =>
        new()
        {
            Success = true,
            SessionId = "ppt-session",
            Status = PowerPointOnlineSessionStatus.Ready,
            DeckUrl = "https://example.sharepoint.com/deck.pptx?web=1",
            CanonicalUrl = "https://example.sharepoint.com/deck.pptx?web=1",
            CurrentUrl = "https://example.sharepoint.com/deck.pptx?web=1",
            CurrentTitle = "Deck - PowerPoint",
            BrowserSessionId = "ppt-session",
            Hwnd = 888,
            ArtifactRoot = new WorkbenchRunRef(
                "run",
                @"Z:\operator-exchange\runs\run",
                "runs/run",
                "/var/lib/windows-server/shared/operator-exchange/runs/run"),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "session_started" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static PowerPointOnlineAddInProbeResult CreatePowerPointOnlineAddInProbeResult() =>
        new()
        {
            Success = true,
            Status = PowerPointOnlineAddInProbeStatus.Ready,
            Session = CreatePowerPointOnlineSessionResult(),
            AddInBaseUrl = "https://localhost:3003",
            HostReachable = true,
            TaskPaneUrl = "https://localhost:3003/taskpane.html",
            TaskPaneReachable = true,
            ManifestUrl = "https://localhost:3003/manifest.xml",
            ManifestReachable = true,
            ManifestId = "6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7",
            ManifestVersion = "1.0.0.0",
            ManifestDisplayName = "Windows Operator PowerPoint",
            ManifestSourceLocation = "https://localhost:3003/taskpane.html",
            TaskPaneVisible = true,
            CommandVisible = true,
            MatchedElements = Array.Empty<UiElementRef>(),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "addin_taskpane_probe_ok", "addin_manifest_probe_ok", "addin_host_probe_ok" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static DevScriptResult CreateDevScriptResult() =>
        new()
        {
            Success = true,
            Status = DevScriptStatus.Succeeded,
            SessionId = "ppt-session",
            ScriptId = "ppt.dom.snapshot",
            Target = "powerpoint-page",
            ResultJson = "{\"ok\":true}",
            Actions = new[] { "dev_script_evaluated" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<string>(),
            ObservedAtUtc = DateTimeOffset.UnixEpoch,
            EvidencePath = "/host/runs/dev/result.json",
        };

    private static PowerAutomateMcpStatusResult CreatePowerAutomateMcpStatusResult() =>
        new()
        {
            Success = true,
            BridgeListening = true,
            BridgeHealthy = true,
            ContextAvailable = true,
            BridgeVersion = "0.4.1",
            EdgeSessionAlive = true,
            EdgeProcessId = 5678,
            EdgeHwnd = 4321,
            EdgeIdleTtlSeconds = 900,
            Actions = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<string>(),
            ObservedAtUtc = DateTimeOffset.UnixEpoch,
        };

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
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingResponseHandler(Func<HttpResponseMessage> responseFactory)
            : this(_ => responseFactory())
        {
        }

        public RecordingResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
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

            return _responseFactory(request);
        }
    }
}
