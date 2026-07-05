namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineAddInProbeRequest
{
    public string AddInBaseUrl { get; init; } = "https://localhost:3003";

    public bool Capture { get; init; } = true;

    public bool ActivateIfNeeded { get; init; }

    public int ActivationTimeoutSeconds { get; init; } = 10;

    public int HostTimeoutSeconds { get; init; } = 10;

    public string? Label { get; init; }
}
