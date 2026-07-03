using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IWorkbenchService
{
    Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken);

    Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
        BrowserEdgeOpenUrlRequest request,
        CancellationToken cancellationToken);

    Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<WorkbenchSessionResult> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken);

    Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);
}
