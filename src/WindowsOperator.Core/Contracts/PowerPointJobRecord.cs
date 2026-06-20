namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointJobRecord
{
    public required string JobId { get; init; }

    public required string Status { get; init; }

    public required PowerPointUpdateJob Job { get; init; }

    public PowerPointUpdateResult? Result { get; init; }

    public PowerPointUpdateError? Error { get; init; }

    public string? ClaimedBy { get; init; }

    public string? ClaimedDocumentUrl { get; init; }

    public DateTimeOffset EnqueuedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClaimedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }
}
