namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineSessionResult
{
    public required bool Success { get; init; }

    public required string SessionId { get; init; }

    public required PowerPointOnlineSessionStatus Status { get; init; }

    public required string DeckUrl { get; init; }

    public string? CanonicalUrl { get; init; }

    public string? CurrentUrl { get; init; }

    public string? CurrentTitle { get; init; }

    public int? CurrentSlide { get; init; }

    public int? SlideCount { get; init; }

    public string? EditMode { get; init; }

    public string? SaveState { get; init; }

    public string? BrowserSessionId { get; init; }

    public long? Hwnd { get; init; }

    public WorkbenchRunRef? ArtifactRoot { get; init; }

    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; }
}
