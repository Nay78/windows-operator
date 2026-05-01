using System.Diagnostics;
using System.Text.Json;
using WindowsOperator.Core;
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
    private string? _lastWorkerError;
    private MailRecoveryResult? _lastRecovery;

    public async Task<IReadOnlyList<MailFolderRef>> ListFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            new MailWorkerRequest { Operation = "list-folders", ListFolders = request },
            cancellationToken);
        return response.Folders ?? throw MailUnavailable("Mail worker returned no folder list.");
    }

    public async Task<IReadOnlyList<MailMessageRef>> SearchMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            new MailWorkerRequest { Operation = "search-messages", Search = request },
            cancellationToken);
        return response.Messages ?? throw MailUnavailable("Mail worker returned no message list.");
    }

    public async Task<MailDownloadResult> DownloadAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            new MailWorkerRequest { Operation = "download-attachments", Download = request },
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
                _lastRecovery,
                DateTimeOffset.UtcNow));
        }
    }

    public async Task<MailSyncResult> SyncAsync(MailSyncRequest request, CancellationToken cancellationToken)
    {
        var response = await RunWorkerAsync(
            new MailWorkerRequest { Operation = "sync", Sync = request },
            cancellationToken);
        return response.Sync ?? throw MailUnavailable("Mail worker returned no sync result.");
    }

    public Task<MailRecoveryResult> RecoverAsync(MailRecoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = NormalizeRecoveryMode(request.Mode);
        var actions = new List<string>();
        var errors = new List<string>();

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw MailUnavailable("Outlook recovery requires Windows.");
            }

            if (mode == "force")
            {
                StopOutlookProcesses(includeVisible: true, actions, errors);
            }
            else
            {
                StopOutlookProcesses(includeVisible: false, actions, errors);
            }

            if (mode is "profile" or "force")
            {
                var visible = CountOutlookProcesses(visible: true);
                if (visible > 0 && mode != "force")
                {
                    errors.Add("Classic Outlook is visible. Close Outlook or use force recovery.");
                }
                else
                {
                    RunOutlookSwitch("/cleanreminders", actions, errors);
                    RunOutlookSwitch("/resetnavpane", actions, errors);
                    TryCloseVisibleOutlook(actions, errors);
                    StopOutlookProcesses(includeVisible: mode == "force", actions, errors);
                }
            }
        }
        catch (Exception ex) when (ex is not OperatorFailureException)
        {
            errors.Add(ex.Message);
        }

        var result = new MailRecoveryResult(
            mode,
            errors.Count == 0,
            actions,
            errors,
            CountOutlookProcesses(visible: true),
            CountOutlookProcesses(visible: false),
            DateTimeOffset.UtcNow);
        lock (_stateLock)
        {
            _lastRecovery = result;
        }

        return Task.FromResult(result);
    }

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
            SetLastWorkerError($"Mail worker timed out after {WorkerTimeout.TotalSeconds:n0}s. Operation root: {runRoot}");
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

    private static string NormalizeRecoveryMode(string? raw)
    {
        var mode = string.IsNullOrWhiteSpace(raw) ? "basic" : raw.Trim().ToLowerInvariant();
        return mode is "basic" or "profile" or "force"
            ? mode
            : throw MailUnavailable($"Unsupported mail recovery mode: {raw}");
    }

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

    private static void StopOutlookProcesses(bool includeVisible, List<string> actions, List<string> errors)
    {
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                var visible = HasMainWindow(process);
                if (visible && !includeVisible)
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    actions.Add(visible ? $"killed_visible_outlook:{process.Id}" : $"killed_headless_outlook:{process.Id}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to kill Outlook PID {process.Id}: {ex.Message}");
                }
            }
        }

        RemoveOutlookTempFilesIfIdle(actions, errors);
    }

    private static void RunOutlookSwitch(string arguments, List<string> actions, List<string> errors)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "outlook.exe",
                Arguments = arguments,
                UseShellExecute = true,
            });
            actions.Add($"started_outlook:{arguments}");
            Thread.Sleep(TimeSpan.FromSeconds(8));
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to run outlook.exe {arguments}: {ex.Message}");
        }
    }

    private static void TryCloseVisibleOutlook(List<string> actions, List<string> errors)
    {
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                if (!HasMainWindow(process))
                {
                    continue;
                }

                try
                {
                    if (process.CloseMainWindow())
                    {
                        actions.Add($"close_requested:{process.Id}");
                        process.WaitForExit(10000);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to close Outlook PID {process.Id}: {ex.Message}");
                }
            }
        }
    }

    private static void RemoveOutlookTempFilesIfIdle(List<string> actions, List<string> errors)
    {
        if (Process.GetProcessesByName("OUTLOOK").Length > 0)
        {
            return;
        }

        var outlookDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Outlook");
        if (!Directory.Exists(outlookDataPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(outlookDataPath, "~*.tmp"))
        {
            try
            {
                File.Delete(path);
                actions.Add($"deleted_temp:{Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to delete temp file {path}: {ex.Message}");
            }
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
