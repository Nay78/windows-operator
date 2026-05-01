using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsOperator.Agent.Hosting;
using WindowsOperator.Core.Configuration;

namespace WindowsOperator.Agent.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void OperatorOptions_BindsLoopbackDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Operator:BindAddress"] = "127.0.0.1",
                    ["Operator:RestPort"] = "43117",
                    ["Operator:EnableMcpStdio"] = "true",
                    ["Operator:UiBackend"] = "FlaUI.UIA3",
                })
            .Build();

        var options = configuration.GetSection(OperatorOptions.SectionName).Get<OperatorOptions>();

        Assert.NotNull(options);
        Assert.Equal("127.0.0.1", options.BindAddress);
        Assert.Equal(43117, options.RestPort);
        Assert.Equal("http://127.0.0.1:43117", options.RestBaseUrl);
    }

    [Fact]
    public async Task OperatorApp_LoadsLocalStateOverrides()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var localConfigDir = Directory.CreateDirectory(Path.Combine(stateRoot.FullName, "run"));
        var localConfigPath = Path.Combine(localConfigDir.FullName, "appsettings.Local.json");
        await File.WriteAllTextAsync(localConfigPath, """
        {
          "Operator": {
            "restPort": 43118
          }
        }
        """);

        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            await using var app = OperatorApp.Build(Array.Empty<string>(), useTestServer: true);
            var options = app.Services.GetRequiredService<IOptions<OperatorOptions>>().Value;

            Assert.Equal(43118, options.RestPort);
            Assert.Equal("http://127.0.0.1:43118", options.RestBaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }
}
