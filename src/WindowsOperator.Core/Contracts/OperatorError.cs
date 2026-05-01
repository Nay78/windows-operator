namespace WindowsOperator.Core.Contracts;

public sealed record OperatorError(
    string Code,
    string Message,
    string Remediation,
    IReadOnlyDictionary<string, string>? Details = null);
