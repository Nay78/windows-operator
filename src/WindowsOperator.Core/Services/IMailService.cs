using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IMailService
{
    Task<MailFoldersResult> ListFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken);

    Task<MailSearchResult> SearchMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> DownloadAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken);

    Task<MailDownloadResult> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<MailStatusResult> GetStatusAsync(CancellationToken cancellationToken);
}
