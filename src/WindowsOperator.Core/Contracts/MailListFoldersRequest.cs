namespace WindowsOperator.Core.Contracts;

public sealed record MailListFoldersRequest
{
    public string Freshness { get; init; } = MailFreshness.Auto;
}
