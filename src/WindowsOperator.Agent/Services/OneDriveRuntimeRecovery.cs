using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Services;

internal sealed class WindowsOneDriveRuntimeRecovery : IOneDriveRuntimeRecovery
{
    internal const string RecoveryComputerName = "WIN-UUKQS009K4J";
    private const int RecoveryAttempts = 2;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public OneDriveRuntimeEvidence Probe(string rootPath, OneDriveProviderReadiness provider)
    {
        _ = rootPath;
        var processes = SnapshotOneDriveProcesses();
        var configuredSessionId = GetCurrentProcessSessionId();
        var interactiveSession = GetInteractiveSession(configuredSessionId);
        var activeSession = interactiveSession?.State == "active" ? interactiveSession.SessionId : (int?)null;
        var targetSessionId = interactiveSession?.SessionId;
        var process = processes
            .Where(candidate => candidate.SessionId == targetSessionId)
            .Select(candidate => (OneDriveProcessSnapshot?)candidate)
            .FirstOrDefault()
            ;
        var recoveryAllowed = IsRecoveryConfigurationAllowed(Environment.MachineName, ExpectedUser());

        return new OneDriveRuntimeEvidence
        {
            ComputerName = Environment.MachineName,
            RecoveryAllowed = recoveryAllowed,
            ProcessPresent = process is not null,
            ProcessSessionId = process?.SessionId,
            ConfiguredSessionId = targetSessionId,
            ActiveInteractiveSessionId = activeSession,
            InteractiveUser = interactiveSession?.UserName,
            InteractiveSessionState = interactiveSession?.State,
            InteractiveSessionProtocol = interactiveSession?.Protocol,
            ProviderReady = provider.Ready,
            ProviderReason = provider.Reason,
            AuthenticationRequired = IsAuthenticationRequired(provider.Reason),
            RecoveryActions = BuildProbeActions(
                recoveryAllowed,
                targetSessionId,
                activeSession,
                interactiveSession,
                process,
                provider),
        };
    }

