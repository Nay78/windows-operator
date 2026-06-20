namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeResetResult(
    bool Success,
    int MatchedProcesses,
    int KilledProcesses,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
