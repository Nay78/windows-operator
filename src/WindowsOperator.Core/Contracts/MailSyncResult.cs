namespace WindowsOperator.Core.Contracts;

public sealed record MailSyncResult(
    bool Success,
    int StartedSyncObjects,
    int WaitedSeconds,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset SyncedAtUtc);
