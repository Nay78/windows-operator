namespace WindowsOperator.Core.Contracts;

public sealed record MailSkippedAttachment(
    string MessageId,
    string FolderPath,
    string Subject,
    DateTimeOffset? ReceivedTime,
    int AttachmentIndex,
    string FileName,
    string Reason);
