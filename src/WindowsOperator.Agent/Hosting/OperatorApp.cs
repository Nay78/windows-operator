using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.TestHost;
using WindowsOperator.Agent.Api;
using WindowsOperator.Agent.Services;
using WindowsOperator.Automation.DependencyInjection;
using WindowsOperator.Capture.DependencyInjection;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Mcp.DependencyInjection;
using WindowsOperator.Mcp.Protocol;

namespace WindowsOperator.Agent.Hosting;

public static class OperatorApp
{
    public static WebApplication Build(
        string[] args,
        Action<IServiceCollection>? overrideServices = null,
        bool useTestServer = false)
    {
        var builder = WebApplication.CreateBuilder(args);
        Configure(builder, overrideServices, useTestServer);
        var app = builder.Build();
        app.MapOperatorEndpoints();
        app.MapMcpHttpEndpoint();
        return app;
    }

    public static void Configure(
        WebApplicationBuilder builder,
        Action<IServiceCollection>? overrideServices = null,
        bool useTestServer = false)
    {
        AddLocalStateOverrides(builder);
        builder.Services.Configure<JsonOptions>(options => OperatorJson.Configure(options.SerializerOptions));
        builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.SectionName));
        builder.Services.Configure<WorkbenchOptions>(builder.Configuration.GetSection(WorkbenchOptions.SectionName));
        builder.Services.Configure<DevAutomationOptions>(builder.Configuration.GetSection(DevAutomationOptions.SectionName));
        builder.Services.PostConfigure<WorkbenchOptions>(ApplyWorkbenchEnvironmentOverrides);
        builder.Services.PostConfigure<DevAutomationOptions>(ApplyDevAutomationEnvironmentOverrides);

        var options = builder.Configuration.GetSection(OperatorOptions.SectionName).Get<OperatorOptions>() ?? new OperatorOptions();
        builder.WebHost.UseUrls(options.RestBaseUrl);
        if (useTestServer)
        {
            builder.WebHost.UseTestServer();
        }

        builder.Services.AddWindowsAutomation();
        builder.Services.AddWindowCapture();
        builder.Services.AddSingleton<IMailService, OutlookMailService>();
        builder.Services.AddSingleton<EdgeMicrosoftAuthService>();
        builder.Services.AddSingleton<IMicrosoftAuthService>(services => services.GetRequiredService<EdgeMicrosoftAuthService>());
        builder.Services.AddSingleton<IEdgeBrowserService>(services => services.GetRequiredService<EdgeMicrosoftAuthService>());
        builder.Services.AddSingleton<IEdgeDevToolsService, EdgeDevToolsService>();
        builder.Services.AddSingleton<IPowerPointDevScriptCatalog, PowerPointDevScriptCatalog>();
        builder.Services.AddSingleton<WorkbenchRunStore>();
        builder.Services.AddSingleton<OwnedSessionRegistry>();
        builder.Services.AddSingleton<IWorkbenchService, WorkbenchService>();
        builder.Services.AddHttpClient<IPowerPointOnlineAddInHostProbe, HttpPowerPointOnlineAddInHostProbe>();
        builder.Services.AddSingleton<IPowerPointOnlineService>(services =>
            new PowerPointOnlineService(
                services.GetRequiredService<IEdgeBrowserService>(),
                services.GetRequiredService<IInputService>(),
                services.GetRequiredService<IPowerPointOnlineAddInHostProbe>(),
                services.GetRequiredService<IUiAutomationService>(),
                services.GetRequiredService<WorkbenchRunStore>(),
                services.GetRequiredService<IWorkbenchService>(),
                services.GetRequiredService<IEdgeDevToolsService>()));
        builder.Services.AddSingleton<IDevAutomationService, PowerPointDevAutomationService>();
        builder.Services.AddSingleton<IOperatorFacade, OperatorFacade>();
        builder.Services.AddOperatorMcp(hostStdioServer: !useTestServer);

        overrideServices?.Invoke(builder.Services);
    }

    private static void AddLocalStateOverrides(WebApplicationBuilder builder)
    {
        var stateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        if (string.IsNullOrWhiteSpace(stateRoot))
        {
            return;
        }

        var localConfigPath = Path.Combine(stateRoot, "run", "appsettings.Local.json");
        builder.Configuration.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
    }

    private static void ApplyWorkbenchEnvironmentOverrides(WorkbenchOptions options)
    {
        var exchangeRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_EXCHANGE_ROOT");
        if (!string.IsNullOrWhiteSpace(exchangeRoot))
        {
            options.ExchangeRoot = exchangeRoot;
        }

        var hostExchangeRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT");
        if (!string.IsNullOrWhiteSpace(hostExchangeRoot))
        {
            options.HostExchangeRoot = hostExchangeRoot;
        }
    }

    private static void ApplyDevAutomationEnvironmentOverrides(DevAutomationOptions options)
    {
        if (IsEnabled(Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_DEV_AUTOMATION")))
        {
            options.Enabled = true;
        }

        if (IsEnabled(Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_DEV_RAW_JS")))
        {
            options.AllowRawJs = true;
        }
    }

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}