    public async Task<OneDriveRuntimeEvidence> EnsureReadyAsync(
        string rootPath,
        Func<OneDriveProviderReadiness> providerProbe,
        CancellationToken cancellationToken)
    {
        var initialProvider = providerProbe();
        var initial = Probe(rootPath, initialProvider);
        if (IsOperational(initial))
        {
            return initial;
        }

        if (!initial.RecoveryAllowed)
        {
            return initial with
            {
                ProviderReason = "recovery_computer_not_allowlisted",
                RecoveryActions = initial.RecoveryActions.Append("recovery_refused_not_allowlisted").Distinct().ToArray(),
            };
        }

        if (initial.ActiveInteractiveSessionId is null ||
            !string.Equals(initial.InteractiveSessionState, "active", StringComparison.OrdinalIgnoreCase) ||
            !IsInteractiveSessionProtocol(initial.InteractiveSessionProtocol) ||
            !string.Equals(initial.InteractiveUser, "Administrator", StringComparison.OrdinalIgnoreCase))
        {
            return initial with
            {
                ProviderReason = "target_rdp_session_not_ready",
                RecoveryActions = initial.RecoveryActions
                    .Append($"operator_open_administrator_rdp_session_{initial.ConfiguredSessionId?.ToString() ?? "dynamic"}")
                    .Distinct().ToArray(),
            };
        }

        if (!initial.ProcessPresent || initial.ProcessSessionId != initial.ConfiguredSessionId)
        {
            return initial with
            {
                ProviderReason = initial.ProcessPresent
                    ? "onedrive_process_in_wrong_session"
                    : "onedrive_process_absent",
                RecoveryActions = initial.RecoveryActions.Append("host_recovery_pending").Distinct().ToArray(),
            };
        }

        if (initial.AuthenticationRequired)
        {
            return initial with
            {
                RecoveryActions = initial.RecoveryActions.Append("operator_sign_in_required").Distinct().ToArray(),
            };
        }

        if (!IsRestartSafe(initial.ProviderReason))
        {
            return initial with
            {
                RecoveryActions = initial.RecoveryActions.Append("operator_inspect_provider_probe").Distinct().ToArray(),
            };
        }

        var actions = initial.RecoveryActions.ToList();
        var timeout = RecoveryTimeout();
        for (var attempt = 1; attempt <= RecoveryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actions.Add($"provider_readiness_poll_{attempt}");

            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = providerProbe();
                var evidence = Probe(rootPath, provider) with
                {
                    RecoveryActions = actions.Distinct().ToArray(),
                };
                if (IsOperational(evidence))
                {
                    actions.Add("onedrive_provider_ready");
                    return evidence with { RecoveryActions = actions.Distinct().ToArray() };
                }

                if (evidence.AuthenticationRequired)
                {
                    actions.Add("operator_sign_in_required");
                    return evidence with { RecoveryActions = actions.Distinct().ToArray() };
                }

                if (evidence.ActiveInteractiveSessionId is null ||
                    !evidence.ProcessPresent ||
                    evidence.ProcessSessionId != evidence.ConfiguredSessionId)
                {
                    actions.Add("runtime_changed_during_provider_poll");
                    return evidence with { RecoveryActions = actions.Distinct().ToArray() };
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            actions.Add($"onedrive_readiness_timeout_{attempt}");
        }

        var finalProvider = providerProbe();
        var finalEvidence = Probe(rootPath, finalProvider);
        return finalEvidence with
        {
            ProviderReason = finalEvidence.ProviderReason ?? "onedrive_provider_readiness_timeout",
            RecoveryActions = actions.Distinct().ToArray(),
        };
    }

    internal static bool IsComputerAllowlisted(string computerName)
    {
        if (!string.Equals(computerName, RecoveryComputerName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS");
        return !string.IsNullOrWhiteSpace(raw) && raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, computerName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsOperational(OneDriveRuntimeEvidence evidence) =>
        evidence.ProviderReady &&
        evidence.RecoveryAllowed &&
        evidence.ProcessPresent &&
        evidence.ConfiguredSessionId is int targetSessionId &&
        evidence.ActiveInteractiveSessionId == targetSessionId &&
        evidence.ProcessSessionId == targetSessionId &&
        string.Equals(evidence.InteractiveUser, "Administrator", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(evidence.InteractiveSessionState, "active", StringComparison.OrdinalIgnoreCase) &&
        IsInteractiveSessionProtocol(evidence.InteractiveSessionProtocol);

    internal static bool IsInteractiveSessionProtocol(int? protocol) =>
        protocol is 0 or 2;

    internal static bool IsAuthenticationRequired(string? reason) =>
        string.Equals(reason, "sync_root_provider_disconnected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "operator_sign_in_required", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRestartSafe(string? reason) =>
        string.Equals(reason, "sync_root_provider_terminated", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "sync_root_provider_error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "sync_root_provider_connectivity_lost", StringComparison.OrdinalIgnoreCase);

    private static string ExpectedUser() =>
        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_USER")?.Trim() is { Length: > 0 } configured
            ? configured
            : "Administrator";

    internal static bool IsRecoveryConfigurationAllowed(string computerName, string configuredUser) =>
        IsComputerAllowlisted(computerName) &&
        string.Equals(configuredUser, "Administrator", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan RecoveryTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_TIMEOUT_SECONDS");
        return int.TryParse(raw, out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60))
            : TimeSpan.FromSeconds(20);
    }

    private static InteractiveSession? GetInteractiveSession(int? preferredSessionId)
    {
        if (!OperatingSystem.IsWindows() ||
            !WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count) ||
            sessions == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var expectedUser = ExpectedUser();
            var sessionsById = new List<InteractiveSession>();
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var session = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(sessions, index * size));
                if (session.SessionId == 0)
                {
                    continue;
                }

                var userName = QuerySessionString(session.SessionId, WtsInfoClass.UserName);
                sessionsById.Add(new InteractiveSession(
                    session.SessionId,
                    userName ?? string.Empty,
                    SessionStateName(session.State),
                    QuerySessionUInt16(session.SessionId, WtsInfoClass.ClientProtocolType)));
            }

            var preferred = sessionsById.FirstOrDefault(candidate => candidate.SessionId == preferredSessionId);
            if (preferred is not null)
            {
                return preferred;
            }

            return sessionsById.FirstOrDefault(candidate =>
                    candidate.State == "active" &&
                    IsInteractiveSessionProtocol(candidate.Protocol) &&
                    string.Equals(candidate.UserName, expectedUser, StringComparison.OrdinalIgnoreCase))
                ?? sessionsById.FirstOrDefault(candidate =>
                    candidate.State == "disconnected" &&
                    string.Equals(candidate.UserName, expectedUser, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            WTSFreeMemory(sessions);
        }
    }

    private static int? GetCurrentProcessSessionId()
    {
        try
        {
            return Environment.ProcessId > 0 ? Process.GetCurrentProcess().SessionId : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? QuerySessionString(int sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                infoClass,
                out var buffer,
                out var bytes) ||
            buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return bytes <= sizeof(char) ? null : Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static ushort? QuerySessionUInt16(int sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                infoClass,
                out var buffer,
                out _) ||
            buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return unchecked((ushort)Marshal.ReadInt16(buffer));
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static IReadOnlyList<string> BuildProbeActions(
        bool recoveryAllowed,
        int? configuredSession,
        int? activeSession,
        InteractiveSession? interactiveSession,
        OneDriveProcessSnapshot? process,
        OneDriveProviderReadiness provider)
    {
        var actions = new List<string>();
        if (!recoveryAllowed)
        {
            actions.Add("recovery_disabled_until_computer_allowlisted");
        }
        if (activeSession is null ||
            !string.Equals(interactiveSession?.UserName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
            !IsInteractiveSessionProtocol(interactiveSession?.Protocol))
        {
            actions.Add($"operator_open_administrator_rdp_session_{configuredSession?.ToString() ?? "dynamic"}");
        }
        if (process is null)
        {
            actions.Add("onedrive_process_absent");
        }
        else if (configuredSession is null || process.Value.SessionId != configuredSession)
        {
            actions.Add("onedrive_process_in_wrong_session");
        }
        if (!provider.Ready && IsAuthenticationRequired(provider.Reason))
        {
            actions.Add("operator_sign_in_required");
        }
        return actions;
    }

    private static IReadOnlyList<OneDriveProcessSnapshot> SnapshotOneDriveProcesses()
    {
        var result = new List<OneDriveProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName("OneDrive"))
        {
            try
            {
                result.Add(new OneDriveProcessSnapshot(
                    process.Id,
                    process.SessionId,
                    TryProcessPath(process)));
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static string? TryProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private readonly record struct OneDriveProcessSnapshot(int ProcessId, int SessionId, string? Path);

    private sealed record InteractiveSession(int SessionId, string UserName, string State, ushort? Protocol);

    private static string SessionStateName(WtsConnectState state) => state switch
    {
        WtsConnectState.Active => "active",
        WtsConnectState.Connected => "connected",
        WtsConnectState.ConnectQuery => "connect_query",
        WtsConnectState.Shadow => "shadow",
        WtsConnectState.Disconnected => "disconnected",
        WtsConnectState.Idle => "idle",
        WtsConnectState.Listen => "listen",
        WtsConnectState.Reset => "reset",
        WtsConnectState.Down => "down",
        WtsConnectState.Init => "init",
        _ => "unknown",
    };

    private enum WtsConnectState
    {
        Active = 0,
        Connected = 1,
        ConnectQuery = 2,
        Shadow = 3,
        Disconnected = 4,
        Idle = 5,
        Listen = 6,
        Reset = 7,
        Down = 8,
        Init = 9,
    }

    private enum WtsInfoClass
    {
        UserName = 5,
        ClientProtocolType = 16,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public WtsConnectState State;
    }

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr serverHandle,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr serverHandle,
        int sessionId,
        WtsInfoClass infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
