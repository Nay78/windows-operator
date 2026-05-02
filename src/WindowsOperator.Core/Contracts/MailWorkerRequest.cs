namespace WindowsOperator.Core.Contracts;

using WindowsOperator.Core.Configuration;

public sealed record MailWorkerRequest
{
    public required string Operation { get; init; }

    public MailOptions? Policy { get; init; }

    public MailListFoldersRequest? ListFolders { get; init; }

    public MailSearchRequest? Search { get; init; }

    public MailDownloadRequest? Download { get; init; }
}
