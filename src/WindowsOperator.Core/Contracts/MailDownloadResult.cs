namespace WindowsOperator.Core.Contracts;

public sealed record MailDownloadResult(
    bool Success,
    string RunId,
    [property: OperatorInternal] string RunRoot,
    [property: OperatorInternal] string DownloadRoot,
    int MessagesScanned,
    int MessagesMatched,
    int AttachmentsSaved,
    int AttachmentsSkipped,
    IReadOnlyList<MailSavedAttachment> Saved,
    IReadOnlyList<MailSkippedAttachment> Skipped,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MailRunError> Errors,
    DateTimeOffset? LastSyncUtc,
    bool Recovered,
    DateTimeOffset CompletedAtUtc);
