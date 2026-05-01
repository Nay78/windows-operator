namespace WindowsOperator.Core.Contracts;

public sealed record MailSavedAttachment(
    string MessageId,
    string FolderPath,
    string Subject,
    DateTimeOffset? ReceivedTime,
    int AttachmentIndex,
    string FileName,
    string RelativePath,
    string AbsolutePath,
    long Bytes,
    bool AlreadyProcessed);
