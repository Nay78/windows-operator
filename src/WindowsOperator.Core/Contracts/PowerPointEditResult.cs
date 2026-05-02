namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditResult(
    bool Success,
    bool DryRun,
    string JobId,
    string? PresentationPath,
    string? OutputPath,
    IReadOnlyList<PowerPointEditOutcome> Edits,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
