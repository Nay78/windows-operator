using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Services;

public sealed class OwnedSessionRegistry
{
    private const uint WmClose = 0x0010;
    private readonly WorkbenchRunStore _runs;

    public OwnedSessionRegistry(WorkbenchRunStore runs)
    {
        _runs = runs;
    }

    public WorkbenchSessionResult UpsertEdgeSession(BrowserEdgeSessionStateResult state, string? runId)
    {
        var existing = TryReadRecord(state.SessionId);
        var run = runId is null && existing is not null
            ? existing.ArtifactRoot
            : _runs.ResolveRun(runId ?? existing?.ArtifactRoot.RunId ?? state.SessionId, "session");
        var now = DateTimeOffset.UtcNow;
        var record = new OwnedSessionRecord(
            state.SessionId,
            "browser.edge",
            state.IsAlive,
            run,
            state.ProcessId is null ? Array.Empty<int>() : new[] { state.ProcessId.Value },
            state.Hwnd is null ? Array.Empty<long>() : new[] { state.Hwnd.Value },
            state.Title,
            state.Url,
            existing?.CreatedAtUtc ?? now,
            now);

        var result = ToResult(record, state.Actions, Array.Empty<string>(), state.Errors, state.ObservedAtUtc);
        WriteRecord(record);
        _runs.WriteJson(run, "state.json", result);
        _runs.AppendEvent(run, "session_observed", new { record.SessionId, record.Kind, record.IsAlive });
        return result;
    }

    public WorkbenchSessionResult GetSession(string sessionId)
    {
        var record = RequireRecord(sessionId);
        return ToResult(
            record,
            new[] { "session_state_observed" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    public WorkbenchSessionCleanupResult CleanupSession(string sessionId)
    {
        var record = RequireRecord(sessionId);
        var actions = new List<string>();
        var errors = new List<string>();
        var matchedWindows = 0;
        var closedWindows = 0;
        var failedWindows = 0;
        var matchedProcesses = 0;
        var closedProcesses = 0;
        var preservedProcesses = 0;
        var failedProcesses = 0;

        if (!OperatingSystem.IsWindows())
        {
            actions.Add("session_cleanup_skipped:not_windows");
            return new WorkbenchSessionCleanupResult(
                false,
                record.SessionId,
                record.Kind,
                record.Hwnds.Count,
                0,
                record.Hwnds.Count,
                0,
                record.OwnedProcessIds.Count,
                0,
                record.OwnedProcessIds.Count,
                0,
                actions,
                errors,
                DateTimeOffset.UtcNow);
        }

        foreach (var hwnd in record.Hwnds.Distinct())
        {
            matchedWindows++;
            if (TryCloseWindow(new IntPtr(hwnd)))
            {
                closedWindows++;
                continue;
            }

            failedWindows++;
            errors.Add($"Failed to close owned session window hwnd={hwnd}.");
        }

        foreach (var processId in record.OwnedProcessIds.Distinct())
        {
            matchedProcesses++;
            if (closedWindows > 0)
            {
                preservedProcesses++;
                continue;
            }

            if (TryKillProcess(processId))
            {
                closedProcesses++;
                continue;
            }

            failedProcesses++;
            errors.Add($"Failed to close owned session process pid={processId}.");
        }

        actions.Add(
            $"session_cleanup:windows={matchedWindows};closed={closedWindows};processes={matchedProcesses};killed={closedProcesses};failed={failedWindows + failedProcesses}");

        var success = errors.Count == 0;
        var updated = record with { IsAlive = !success, UpdatedAtUtc = DateTimeOffset.UtcNow };
        WriteRecord(updated);
        _runs.AppendEvent(record.ArtifactRoot, "session_cleanup", new { record.SessionId, success });
        _runs.WriteJson(
            record.ArtifactRoot,
            "state.json",
            ToResult(
                updated,
                actions,
                Array.Empty<string>(),
                errors,
                DateTimeOffset.UtcNow));

        return new WorkbenchSessionCleanupResult(
            success,
            record.SessionId,
            record.Kind,
            matchedWindows,
            closedWindows,
            0,
            failedWindows,
            matchedProcesses,
            closedProcesses,
            preservedProcesses,
            failedProcesses,
            actions,
            errors,
            DateTimeOffset.UtcNow);
    }

    private WorkbenchSessionResult ToResult(
        OwnedSessionRecord record,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc) =>
        new(
            errors.Count == 0,
            record.SessionId,
            record.Kind,
            record.IsAlive,
            record.ArtifactRoot,
            record.OwnedProcessIds,
            record.Hwnds,
            record.Title,
            record.Url,
            Path.Combine(record.ArtifactRoot.Path, "state.json"),
            actions,
            warnings,
            errors,
            record.CreatedAtUtc,
            observedAtUtc);

    private OwnedSessionRecord RequireRecord(string sessionId) =>
        TryReadRecord(sessionId)
        ?? throw new OperatorFailureException(OperatorErrors.AuthUnavailable($"Workbench session was not found: {sessionId}"));

    private OwnedSessionRecord? TryReadRecord(string sessionId)
    {
        var path = RecordPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OwnedSessionRecord>(
            File.ReadAllText(path),
            OperatorJson.SerializerOptions);
    }

    private void WriteRecord(OwnedSessionRecord record)
    {
        var path = RecordPath(record.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, OperatorJson.SerializerOptions));
    }

    private string RecordPath(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new OperatorFailureException(OperatorErrors.AuthUnavailable("Workbench session id is required."));
        }

        var safeSessionId = WorkbenchRunStore.SanitizePathSegment(sessionId, string.Empty);
        if (string.IsNullOrWhiteSpace(safeSessionId))
        {
            throw new OperatorFailureException(OperatorErrors.AuthUnavailable("Workbench session id is required."));
        }

        return Path.Combine(_runs.ExchangeRoot, "sessions", safeSessionId + ".json");
    }

    private static bool TryCloseWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!IsWindow(hwnd))
            {
                return true;
            }

            PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow <= deadline)
            {
                if (!IsWindow(hwnd))
                {
                    return true;
                }

                Thread.Sleep(100);
            }
        }
        catch
        {
            return false;
        }

        return !IsWindow(hwnd);
    }

    private static bool TryKillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            process.CloseMainWindow();
            if (process.WaitForExit(2000))
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private sealed record OwnedSessionRecord(
        string SessionId,
        string Kind,
        bool IsAlive,
        WorkbenchRunRef ArtifactRoot,
        IReadOnlyList<int> OwnedProcessIds,
        IReadOnlyList<long> Hwnds,
        string? Title,
        string? Url,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
