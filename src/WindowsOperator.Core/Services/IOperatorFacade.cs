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

    Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken);

    Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MailFolderRef>> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailMessageRef>> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken);

    Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken);

    Task<MailSyncResult> SyncMailAsync(MailSyncRequest request, CancellationToken cancellationToken);

    Task<MailRecoveryResult> RecoverMailAsync(MailRecoveryRequest request, CancellationToken cancellationToken);
}
