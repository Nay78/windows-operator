namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineUpdateRequest
{
    public string? SessionId { get; init; }

    public string? DeckUrl { get; init; }

    public required PowerPointUpdateJob Job { get; init; }

    public int? EvidenceSlideNumber { get; init; }

    public bool Capture { get; init; } = true;

    public int OpenWaitSeconds { get; init; } = 30;

    public int JobTimeoutSeconds { get; init; } = 60;

    public int PollSeconds { get; init; } = 1;

    public int SaveTimeoutSeconds { get; init; } = 30;

    public int SavePollSeconds { get; init; } = 1;

    public bool VerifyReopen { get; init; }

    public int ReopenWaitSeconds { get; init; } = 30;

    public bool PrepareTemplate { get; init; }

    public bool CleanupTemplate { get; init; }

    public bool CleanupTemplateOnFailure { get; init; } = true;

    public int TemplateWaitSeconds { get; init; } = 2;

    public bool AllowDeckMutation { get; init; }

    public bool CleanupSession { get; init; }
}
