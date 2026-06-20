namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionDomFillRequest
{
    public string? Selector { get; init; }

    public string? VisibleText { get; init; }

    public string? LabelText { get; init; }

    public required string Value { get; init; }

    public int MatchIndex { get; init; }

    public int TimeoutSeconds { get; init; } = 10;
}
