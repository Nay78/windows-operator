using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IOperatorFacade
{
    Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken);

    Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken);

    Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken);

    Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken);

    Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken);

    Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken);

    Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken);

    Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken);

    Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken);

    Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken);

    Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken);

    Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken);

    Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken);
}
