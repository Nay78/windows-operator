using System.Diagnostics;
using System.Text.Json;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Services;

internal static class IsolatedOneDriveHydration
{
    public static async Task<OneDriveFilesOnDemandService.HydrationSnapshot> ReadAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => ReadBounded(path, timeout, cancellationToken),
            CancellationToken.None);
    }

    public static int RunChild(string path)
    {
        try
        {
            var snapshot = OneDriveFilesOnDemandService.HydrateDirect(path);
            WriteResponse(new HydrationResponse(true, snapshot, null));
            return 0;
        }
        catch (OperatorFailureException failure)
        {
            WriteResponse(new HydrationResponse(false, null, failure.Error));
            return 2;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            IOException or
            InvalidOperationException)
        {
            WriteResponse(new HydrationResponse(
                false,
                null,
                OperatorErrors.OneDriveHydrationFailed($"isolated hydration failed;exception={exception.GetType().Name}")));
            return 2;
        }
    }

    private static OneDriveFilesOnDemandService.HydrationSnapshot ReadBounded(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(BuildStartInfo(path))
            ?? throw new OperatorFailureException(OperatorErrors.OneDriveHydrationFailed(
                "isolated hydration process did not start."));
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!process.WaitForExit(250))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Stop(process);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Stop(process);
                throw new OperatorFailureException(OperatorErrors.OneDriveHydrationTimeout(
                    $"isolated hydration exceeded {timeout.TotalSeconds:0}s."));
            }
        }

        var output = process.StandardOutput.ReadToEnd();
        HydrationResponse? response = null;
        try
        {
            response = JsonSerializer.Deserialize<HydrationResponse>(output, OperatorJson.SerializerOptions);
        }
        catch (JsonException)
        {
        }

        if (process.ExitCode == 0 && response is { Success: true, Snapshot: not null })
        {
            return response.Snapshot;
        }
        if (response?.Error is not null)
        {
            throw new OperatorFailureException(response.Error);
        }

        throw new OperatorFailureException(OperatorErrors.OneDriveHydrationFailed(
            $"isolated hydration process failed;exitCode={process.ExitCode}."));
    }

    private static ProcessStartInfo BuildStartInfo(string path)
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
        info.ArgumentList.Add("--onedrive-hydration-read");
        info.ArgumentList.Add(path);
        return info;
    }

    private static void Stop(Process process)
    {
        if (process.HasExited)
        {
            return;
        }
        process.Kill(entireProcessTree: true);
        process.WaitForExit(2000);
    }

    private static void WriteResponse(HydrationResponse response) =>
        Console.Out.Write(JsonSerializer.Serialize(response, OperatorJson.SerializerOptions));

    private sealed record HydrationResponse(
        bool Success,
        OneDriveFilesOnDemandService.HydrationSnapshot? Snapshot,
        OperatorError? Error);
}
