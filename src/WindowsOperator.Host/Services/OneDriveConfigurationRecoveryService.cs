using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Host.Services;

/// <summary>
/// Starts OneDrive non-destructively from the SYSTEM Host.
/// The process is created with the dynamically resolved Administrator user's
/// token in that exact nonzero session. A disconnected allowlisted session may
/// be transferred to the console first; the Host never uses session 0.
/// </summary>
public sealed class OneDriveConfigurationRecoveryService
{
    private const uint TokenAllAccess = 0x000F01FF;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;
    private const int WtsConnectStateInfoClass = 8;
    private const int WtsClientProtocolType = 16;
    private const int RdpProtocol = 2;
    private const int DesktopAgentPort = 43119;
    private const int AddressFamilyInterNetwork = 2;
    private const int TcpTableOwnerPidListener = 3;
    private const int ConsoleSessionTransferTimeoutSeconds = 10;
    private const string RequiredUser = "Administrator";
    private const string RequiredComputer = "WIN-UUKQS009K4J";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly OneDriveRuntimeStateStore _stateStore;

    public OneDriveConfigurationRecoveryService(OneDriveRuntimeStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<OneDriveConfigurationRecoveryResult> RecoverAsync(
        OneDriveConfigurationRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                _stateStore.BeginAttempt(
                    Environment.MachineName,
                    IsRecoveryEnabled(
                        Environment.MachineName,
                        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS")),
                    request.TargetSessionId);
                var result = RecoverCore(request, cancellationToken);
                _stateStore.RecordSuccess(result);
                return result;
            }
            catch (OperatorFailureException failure)
            {
                _stateStore.RecordFailure(failure.Error);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = Unavailable($"onedrive_configuration_recovery_failed;exception={exception.GetType().Name}");
                _stateStore.RecordFailure(failure.Error);
                throw failure;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static bool IsRecoveryEnabled(string computerName, string? allowedComputer) =>
        string.Equals(computerName, RequiredComputer, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(allowedComputer, RequiredComputer, StringComparison.OrdinalIgnoreCase);

    private OneDriveConfigurationRecoveryResult RecoverCore(
        OneDriveConfigurationRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var computerName = Environment.MachineName;
        var allowedComputer = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS");
        if (!IsRecoveryEnabled(computerName, allowedComputer))
        {
            throw Unavailable($"onedrive_recovery_denied;computer={computerName};allowlistedComputer={allowedComputer ?? "<missing>"}");
        }

        var targetSessionId = ResolveTargetSessionId();
        if (targetSessionId is null)
        {
            throw Unavailable("target_rdp_session_not_found;requiredUser=Administrator;requiredProtocol=RDP-or-console");
        }

        var processes = SnapshotOneDriveProcesses();
        var existing = processes.Where(process => process.SessionId == targetSessionId.Value).ToArray();
        if (existing.Length > 1)
        {
            throw Unavailable($"multiple_onedrive_processes_in_target_session;session={targetSessionId.Value};count={existing.Length}");
        }

        var sessionState = QuerySessionString(targetSessionId.Value, WtsConnectStateInfoClass) ?? "unknown";
        var userName = QuerySessionString(targetSessionId.Value, WtsUserName) ?? string.Empty;
        var protocol = QuerySessionUInt16(targetSessionId.Value, WtsClientProtocolType);
        var transferredToConsole = false;
        if (string.Equals(sessionState, "disconnected", StringComparison.OrdinalIgnoreCase))
        {
            if (!ShouldTransferDisconnectedSession(
                    computerName,
                    allowedComputer,
                    targetSessionId.Value,
                    userName,
                    sessionState,
                    protocol))
            {
                throw Unavailable(
                    $"target_rdp_session_console_transfer_ineligible;session={targetSessionId.Value};user={userName};state={sessionState};protocol={protocol?.ToString() ?? "<unknown>"}");
            }

            TransferDisconnectedSessionToConsole(targetSessionId.Value, cancellationToken);
            sessionState = WaitForActiveSessionAfterConsoleTransfer(
                targetSessionId.Value,
                cancellationToken);
            userName = QuerySessionString(targetSessionId.Value, WtsUserName) ?? userName;
            transferredToConsole = true;
        }

        if (!string.Equals(sessionState, "active", StringComparison.OrdinalIgnoreCase))
        {
            // Clean up any legacy console/logon launcher even while the
            // target RDP session is disconnected. Do not start a replacement
            // until that same session becomes active.
            DisableDesktopAgentTask();
            var staleAgent = FindDesktopAgentListener();
            if (staleAgent is not null && staleAgent.SessionId != targetSessionId.Value)
            {
                if (!IsDesktopAgentListenerEligibleForStop(
                        staleAgent.SessionId,
                        targetSessionId.Value,
                        staleAgent.ProcessName))
                {
                    throw Unavailable(
                        $"desktop_agent_listener_identity_unexpected;pid={staleAgent.ProcessId};session={staleAgent.SessionId};process={staleAgent.ProcessName}");
                }

                EndDesktopAgentTaskIfRunning();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
                while (DateTimeOffset.UtcNow < deadline && FindDesktopAgentListener() is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(250);
                }

                var remaining = FindDesktopAgentListener();
                if (remaining is not null)
                {
                    using var staleProcess = Process.GetProcessById(remaining.ProcessId);
                    staleProcess.Kill(entireProcessTree: true);
                    if (!staleProcess.WaitForExit(5000))
                    {
                        throw Unavailable(
                            $"desktop_agent_wrong_session_stop_timeout;pid={remaining.ProcessId};session={remaining.SessionId}");
                    }
                }
            }

            if (existing.Length == 1)
            {
                return new OneDriveConfigurationRecoveryResult
                {
                    ConfigurationCleared = false,
                    RuntimeStarted = false,
                    ComputerName = computerName,
                    UserName = userName,
                    TargetSessionId = targetSessionId.Value,
                    TargetSessionState = sessionState,
                    ProcessId = existing[0].ProcessId,
                    ProcessSessionId = existing[0].SessionId,
                    Actions = new[] { "target_session_resolved", "target_session_disconnected_process_preserved" },
                };
            }

            throw Unavailable($"target_rdp_session_not_ready;session={targetSessionId.Value};user={userName};state={sessionState};requiredUser={RequiredUser};requiredProtocol=RDP-or-console");
        }

        var userToken = OpenTargetSession(targetSessionId.Value, out userName, out sessionState);
        try
        {
            var profilePath = GetProfilePath(userToken);
            var actions = new List<string>();
            if (transferredToConsole)
            {
                actions.Add("target_session_transferred_to_console");
                actions.Add("target_session_active_after_console_transfer");
            }
            actions.Add("target_session_verified");
            var warnings = new List<string>();
            EnsureDesktopAgentInSession(userToken, targetSessionId.Value, profilePath, actions, cancellationToken);
            processes = SnapshotOneDriveProcesses();
            existing = processes.Where(process => process.SessionId == targetSessionId.Value).ToArray();
            if (existing.Length > 1)
            {
                throw Unavailable($"multiple_onedrive_processes_in_target_session;session={targetSessionId.Value};count={existing.Length}");
            }
            if (existing.Length == 1)
            {
                actions.Add("onedrive_already_running_in_target_rdp_session");
                return new OneDriveConfigurationRecoveryResult
                {
                    ConfigurationCleared = false,
                    RuntimeStarted = false,
                    ComputerName = computerName,
                    UserName = userName,
                    TargetSessionId = targetSessionId.Value,
                    TargetSessionState = sessionState,
                    ProcessId = existing[0].ProcessId,
                    ProcessSessionId = existing[0].SessionId,
                    Actions = actions,
                    Warnings = warnings,
                };
            }

            var executablePath = ResolveOneDriveExecutable(profilePath, targetSessionId.Value);
            StopVerifiedStaleProcesses(processes, targetSessionId.Value, executablePath, actions, cancellationToken);
            using var startedProcess = StartOneDriveInSession(userToken, targetSessionId.Value, executablePath, profilePath);
            actions.Add("onedrive_started_in_target_rdp_session");
            var process = WaitForStableProcess(targetSessionId.Value, startedProcess.Id, cancellationToken);
            actions.Add("onedrive_target_session_process_verified");

            return new OneDriveConfigurationRecoveryResult
            {
                ConfigurationCleared = false,
                RuntimeStarted = true,
                ComputerName = computerName,
                UserName = userName,
                TargetSessionId = targetSessionId.Value,
                TargetSessionState = sessionState,
                ProcessId = process.ProcessId,
                ProcessSessionId = process.SessionId,
                Actions = actions,
                Warnings = warnings,
            };
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    private static void ValidateRequest(OneDriveConfigurationRecoveryRequest request)
    {
        if (request.ClearConfiguration)
        {
            throw new OperatorFailureException(
                OperatorErrors.InvalidRequest("clearConfiguration is not supported; OneDrive authentication configuration is operator-controlled."));
        }

        if (request.TargetSessionId < 0)
        {
            throw new OperatorFailureException(
                OperatorErrors.InvalidRequest("targetSessionId must be zero (dynamic) or a positive session identifier."));
        }
    }

    /// <summary>
    /// Allows console transfer only for the one explicitly allowlisted VM and
    /// the dynamically selected, non-session-0 Administrator desktop session.
    /// </summary>
    internal static bool ShouldTransferDisconnectedSession(
        string computerName,
        string? allowedComputer,
        int sessionId,
        string userName,
        string sessionState,
        ushort? protocol) =>
        IsRecoveryEnabled(computerName, allowedComputer) &&
        sessionId > 0 &&
        string.Equals(userName, RequiredUser, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(sessionState, "disconnected", StringComparison.OrdinalIgnoreCase) &&
        IsInteractiveSessionProtocol(protocol);

    private static void TransferDisconnectedSessionToConsole(
        int sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId <= 0)
        {
            throw Unavailable($"target_rdp_session_console_transfer_ineligible;session={sessionId};reason=session_id_must_be_nonzero");
        }

        var tsconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "tscon.exe");
        if (!File.Exists(tsconPath))
        {
            throw Unavailable($"target_rdp_session_console_transfer_unavailable;session={sessionId};reason=tscon_not_found");
        }

        try
        {
            using var command = Process.Start(new ProcessStartInfo
            {
                FileName = tsconPath,
                Arguments = $"{sessionId} /dest:console",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (command is null || !command.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                throw Unavailable($"target_rdp_session_console_transfer_timeout;session={sessionId}");
            }

            if (command.ExitCode != 0)
            {
                throw Unavailable($"target_rdp_session_console_transfer_failed;session={sessionId};exitCode={command.ExitCode}");
            }
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw Unavailable($"target_rdp_session_console_transfer_failed;session={sessionId};exception={exception.GetType().Name}");
        }
    }

    private static string WaitForActiveSessionAfterConsoleTransfer(
        int sessionId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(ConsoleSessionTransferTimeoutSeconds);
        var lastState = "unknown";
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastState = QuerySessionString(sessionId, WtsConnectStateInfoClass) ?? "unknown";
            var userName = QuerySessionString(sessionId, WtsUserName) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(userName) &&
                !string.Equals(userName, RequiredUser, StringComparison.OrdinalIgnoreCase))
            {
                throw Unavailable(
                    $"target_rdp_session_console_transfer_identity_changed;session={sessionId};user={userName};state={lastState}");
            }

            if (string.Equals(lastState, "active", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(userName, RequiredUser, StringComparison.OrdinalIgnoreCase))
            {
                return lastState;
            }

            Thread.Sleep(250);
        }

        throw Unavailable(
            $"target_rdp_session_console_transfer_timeout;session={sessionId};state={lastState}");
    }

    private static (int ProcessId, int SessionId)? FindOneDriveProcessInSession(int sessionId)
    {
        foreach (var process in Process.GetProcessesByName("OneDrive"))
        {
            try
            {
                if (!process.HasExited && process.SessionId == sessionId)
                {
                    return (process.Id, process.SessionId);
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static IReadOnlyList<OneDriveProcessSnapshot> SnapshotOneDriveProcesses()
    {
        var result = new List<OneDriveProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName("OneDrive"))
        {
            try
            {
                if (!process.HasExited)
                {
                    result.Add(new OneDriveProcessSnapshot(
                        process.Id,
                        process.SessionId,
                        TryProcessPath(process)));
                }
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

    private static void StopVerifiedStaleProcesses(
        IEnumerable<OneDriveProcessSnapshot> processes,
        int targetSessionId,
        string executablePath,
        ICollection<string> actions,
        CancellationToken cancellationToken)
    {
        foreach (var process in processes.Where(process => process.SessionId != targetSessionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExecutablePath is null)
            {
                throw Unavailable($"stale_onedrive_process_identity_unavailable;pid={process.ProcessId};session={process.SessionId}");
            }
            if (!ShouldStopStaleProcess(
                    process.SessionId,
                    targetSessionId,
                    process.ExecutablePath,
                    executablePath))
            {
                throw Unavailable($"stale_onedrive_process_path_unexpected;pid={process.ProcessId};session={process.SessionId}");
            }

            try
            {
                using var stale = Process.GetProcessById(process.ProcessId);
                if (stale.HasExited)
                {
                    continue;
                }

                stale.CloseMainWindow();
                if (!stale.WaitForExit(3000))
                {
                    stale.Kill(entireProcessTree: true);
                    if (!stale.WaitForExit(5000))
                    {
                        throw Unavailable($"stale_onedrive_process_stop_timeout;pid={process.ProcessId};session={process.SessionId}");
                    }
                }
                actions.Add($"stale_onedrive_session_{process.SessionId}_stopped");
            }
            catch (OperatorFailureException)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                throw Unavailable($"stale_onedrive_process_stop_failed;pid={process.ProcessId};session={process.SessionId};exception={exception.GetType().Name}");
            }
        }
    }

    private static OneDriveProcessSnapshot WaitForStableProcess(
        int targetSessionId,
        int startedProcessId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        OneDriveProcessSnapshot? observed = null;
        var stableObservations = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = SnapshotOneDriveProcesses()
                .FirstOrDefault(process => process.SessionId == targetSessionId);
            if (current is not null)
            {
                observed = current;
                stableObservations++;
                if (stableObservations >= 3)
                {
                    return current;
                }
            }
            else
            {
                stableObservations = 0;
            }

            Thread.Sleep(250);
        }

        throw Unavailable(
            $"target_rdp_onedrive_start_did_not_stabilize;session={targetSessionId};startedPid={startedProcessId};observedPid={observed?.ProcessId.ToString() ?? "none"}");
    }

    private static string? TryProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private int? ResolveTargetSessionId()
    {
        if (!OperatingSystem.IsWindows() ||
            !WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count) ||
            sessions == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var candidates = new List<TargetSession>();
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var session = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(sessions, index * size));
                if (session.SessionId == 0)
                {
                    continue;
                }

                var userName = QuerySessionString(session.SessionId, WtsUserName);
                if (!string.Equals(userName, RequiredUser, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new TargetSession(
                    session.SessionId,
                    SessionStateName(session.State),
                    QuerySessionUInt16(session.SessionId, WtsClientProtocolType)));
            }

            var previousSessionId = _stateStore.Read()?.TargetSessionId;
            var activeCandidates = candidates
                .Where(candidate =>
                    string.Equals(candidate.State, "active", StringComparison.OrdinalIgnoreCase) &&
                    IsInteractiveSessionProtocol(candidate.Protocol))
                .ToArray();
            var preferred = activeCandidates.FirstOrDefault(candidate => candidate.SessionId == previousSessionId);
            if (preferred is not null)
            {
                return preferred.SessionId;
            }

            var active = activeCandidates.FirstOrDefault(candidate => candidate.Protocol == RdpProtocol)
                ?? activeCandidates.FirstOrDefault();
            if (active is not null)
            {
                return active.SessionId;
            }

            var quserActive = GetQUserSessions()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.UserName, RequiredUser, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.State, "active", StringComparison.OrdinalIgnoreCase));
            if (quserActive is not null)
            {
                return quserActive.SessionId;
            }

            var processSessionIds = SnapshotOneDriveProcesses()
                .Select(process => process.SessionId)
                .ToHashSet();
            return candidates
                .Where(candidate => string.Equals(candidate.State, "disconnected", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => processSessionIds.Contains(candidate.SessionId))
                .Select(candidate => (int?)candidate.SessionId)
                .FirstOrDefault();
        }
        finally
        {
            WtsFreeMemory(sessions);
        }
    }

    private static IntPtr OpenTargetSession(int sessionId, out string userName, out string sessionState)
    {
        var quserSession = GetQUserSessions().FirstOrDefault(session => session.SessionId == sessionId);
        userName = quserSession?.UserName ?? QuerySessionString(sessionId, WtsUserName) ?? string.Empty;
        var domain = QuerySessionString(sessionId, WtsDomainName);
        sessionState = quserSession?.State ?? QuerySessionString(sessionId, WtsConnectStateInfoClass) ?? "unknown";
        var protocol = quserSession?.Protocol ?? QuerySessionUInt16(sessionId, WtsClientProtocolType);

        if (!IsTargetSessionEligible(userName, sessionState, protocol))
        {
            throw Unavailable($"target_rdp_session_not_ready;session={sessionId};user={userName};state={sessionState};protocol={protocol?.ToString() ?? "<unknown>"};requiredUser={RequiredUser};requiredProtocol=RDP-or-console");
        }

        if (!WtsQueryUserToken((uint)sessionId, out var token) || token == IntPtr.Zero)
        {
            throw Unavailable($"target_rdp_session_token_unavailable;session={sessionId};user={domain}\\{userName};state={sessionState}");
        }

        return token;
    }

    internal static bool IsTargetSessionEligible(string userName, string sessionState, ushort? protocol) =>
        string.Equals(userName, RequiredUser, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(sessionState, "active", StringComparison.OrdinalIgnoreCase) &&
        IsInteractiveSessionProtocol(protocol);

    internal static bool IsInteractiveSessionProtocol(ushort? protocol) =>
        protocol is 0 or RdpProtocol;

    private static string GetProfilePath(IntPtr token)
    {
        var capacity = 260u;
        var buffer = new StringBuilder((int)capacity);
        while (!GetUserProfileDirectory(token, buffer, ref capacity))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 122 || capacity <= buffer.Capacity)
            {
                throw Unavailable($"target_user_profile_unavailable;win32={error}");
            }

            buffer = new StringBuilder((int)capacity);
        }

        return buffer.ToString();
    }

    private static Process StartOneDriveInSession(
        IntPtr userToken,
        int sessionId,
        string executablePath,
        string profilePath) =>
        StartProcessInSession(
            userToken,
            sessionId,
            executablePath,
            "/background",
            profilePath,
            "target_rdp_onedrive_start_failed");

    private static Process StartProcessInSession(
        IntPtr userToken,
        int sessionId,
        string executablePath,
        string arguments,
        string workingDirectory,
        string failureReason)
    {
        var environment = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        var processInfo = new ProcessInformation();
        try
        {
            if (!DuplicateTokenEx(userToken, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
            {
                throw Unavailable($"target_rdp_token_duplication_failed;session={sessionId};win32={Marshal.GetLastWin32Error()}");
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                throw Unavailable($"target_rdp_environment_creation_failed;session={sessionId};win32={Marshal.GetLastWin32Error()}");
            }

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = "winsta0\\default",
            };
            var commandLine = new StringBuilder($"\"{executablePath}\" {arguments}");
            if (!CreateProcessAsUser(
                    primaryToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    environment,
                    workingDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                throw Unavailable($"{failureReason};session={sessionId};win32={Marshal.GetLastWin32Error()}");
            }

            return Process.GetProcessById((int)processInfo.ProcessId);
        }
        finally
        {
            if (processInfo.ProcessHandle != IntPtr.Zero)
            {
                CloseHandle(processInfo.ProcessHandle);
            }
            if (processInfo.ThreadHandle != IntPtr.Zero)
            {
                CloseHandle(processInfo.ThreadHandle);
            }
            if (environment != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environment);
            }
            if (primaryToken != IntPtr.Zero)
            {
                CloseHandle(primaryToken);
            }
        }
    }

    private static void EnsureDesktopAgentInSession(
        IntPtr userToken,
        int targetSessionId,
        string profilePath,
        ICollection<string> actions,
        CancellationToken cancellationToken)
    {
        // Host is the sole Agent lifecycle owner. Keep the logon task disabled
        // even when the existing listener is already in the correct session.
        DisableDesktopAgentTask();
        var listener = FindDesktopAgentListener();
        if (listener?.SessionId == targetSessionId)
        {
            actions.Add("desktop_agent_logon_task_disabled");
            actions.Add("desktop_agent_already_running_in_target_rdp_session");
            return;
        }

        try
        {
            if (listener is not null)
            {
                if (!IsDesktopAgentListenerEligibleForStop(listener.SessionId, targetSessionId, listener.ProcessName))
                {
                    throw Unavailable(
                        $"desktop_agent_listener_identity_unexpected;pid={listener.ProcessId};session={listener.SessionId};process={listener.ProcessName}");
                }

                EndDesktopAgentTaskIfRunning();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
                while (DateTimeOffset.UtcNow < deadline && FindDesktopAgentListener() is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(250);
                }

                var remaining = FindDesktopAgentListener();
                if (remaining is not null)
                {
                    try
                    {
                        using var process = Process.GetProcessById(remaining.ProcessId);
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(5000))
                        {
                            throw Unavailable(
                                $"desktop_agent_wrong_session_stop_timeout;pid={remaining.ProcessId};session={remaining.SessionId}");
                        }
                    }
                    catch (OperatorFailureException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                    {
                        throw Unavailable(
                            $"desktop_agent_wrong_session_stop_failed;pid={remaining.ProcessId};session={remaining.SessionId};exception={exception.GetType().Name}");
                    }
                }

                actions.Add($"desktop_agent_session_{listener.SessionId}_stopped");
            }

            var launcherPath = Path.Combine(
                profilePath,
                "AppData",
                "Local",
                "WindowsOperator",
                "run",
                "start-agent.ps1");
            if (!File.Exists(launcherPath))
            {
                throw Unavailable($"desktop_agent_launcher_not_found;session={targetSessionId}");
            }

            var powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var agentRoot = Path.Combine(
                profilePath,
                "AppData",
                "Local",
                "WindowsOperator",
                "agent");
            if (!Directory.Exists(agentRoot))
            {
                throw Unavailable($"desktop_agent_runtime_not_found;session={targetSessionId}");
            }
            using var started = StartProcessInSession(
                userToken,
                targetSessionId,
                powershellPath,
                $"-NoProfile -ExecutionPolicy Bypass -File \"{launcherPath}\"",
                agentRoot,
                "target_rdp_desktop_agent_start_failed");
            actions.Add("desktop_agent_started_in_target_rdp_session");

            WaitForDesktopAgentListener(targetSessionId, started.Id, cancellationToken);
            actions.Add("desktop_agent_target_session_listener_verified");
        }
        finally
        {
            // The Host remains the sole Agent lifecycle owner. The logon task
            // stays disabled so RDP reconnect cannot launch a competing Agent.
        }
    }

    private static void DisableDesktopAgentTask()
    {
        RunDesktopAgentTaskCommand(
            "/Change /TN \"WindowsOperator.Agent\" /Disable",
            "desktop_agent_task_disable",
            1);
    }

    private static void EndDesktopAgentTaskIfRunning()
    {
        RunDesktopAgentTaskCommand(
            "/End /TN \"WindowsOperator.Agent\"",
            "desktop_agent_task_stop",
            1);
    }

    private static void RunDesktopAgentTaskCommand(
        string arguments,
        string failureReason,
        params int[] additionalSuccessExitCodes)
    {
        using var command = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "schtasks.exe"),
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (command is null || !command.WaitForExit(10000))
        {
            throw Unavailable($"{failureReason}_timeout");
        }
        if (command.ExitCode != 0 && !additionalSuccessExitCodes.Contains(command.ExitCode))
        {
            throw Unavailable($"{failureReason}_failed;exitCode={command.ExitCode}");
        }
    }

    private static void WaitForDesktopAgentListener(
        int targetSessionId,
        int startedProcessId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var stableObservations = 0;
        DesktopAgentListener? observed = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observed = FindDesktopAgentListener();
            if (observed?.SessionId == targetSessionId)
            {
                stableObservations++;
                if (stableObservations >= 3)
                {
                    return;
                }
            }
            else
            {
                stableObservations = 0;
            }

            Thread.Sleep(250);
        }

        throw Unavailable(
            $"target_rdp_desktop_agent_listener_timeout;session={targetSessionId};startedPid={startedProcessId};observedSession={observed?.SessionId.ToString() ?? "none"}");
    }

    private static DesktopAgentListener? FindDesktopAgentListener()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var size = 0;
        _ = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            false,
            AddressFamilyInterNetwork,
            TcpTableOwnerPidListener,
            0);
        if (size <= 0)
        {
            return null;
        }

        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(
                    table,
                    ref size,
                    false,
                    AddressFamilyInterNetwork,
                    TcpTableOwnerPidListener,
                    0) != 0)
            {
                return null;
            }

            var count = Marshal.ReadInt32(table);
            const int rowSize = 24;
            for (var index = 0; index < count; index++)
            {
                var row = IntPtr.Add(table, sizeof(int) + (index * rowSize));
                var localPort = unchecked((ushort)IPAddress.NetworkToHostOrder(
                    unchecked((short)Marshal.ReadInt32(row, 8))));
                if (localPort != DesktopAgentPort)
                {
                    continue;
                }

                var processId = Marshal.ReadInt32(row, 20);
                try
                {
                    using var process = Process.GetProcessById(processId);
                    return new DesktopAgentListener(processId, process.SessionId, process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    internal static bool IsDesktopAgentListenerEligibleForStop(
        int processSessionId,
        int targetSessionId,
        string processName) =>
        processSessionId != targetSessionId &&
        (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(processName, "WindowsOperator.Agent", StringComparison.OrdinalIgnoreCase));

    private static string ResolveOneDriveExecutable(string profilePath, int targetSessionId)
    {
        var candidates = new[]
        {
            Path.Combine(profilePath, "AppData", "Local", "Microsoft", "OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe"),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        return path ?? throw Unavailable($"onedrive_executable_not_found;userProfile={profilePath};session={targetSessionId}");
    }

    private static string? QuerySessionString(int sessionId, int infoClass)
    {
        if (!WtsQuerySessionInformation(IntPtr.Zero, (uint)sessionId, infoClass, out var buffer, out var bytes) || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (infoClass == WtsConnectStateInfoClass)
            {
                return Marshal.ReadInt32(buffer) switch
                {
                    0 => "active",
                    1 => "connected",
                    2 => "connect_query",
                    3 => "shadow",
                    4 => "disconnected",
                    5 => "idle",
                    6 => "listen",
                    7 => "reset",
                    8 => "down",
                    9 => "init",
                    _ => "unknown",
                };
            }

            return Marshal.PtrToStringUni(buffer, Math.Max(0, (int)(bytes / 2) - 1));
        }
        finally
        {
            WtsFreeMemory(buffer);
        }
    }

    private static ushort? QuerySessionUInt16(int sessionId, int infoClass)
    {
        if (!WtsQuerySessionInformation(IntPtr.Zero, (uint)sessionId, infoClass, out var buffer, out _)
            || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return unchecked((ushort)Marshal.ReadInt16(buffer));
        }
        finally
        {
            WtsFreeMemory(buffer);
        }
    }

    private static OperatorFailureException Unavailable(string detail)
    {
        var sessionId = TrySessionIdFromDetail(detail) ?? FindObservedAdministratorSessionId();
        var process = sessionId is int observedSession
            ? FindOneDriveProcessInSession(observedSession)
            : null;
        var sessionState = sessionId is int stateSession && OperatingSystem.IsWindows()
            ? QuerySessionString(stateSession, WtsConnectStateInfoClass)
            : null;
        var interactiveUser = sessionId is int userSession && OperatingSystem.IsWindows()
            ? QuerySessionString(userSession, WtsUserName)
            : null;
        var protocol = sessionId is int protocolSession && OperatingSystem.IsWindows()
            ? QuerySessionUInt16(protocolSession, WtsClientProtocolType)
            : null;
        var runtime = BuildRuntimeEvidence(
            detail,
            Environment.MachineName,
            Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS"),
            interactiveUser,
            sessionState,
            protocol,
            process,
            sessionId);
        return new OperatorFailureException(OperatorErrors.OneDriveUnavailable(detail, runtime));
    }

    private static int? TrySessionIdFromDetail(string detail)
    {
        var marker = "session=";
        var start = detail.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = detail.IndexOf(';', start);
        var value = end < 0 ? detail[start..] : detail[start..end];
        return int.TryParse(value, out var sessionId) && sessionId > 0 ? sessionId : null;
    }

    private static int? FindObservedAdministratorSessionId()
    {
        if (!OperatingSystem.IsWindows() ||
            !WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count) ||
            sessions == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var session = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(sessions, index * size));
                if (session.SessionId == 0 ||
                    !string.Equals(QuerySessionString(session.SessionId, WtsUserName), RequiredUser, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return session.SessionId;
            }
        }
        finally
        {
            WtsFreeMemory(sessions);
        }

        return null;
    }

    internal static OneDriveRuntimeEvidence BuildRuntimeEvidence(
        string detail,
        string computerName,
        string? allowedComputer,
        string? interactiveUser,
        string? sessionState,
        ushort? protocol,
        (int ProcessId, int SessionId)? process,
        int? sessionId = null)
    {
        var activeSession = IsTargetSessionEligible(
                interactiveUser ?? string.Empty,
                sessionState ?? string.Empty,
                protocol)
            ? sessionId
            : (int?)null;
        var sessionAction = sessionId?.ToString() ?? "dynamic";
        var action = detail.StartsWith("target_rdp_session_console_transfer", StringComparison.Ordinal)
            ? $"operator_retry_administrator_console_transfer_{sessionAction}"
            : detail.StartsWith("target_rdp_session_not_ready", StringComparison.Ordinal)
            ? $"operator_open_administrator_rdp_session_{sessionAction}"
            : detail.StartsWith("target_rdp_session_token_unavailable", StringComparison.Ordinal)
                ? $"operator_unlock_administrator_rdp_session_{sessionAction}"
                : detail.StartsWith("onedrive_executable_not_found", StringComparison.Ordinal)
                    ? "operator_repair_onedrive_installation"
                    : "operator_inspect_onedrive_runtime";
        return new OneDriveRuntimeEvidence
        {
            ComputerName = computerName,
            RecoveryAllowed = IsRecoveryEnabled(computerName, allowedComputer),
            ProcessPresent = process is not null,
            ProcessSessionId = process?.SessionId,
            ConfiguredSessionId = sessionId,
            ActiveInteractiveSessionId = activeSession,
            InteractiveUser = interactiveUser,
            InteractiveSessionState = sessionState,
            InteractiveSessionProtocol = protocol,
            ProviderReady = false,
            ProviderReason = detail.Split(';', 2)[0],
            AuthenticationRequired = false,
            RecoveryActions = new[] { action },
        };
    }

    internal static bool ShouldStopStaleProcess(
        int processSessionId,
        int targetSessionId,
        string? processPath,
        string expectedPath) =>
        processSessionId != targetSessionId &&
        processPath is not null &&
        string.Equals(
            Path.GetFullPath(processPath),
            Path.GetFullPath(expectedPath),
            StringComparison.OrdinalIgnoreCase);

    private sealed record OneDriveProcessSnapshot(int ProcessId, int SessionId, string? ExecutablePath);

    private sealed record DesktopAgentListener(int ProcessId, int SessionId, string ProcessName);

    private sealed record TargetSession(int SessionId, string State, ushort? Protocol);

    private sealed record QUserSession(int SessionId, string UserName, string State, ushort Protocol);

    private static IReadOnlyList<QUserSession> GetQUserSessions()
    {
        var quserPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "quser.exe");
        if (!File.Exists(quserPath))
        {
            return Array.Empty<QUserSession>();
        }

        using var command = Process.Start(new ProcessStartInfo
        {
            FileName = quserPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (command is null || !command.WaitForExit(5000))
        {
            return Array.Empty<QUserSession>();
        }

        var sessions = new List<QUserSession>();
        foreach (var line in command.StandardOutput.ReadToEnd().Split('\n'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                line.TrimEnd('\r'),
                @"^\s*>?(?<user>\S+)\s+(?:(?<session>\S+)\s+)?(?<id>\d+)\s+(?<state>\S+)");
            if (!match.Success ||
                !int.TryParse(match.Groups["id"].Value, out var sessionId) ||
                !string.Equals(match.Groups["user"].Value, RequiredUser, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sessionName = match.Groups["session"].Value;
            var protocol = sessionName.StartsWith("rdp-", StringComparison.OrdinalIgnoreCase)
                ? (ushort)RdpProtocol
                : (ushort)0;
            sessions.Add(new QUserSession(sessionId, match.Groups["user"].Value, match.Groups["state"].Value.ToLowerInvariant(), protocol));
        }

        return sessions;
    }

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public WtsConnectState State;
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr serverHandle,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQueryUserToken", SetLastError = true)]
    private static extern bool WtsQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WtsQuerySessionInformation(
        IntPtr server,
        uint sessionId,
        int informationClass,
        out IntPtr buffer,
        out uint bytesReturned);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory")]
    private static extern void WtsFreeMemory(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr primaryToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserProfileDirectory(
        IntPtr token,
        StringBuilder profilePath,
        ref uint size);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }
}
