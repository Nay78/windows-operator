namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointSlideRef(
    int Index,
    int SlideId,
    string? Title,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<PowerPointShapeRef> Shapes);
