using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;

namespace WindowsOperator.Agent.Tests;

internal sealed class ExchangeRootScope : IDisposable
{
    public ExchangeRootScope(string rootPrefix)
    {
        Root = Path.Combine(Path.GetTempPath(), rootPrefix, Guid.NewGuid().ToString("N"));
        Options = Microsoft.Extensions.Options.Options.Create(
            new WorkbenchOptions
            {
                ExchangeRoot = Root,
                HostExchangeRoot = "/host-exchange",
            });
    }

    public string Root { get; }

    public IOptions<WorkbenchOptions> Options { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
