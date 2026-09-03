using WindowsOperator.Agent.Hosting;
using WindowsOperator.Agent.Services;

if (args is ["--onedrive-provider-probe", var rootPath])
{
    return IsolatedOneDriveProviderProbe.RunChild(rootPath);
}

if (args is ["--onedrive-hydration-read", var filePath])
{
    return IsolatedOneDriveHydration.RunChild(filePath);
}

var app = OperatorApp.Build(args);
await app.RunAsync();
return 0;
