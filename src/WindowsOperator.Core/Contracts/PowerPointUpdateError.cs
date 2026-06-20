namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointUpdateError(
    string Code,
    bool Retryable,
    string OperatorMessage,
    string? TechnicalMessage = null);
