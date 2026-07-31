namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowUpdateRequest
{
    public string? FlowId { get; init; }

    public string? DisplayName { get; init; }

    public string FlowJson { get; init; } = string.Empty;

    public bool ValidateBefore { get; init; }

    public bool ValidateAfter { get; init; }

    public bool Create { get; init; }

    public bool DryRun { get; init; }

    public string? BridgeHost { get; init; }

    public int? BridgePort { get; init; }
}
