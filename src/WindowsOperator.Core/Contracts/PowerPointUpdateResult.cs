namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointUpdateResult
{
    public required string JobId { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public IReadOnlyList<PowerPointTargetResult> Targets { get; init; } = Array.Empty<PowerPointTargetResult>();
}
