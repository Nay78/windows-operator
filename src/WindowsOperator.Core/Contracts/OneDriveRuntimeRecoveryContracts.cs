namespace WindowsOperator.Core.Contracts;

public sealed record OneDriveConfigurationRecoveryRequest
{
    public bool ClearConfiguration { get; init; }

    // Zero means resolve the current active Administrator desktop session dynamically.
    public int TargetSessionId { get; init; }
}

public sealed record OneDriveConfigurationRecoveryResult
{
    public bool ConfigurationCleared { get; init; }

    public bool RuntimeStarted { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public int TargetSessionId { get; init; }

    public string TargetSessionState { get; init; } = string.Empty;

    public string? BackupDirectoryName { get; init; }

    public int? ProcessId { get; init; }

    public int? ProcessSessionId { get; init; }

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
