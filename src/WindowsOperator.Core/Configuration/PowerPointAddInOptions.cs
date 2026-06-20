namespace WindowsOperator.Core.Configuration;

public sealed class PowerPointAddInOptions
{
    public const string SectionName = "PowerPointAddIn";

    public string BaseUrl { get; set; } = "https://localhost:3003";

    public string StaticRoot { get; set; } = string.Empty;

    public string StateRoot { get; set; } = string.Empty;

    public int MaxArtifactBytes { get; set; } = 15 * 1024 * 1024;
}
