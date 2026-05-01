namespace WindowsOperator.Core.Contracts;

public sealed record MailDownloadRequest
{
    public string? FolderPath { get; init; }

    public IReadOnlyList<string>? MessageIds { get; init; }

    public string? SubjectContains { get; init; }

    public DateTimeOffset? ReceivedAfterUtc { get; init; }

    public DateTimeOffset? ReceivedBeforeUtc { get; init; }

    public int MaxMessages { get; init; } = 25;

    public IReadOnlyList<int>? AttachmentIndexes { get; init; }

    public string? RunId { get; init; }

    public bool DryRun { get; init; }

    public bool SyncBeforeRead { get; init; }

    public int SyncWaitSeconds { get; init; } = 30;
}
