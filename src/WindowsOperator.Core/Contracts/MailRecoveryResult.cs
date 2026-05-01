namespace WindowsOperator.Core.Contracts;

public sealed record MailRecoveryResult(
    string Mode,
    bool Success,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    int VisibleOutlookCount,
    int HeadlessOutlookCount,
    DateTimeOffset CompletedAtUtc);
