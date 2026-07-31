namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowReadResult
{
    public bool Success { get; init; }

    public string EnvId { get; init; } = string.Empty;

    public string FlowId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string FlowJson { get; init; } = "{}";

    public string Source { get; init; } = string.Empty;

    public PowerAutomateMcpFlowSummary Summary { get; init; } = new();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
