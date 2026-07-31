namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpStartResult
{
    public bool Success { get; init; }

    public int? ProcessId { get; init; }

    [OperatorInternal]
    public string? StatePath { get; init; }

    [OperatorInternal]
    public string? LogPath { get; init; }

    public PowerAutomateMcpStatusResult Status { get; init; } = new();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
