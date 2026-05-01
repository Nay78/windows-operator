using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IMailService
{
    Task<IReadOnlyList<MailFolderRef>> ListFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailMessageRef>> SearchMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> DownloadAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<MailStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<MailSyncResult> SyncAsync(MailSyncRequest request, CancellationToken cancellationToken);

    Task<MailRecoveryResult> RecoverAsync(MailRecoveryRequest request, CancellationToken cancellationToken);
}
