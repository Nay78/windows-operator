namespace WindowsOperator.Core.Configuration;

public sealed class PowerAutomateMcpOptions
{
    public const string SectionName = "PowerAutomateMcp";

    public int EdgeIdleTtlSeconds { get; set; } = 15 * 60;
}
