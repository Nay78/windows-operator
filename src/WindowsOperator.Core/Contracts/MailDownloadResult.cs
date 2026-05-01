namespace WindowsOperator.Core.Contracts;

public sealed record MailDownloadResult(
    string RunId,
    string RunRoot,
    string DownloadRoot,
    int MessagesScanned,
    int MessagesMatched,
    int AttachmentsSaved,
    int AttachmentsSkipped,
    IReadOnlyList<MailSavedAttachment> Saved,
    IReadOnlyList<MailSkippedAttachment> Skipped,
    IReadOnlyList<MailRunError> Errors,
    DateTimeOffset CompletedAtUtc);
