namespace WindowsOperator.Core.Contracts;

using System.Text.Json.Serialization;

public enum OneDriveFinalReleaseAction
{
    Dehydrate,
}

public enum OneDriveReclaimScope
{
    ModuleOwned,
}

public sealed record OneDriveRootConfig
{
    public required string Path { get; init; }

    public bool Enabled { get; init; } = true;

    public OneDriveFinalReleaseAction FinalRelease { get; init; } = OneDriveFinalReleaseAction.Dehydrate;
}

public sealed record OneDriveConfig
{
    public int Version { get; init; } = 1;

    public IReadOnlyDictionary<string, OneDriveRootConfig> Roots { get; init; } =
        new Dictionary<string, OneDriveRootConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["geosupport"] = new()
            {
                Path = @"C:\Users\Administrator\Geosupport S.A",
            },
        };

    public bool PreserveUserPins { get; init; } = true;

    public OneDriveReclaimScope ReclaimScope { get; init; } = OneDriveReclaimScope.ModuleOwned;

    public bool PeriodicReclaim { get; init; }

    public long MinimumFreeBytes { get; init; } = 10L * 1024 * 1024 * 1024;

    public long MaximumAcquireBytes { get; init; } = 1024L * 1024 * 1024;

    public int DefaultTtlSeconds { get; init; } = 300;

    public int MaximumTtlSeconds { get; init; } = 900;
}

public sealed record OneDriveConfigResult
{
    public required OneDriveConfig Config { get; init; }

    public required string ETag { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OneDriveConfigUpdateRequest
{
    public required OneDriveConfig Config { get; init; }

    // Transport adapter fills this from the required If-Match header. Keep it
    // out of JSON so callers cannot bypass the HTTP precondition in the body.
    [JsonIgnore]
    public string? IfMatch { get; init; }
}
