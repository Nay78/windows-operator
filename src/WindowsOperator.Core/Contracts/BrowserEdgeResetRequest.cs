namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeResetRequest
{
    public bool DryRun { get; init; }
}
