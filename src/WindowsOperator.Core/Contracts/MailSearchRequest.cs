namespace WindowsOperator.Core.Contracts;

public sealed record MailSearchRequest
{
    public string? FolderPath { get; init; }

    public string? SubjectContains { get; init; }

    public DateTimeOffset? ReceivedAfterUtc { get; init; }

    public DateTimeOffset? ReceivedBeforeUtc { get; init; }

    public bool? HasAttachments { get; init; }

    public int MaxResults { get; init; } = 25;

    public bool IncludeAttachmentDetails { get; init; } = true;

    public string Freshness { get; init; } = MailFreshness.Auto;
}
