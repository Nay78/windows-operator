namespace WindowsOperator.Core.Contracts;

public sealed record MailRunError(
    string Code,
    string Message,
    string? Detail);
