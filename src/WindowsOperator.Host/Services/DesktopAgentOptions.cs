namespace WindowsOperator.Host.Services;

public sealed class DesktopAgentOptions
{
    public const string SectionName = "DesktopAgent";

    public string BaseUrl { get; set; } = "http://127.0.0.1:43119";
}
