using Microsoft.Extensions.Options;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class WorkbenchServiceTests
{
    [Fact]
    public async Task CaptureDesktopScreenshotAsync_TargetModes_WriteArtifactRefs()
    {
        using var env = new ExchangeRootScope();
        var windows = new FakeWindowCatalogService();
        var screenshots = new FakeScreenshotService();
        var service = new WorkbenchService(windows, screenshots, new FakeEdgeBrowserService(), env.Options);

        var foreground = await service.CaptureDesktopScreenshotAsync(
            new DesktopScreenshotRequest { Target = "foreground", RunId = "run-1", Label = "front" },
            CancellationToken.None);
        var hwnd = await service.CaptureDesktopScreenshotAsync(
            new DesktopScreenshotRequest { Target = "hwnd", Hwnd = 22, RunId = "run-1", Label = "hwnd" },
            CancellationToken.None);
        var title = await service.CaptureDesktopScreenshotAsync(
            new DesktopScreenshotRequest { Target = "title", TitleContains = "target", RunId = "run-1", Label = "title" },
            CancellationToken.None);

        Assert.Equal(11, foreground.Window.Hwnd);
        Assert.Equal(22, hwnd.Window.Hwnd);
        Assert.Equal(22, title.Window.Hwnd);
        Assert.Equal("runs/run-1/screenshots/front.png", foreground.Artifact.RelativePath);
        Assert.Equal("/host-exchange/runs/run-1/screenshots/front.png", foreground.Artifact.HostPath);
        Assert.Equal(3, foreground.Artifact.Bytes);
        Assert.True(File.Exists(foreground.Artifact.Path));
        Assert.Equal(22, screenshots.LastWindow?.Hwnd);
    }

    [Fact]
    public async Task CaptureDesktopScreenshotAsync_InvalidTargets_ReturnStableErrors()
    {
        using var env = new ExchangeRootScope();
        var service = new WorkbenchService(
            new FakeWindowCatalogService(),
            new FakeScreenshotService(),
            new FakeEdgeBrowserService(),
            env.Options);

        var missingTitle = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.CaptureDesktopScreenshotAsync(
                new DesktopScreenshotRequest { Target = "title" },
                CancellationToken.None));
        var unsupported = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.CaptureDesktopScreenshotAsync(
                new DesktopScreenshotRequest { Target = "desktop" },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.WindowNotFound, missingTitle.Error.Code);
        Assert.Equal(ErrorCodes.UnsupportedControl, unsupported.Error.Code);
    }

    [Fact]
    public async Task OpenEdgeUrlAsync_StartsOwnedSessionAndCapturesOptionalScreenshot()
    {
        using var env = new ExchangeRootScope();
        var edge = new FakeEdgeBrowserService();
        var service = new WorkbenchService(
            new FakeWindowCatalogService(),
            new FakeScreenshotService(),
            edge,
            env.Options);

        var result = await service.OpenEdgeUrlAsync(
            new BrowserEdgeOpenUrlRequest
            {
                Url = "https://example.com",
                SessionId = "example",
                ProfileMode = BrowserEdgeProfileMode.Work,
                WaitSeconds = 7,
                Capture = true,
                RunId = "run-edge",
                Label = "edge-open",
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://example.com", edge.LastStart?.StartUrl);
        Assert.Equal("example", edge.LastStart?.SessionId);
        Assert.Equal(BrowserEdgeProfileMode.Work, edge.LastStart?.ProfileMode);
        Assert.Equal(7, edge.LastStart?.PageLoadSeconds);
        Assert.NotNull(result.Screenshot);
        Assert.Equal("runs/run-edge/screenshots/edge-open.png", result.Screenshot!.Artifact.RelativePath);
    }

    [Fact]
    public async Task OpenEdgeUrlAsync_ReturnsUnderlyingSessionSuccess()
    {
        using var env = new ExchangeRootScope();
        var edge = new FakeEdgeBrowserService
        {
            NextSuccess = false,
            NextErrors = new[] { "edge_failed" },
        };
        var service = new WorkbenchService(
            new FakeWindowCatalogService(),
            new FakeScreenshotService(),
            edge,
            env.Options);

        var result = await service.OpenEdgeUrlAsync(
            new BrowserEdgeOpenUrlRequest
            {
                Url = "https://example.com",
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.State.Success);
        Assert.Contains("edge_failed", result.Warnings);
    }

    [Fact]
    public async Task EdgeSessionScreenshotAndCleanup_UseStoredSessionHwndAndClose()
    {
        using var env = new ExchangeRootScope();
        var edge = new FakeEdgeBrowserService();
        var service = new WorkbenchService(
            new FakeWindowCatalogService(),
            new FakeScreenshotService(),
            edge,
            env.Options);

        var screenshot = await service.CaptureEdgeSessionScreenshotAsync(
            "example",
            new DesktopScreenshotRequest { RunId = "run-edge", Label = "edge-session" },
            CancellationToken.None);
        var cleanup = await service.CleanupEdgeSessionAsync("example", CancellationToken.None);

        Assert.Equal(22, screenshot.Window.Hwnd);
        Assert.Equal("runs/run-edge/screenshots/edge-session.png", screenshot.Artifact.RelativePath);
        Assert.Equal("example", edge.LastStateSessionId);
        Assert.Equal("example", edge.LastClosedSessionId);
        Assert.False(cleanup.IsAlive);
    }

    private sealed class ExchangeRootScope : IDisposable
    {
        public ExchangeRootScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "windows-operator-workbench-tests", Guid.NewGuid().ToString("N"));
            Options = Microsoft.Extensions.Options.Options.Create(
                new WorkbenchOptions
                {
                    ExchangeRoot = Root,
                    HostExchangeRoot = "/host-exchange",
                });
        }

        public string Root { get; }

        public IOptions<WorkbenchOptions> Options { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeWindowCatalogService : IWindowCatalogService
    {
        private readonly IReadOnlyList<WindowRef> _windows = new[]
        {
            new WindowRef(11, 101, "Foreground App", "App", new WindowBounds(0, 0, 400, 300), 1, DateTimeOffset.Parse("2026-06-16T00:00:00Z"), true, false),
            new WindowRef(22, 202, "Target App", "App", new WindowBounds(10, 10, 500, 400), 1, DateTimeOffset.Parse("2026-06-16T00:00:00Z"), false, false),
        };

        public Task<IReadOnlyList<WindowRef>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_windows);

        public Task<WindowRef?> GetAsync(long hwnd, CancellationToken cancellationToken) =>
            Task.FromResult(_windows.FirstOrDefault(window => window.Hwnd == hwnd));
    }

    private sealed class FakeScreenshotService : IScreenshotService
    {
        public WindowRef? LastWindow { get; private set; }

        public Task<ScreenshotResult> CaptureAsync(
            WindowRef window,
            ScreenshotFormat? format,
            CancellationToken cancellationToken)
        {
            LastWindow = window;
            return Task.FromResult(new ScreenshotResult(
                "image/png",
                Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                4,
                3,
                window.Bounds,
                window.DpiScale,
                DateTimeOffset.Parse("2026-06-16T00:00:01Z"),
                "Synthetic",
                1600,
                null,
                true));
        }
    }

    private sealed class FakeEdgeBrowserService : IEdgeBrowserService
    {
        public BrowserEdgeSessionStartRequest? LastStart { get; private set; }

        public string? LastStateSessionId { get; private set; }

        public string? LastClosedSessionId { get; private set; }

        public bool NextSuccess { get; init; } = true;

        public IReadOnlyList<string> NextErrors { get; init; } = Array.Empty<string>();

        public Task<BrowserEdgeResetResult> ResetAsync(
            BrowserEdgeResetRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> StartSessionAsync(
            BrowserEdgeSessionStartRequest request,
            CancellationToken cancellationToken)
        {
            LastStart = request;
            return Task.FromResult(State(request.SessionId ?? "edge-session", request.StartUrl, isAlive: true));
        }

        public Task<BrowserEdgeSessionStateResult> GetSessionStateAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            LastStateSessionId = sessionId;
            return Task.FromResult(State(sessionId, "https://example.com", isAlive: true));
        }

        public Task<BrowserEdgeSessionStateResult> NavigateSessionAsync(
            string sessionId,
            BrowserEdgeSessionNavigateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionDomActionResult> ClickDomAsync(
            string sessionId,
            BrowserEdgeSessionDomClickRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionDomActionResult> FillDomAsync(
            string sessionId,
            BrowserEdgeSessionDomFillRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> CloseSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            LastClosedSessionId = sessionId;
            return Task.FromResult(State(sessionId, "https://example.com", isAlive: false));
        }

        private BrowserEdgeSessionStateResult State(string sessionId, string url, bool isAlive) =>
            new(
                NextSuccess,
                sessionId,
                BrowserEdgeProfileMode.Work,
                false,
                isAlive,
                isAlive ? new[] { "session_started" } : new[] { "session_window_closed" },
                NextErrors,
                DateTimeOffset.Parse("2026-06-16T00:00:02Z"),
                202,
                22,
                "Target App",
                url,
                "Example",
                Array.Empty<BrowserEdgeSessionElementRef>(),
                9222,
                isAlive ? "page_ready" : "session_closed",
                $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json");
    }
}
