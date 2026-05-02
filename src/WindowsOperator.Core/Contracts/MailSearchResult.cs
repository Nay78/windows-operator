namespace WindowsOperator.Core.Contracts;

public sealed record MailSearchResult(
    bool Success,
    IReadOnlyList<MailMessageRef> Messages,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MailRunError> Errors,
    DateTimeOffset? LastSyncUtc,
    bool Recovered,
    DateTimeOffset CompletedAtUtc);
