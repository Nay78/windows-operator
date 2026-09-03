namespace WindowsOperator.Core.Contracts;

public sealed record OneDriveFilesOnDemandStatusResult
{
    public bool Available { get; init; }

    public OneDriveRuntimeEvidence Runtime { get; init; } = new();

    public OneDriveRuntimeSupervisorState? RuntimeSupervisor { get; init; }

    public string? ProviderReadinessReason { get; init; }

    public int ActiveLeaseCount { get; init; }

    public int ActiveReclaimCount { get; init; }

    public int RecoveryRequiredLeaseCount { get; init; }

    public int RecoveryRequiredReclaimCount { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OneDriveRuntimeEvidence
{
    public string? ComputerName { get; init; }

    public bool RecoveryAllowed { get; init; }

    public bool ProcessPresent { get; init; }

    public int? ProcessSessionId { get; init; }

    public int? ConfiguredSessionId { get; init; }

    public int? ActiveInteractiveSessionId { get; init; }

    public string? InteractiveUser { get; init; }

    public string? InteractiveSessionState { get; init; }

    public int? InteractiveSessionProtocol { get; init; }

    public bool ProviderReady { get; init; }

    public string? ProviderReason { get; init; }

    public bool AuthenticationRequired { get; init; }

    public IReadOnlyList<string> RecoveryActions { get; init; } = Array.Empty<string>();
}

public sealed record OneDriveRuntimeSupervisorState
{
    public string ComputerName { get; init; } = string.Empty;

    public bool RecoveryAllowed { get; init; }

    public int TargetSessionId { get; init; }

    public string State { get; init; } = "unknown";

    public string? SessionState { get; init; }

    public int? ProcessId { get; init; }

    public int? ProcessSessionId { get; init; }

    public string? Reason { get; init; }

    public long AttemptCount { get; init; }

    public long RestartCount { get; init; }

    public int ConsecutiveFailureCount { get; init; }

    public DateTimeOffset? LastAttemptAtUtc { get; init; }

    public DateTimeOffset? LastSuccessAtUtc { get; init; }

    public DateTimeOffset? NextAttemptAtUtc { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
