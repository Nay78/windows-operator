namespace WindowsOperator.Core.Contracts;

public sealed record MailFolderRef(
    int Depth,
    string Path,
    string Name,
    int ChildCount);
