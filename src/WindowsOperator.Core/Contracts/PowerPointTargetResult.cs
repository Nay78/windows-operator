namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTargetResult(
    string TargetId,
    string OperationKind,
    string Status,
    PowerPointUpdateError? Error = null);
