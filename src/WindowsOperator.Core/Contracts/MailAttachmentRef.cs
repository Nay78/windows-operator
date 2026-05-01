namespace WindowsOperator.Core.Contracts;

public sealed record MailAttachmentRef(
    int Index,
    string FileName,
    string Extension,
    long? Size);
