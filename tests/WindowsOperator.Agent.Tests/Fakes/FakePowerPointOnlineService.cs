using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests.Fakes;

internal sealed class FakePowerPointOnlineService : IPowerPointOnlineService
{
    public Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(
        PowerPointOnlineSessionStartRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(request.SessionId ?? "ppt-online-session", request.DeckUrl, PowerPointOnlineSessionStatus.Ready, "session_started"));

    public Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "session_state_observed"));

    public Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(
        string sessionId,
        PowerPointOnlineSlideSelectRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, $"slide_selected:{request.SlideNumber}"));

    public Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(
        string sessionId,
        PowerPointOnlineAddInProbeRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new PowerPointOnlineAddInProbeResult
            {
                Success = true,
                Status = PowerPointOnlineAddInProbeStatus.Ready,
                Session = Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "addin_probe"),
                AddInBaseUrl = request.AddInBaseUrl,
                HostReachable = true,
                TaskPaneUrl = $"{request.AddInBaseUrl.TrimEnd('/')}/taskpane.html",
                TaskPaneReachable = true,
                ManifestUrl = $"{request.AddInBaseUrl.TrimEnd('/')}/manifest.xml",
                ManifestReachable = true,
                ManifestId = "6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7",
                ManifestVersion = "1.0.0.0",
                ManifestDisplayName = "Windows Operator PowerPoint",
                ManifestSourceLocation = $"{request.AddInBaseUrl.TrimEnd('/')}/taskpane.html",
                TaskPaneVisible = true,
                CommandVisible = true,
                MatchedElements = Array.Empty<UiElementRef>(),
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = new[] { "addin_taskpane_probe_ok", "addin_manifest_probe_ok", "addin_host_probe_ok", "addin_taskpane_visible", "addin_command_visible" },
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:03Z"),
            });

    public Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(
        string sessionId,
        PowerPointOnlineSaveWaitRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Session(
                sessionId,
                "https://example.sharepoint.com/deck.pptx?web=1",
                PowerPointOnlineSessionStatus.Ready,
                "save_wait_observed:saved",
                saveState: "saved"));

    public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "template_prepare_click_dispatched", includeEvidence: request.Capture, label: request.Label ?? "template-prepare"));

    public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "template_cleanup_click_dispatched", includeEvidence: request.Capture, label: request.Label ?? "template-cleanup"));

    public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(
        string sessionId,
        PowerPointOnlineAddInCommandRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "addin_run_pending_job_click_dispatched", includeEvidence: request.Capture, label: request.Label ?? "run-pending-job"));

    public Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(
        string sessionId,
        PowerPointOnlineSessionScreenshotRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Ready, "screenshot_requested", includeEvidence: true, label: request.Label ?? "powerpoint-online"));

    public Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Session(sessionId, "https://example.sharepoint.com/deck.pptx?web=1", PowerPointOnlineSessionStatus.Closed, "powerpoint_online_cleanup"));

    private static PowerPointOnlineSessionResult Session(
        string sessionId,
        string deckUrl,
        PowerPointOnlineSessionStatus status,
        string action,
        bool includeEvidence = false,
        string label = "powerpoint-online",
        string? saveState = null)
    {
        var evidence = includeEvidence
            ? new[]
            {
                new DesktopScreenshotResult(
                    true,
                    new WorkbenchArtifactRef(
                        $@"Z:\operator-exchange\runs\workbench-test\screenshots\{label}.png",
                        $"runs/workbench-test/screenshots/{label}.png",
                        $"/var/lib/windows-server/shared/operator-exchange/runs/workbench-test/screenshots/{label}.png",
                        "image/png",
                        3,
                        ArtifactRef.Create($"runs/workbench-test/screenshots/{label}.png", "image/png", 3)),
                    new WindowRef(
                        888,
                        777,
                        "Deck - PowerPoint",
                        "Chrome_WidgetWin_1",
                        new WindowBounds(0, 0, 1200, 900),
                        1.0,
                        DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
                        true,
                        false),
                    1200,
                    900,
                    "Synthetic",
                    DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
                    new[] { "artifact_written" },
                    Array.Empty<string>()),
            }
            : Array.Empty<DesktopScreenshotResult>();

        return new PowerPointOnlineSessionResult
        {
            Success = status is PowerPointOnlineSessionStatus.Ready or PowerPointOnlineSessionStatus.Closed,
            SessionId = sessionId,
            Status = status,
            DeckUrl = deckUrl,
            CanonicalUrl = deckUrl,
            CurrentUrl = deckUrl,
            CurrentTitle = "Deck - PowerPoint",
            SaveState = saveState,
            BrowserSessionId = sessionId,
            Hwnd = 888,
            ArtifactRoot = new WorkbenchRunRef(
                "workbench-test",
                @"Z:\operator-exchange\runs\workbench-test",
                "runs/workbench-test",
                "/var/lib/windows-server/shared/operator-exchange/runs/workbench-test"),
            Evidence = evidence,
            Actions = new[] { action },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:02Z"),
        };
    }
}
