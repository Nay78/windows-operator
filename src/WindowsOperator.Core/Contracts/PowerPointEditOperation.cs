namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditOperation
{
    public string Id { get; init; } = "";

    public string Op { get; init; } = "";

    public PowerPointEditTarget? Target { get; init; }

    public string? Find { get; init; }

    public string? Value { get; init; }

    public string? ImagePath { get; init; }

    public string? Fit { get; init; }

    public string? FillColor { get; init; }

    public string? OutputPath { get; init; }

    public int? Row { get; init; }

    public int? Column { get; init; }

    public bool? Visible { get; init; }

    public PowerPointEditAssert? Assert { get; init; }
}
