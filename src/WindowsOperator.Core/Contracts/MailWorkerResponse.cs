namespace WindowsOperator.Core.Contracts;

public sealed record MailWorkerResponse
{
    public MailFoldersResult? Folders { get; init; }

    public MailSearchResult? Messages { get; init; }

    public MailDownloadResult? Download { get; init; }

    public OperatorError? Error { get; init; }
}
