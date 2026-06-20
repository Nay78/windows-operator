namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionStartRequest
{
    public string? SessionId { get; init; }

    public string StartUrl { get; init; } = "https://microsoft.com/devicelogin";

    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Temp;

    public int PageLoadSeconds { get; init; } = 4;

    public bool InPrivate { get; init; }

    public bool DryRun { get; init; }
}
