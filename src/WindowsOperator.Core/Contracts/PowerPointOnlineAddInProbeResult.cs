namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineAddInProbeResult
{
    public required bool Success { get; init; }

    public required PowerPointOnlineAddInProbeStatus Status { get; init; }

    public required PowerPointOnlineSessionResult Session { get; init; }

    public required string AddInBaseUrl { get; init; }

    public bool HostReachable { get; init; }

    public string? TaskPaneUrl { get; init; }

    public bool TaskPaneReachable { get; init; }

    public string? ManifestUrl { get; init; }

    public bool ManifestReachable { get; init; }

    public string? ManifestId { get; init; }

    public string? ManifestVersion { get; init; }

    public string? ManifestDisplayName { get; init; }

    public string? ManifestSourceLocation { get; init; }

    public bool TaskPaneVisible { get; init; }

    public bool CommandVisible { get; init; }

    public IReadOnlyList<UiElementRef> MatchedElements { get; init; } = Array.Empty<UiElementRef>();

    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; }
}
