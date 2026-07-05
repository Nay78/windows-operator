namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointDiscoveredTarget(
    string TargetId,
    bool Editable,
    string Type,
    string? Message = null,
    string? ShapeName = null,
    string? Source = null,
    bool? Bound = null,
    bool? Tagged = null);
