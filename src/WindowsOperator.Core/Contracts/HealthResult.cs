namespace WindowsOperator.Core.Contracts;

public sealed record HealthResult(
    string Status,
    string RuntimeMode,
    string Platform,
    string RestBaseUrl,
    string UiBackend,
    IReadOnlyList<string> CaptureBackends,
    bool McpEnabled,
    DateTimeOffset CheckedAtUtc);
