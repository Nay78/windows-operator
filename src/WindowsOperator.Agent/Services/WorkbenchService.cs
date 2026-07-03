using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class WorkbenchService : IWorkbenchService
{
    private readonly IEdgeBrowserService _edgeBrowserService;
    private readonly OwnedSessionRegistry _sessions;
    private readonly WorkbenchRunStore _runs;
    private readonly IScreenshotService _screenshotService;
    private readonly IWindowCatalogService _windowCatalogService;

    public WorkbenchService(
        IWindowCatalogService windowCatalogService,
        IScreenshotService screenshotService,
        IEdgeBrowserService edgeBrowserService,
        WorkbenchRunStore runs,
        OwnedSessionRegistry sessions)
    {
        _windowCatalogService = windowCatalogService;
        _screenshotService = screenshotService;
        _edgeBrowserService = edgeBrowserService;
        _runs = runs;
        _sessions = sessions;
    }

    public async Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken)
    {
        var windows = await _windowCatalogService.ListAsync(cancellationToken);
        var foreground = windows.FirstOrDefault(window => window.IsForeground) ?? windows.FirstOrDefault();
        if (foreground is not null)
        {
            return foreground;
        }

        throw new OperatorFailureException(OperatorErrors.WindowNotFound("foreground"));
    }

    public async Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new DesktopScreenshotRequest();
        var target = NormalizeTarget(request.Target);
        var window = await ResolveWindowAsync(target, request, cancellationToken);
        var screenshot = await _screenshotService.CaptureAsync(window, request.Format, cancellationToken);
        var bytes = Convert.FromBase64String(screenshot.ImageBase64);
        var stored = _runs.WriteArtifact(
            bytes,
            screenshot.MediaType,
            request.RunId,
            request.Label,
            DefaultLabel(target, window));
        var windows = await _windowCatalogService.ListAsync(cancellationToken);
        _runs.WriteWindowsSnapshot(stored.Run, windows);

        return new DesktopScreenshotResult(
            true,
            stored.Artifact,
            window,
            screenshot.PixelWidth,
            screenshot.PixelHeight,
            screenshot.Backend,
            screenshot.CapturedAtUtc,
            new[] { $"target:{target}", "artifact_written" },
            Array.Empty<string>());
    }

    public async Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
        BrowserEdgeOpenUrlRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new BrowserEdgeOpenUrlRequest { Url = string.Empty };
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new OperatorFailureException(OperatorErrors.AuthUnavailable("Edge URL is required."));
        }

        var state = await _edgeBrowserService.StartSessionAsync(
            new BrowserEdgeSessionStartRequest
            {
                SessionId = request.SessionId,
                StartUrl = request.Url,
                ProfileMode = request.ProfileMode,
                PageLoadSeconds = request.WaitSeconds,
                InPrivate = request.InPrivate,
            },
            cancellationToken);
        var session = _sessions.UpsertEdgeSession(state, request.RunId ?? request.SessionId);

        DesktopScreenshotResult? screenshot = null;
        if (request.Capture)
        {
            if (state.Hwnd is null)
            {
                throw new OperatorFailureException(
                    OperatorErrors.WindowNotFound($"Edge session has no hwnd: {state.SessionId}"));
            }

            screenshot = await CaptureDesktopScreenshotAsync(
                new DesktopScreenshotRequest
                {
                    Target = "hwnd",
                    Hwnd = state.Hwnd.Value,
                    RunId = session.ArtifactRoot.RunId,
                    Label = request.Label ?? $"edge-open-{state.SessionId}",
                    Format = ScreenshotFormat.Png,
                },
                cancellationToken);
        }

        var actions = state.Actions.Concat(request.Capture ? new[] { "screenshot_captured" } : Array.Empty<string>()).ToArray();
        return new BrowserEdgeOpenUrlResult(state.Success, state, screenshot, actions, state.Errors);
    }

    public async Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        var state = await _edgeBrowserService.GetSessionStateAsync(sessionId, cancellationToken);
        if (state.Hwnd is null)
        {
            throw new OperatorFailureException(OperatorErrors.WindowNotFound($"Edge session has no hwnd: {sessionId}"));
        }
        var session = _sessions.UpsertEdgeSession(state, request?.RunId);

        request ??= new DesktopScreenshotRequest();
        return await CaptureDesktopScreenshotAsync(
            request with
            {
                Target = "hwnd",
                Hwnd = state.Hwnd.Value,
                RunId = request.RunId ?? session.ArtifactRoot.RunId,
                Label = request.Label ?? $"edge-session-{sessionId}",
            },
            cancellationToken);
    }

    public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        CleanupEdgeSessionCoreAsync(sessionId, cancellationToken);

    public Task<WorkbenchSessionResult> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetSession(sessionId));

    public async Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        var session = _sessions.GetSession(sessionId);
        var hwnd = session.Hwnds.FirstOrDefault();
        if (hwnd == 0)
        {
            throw new OperatorFailureException(OperatorErrors.WindowNotFound($"Workbench session has no hwnd: {sessionId}"));
        }

        request ??= new DesktopScreenshotRequest();
        return await CaptureDesktopScreenshotAsync(
            request with
            {
                Target = "hwnd",
                Hwnd = hwnd,
                RunId = request.RunId ?? session.ArtifactRoot.RunId,
                Label = request.Label ?? $"session-{sessionId}",
            },
            cancellationToken);
    }

    public Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.CleanupSession(sessionId));

    private async Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionCoreAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var state = await _edgeBrowserService.CloseSessionAsync(sessionId, cancellationToken);
        _sessions.UpsertEdgeSession(state, null);
        return state;
    }

    private async Task<WindowRef> ResolveWindowAsync(
        string target,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        if (target == "foreground")
        {
            return await GetForegroundWindowAsync(cancellationToken);
        }

        if (target == "hwnd")
        {
            if (request.Hwnd is null)
            {
                throw new OperatorFailureException(OperatorErrors.WindowNotFound("hwnd is required for target=hwnd"));
            }

            var window = await _windowCatalogService.GetAsync(request.Hwnd.Value, cancellationToken);
            return window ?? throw new OperatorFailureException(OperatorErrors.WindowNotFound($"hwnd={request.Hwnd.Value}"));
        }

        if (target == "title")
        {
            if (string.IsNullOrWhiteSpace(request.TitleContains))
            {
                throw new OperatorFailureException(
                    OperatorErrors.WindowNotFound("titleContains is required for target=title"));
            }

            var windows = await _windowCatalogService.ListAsync(cancellationToken);
            var window = windows.FirstOrDefault(candidate =>
                candidate.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase));
            return window ?? throw new OperatorFailureException(
                OperatorErrors.WindowNotFound($"titleContains={request.TitleContains}"));
        }

        throw new OperatorFailureException(
            OperatorErrors.UnsupportedControl($"Unsupported desktop screenshot target: {request.Target}"));
    }

    private static string NormalizeTarget(string? target) =>
        string.IsNullOrWhiteSpace(target) ? "foreground" : target.Trim().ToLowerInvariant();

    private static string DefaultLabel(string target, WindowRef window) =>
        target == "foreground" ? "foreground" : $"window-{window.Hwnd}";
}
