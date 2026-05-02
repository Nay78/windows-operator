namespace WindowsOperator.Core.Contracts;

public sealed record MailStatusResult(
    bool WorkerAvailable,
    int VisibleOutlookCount,
    int HeadlessOutlookCount,
    string? LastWorkerError,
    DateTimeOffset CheckedAtUtc);
