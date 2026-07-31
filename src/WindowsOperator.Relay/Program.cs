using WindowsOperator.Relay;

var builder = WebApplication.CreateBuilder(args);
var externalConfigPath = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_RELAY_CONFIG");
if (!string.IsNullOrWhiteSpace(externalConfigPath))
{
    builder.Configuration.AddJsonFile(
        Path.GetFullPath(externalConfigPath),
        optional: false,
        reloadOnChange: true);
}

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddWindowsOperatorRelay(builder.Configuration.GetSection(RelayOptions.SectionName));

var app = builder.Build();
app.MapWindowsOperatorRelay();
await app.RunAsync();

public partial class Program;
