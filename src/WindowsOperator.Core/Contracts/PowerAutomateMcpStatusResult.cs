namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpStatusResult
{
    public bool Success { get; init; }

    public string BridgeHost { get; init; } = "127.0.0.1";

    public int BridgePort { get; init; } = 17373;

    public string PackageSpec { get; init; } = "@kaael1/mcp-power-automate@0.4.1";

    public bool BridgeListening { get; init; }

    public bool BridgeHealthy { get; init; }

    public bool ContextAvailable { get; init; }

    public string? BridgeMode { get; init; }

    public string? BridgeVersion { get; init; }

    public int? BridgeProcessId { get; init; }

    [OperatorInternal]
    public string? NodePath { get; init; }

    public string? NodeVersion { get; init; }

    [OperatorInternal]
    public string? NpmPath { get; init; }

    public string? NpmVersion { get; init; }

    [OperatorInternal]
    public string? NpxPath { get; init; }

    [OperatorInternal]
    public string? EdgePath { get; init; }

    [OperatorInternal]
    public string? ExtensionPath { get; init; }

    public bool ExtensionPathResolved { get; init; }

    public bool EdgeSessionAlive { get; init; }

    public int? EdgeProcessId { get; init; }

    public long? EdgeHwnd { get; init; }

    public DateTimeOffset? EdgeStartedAtUtc { get; init; }

    public DateTimeOffset? EdgeLastUsedAtUtc { get; init; }

    public DateTimeOffset? EdgeLeaseExpiresAtUtc { get; init; }

    public DateTimeOffset? EdgeClosedAtUtc { get; init; }

    public int EdgeIdleTtlSeconds { get; init; }

    public BrowserEdgeProfileMode? EdgeProfileMode { get; init; }

    public string? EdgeUrl { get; init; }

    [OperatorInternal]
    public string? StatePath { get; init; }

    [OperatorInternal]
    public string? LogPath { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
