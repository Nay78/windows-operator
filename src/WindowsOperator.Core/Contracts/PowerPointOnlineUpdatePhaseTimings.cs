namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineUpdatePhaseTimings
{
    public long? TotalMs { get; init; }

    public long? OpenSessionMs { get; init; }

    public long? AddInProbeMs { get; init; }

    public long? TemplatePreparationMs { get; init; }

    public long? JobMs { get; init; }

    public long? SaveMs { get; init; }

    public long? EvidenceMs { get; init; }

    public long? VerificationReopenMs { get; init; }

    public long? TemplateCleanupMs { get; init; }

    public long? SessionCleanupMs { get; init; }
}
