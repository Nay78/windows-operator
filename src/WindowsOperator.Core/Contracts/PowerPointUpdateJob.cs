namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointUpdateJob
{
    public required string JobId { get; init; }

    public string? ExpectedDocumentUrl { get; init; }

    public bool DiscoverTargets { get; init; }

    public bool BindNamedTargets { get; init; }

    public bool ValidateOnly { get; init; }

    public IReadOnlyList<PowerPointUpdateOperation> Operations { get; init; } = Array.Empty<PowerPointUpdateOperation>();

    public required string RequestedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
