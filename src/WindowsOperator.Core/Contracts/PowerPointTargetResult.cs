namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTargetResult(
    string TargetId,
    string OperationKind,
    string Status,
    PowerPointUpdateError? Error = null,
    bool? Found = null,
    bool? Editable = null,
    string? Type = null,
    string? Message = null,
    string? ShapeName = null,
    string? Source = null,
    bool? Bound = null,
    bool? Tagged = null,
    PowerPointTableSnapshot? Table = null,
    PowerPointShapeBounds? Bounds = null,
    PowerPointTableMatch? TableMatch = null);
