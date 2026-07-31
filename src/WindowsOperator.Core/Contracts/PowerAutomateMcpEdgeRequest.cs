namespace WindowsOperator.Core.Contracts;

public sealed record PowerAutomateMcpEdgeRequest
{
    public string Url { get; init; } = "https://make.powerautomate.com/";

    public string? ExtensionPath { get; init; }

    public string PackageSpec { get; init; } = "@kaael1/mcp-power-automate@0.4.1";

    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;

    public int? IdleTtlSeconds { get; init; }

    public int WaitSeconds { get; init; } = 4;

    public bool DryRun { get; init; }
}
