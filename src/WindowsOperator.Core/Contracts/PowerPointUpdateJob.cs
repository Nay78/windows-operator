namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointUpdateJob
{
    public required string JobId { get; init; }

    public string? ExpectedDocumentUrl { get; init; }

    public IReadOnlyList<PowerPointUpdateOperation> Operations { get; init; } = Array.Empty<PowerPointUpdateOperation>();

    public required string RequestedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
