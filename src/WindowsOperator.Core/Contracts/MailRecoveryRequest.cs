namespace WindowsOperator.Core.Contracts;

public sealed record MailRecoveryRequest
{
    public string Mode { get; init; } = "basic";
}
