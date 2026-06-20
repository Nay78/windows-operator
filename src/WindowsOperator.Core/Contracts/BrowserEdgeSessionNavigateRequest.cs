namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionNavigateRequest
{
    public required string Url { get; init; }

    public int WaitSeconds { get; init; } = 2;
}
