namespace WindowsOperator.Core.Contracts;

public enum OneDriveLeaseState
{
    Acquiring,
    Ready,
    Expired,
    Releasing,
    Released,
    Failed,
    RecoveryRequired,
}

public sealed record OneDriveLeaseRequest
{
    public required string RequestId { get; init; }

    public required string RootId { get; init; }

    public required string RelativePath { get; init; }

    public int? TtlSeconds { get; init; }

    public long? ExpectedLength { get; init; }

    public string? ExpectedSha256 { get; init; }
}

public sealed record OneDriveListRequest
{
    public required string RootId { get; init; }

    public string RelativePath { get; init; } = string.Empty;
}

public sealed record OneDriveFileEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string MimeType { get; init; } = "";

    public long? LogicalLength { get; init; }

    public string? ModifiedTime { get; init; }
}

public sealed record OneDriveLeaseRenewRequest
{
    public required string RequestId { get; init; }

    public required int TtlSeconds { get; init; }
}

public sealed record OneDriveFileOnDemandAttributes
{
    public bool Offline { get; init; }

    public bool RecallOnDataAccess { get; init; }

    public bool Pinned { get; init; }

    public bool Unpinned { get; init; }
}

public sealed record OneDriveLeaseResult
{
    public required bool Success { get; init; }

    public required string LeaseId { get; init; }

    public required string RootId { get; init; }

    public required string RelativePath { get; init; }

    public required OneDriveLeaseState State { get; init; }

    public long? LogicalLength { get; init; }

    public long? AllocatedBytesBeforeHydration { get; init; }

    public long? AllocatedBytesAfterHydration { get; init; }

    public long? AllocatedBytesAfterRelease { get; init; }

    public OneDriveFileOnDemandAttributes? Attributes { get; init; }

    public string? Sha256 { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ReadyAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? ReleasedAtUtc { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OneDriveLeaseStatusResult
{
    public required bool Found { get; init; }

    public OneDriveLeaseResult? Lease { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
