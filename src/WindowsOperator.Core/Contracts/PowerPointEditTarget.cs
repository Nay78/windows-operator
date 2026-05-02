namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditTarget
{
    public PowerPointSlideSelector? Slide { get; init; }

    public PowerPointShapeSelector? Shape { get; init; }
}
