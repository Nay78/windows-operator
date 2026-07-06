namespace WindowsOperator.Core.Contracts;

public sealed record OperatorError(
    string Code,
    string Message,
    string Remediation,
    IReadOnlyDictionary<string, string>? Details = null,
    string? CorrelationId = null,
    bool? Retryable = null,
    OperatorErrorCategory? Category = null);
