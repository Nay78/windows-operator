namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpEdgeCleanupResult
{
    public bool Success { get; init; }

    public bool Alive { get; init; }

    public int? ProcessId { get; init; }

    public long? Hwnd { get; init; }

    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;

    public string? Url { get; init; }

    [OperatorInternal]
    public string? ExtensionPath { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? LastUsedAtUtc { get; init; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    public DateTimeOffset? ClosedAtUtc { get; init; }

    public int TtlSeconds { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
