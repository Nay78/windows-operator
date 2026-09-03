namespace WindowsOperator.Core.Contracts;

public enum OneDriveReclaimState
{
    Pending,
    Running,
    Completed,
    Failed,
    RecoveryRequired,
}

public sealed record OneDriveReclaimRequest
{
    public required string RequestId { get; init; }

    public required string RootId { get; init; }

    public IReadOnlyList<string> RelativePaths { get; init; } = Array.Empty<string>();

    public bool DryRun { get; init; } = true;
}

public sealed record OneDriveReclaimFileProgress
{
    public required string RelativePath { get; init; }

    public required string Identity { get; init; }

    public OneDriveFileOnDemandAttributes? OriginalAttributes { get; init; }

    public long AllocatedBytesBefore { get; init; }

    public long? AllocatedBytesAfter { get; init; }

    public bool Completed { get; init; }

    public string? Outcome { get; init; }

    // Durable operation journal. A restart must never infer success from a
    // reclaim record left Running around a provider mutation.
    public string OperationPhase { get; init; } = "not_started";

    public string? Evidence { get; init; }

    public DateTimeOffset? EvidenceRecordedAtUtc { get; init; }
}

public sealed record OneDriveReclaimResult
{
    public required string RequestId { get; init; }

    public required string RequestFingerprint { get; init; }

    public required bool Success { get; init; }

    public required string RunId { get; init; }

    public required OneDriveReclaimState State { get; init; }

    public required string RootId { get; init; }

    public bool DryRun { get; init; }

    public long AllocatedBytesBefore { get; init; }

    public long AllocatedBytesAfter { get; init; }

    public long EstimatedReclaimableBytes { get; init; }

    public long ReclaimedLocalBytes { get; init; }

    public int FilesConsidered { get; init; }

    public int FilesReclaimed { get; init; }

    // Durable recovery evidence. Paths remain root-relative; full paths stay
    // inside local run state and are reconstructed from the approved root.
    public IReadOnlyList<OneDriveReclaimFileProgress> Files { get; init; } = Array.Empty<OneDriveReclaimFileProgress>();

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
