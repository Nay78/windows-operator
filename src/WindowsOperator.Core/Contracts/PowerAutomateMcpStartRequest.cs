namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpStartRequest
{
    public string BridgeHost { get; init; } = "127.0.0.1";

    public int BridgePort { get; init; } = 17373;

    public string PackageSpec { get; init; } = "@kaael1/mcp-power-automate@0.4.1";

    public int WaitSeconds { get; init; } = 10;

    public bool ResolveExtensionPath { get; init; } = true;

    public bool DryRun { get; init; }
}
