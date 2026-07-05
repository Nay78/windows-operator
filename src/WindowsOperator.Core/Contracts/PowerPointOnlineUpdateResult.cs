namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineUpdateResult
{
    public required bool Success { get; init; }

    public required PowerPointOnlineUpdateStatus Status { get; init; }

    public required PowerPointOnlineSaveProofTier SaveProofTier { get; init; }

    public required PowerPointOnlineSessionResult Session { get; init; }

    public PowerPointOnlineSessionResult? VerificationSession { get; init; }

    public PowerPointOnlineSessionResult? TemplatePreparationSession { get; init; }

    public PowerPointOnlineSessionResult? TemplateCleanupSession { get; init; }

    public PowerPointOnlineSessionResult? SessionCleanupSession { get; init; }

    public required PowerPointJobRecord JobRecord { get; init; }

    public PowerPointOnlineUpdatePhaseTimings? PhaseTimings { get; init; }

    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
