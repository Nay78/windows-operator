namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowReadRequest
{
    public string? FlowId { get; init; }

    public string? BridgeHost { get; init; }

    public int? BridgePort { get; init; }
}
