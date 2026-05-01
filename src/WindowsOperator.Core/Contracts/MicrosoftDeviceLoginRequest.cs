namespace WindowsOperator.Core.Contracts;

public sealed record MicrosoftDeviceLoginRequest
{
    public required string DeviceCode { get; init; }

    public string LoginUrl { get; init; } = "https://microsoft.com/devicelogin";

    public int PageLoadSeconds { get; init; } = 6;

    public bool InPrivate { get; init; }

    public bool DryRun { get; init; }
}
