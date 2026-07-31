namespace WindowsOperator.Core.Contracts;

public sealed record MailStatusResult(
    bool WorkerAvailable,
    int VisibleOutlookCount,
    int HeadlessOutlookCount,
    [property: OperatorInternal] string? LastWorkerError,
    DateTimeOffset CheckedAtUtc);
