namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointSlideSelector
{
    public int? SlideId { get; init; }

    public int? Index { get; init; }

    public string? Title { get; init; }

    public IReadOnlyDictionary<string, string>? Tag { get; init; }
}
