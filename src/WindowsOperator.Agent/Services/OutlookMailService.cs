using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class OutlookMailService : IMailService
{
    private static readonly TimeSpan WorkerTimeout = ReadTimeout(
        "WINDOWS_OPERATOR_MAIL_WORKER_TIMEOUT_SECONDS",
        TimeSpan.FromSeconds(90));
    private readonly object _stateLock = new();
    private readonly IOptions<OperatorOptions> _options;
    private string? _lastWorkerError;

    public OutlookMailService(IOptions<OperatorOptions> options)
    {
        _options = options;
    }

    public async Task<MailFoldersResult> ListFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            WorkerRequest("list-folders") with { ListFolders = request },
            cancellationToken);
        return response.Folders ?? throw MailUnavailable("Mail worker returned no folder list.");
    }

    public async Task<MailSearchResult> SearchMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            WorkerRequest("search-messages") with { Search = request },
            cancellationToken);
        return response.Messages ?? throw MailUnavailable("Mail worker returned no message list.");
    }

    public async Task<MailDownloadResult> DownloadAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            WorkerRequest("download-attachments") with { Download = request },
            cancellationToken);
        return response.Download ?? throw MailUnavailable("Mail worker returned no download result.");
    }

    public Task<MailDownloadResult> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(ExchangeRoot(), "runs", SafeFileName(runId, "missing"), "result.json");
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(OperatorErrors.MailRunNotFound(runId));
        }

        return Task.FromResult(
            JsonSerializer.Deserialize<MailDownloadResult>(File.ReadAllText(path), OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(OperatorErrors.MailRunNotFound(runId)));
    }

    public Task<MailStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            return Task.FromResult(new MailStatusResult(
                File.Exists(WorkerPath()),
                CountOutlookProcesses(visible: true),
                CountOutlookProcesses(visible: false),
                _lastWorkerError,
                DateTimeOffset.UtcNow));
        }
    }

    private MailWorkerRequest WorkerRequest(string operation) =>
        new()
        {
            Operation = operation,
            Policy = _options.Value.Mail,
        };

    private async Task<MailWorkerResponse> RunWorkerAsync(MailWorkerRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw MailUnavailable("Outlook mail worker requires Windows.");
        }

        var workerPath = WorkerPath();
        if (!File.Exists(workerPath))
        {
            throw MailUnavailable($"Mail worker missing: {workerPath}");
        }

        var operationId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var runRoot = Path.Combine(StateRoot(), "run", "mail-worker", operationId);
        Directory.CreateDirectory(runRoot);
        var requestPath = Path.Combine(runRoot, "request.json");
        var responsePath = Path.Combine(runRoot, "response.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, OperatorJson.SerializerOptions),
            cancellationToken);
        var progressPath = Path.Combine(runRoot, "progress.log");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = DotnetPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.ArgumentList.Add(workerPath);
        process.StartInfo.ArgumentList.Add(requestPath);
        process.StartInfo.ArgumentList.Add(responsePath);

        if (!process.Start())
        {
            throw MailUnavailable("Unable to start mail worker.");
        }

        var startedAt = Stopwatch.StartNew();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(WorkerTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            SetLastWorkerError(BuildWorkerTimeoutDetail(
                operationId,
                runRoot,
                requestPath,
                responsePath,
                progressPath,
                process.Id,
                WorkerTimeout,
                startedAt.ElapsedMilliseconds));
            throw MailUnavailable(_lastWorkerError!);
        }
        catch
        {
            KillProcessTree(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!File.Exists(responsePath))
        {
            var detail = $"Mail worker wrote no response. ExitCode={process.ExitCode}. Stderr={stderr.Trim()} Stdout={stdout.Trim()}";
            SetLastWorkerError(detail);
            throw MailUnavailable(detail);
        }

        var response = JsonSerializer.Deserialize<MailWorkerResponse>(
            await File.ReadAllTextAsync(responsePath, cancellationToken),
            OperatorJson.SerializerOptions);
        if (response is null)
        {
            throw MailUnavailable($"Mail worker returned invalid JSON: {responsePath}");
        }

        if (response.Error is not null)
        {
            SetLastWorkerError(response.Error.Details?.GetValueOrDefault("detail") ?? response.Error.Message);
            throw new OperatorFailureException(response.Error);
        }

        if (process.ExitCode != 0)
        {
            var detail = $"Mail worker exited {process.ExitCode}. Stderr={stderr.Trim()}";
            SetLastWorkerError(detail);
            throw MailUnavailable(detail);
        }

        SetLastWorkerError(null);
        return response;
    }

    internal static string BuildWorkerTimeoutDetail(
        string operationId,
        string runRoot,
        string requestPath,
        string responsePath,
        string progressPath,
        int? processId,
        TimeSpan timeout,
        long elapsedMs)
    {
        var detail = $"Mail worker timed out after {timeout.TotalSeconds:n0}s. " +
            $"OperationId={operationId}. ElapsedMs={elapsedMs}. OperationRoot={runRoot}. " +
            $"RequestPath={requestPath}. ResponsePath={responsePath}. ProgressPath={progressPath}.";
        if (processId is int pid && pid > 0)
        {
            detail += $" WorkerPid={pid}.";
        }

        var lastStage = ReadLastProgressStage(progressPath);
        if (!string.IsNullOrWhiteSpace(lastStage))
        {
            detail += $" LastStage={lastStage}.";
        }

        return detail;
    }

    internal static string? ReadLastProgressStage(string progressPath)
    {
        if (!File.Exists(progressPath))
        {
            return null;
        }

        var lastLine = File.ReadLines(progressPath)
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(lastLine))
        {
            return null;
        }

        var separator = lastLine.IndexOf('\t');
        return separator >= 0
            ? lastLine[(separator + 1)..].Trim()
            : lastLine.Trim();
    }

    private static string DotnetPath() =>
        Environment.ProcessPath is { Length: > 0 } path && Path.GetFileName(path).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            ? path
            : "dotnet";

    private static string WorkerPath()
    {
        var root = StateRoot();
        var debug = Path.Combine(root, "artifacts", "bin", "WindowsOperator.MailWorker", "Debug", "net8.0-windows10.0.19041.0", "WindowsOperator.MailWorker.dll");
        if (File.Exists(debug))
        {
            return debug;
        }

        var release = Path.Combine(root, "artifacts", "bin", "WindowsOperator.MailWorker", "Release", "net8.0-windows10.0.19041.0", "WindowsOperator.MailWorker.dll");
        if (File.Exists(release))
        {
            return release;
        }

        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WindowsOperator.MailWorker",
            "bin",
            configuration,
            "net8.0-windows10.0.19041.0",
            "WindowsOperator.MailWorker.dll"));
    }

    private static string StateRoot() =>
        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOperator");

    private static string ExchangeRoot() =>
        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_EXCHANGE_ROOT")
        ?? @"Z:\operator-exchange";

    private static OperatorFailureException MailUnavailable(string detail) =>
        new(OperatorErrors.MailUnavailable(detail));

    private static int CountOutlookProcesses(bool visible)
    {
        var count = 0;
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                if (HasMainWindow(process) == visible)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool HasMainWindow(Process process)
    {
        try
        {
            process.Refresh();
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private void SetLastWorkerError(string? detail)
    {
        lock (_stateLock)
        {
            _lastWorkerError = detail;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static TimeSpan ReadTimeout(string name, TimeSpan fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return fallback;
    }

    private static string SafeFileName(string raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length > 180 ? value[..180] : value;
    }
}
