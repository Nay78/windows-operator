namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditOutcome(
    string Id,
    string Op,
    int Matched,
    int Changed,
    string? Before,
    string? After,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
