namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointInspectRequest
{
    public string? PresentationUrl { get; init; }

    public string? PresentationPath { get; init; }

    public string? ExchangePath { get; init; }

    public bool IncludeText { get; init; }

    public bool IncludeHidden { get; init; }
}
