namespace WindowsOperator.Core.Contracts;

public sealed record MailSyncRequest
{
    public int WaitSeconds { get; init; } = 30;
}
