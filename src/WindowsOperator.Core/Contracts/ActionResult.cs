namespace WindowsOperator.Core.Contracts;

public sealed record ActionResult(
    bool Success,
    string Message,
    IReadOnlyDictionary<string, string>? Metadata = null);
