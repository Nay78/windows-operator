namespace WindowsOperator.Core.Contracts;

public sealed record MailWorkerResponse
{
    public IReadOnlyList<MailFolderRef>? Folders { get; init; }

    public IReadOnlyList<MailMessageRef>? Messages { get; init; }

    public MailDownloadResult? Download { get; init; }

    public MailSyncResult? Sync { get; init; }

    public OperatorError? Error { get; init; }
}
