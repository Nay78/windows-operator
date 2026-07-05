namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineSessionStartRequest
{
    public required string DeckUrl { get; init; }

    public string? SessionId { get; init; }

    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;

    public bool Capture { get; init; } = true;

    public int WaitSeconds { get; init; } = 30;

    public string? RunId { get; init; }

    public string? Label { get; init; }
}
