namespace WindowsOperator.Core.Contracts;

public sealed record MailMessageRef(
    string MessageId,
    string FolderPath,
    string Subject,
    DateTimeOffset? ReceivedTime,
    DateTimeOffset? ModifiedTime,
    int AttachmentCount,
    IReadOnlyList<MailAttachmentRef> Attachments);
