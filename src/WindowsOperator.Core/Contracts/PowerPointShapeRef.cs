namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointShapeRef(
    int Id,
    string Name,
    string Type,
    string? ParentName,
    int Level,
    IReadOnlyDictionary<string, string> Tags,
    string? AltText,
    bool HasText,
    bool HasTable,
    bool HasChart,
    bool Visible,
    string? Text);
