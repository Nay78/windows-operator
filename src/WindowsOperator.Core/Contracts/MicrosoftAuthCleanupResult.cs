namespace WindowsOperator.Core.Contracts;

public sealed record MicrosoftAuthCleanupResult(
    bool Success,
    int MatchedWindows,
    int ClosedWindows,
    int PreservedWindows,
    int FailedWindows,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
