namespace WindowsOperator.Core.Contracts;

public sealed record DevScriptResult
{
    public bool Success { get; init; }

    public DevScriptStatus Status { get; init; }

    public string SessionId { get; init; } = string.Empty;

    public string ScriptId { get; init; } = string.Empty;

    public string? Target { get; init; }

    public string? TargetUrl { get; init; }

    public string? TargetTitle { get; init; }

    public string? ResultJson { get; init; }

    public string? ResultText { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; }

    [OperatorInternal]
    public string? EvidencePath { get; init; }

    public string? SourceSha256 { get; init; }
}
