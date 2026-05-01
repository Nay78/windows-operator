namespace WindowsOperator.Core.Contracts;

public sealed record MailWorkerRequest
{
    public required string Operation { get; init; }

    public MailListFoldersRequest? ListFolders { get; init; }

    public MailSearchRequest? Search { get; init; }

    public MailDownloadRequest? Download { get; init; }

    public MailSyncRequest? Sync { get; init; }
}
