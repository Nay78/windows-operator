using System.Diagnostics;
using System.Text.Json;

namespace WindowsOperator.Agent.Services;

internal static class IsolatedOneDriveProviderProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public static CloudFilesProviderStatusQuery Query(string rootPath)
    {
        try
        {
            using var process = Process.Start(BuildStartInfo(rootPath));
            if (process is null)
            {
                return Failed();
            }

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
                return Failed();
            }

            var output = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Failed();
            }

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var hresult = root.GetProperty("hresult").GetInt32();
            var status = root.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.Number
                    ? statusElement.GetUInt32()
                    : (uint?)null;
            return new CloudFilesProviderStatusQuery(hresult, status);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            JsonException)
        {
            return Failed();
        }
    }

    public static int RunChild(string rootPath)
    {
        var result = OneDriveFilesOnDemandService.CloudFilesApi.QuerySyncRootProviderStatusDirect(rootPath);
        Console.Out.Write(JsonSerializer.Serialize(new { hresult = result.HResult, status = result.Status }));
        return 0;
    }

    private static ProcessStartInfo BuildStartInfo(string rootPath)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Current process path is unavailable.");
        var info = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }
        info.ArgumentList.Add("--onedrive-provider-probe");
        info.ArgumentList.Add(rootPath);
        return info;
    }

    private static CloudFilesProviderStatusQuery Failed() =>
        new(unchecked((int)0x80004005), null);
}
