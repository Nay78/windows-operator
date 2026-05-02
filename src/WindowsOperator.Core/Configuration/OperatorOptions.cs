namespace WindowsOperator.Core.Configuration;

public sealed class OperatorOptions
{
    public const string SectionName = "Operator";

    public string BindAddress { get; set; } = "127.0.0.1";

    public int RestPort { get; set; } = 43117;

    public bool EnableMcpStdio { get; set; } = true;

    public string UiBackend { get; set; } = "FlaUI.UIA3";

    public ScreenshotOptions Screenshot { get; set; } = new();

    public MailOptions Mail { get; set; } = new();

    public string RestBaseUrl => $"http://{BindAddress}:{RestPort}";
}
