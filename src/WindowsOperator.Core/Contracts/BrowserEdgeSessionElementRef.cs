namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionElementRef(
    string TagName,
    string? Type,
    string? Text,
    string? Label,
    string? Id,
    string? Name);
