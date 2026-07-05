namespace WindowsOperator.Core.Configuration;

public sealed class DevAutomationOptions
{
    public const string SectionName = "DevAutomation";

    public bool Enabled { get; set; }

    public bool AllowRawJs { get; set; }

    public int MaxResultBytes { get; set; } = 65536;
}
