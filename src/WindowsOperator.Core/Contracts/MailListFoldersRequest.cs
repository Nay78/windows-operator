namespace WindowsOperator.Core.Contracts;

public sealed record MailListFoldersRequest
{
    public bool SyncBeforeRead { get; init; }

    public int SyncWaitSeconds { get; init; } = 30;
}
