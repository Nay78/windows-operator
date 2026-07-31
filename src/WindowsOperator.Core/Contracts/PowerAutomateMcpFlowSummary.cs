namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowSummary
{
    public int TriggerCount { get; init; }

    public int ActionCount { get; init; }

    public IReadOnlyList<string> TriggerNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ActionNames { get; init; } = Array.Empty<string>();
}
