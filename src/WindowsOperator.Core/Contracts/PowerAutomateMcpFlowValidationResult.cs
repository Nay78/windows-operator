namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpFlowValidationResult
{
    public bool Available { get; init; }

    public string? Source { get; init; }

    public int ErrorCount { get; init; }

    public int WarningCount { get; init; }

    public string ErrorsJson { get; init; } = "[]";

    public string WarningsJson { get; init; } = "[]";

    public string? Message { get; init; }
}
