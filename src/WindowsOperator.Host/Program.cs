using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Host.Api;
using WindowsOperator.Host.Services;
using WindowsOperator.Mcp.DependencyInjection;
using WindowsOperator.Mcp.Protocol;

var builder = WebApplication.CreateBuilder(args);

AddLocalStateOverrides(builder);
builder.Services.Configure<JsonOptions>(options => OperatorJson.ConfigureHttp(options.SerializerOptions));
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.SectionName));
builder.Services.Configure<DesktopAgentOptions>(builder.Configuration.GetSection(DesktopAgentOptions.SectionName));
builder.Services.Configure<PowerPointAddInOptions>(builder.Configuration.GetSection(PowerPointAddInOptions.SectionName));
builder.Services.Configure<WorkbenchOptions>(builder.Configuration.GetSection(WorkbenchOptions.SectionName));
builder.Services.AddSingleton(
    RuntimeBuildIdentityReader.Read(typeof(HostOperatorFacade).Assembly));

var options = builder.Configuration.GetSection(OperatorOptions.SectionName).Get<OperatorOptions>() ?? new OperatorOptions();
var addInOptions = builder.Configuration.GetSection(PowerPointAddInOptions.SectionName).Get<PowerPointAddInOptions>() ?? new PowerPointAddInOptions();
var urls = new List<string> { options.RestBaseUrl };
if (addInOptions.Enabled && !string.IsNullOrWhiteSpace(addInOptions.BaseUrl))
{
    urls.Add(addInOptions.BaseUrl);
}

builder.WebHost.UseUrls(urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

builder.Services.AddHttpClient<DesktopAgentClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(11);
});
builder.Services.AddHttpClient("powerpoint-artifacts");
builder.Services.AddTransient<IWorkbenchService>(services => services.GetRequiredService<DesktopAgentClient>());
builder.Services.AddTransient<IPowerPointOnlineService>(services => services.GetRequiredService<DesktopAgentClient>());
builder.Services.AddTransient<IDevAutomationService>(services => services.GetRequiredService<DesktopAgentClient>());
builder.Services.AddTransient<IPowerAutomateMcpService>(services => services.GetRequiredService<DesktopAgentClient>());
builder.Services.AddTransient<IPowerPointOnlineUpdateService, PowerPointOnlineUpdateService>();
builder.Services.AddSingleton<IPowerPointJobService>(services =>
    new PowerPointJobService(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("powerpoint-artifacts"),
        services.GetRequiredService<IOptions<PowerPointAddInOptions>>()));
builder.Services.AddSingleton<IArtifactService, ExchangeArtifactService>();
builder.Services.AddSingleton<OneDriveRuntimeStateStore>();
builder.Services.AddSingleton<IOperatorFacade, HostOperatorFacade>();
builder.Services.AddSingleton<BrowserCallbackRelayService>();
builder.Services.AddSingleton<OneDriveConfigurationRecoveryService>();
builder.Services.AddHostedService<OneDriveRuntimeSupervisor>();
builder.Services.AddOperatorMcp(hostStdioServer: true);

var app = builder.Build();
app.UseHostOperatorErrorHandling();
MapPowerPointAddInStaticFiles(app);
app.MapHostOperatorEndpoints();
app.MapMcpHttpEndpoint();
await app.RunAsync();

static void MapPowerPointAddInStaticFiles(WebApplication app)
{
    var options = app.Services.GetRequiredService<IOptions<PowerPointAddInOptions>>().Value;
    if (!options.Enabled)
    {
        return;
    }

    var staticRoot = ResolvePowerPointAddInStaticRoot(app.Environment.ContentRootPath, options.StaticRoot);
    if (!Directory.Exists(staticRoot))
    {
        return;
    }

    var fileProvider = new PhysicalFileProvider(staticRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

static string ResolvePowerPointAddInStaticRoot(string contentRoot, string configuredRoot)
{
    if (!string.IsNullOrWhiteSpace(configuredRoot))
    {
        return Path.GetFullPath(configuredRoot);
    }

    return Path.GetFullPath(Path.Combine(contentRoot, "..", "WindowsOperator.PowerPointAddIn", "dist"));
}

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
