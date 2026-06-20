namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeOpenUrlRequest
{
    public required string Url { get; init; }

    public string? SessionId { get; init; }

    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;

    public int WaitSeconds { get; init; } = 12;

    public bool Capture { get; init; }

    public string? Label { get; init; }

    public string? RunId { get; init; }

    public bool InPrivate { get; init; }
}
