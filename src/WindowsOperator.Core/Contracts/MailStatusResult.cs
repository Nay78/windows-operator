namespace WindowsOperator.Core.Contracts;

public sealed record MailStatusResult(
    bool WorkerAvailable,
    int VisibleOutlookCount,
    int HeadlessOutlookCount,
    string? LastWorkerError,
    MailRecoveryResult? LastRecovery,
    DateTimeOffset CheckedAtUtc);
