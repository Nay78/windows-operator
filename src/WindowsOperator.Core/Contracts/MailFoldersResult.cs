namespace WindowsOperator.Core.Contracts;

public sealed record MailFoldersResult(
    bool Success,
    IReadOnlyList<MailFolderRef> Folders,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MailRunError> Errors,
    DateTimeOffset? LastSyncUtc,
    bool Recovered,
    DateTimeOffset CompletedAtUtc);
