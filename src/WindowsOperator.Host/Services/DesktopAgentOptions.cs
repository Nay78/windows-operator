namespace WindowsOperator.Host.Services;

public sealed class DesktopAgentOptions
{
    public const string SectionName = "DesktopAgent";

    public string BaseUrl { get; set; } = "http://127.0.0.1:43119";

    public int OneDriveReadinessAttempts { get; set; } = 6;

    public int OneDriveReadinessDelayMilliseconds { get; set; } = 1000;
}
