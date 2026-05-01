namespace WindowsOperator.Core.Contracts;

public sealed record MailMessageRef(
    string MessageId,
    string FolderPath,
    string Subject,
    DateTimeOffset? ReceivedTime,
    int AttachmentCount,
    IReadOnlyList<MailAttachmentRef> Attachments);
