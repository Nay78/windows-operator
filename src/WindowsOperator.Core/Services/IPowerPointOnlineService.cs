using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IPowerPointOnlineService
{
    Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(
        PowerPointOnlineSessionStartRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(
        string sessionId,
        PowerPointOnlineSlideSelectRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(
        string sessionId,
        PowerPointOnlineAddInProbeRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(
        string sessionId,
        PowerPointOnlineSaveWaitRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(
        string sessionId,
        PowerPointOnlineAddInCommandRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(
        string sessionId,
        PowerPointOnlineSessionScreenshotRequest request,
        CancellationToken cancellationToken);

    Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);
}
