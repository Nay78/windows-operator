namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointShapeSelector
{
    public int? Id { get; init; }

    public string? Name { get; init; }

    public IReadOnlyDictionary<string, string>? Tag { get; init; }

    public string? AltText { get; init; }

    public string? TextContains { get; init; }
}
