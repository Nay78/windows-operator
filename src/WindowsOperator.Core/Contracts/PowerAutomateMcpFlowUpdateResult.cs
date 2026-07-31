namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowUpdateResult
{
    public bool Success { get; init; }

    public PowerAutomateMcpFlowUpdateStatus Status { get; init; }

    public bool DryRun { get; init; }

    public PowerAutomateMcpFlowReadResult Before { get; init; } = new();

    public PowerAutomateMcpFlowReadResult After { get; init; } = new();

    public PowerAutomateMcpFlowValidationResult? BeforeValidation { get; init; }

    public PowerAutomateMcpFlowValidationResult? AfterValidation { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
