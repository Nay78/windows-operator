namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointPresentationRef(
    string Name,
    string? Path,
    int SlideCount);
