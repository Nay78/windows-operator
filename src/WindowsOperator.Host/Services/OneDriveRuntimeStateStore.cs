using System.Text.Json;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Host.Services;

public sealed class OneDriveRuntimeStateStore
{
    private readonly object _gate = new();
    private readonly string _statePath;

    public OneDriveRuntimeStateStore()
        : this(ResolveStatePath())
    {
    }

    internal OneDriveRuntimeStateStore(string statePath)
    {
        _statePath = Path.GetFullPath(statePath);
    }

    public OneDriveRuntimeSupervisorState? Read()
    {
        lock (_gate)
        {
            return ReadCore();
        }
    }

    public bool ShouldAttempt(DateTimeOffset now)
    {
        var state = Read();
        return state?.NextAttemptAtUtc is not DateTimeOffset next || next <= now;
    }

    public void BeginAttempt(string computerName, bool recoveryAllowed, int targetSessionId = 0)
    {
        lock (_gate)
        {
            var previous = ReadCore();
            var now = DateTimeOffset.UtcNow;
            WriteCore(new OneDriveRuntimeSupervisorState
            {
                ComputerName = computerName,
                RecoveryAllowed = recoveryAllowed,
                TargetSessionId = targetSessionId > 0
                    ? targetSessionId
                    : previous?.TargetSessionId ?? 0,
                State = "recovering",
                SessionState = previous?.SessionState,
                ProcessId = previous?.ProcessId,
                ProcessSessionId = previous?.ProcessSessionId,
                AttemptCount = (previous?.AttemptCount ?? 0) + 1,
                RestartCount = previous?.RestartCount ?? 0,
                ConsecutiveFailureCount = previous?.ConsecutiveFailureCount ?? 0,
                LastAttemptAtUtc = now,
                LastSuccessAtUtc = previous?.LastSuccessAtUtc,
                Actions = new[] { "runtime_recovery_started" },
                ObservedAtUtc = now,
            });
        }
    }

    public void RecordSuccess(OneDriveConfigurationRecoveryResult result)
    {
        lock (_gate)
        {
            var previous = ReadCore();
            var now = DateTimeOffset.UtcNow;
            WriteCore(new OneDriveRuntimeSupervisorState
            {
                ComputerName = result.ComputerName,
                RecoveryAllowed = true,
                TargetSessionId = result.TargetSessionId,
                State = string.Equals(result.TargetSessionState, "active", StringComparison.OrdinalIgnoreCase)
                    ? "ready"
                    : "waiting_for_session",
                SessionState = result.TargetSessionState,
                ProcessId = result.ProcessId,
                ProcessSessionId = result.ProcessSessionId,
                AttemptCount = previous?.AttemptCount ?? 1,
                RestartCount = (previous?.RestartCount ?? 0) + (result.RuntimeStarted ? 1 : 0),
                ConsecutiveFailureCount = 0,
                LastAttemptAtUtc = previous?.LastAttemptAtUtc ?? now,
                LastSuccessAtUtc = now,
                Reason = string.Equals(result.TargetSessionState, "active", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "target_rdp_session_not_ready",
                Actions = result.Actions,
                ObservedAtUtc = now,
            });
        }
    }

    public void RecordFailure(OperatorError error)
    {
        lock (_gate)
        {
            var previous = ReadCore();
            var details = error.Details ?? new Dictionary<string, string>();
            var now = DateTimeOffset.UtcNow;
            var sessionState = Value(details, "interactiveSessionState");
            var sessionChanged = previous?.SessionState is not null &&
                sessionState is not null &&
                !string.Equals(previous.SessionState, sessionState, StringComparison.OrdinalIgnoreCase);
            var failures = sessionChanged ? 1 : Math.Min((previous?.ConsecutiveFailureCount ?? 0) + 1, 1000);
            var delaySeconds = Math.Min(300, 15 * (1 << Math.Min(failures - 1, 4)));
            var detail = Value(details, "detail") ?? error.Code;
            var reason = Value(details, "reason") ?? detail.Split(';', 2)[0];
            var state = reason.StartsWith("target_rdp_session_", StringComparison.Ordinal)
                ? "waiting_for_session"
                : reason.StartsWith("onedrive_recovery_denied", StringComparison.Ordinal) ||
                  reason.StartsWith("target_rdp_session_not_allowlisted", StringComparison.Ordinal)
                    ? "disabled"
                    : "failed";

            WriteCore(new OneDriveRuntimeSupervisorState
            {
                ComputerName = Value(details, "computerName") ?? previous?.ComputerName ?? Environment.MachineName,
                RecoveryAllowed = bool.TryParse(Value(details, "recoveryAllowed"), out var allowed) && allowed,
                TargetSessionId = int.TryParse(Value(details, "targetSessionId") ?? Value(details, "configuredSessionId"), out var targetSessionId)
                    ? targetSessionId
                    : previous?.TargetSessionId ?? 0,
                State = state,
                SessionState = sessionState,
                ProcessId = null,
                ProcessSessionId = int.TryParse(Value(details, "processSessionId"), out var processSessionId)
                    ? processSessionId
                    : null,
                Reason = reason,
                AttemptCount = previous?.AttemptCount ?? 1,
                RestartCount = previous?.RestartCount ?? 0,
                ConsecutiveFailureCount = failures,
                LastAttemptAtUtc = previous?.LastAttemptAtUtc ?? now,
                LastSuccessAtUtc = previous?.LastSuccessAtUtc,
                NextAttemptAtUtc = now.AddSeconds(delaySeconds),
                Actions = SplitActions(Value(details, "actions")),
                ObservedAtUtc = now,
            });
        }
    }

    private OneDriveRuntimeSupervisorState? ReadCore()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OneDriveRuntimeSupervisorState>(
                File.ReadAllText(_statePath),
                OperatorJson.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteCore(OneDriveRuntimeSupervisorState state)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(state, OperatorJson.SerializerOptions) + Environment.NewLine);
        File.Move(temporaryPath, _statePath, true);
    }

    private static string ResolveStatePath()
    {
        var stateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_HOST_STATE_ROOT");
        if (string.IsNullOrWhiteSpace(stateRoot))
        {
            stateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        }
        if (string.IsNullOrWhiteSpace(stateRoot))
        {
            stateRoot = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsOperator")
                : Path.Combine(Path.GetTempPath(), "WindowsOperator", Environment.ProcessId.ToString());
        }

        return Path.Combine(stateRoot, "run", "onedrive-runtime-supervisor.json");
    }

    private static string? Value(IReadOnlyDictionary<string, string> details, string key) =>
        details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) && value != "none"
            ? value
            : null;

    private static IReadOnlyList<string> SplitActions(string? actions) =>
        string.IsNullOrWhiteSpace(actions)
            ? Array.Empty<string>()
            : actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
