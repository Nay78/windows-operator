namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchSessionCleanupResult(
    bool Success,
    string SessionId,
    string Kind,
    int MatchedWindows,
    int ClosedWindows,
    int PreservedWindows,
    int FailedWindows,
    int MatchedProcesses,
    int ClosedProcesses,
    int PreservedProcesses,
    int FailedProcesses,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
