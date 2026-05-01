using Microsoft.AspNetCore.Http.Json;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Host.Api;
using WindowsOperator.Host.Services;
using WindowsOperator.Mcp.DependencyInjection;
using WindowsOperator.Mcp.Protocol;

var builder = WebApplication.CreateBuilder(args);

AddLocalStateOverrides(builder);
builder.Services.Configure<JsonOptions>(options => OperatorJson.Configure(options.SerializerOptions));
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.SectionName));
builder.Services.Configure<DesktopAgentOptions>(builder.Configuration.GetSection(DesktopAgentOptions.SectionName));

var options = builder.Configuration.GetSection(OperatorOptions.SectionName).Get<OperatorOptions>() ?? new OperatorOptions();
builder.WebHost.UseUrls(options.RestBaseUrl);

builder.Services.AddHttpClient<DesktopAgentClient>();
builder.Services.AddSingleton<IOperatorFacade, HostOperatorFacade>();
builder.Services.AddOperatorMcp(hostStdioServer: true);

var app = builder.Build();
app.MapHostOperatorEndpoints();
app.MapMcpHttpEndpoint();
await app.RunAsync();

static void AddLocalStateOverrides(WebApplicationBuilder builder)
{
    var stateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_HOST_STATE_ROOT");
    if (string.IsNullOrWhiteSpace(stateRoot))
    {
        stateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
    }

    if (string.IsNullOrWhiteSpace(stateRoot))
    {
        return;
    }

    var localConfigPath = Path.Combine(stateRoot, "run", "host.appsettings.Local.json");
    builder.Configuration.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
}
