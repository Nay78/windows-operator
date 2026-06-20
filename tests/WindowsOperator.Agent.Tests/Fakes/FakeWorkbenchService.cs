using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests.Fakes;

internal sealed class FakeWorkbenchService : IWorkbenchService
{
    public Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FakeWindow());

    public Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(FakeDesktopScreenshot(request.Label ?? "foreground"));

    public Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
        BrowserEdgeOpenUrlRequest request,
        CancellationToken cancellationToken)
    {
        var state = new BrowserEdgeSessionStateResult(
            true,
            request.SessionId ?? "edge-session-run",
            request.ProfileMode,
            request.InPrivate,
            true,
            new[] { "session_started" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:07:00Z"),
            777,
            888L,
            "Example - Microsoft Edge",
            request.Url,
            "Example Domain",
            Array.Empty<BrowserEdgeSessionElementRef>(),
            9222,
            "page_ready",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{request.SessionId ?? "edge-session-run"}\state.json");

        return Task.FromResult(new BrowserEdgeOpenUrlResult(
            true,
            state,
            request.Capture ? FakeDesktopScreenshot(request.Label ?? "edge-open") : null,
            request.Capture ? new[] { "session_started", "screenshot_captured" } : new[] { "session_started" },
            Array.Empty<string>()));
    }

    public Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(FakeDesktopScreenshot(request.Label ?? $"edge-session-{sessionId}"));

    public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionStateResult(
            true,
            sessionId,
            BrowserEdgeProfileMode.Work,
            false,
            false,
            new[] { "session_window_closed" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:06:00Z"),
            777,
            888L,
            "Enter code - Microsoft Edge",
            "https://microsoft.com/devicelogin",
            null,
            null,
            9222,
            "session_closed",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    private static WindowRef FakeWindow() =>
        new(
            101,
            202,
            "Fake",
            "FakeWindow",
            new WindowBounds(0, 0, 1200, 900),
            1.0,
            DateTimeOffset.Parse("2026-05-25T12:08:00Z"),
            true,
            false);

    private static DesktopScreenshotResult FakeDesktopScreenshot(string label) =>
        new(
            true,
            new WorkbenchArtifactRef(
                $@"Z:\operator-exchange\runs\workbench-test\screenshots\{label}.png",
                $"runs/workbench-test/screenshots/{label}.png",
                $"/var/lib/windows-server/shared/operator-exchange/runs/workbench-test/screenshots/{label}.png",
                "image/png",
                3),
            FakeWindow(),
            100,
            80,
            "Synthetic",
            DateTimeOffset.Parse("2026-05-25T12:08:01Z"),
            new[] { "target:foreground", "artifact_written" },
            Array.Empty<string>());
}
