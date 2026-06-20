namespace WindowsOperator.Core.Configuration;

public sealed class WorkbenchOptions
{
    public const string SectionName = "Workbench";

    public string ExchangeRoot { get; set; } = @"Z:\operator-exchange";

    public string HostExchangeRoot { get; set; } = "/var/lib/windows-server/shared/operator-exchange";
}
