using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed partial class EdgeMicrosoftAuthService : IMicrosoftAuthService, IEdgeBrowserService, IDisposable
{
    private readonly StaComDispatcher _dispatcher = new();
    private readonly Dictionary<string, EdgeBrowserSessionMetadata> _browserSessions = new(StringComparer.Ordinal);
    private static readonly HttpClient DevToolsHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private const uint WmClose = 0x0010;

    public Task<MicrosoftAuthCleanupResult> CleanupAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => CleanupAuthWindowsCore(request), cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> StartAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => StartAuthorizeProbeCore(request), cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> GetAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        Task.FromResult(ReadAuthorizeProbeResult(runId));

    public Task<MicrosoftDeviceLoginResult> StartDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => StartDeviceLoginCore(request), cancellationToken);

    public Task<MicrosoftDeviceLoginResult> GetDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        Task.FromResult(ReadResult(runId));

    public void Dispose() => _dispatcher.Dispose();

    private static MicrosoftAuthorizeProbeResult StartAuthorizeProbeCore(MicrosoftAuthorizeProbeRequest request)
    {
        var runId = SafeFileName(request.RunId, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var runRoot = AuthorizeProbeRunRoot(runId);
        Directory.CreateDirectory(runRoot);
        var progressPath = Path.Combine(runRoot, "progress.log");
        var statusPath = AuthorizeProbeResultPath(runId);
        var actions = new List<string>();
        var errors = new List<string>();
        var authorizeUrl = NormalizeHttpsUrl(request.AuthorizeUrl, "authorize URL");
        var pageLoadSeconds = Math.Clamp(request.PageLoadSeconds, 1, 30);
        var observationTimeoutSeconds = Math.Clamp(request.ObservationTimeoutSeconds, 1, 180);
        var reuseExistingProfile = request.ReuseExistingProfile;
        string? browserTitle = null;
        string? browserState = null;
        string? observedUrl = null;
        string? observedOrigin = null;
        string? observedError = null;
        var observedCodePresent = false;
        var status = MicrosoftAuthorizeProbeStatus.Opened;
        var observedAtUtc = DateTimeOffset.UtcNow;

        void Trace(string stage)
        {
            try
            {
                File.AppendAllText(
                    progressPath,
                    $"{DateTimeOffset.UtcNow:o}\t{stage}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must not break auth probe.
            }
        }

        try
        {
            Trace("start");
            var preCleanup = CleanupAuthWindows(
                new MicrosoftAuthCleanupRequest { DryRun = request.DryRun },
                actions,
                null);
            Trace($"pre_cleanup_closed:{preCleanup.ClosedWindows}");
            if (!OperatingSystem.IsWindows())
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("Microsoft authorize probe requires Windows desktop session."));
            }

            if (string.IsNullOrWhiteSpace(request.AuthorizeUrl))
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("AuthorizeUrl is required."));
            }

            var edgePath = FindEdgePath();
            if (request.DryRun)
            {
                status = MicrosoftAuthorizeProbeStatus.DryRun;
                actions.Add("dry_run");
                actions.Add("edge_available");
                var dryRunResult = AuthorizeProbeResult(
                    true,
                    runId,
                    authorizeUrl,
                    request.InPrivate,
                    status,
                    "dry_run",
                    null,
                    null,
                    null,
                    null,
                    false,
                    actions,
                    errors,
                    DateTimeOffset.UtcNow,
                    statusPath);
                WriteAuthorizeProbeResult(runId, dryRunResult);
                Trace("dry_run");
                return dryRunResult;
            }

            var devToolsPort = ReserveLoopbackPort();
            actions.Add($"remote_debugging_port:{devToolsPort}");
            Trace($"remote_debugging_port:{devToolsPort}");
            if (reuseExistingProfile)
            {
                actions.Add("reuse_existing_profile");
                Trace("reuse_existing_profile");
            }

            var startedAfterUtc = DateTimeOffset.UtcNow.AddSeconds(-2);
            using var edge = StartEdge(
                edgePath,
                reuseExistingProfile ? EdgeWorkProfileSelection() : EdgeProfileSelection.Temp(Path.Combine(runRoot, "edge-profile")),
                request.InPrivate,
                authorizeUrl,
                devToolsPort);
            actions.Add("edge_opened");
            Thread.Sleep(TimeSpan.FromSeconds(pageLoadSeconds));

            var shell = CreateWScriptShell();
            var edgeWindow = FindAuthorizeWindow(
                edge.Id,
                startedAfterUtc,
                reuseExistingProfile,
                TimeSpan.FromSeconds(Math.Max(1, pageLoadSeconds)));
            if (edgeWindow is null || !TryActivateEdge(shell, edgeWindow.Value.ProcessId, edgeWindow.Value.Title, actions))
            {
                status = MicrosoftAuthorizeProbeStatus.Failed;
                errors.Add("Edge authorize window was not visible or could not be activated.");
                browserState = "edge_activation_failed";
                browserTitle = edgeWindow?.Title;
                Trace("edge_activation_failed");
                var failedResult = AuthorizeProbeResult(
                    false,
                    runId,
                    authorizeUrl,
                    request.InPrivate,
                    status,
                    browserState,
                    browserTitle,
                    null,
                    null,
                    null,
                    false,
                    actions,
                    errors,
                    DateTimeOffset.UtcNow,
                    statusPath);
                WriteAuthorizeProbeResult(runId, failedResult);
                return failedResult;
            }

            var observation = ObserveAuthorizeState(
                startedAfterUtc,
                edge.Id,
                request.ReuseExistingProfile,
                devToolsPort,
                authorizeUrl,
                TimeSpan.FromSeconds(observationTimeoutSeconds));
            browserState = observation.State;
            browserTitle = observation.Title;
            observedUrl = observation.Url;
            observedOrigin = observation.Origin;
            observedError = observation.Error;
            observedCodePresent = observation.CodePresent;
            observedAtUtc = observation.ObservedAtUtc;
            status = observation.Status;
            actions.Add($"browser_observed:{status}");
            if (!string.IsNullOrWhiteSpace(observedUrl))
            {
                actions.Add("observed_url");
            }

            if (status is MicrosoftAuthorizeProbeStatus.RedirectObserved or MicrosoftAuthorizeProbeStatus.Failed or MicrosoftAuthorizeProbeStatus.TimedOut)
            {
                var postCleanup = CleanupAuthWindows(new MicrosoftAuthCleanupRequest(), actions, null);
                Trace($"post_cleanup_closed:{postCleanup.ClosedWindows}");
            }
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            status = MicrosoftAuthorizeProbeStatus.Failed;
            browserState = "exception";
            Trace($"failed:{ex.GetType().Name}");
        }

        var result = AuthorizeProbeResult(
            errors.Count == 0,
            runId,
            authorizeUrl,
            request.InPrivate,
            status,
            browserState,
            browserTitle,
            observedUrl,
            observedOrigin,
            observedError,
            observedCodePresent,
            actions,
            errors,
            observedAtUtc,
            statusPath);
        WriteAuthorizeProbeResult(runId, result);
        Trace($"complete:{status}");
        return result;
    }

    private static MicrosoftDeviceLoginResult StartDeviceLoginCore(MicrosoftDeviceLoginRequest request)
    {
        var runId = SafeFileName(request.RunId, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var runRoot = RunRoot(runId);
        Directory.CreateDirectory(runRoot);
        var progressPath = Path.Combine(runRoot, "progress.log");
        var statusPath = ResultPath(runId);
        var actions = new List<string>();
        var errors = new List<string>();
        var loginUrl = NormalizeLoginUrl(request.LoginUrl);
        var pageLoadSeconds = Math.Clamp(request.PageLoadSeconds, 1, 30);
        var verificationWaitSeconds = Math.Clamp(request.VerificationWaitSeconds, 0, 120);
        var reuseExistingProfile = request.ReuseExistingProfile;
        string? browserTitle = null;
        string? browserState = null;
        var status = MicrosoftDeviceLoginStatus.Submitted;
        var observedAtUtc = DateTimeOffset.UtcNow;

        void Trace(string stage)
        {
            try
            {
                File.AppendAllText(
                    progressPath,
                    $"{DateTimeOffset.UtcNow:o}\t{stage}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must not break auth handoff.
            }
        }

        try
        {
            Trace("start");
            var preCleanup = CleanupAuthWindows(
                new MicrosoftAuthCleanupRequest { DryRun = request.DryRun },
                actions,
                null);
            Trace($"pre_cleanup_closed:{preCleanup.ClosedWindows}");
            if (!OperatingSystem.IsWindows())
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("Microsoft device login requires Windows desktop session."));
            }

            if (string.IsNullOrWhiteSpace(request.DeviceCode))
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("DeviceCode is required."));
            }

            var edgePath = FindEdgePath();
            if (request.DryRun)
            {
                status = MicrosoftDeviceLoginStatus.DryRun;
                actions.Add("dry_run");
                actions.Add("edge_available");
                var dryRunResult = Result(
                    true,
                    runId,
                    loginUrl,
                    request.InPrivate,
                    status,
                    "dry_run",
                    null,
                    actions,
                    errors,
                    DateTimeOffset.UtcNow,
                    statusPath);
                WriteResult(runId, dryRunResult);
                Trace("dry_run");
                return dryRunResult;
            }

            var devToolsPort = ReserveLoopbackPort();
            actions.Add($"remote_debugging_port:{devToolsPort}");
            Trace($"remote_debugging_port:{devToolsPort}");
            CleanupHiddenEdgeProcesses(actions);

            var startedAfterUtc = DateTimeOffset.UtcNow.AddSeconds(-2);
            if (reuseExistingProfile)
            {
                actions.Add("reuse_existing_profile");
                Trace("reuse_existing_profile");
            }

            using var edge = StartEdge(
                edgePath,
                reuseExistingProfile ? EdgeWorkProfileSelection() : EdgeProfileSelection.Temp(Path.Combine(runRoot, "edge-profile")),
                request.InPrivate,
                loginUrl,
                devToolsPort);
            actions.Add("edge_opened");
            Thread.Sleep(TimeSpan.FromSeconds(pageLoadSeconds));

            var shell = CreateWScriptShell();
            var edgeWindow = FindAuthorizeWindow(
                edge.Id,
                startedAfterUtc,
                reuseExistingProfile,
                TimeSpan.FromSeconds(Math.Max(1, pageLoadSeconds)));
            if (edgeWindow is null || !TryActivateEdge(shell, edgeWindow.Value.ProcessId, edgeWindow.Value.Title, actions))
            {
                status = MicrosoftDeviceLoginStatus.Failed;
                errors.Add("Edge login window was not visible or could not be activated.");
                browserState = "edge_activation_failed";
                browserTitle = edgeWindow?.Title;
                Trace("edge_activation_failed");
                var failedResult = Result(
                    false,
                    runId,
                    loginUrl,
                    request.InPrivate,
                    status,
                    browserState,
                    browserTitle,
                    actions,
                    errors,
                    DateTimeOffset.UtcNow,
                    statusPath);
                WriteResult(runId, failedResult);
                return failedResult;
            }

            var submittedWithDevTools = TrySubmitDeviceCodeWithDevTools(
                devToolsPort,
                loginUrl,
                request.DeviceCode,
                TimeSpan.FromSeconds(Math.Max(5, pageLoadSeconds)),
                actions);
            if (submittedWithDevTools)
            {
                actions.Add("device_code_submitted:devtools");
            }
            else
            {
                shell.SendKeys(request.DeviceCode);
                Thread.Sleep(500);
                shell.SendKeys("{ENTER}");
                actions.Add("device_code_submitted:sendkeys");
            }

            actions.Add("device_code_submitted");
            Trace("device_code_submitted");

            var observation = ObserveBrowserState(
                startedAfterUtc,
                edge.Id,
                reuseExistingProfile,
                TimeSpan.FromSeconds(verificationWaitSeconds),
                devToolsPort,
                loginUrl,
                actions);
            browserState = observation.State;
            browserTitle = observation.Title;
            observedAtUtc = observation.ObservedAtUtc;
            status = observation.Status;
            actions.Add($"browser_observed:{status.ToString()}");

            if (status is MicrosoftDeviceLoginStatus.BrowserAccepted or MicrosoftDeviceLoginStatus.InvalidCode or MicrosoftDeviceLoginStatus.Failed or MicrosoftDeviceLoginStatus.TimedOut)
            {
                var postCleanup = CleanupAuthWindows(new MicrosoftAuthCleanupRequest(), actions, null);
                Trace($"post_cleanup_closed:{postCleanup.ClosedWindows}");
            }
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            status = MicrosoftDeviceLoginStatus.Failed;
            browserState = "exception";
            Trace($"failed:{ex.GetType().Name}");
        }

        var result = Result(
            errors.Count == 0 && MicrosoftDeviceLoginOutcomes.IsSuccess(status),
            runId,
            loginUrl,
            request.InPrivate,
            status,
            browserState,
            browserTitle,
            actions,
            errors,
            observedAtUtc,
            statusPath);
        WriteResult(runId, result);
        Trace($"complete:{status}");
        return result;
    }

    private static MicrosoftDeviceLoginResult Result(
        bool success,
        string runId,
        string loginUrl,
        bool inPrivate,
        MicrosoftDeviceLoginStatus status,
        string? browserState,
        string? browserTitle,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> errors) =>
        new(
            success,
            loginUrl,
            inPrivate,
            actions,
            errors,
            DateTimeOffset.UtcNow,
            runId,
            status,
            browserState,
            browserTitle,
            DateTimeOffset.UtcNow,
            ResultPath(runId));

    private static MicrosoftDeviceLoginResult Result(
        bool success,
        string runId,
        string loginUrl,
        bool inPrivate,
        MicrosoftDeviceLoginStatus status,
        string? browserState,
        string? browserTitle,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc,
        string statusPath) =>
        new(
            success,
            loginUrl,
            inPrivate,
            actions,
            errors,
            DateTimeOffset.UtcNow,
            runId,
            status,
            browserState,
            browserTitle,
            observedAtUtc,
            statusPath);

    private static MicrosoftAuthorizeProbeResult AuthorizeProbeResult(
        bool success,
        string runId,
        string authorizeUrl,
        bool inPrivate,
        MicrosoftAuthorizeProbeStatus status,
        string? browserState,
        string? browserTitle,
        string? observedUrl,
        string? observedOrigin,
        string? observedError,
        bool observedCodePresent,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc,
        string statusPath) =>
        new(
            success,
            authorizeUrl,
            inPrivate,
            actions,
            errors,
            DateTimeOffset.UtcNow,
            runId,
            status,
            browserState,
            browserTitle,
            observedUrl,
            observedOrigin,
            observedError,
            observedCodePresent,
            observedAtUtc,
            statusPath);

    private static string NormalizeLoginUrl(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "https://microsoft.com/devicelogin" : raw.Trim();
        return NormalizeHttpsUrl(value, "device login URL");
    }

    private static string NormalizeHttpsUrl(string? raw, string label)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Unsupported Microsoft {label}: {raw}"));
        }

        return uri.ToString();
    }

    private static Process StartEdge(
        string edgePath,
        EdgeProfileSelection profile,
        bool inPrivate,
        string url,
        int? remoteDebuggingPort = null)
    {
        var edge = new Process();
        edge.StartInfo = new ProcessStartInfo
        {
            FileName = edgePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        edge.StartInfo.ArgumentList.Add("--new-window");
        edge.StartInfo.ArgumentList.Add("--no-first-run");
        edge.StartInfo.ArgumentList.Add("--no-session-restore");
        if (!string.IsNullOrWhiteSpace(profile.UserDataDir))
        {
            edge.StartInfo.ArgumentList.Add($"--user-data-dir={profile.UserDataDir}");
        }
        if (!string.IsNullOrWhiteSpace(profile.ProfileDirectory))
        {
            edge.StartInfo.ArgumentList.Add($"--profile-directory={profile.ProfileDirectory}");
        }
        if (inPrivate)
        {
            edge.StartInfo.ArgumentList.Add("--inprivate");
        }

        if (remoteDebuggingPort is not null)
        {
            edge.StartInfo.ArgumentList.Add($"--remote-debugging-port={remoteDebuggingPort.Value}");
        }

        edge.StartInfo.ArgumentList.Add(url);
        if (!edge.Start())
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Unable to start Microsoft Edge."));
        }

        _ = edge.StandardOutput.ReadToEndAsync();
        _ = edge.StandardError.ReadToEndAsync();
        return edge;
    }

    private static string FindEdgePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new OperatorFailureException(
            OperatorErrors.AuthUnavailable("Microsoft Edge executable not found."));
    }

    private static EdgeProfileSelection EdgeWorkProfileSelection()
    {
        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Edge",
            "User Data");
        var profileDirectory = TryResolveSignedInProfileDirectory(userDataDir) ?? "Default";
        return new EdgeProfileSelection(userDataDir, profileDirectory);
    }

    internal static string NormalizeEdgePreferencesExitState(string preferencesPath)
    {
        try
        {
            if (!File.Exists(preferencesPath))
            {
                return "profile_exit_state_normalize_skipped:preferences_missing";
            }

            var root = JsonNode.Parse(File.ReadAllText(preferencesPath)) as JsonObject;
            if (root is null)
            {
                return "profile_exit_state_normalize_skipped:invalid_root";
            }

            JsonObject profile;
            if (root["profile"] is null)
            {
                profile = new JsonObject();
                root["profile"] = profile;
            }
            else if (root["profile"] is JsonObject existingProfile)
            {
                profile = existingProfile;
            }
            else
            {
                return "profile_exit_state_normalize_skipped:profile_not_object";
            }

            var changed = false;
            changed |= SetJsonValue(profile, "exited_cleanly", true);
            changed |= SetJsonValue(profile, "exit_type", "Normal");

            if (root.ContainsKey("exited_cleanly"))
            {
                changed |= SetJsonValue(root, "exited_cleanly", true);
            }

            if (root.ContainsKey("exit_type"))
            {
                changed |= SetJsonValue(root, "exit_type", "Normal");
            }

            if (changed)
            {
                File.WriteAllText(
                    preferencesPath,
                    root.ToJsonString());
            }

            return "profile_exit_state_normalized";
        }
        catch (JsonException)
        {
            return "profile_exit_state_normalize_skipped:invalid_json";
        }
        catch (IOException)
        {
            return "profile_exit_state_normalize_skipped:io_failed";
        }
        catch (UnauthorizedAccessException)
        {
            return "profile_exit_state_normalize_skipped:access_denied";
        }
    }

    private static bool SetJsonValue<T>(JsonObject obj, string propertyName, T value)
    {
        if (obj.TryGetPropertyValue(propertyName, out var existing) &&
            JsonNode.DeepEquals(existing, JsonValue.Create(value)))
        {
            return false;
        }

        obj[propertyName] = JsonValue.Create(value);
        return true;
    }

    private static string? TryResolveSignedInProfileDirectory(string userDataDir)
    {
        try
        {
            var localStatePath = Path.Combine(userDataDir, "Local State");
            if (!File.Exists(localStatePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (!document.RootElement.TryGetProperty("profile", out var profileElement))
            {
                return null;
            }

            var lastUsed = profileElement.TryGetProperty("last_used", out var lastUsedElement)
                ? lastUsedElement.GetString()
                : null;
            if (profileElement.TryGetProperty("info_cache", out var infoCache) &&
                infoCache.ValueKind == JsonValueKind.Object)
            {
                if (!string.IsNullOrWhiteSpace(lastUsed) &&
                    infoCache.TryGetProperty(lastUsed, out var lastUsedProfile) &&
                    LooksSignedInProfile(lastUsedProfile))
                {
                    return lastUsed;
                }

                foreach (var candidate in infoCache.EnumerateObject())
                {
                    if (LooksSignedInProfile(candidate.Value))
                    {
                        return candidate.Name;
                    }
                }
            }

            return string.IsNullOrWhiteSpace(lastUsed) ? null : lastUsed;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksSignedInProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (profile.TryGetProperty("user_name", out var userName) &&
            !string.IsNullOrWhiteSpace(userName.GetString()))
        {
            return true;
        }

        if (profile.TryGetProperty("edge_sync_enabled", out var syncEnabled) &&
            syncEnabled.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        if (profile.TryGetProperty("is_consented_primary_account", out var consented) &&
            consented.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        return false;
    }

    private static dynamic CreateWScriptShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("WScript.Shell COM object is unavailable."));
        return Activator.CreateInstance(type)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Unable to create WScript.Shell COM object."));
    }

    private static bool TryActivateEdge(dynamic shell, int processId, string? title, List<string> actions)
    {
        try
        {
            if (shell.AppActivate(processId))
            {
                actions.Add("edge_activated:process");
                return true;
            }

            foreach (var candidate in new[] { title, "Microsoft Edge", "Sign in to your account", "Enter code" }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!shell.AppActivate(candidate))
                {
                    continue;
                }

                actions.Add("edge_activated:title");
                return true;
            }
        }
        catch
        {
            // Best effort: SendKeys targets current foreground window if Edge activation fails.
        }

        return false;
    }

    private static void CleanupHiddenEdgeProcesses(List<string> actions)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var visibleWindows = EdgeWindows().ToList();
        if (visibleWindows.Count > 0)
        {
            actions.Add($"hidden_edge_cleanup:skipped_visible={visibleWindows.Count}");
            return;
        }

        var matched = 0;
        var closed = 0;
        var failed = 0;
        foreach (var process in Process.GetProcessesByName("msedge"))
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                    {
                        continue;
                    }

                    matched++;
                    process.Kill(entireProcessTree: true);
                    if (process.WaitForExit(3000))
                    {
                        closed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }
        }

        if (matched > 0 || failed > 0)
        {
            actions.Add($"hidden_edge_cleanup:matched={matched};closed={closed};failed={failed}");
        }
    }

    private static BrowserWindow? FindAuthorizeWindow(
        int startedProcessId,
        DateTimeOffset startedAfterUtc,
        bool reuseExistingProfile,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            var match = EdgeWindows()
                .Where(window => reuseExistingProfile || window.ProcessId == startedProcessId || window.StartedAtUtc >= startedAfterUtc)
                .OrderByDescending(window => ScoreAuthorizeWindow(window, startedProcessId, startedAfterUtc, reuseExistingProfile))
                .ThenByDescending(window => window.StartedAtUtc)
                .FirstOrDefault();
            if (match.ProcessId != 0)
            {
                return match;
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static int ScoreAuthorizeWindow(
        BrowserWindow window,
        int startedProcessId,
        DateTimeOffset startedAfterUtc,
        bool reuseExistingProfile)
    {
        var score = 0;
        if (window.ProcessId == startedProcessId)
        {
            score += 1000;
        }

        if (window.StartedAtUtc >= startedAfterUtc)
        {
            score += 400;
        }

        if (reuseExistingProfile && window.Hwnd == GetForegroundWindow())
        {
            score += 800;
        }

        var title = window.Title ?? string.Empty;
        if (title.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Iniciar", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (title.Contains("Work", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        return score;
    }

    private static BrowserObservation ObserveBrowserState(
        DateTimeOffset startedAfterUtc,
        int startedProcessId,
        bool reuseExistingProfile,
        TimeSpan timeout,
        int? devToolsPort = null,
        string? preferredUrl = null,
        List<string>? actions = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        BrowserObservation? last = null;
        var advanceAttempts = 0;
        do
        {
            if (devToolsPort is { } port)
            {
                var target = TryReadEdgeTarget(port, preferredUrl);
                var snapshot = target is null ? null : TryReadEdgeSnapshot(target.Value);
                if (target is not null || snapshot is not null)
                {
                    var observedAtUtc = DateTimeOffset.UtcNow;
                    var title = snapshot?.Title ?? target?.Title;
                    var text = snapshot?.BodyText ?? string.Empty;
                    var classification = ClassifyBrowserState(title, text);
                    last = new BrowserObservation(classification.Status, classification.State, title, observedAtUtc);
                    if (classification.Status is MicrosoftDeviceLoginStatus.BrowserAccepted or MicrosoftDeviceLoginStatus.InvalidCode)
                    {
                        return last.Value;
                    }

                    if (classification.Status is MicrosoftDeviceLoginStatus.NeedsUserAction &&
                        target is not null &&
                        advanceAttempts < 5)
                    {
                        advanceAttempts++;
                        if (TryAdvanceDeviceLoginWithDevTools(target.Value, actions))
                        {
                            Thread.Sleep(1500);
                            continue;
                        }
                    }

                    if (classification.State == "browser_needs_password")
                    {
                        return last.Value;
                    }
                }
            }

            var window = FindAuthorizeWindow(
                startedProcessId,
                startedAfterUtc,
                reuseExistingProfile,
                TimeSpan.Zero);
            if (window is { ProcessId: not 0 })
            {
                var observedAtUtc = DateTimeOffset.UtcNow;
                var text = ReadWindowText(window.Value.Hwnd);
                var classification = ClassifyBrowserState(window.Value.Title, text);
                last = new BrowserObservation(classification.Status, classification.State, window.Value.Title, observedAtUtc);
                if (classification.Status is MicrosoftDeviceLoginStatus.BrowserAccepted or MicrosoftDeviceLoginStatus.InvalidCode)
                {
                    return last.Value;
                }
            }

            Thread.Sleep(500);
        }
        while (DateTimeOffset.UtcNow <= deadline);

        return last ?? new BrowserObservation(
            timeout == TimeSpan.Zero ? MicrosoftDeviceLoginStatus.Submitted : MicrosoftDeviceLoginStatus.TimedOut,
            "browser_state_not_observed",
            null,
            DateTimeOffset.UtcNow);
    }

    private static bool TrySubmitDeviceCodeWithDevTools(
        int devToolsPort,
        string loginUrl,
        string deviceCode,
        TimeSpan timeout,
        List<string> actions)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        DomActionPayload? payload = null;
        do
        {
            var target = TryReadEdgeTarget(devToolsPort, loginUrl);
            if (target is not null)
            {
                payload = EvaluateJson<DomActionPayload>(
                    target.Value.WebSocketDebuggerUrl,
                    BuildDeviceCodeSubmitExpression(deviceCode));
                if (payload is { Success: true })
                {
                    return true;
                }
            }

            Thread.Sleep(350);
        }
        while (DateTimeOffset.UtcNow <= deadline);

        actions.Add(payload is null
            ? "device_code_devtools_submit_unavailable"
            : $"device_code_devtools_submit_failed:{payload.Message}");
        return false;
    }

    private static bool TryAdvanceDeviceLoginWithDevTools(
        EdgePageTarget target,
        List<string>? actions)
    {
        var payload = EvaluateJson<DomActionPayload>(
            target.WebSocketDebuggerUrl,
            BuildDeviceLoginAdvanceExpression());
        if (payload is not { Success: true })
        {
            actions?.Add(payload is null
                ? "device_login_advance_unavailable"
                : $"device_login_advance_unavailable:{TrimValue(payload.Message, 80)}");
            return false;
        }

        var matched = string.IsNullOrWhiteSpace(payload.MatchedText)
            ? payload.MatchedBy
            : payload.MatchedText;
        if (payload.ClickX is { } x && payload.ClickY is { } y)
        {
            if (DispatchMouseClick(target.WebSocketDebuggerUrl, x, y))
            {
                actions?.Add($"device_login_advanced:{payload.MatchedBy}:{TrimValue(matched, 80)}");
                return true;
            }

            actions?.Add($"device_login_advance_unavailable:cdp_click_failed:{TrimValue(matched, 80)}");
            return false;
        }

        actions?.Add($"device_login_advance_unavailable:missing_click_point:{TrimValue(matched, 80)}");
        return false;
    }

    private static string BuildDeviceCodeSubmitExpression(string deviceCode)
    {
        var value = JsonSerializer.Serialize(deviceCode);
        return $$"""
(() => {
  const deviceCode = {{value}};
  const normalize = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
  const isVisible = element => {
    if (!element || !element.getBoundingClientRect) {
      return false;
    }
    const style = window.getComputedStyle(element);
    if (!style || style.visibility === "hidden" || style.display === "none") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };
  const textOf = element => normalize(
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.placeholder ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  );
  const input = Array.from(document.querySelectorAll("input,textarea"))
    .filter(isVisible)
    .find(element => {
      const type = normalize(element.getAttribute("type"));
      return !element.disabled &&
        !element.readOnly &&
        (!type || ["text", "tel", "email", "search", "password"].includes(type));
    });
  if (!input) {
    return JSON.stringify({ success: false, message: "No visible device-code input." });
  }

  input.focus();
  input.value = deviceCode;
  input.dispatchEvent(new Event("input", { bubbles: true }));
  input.dispatchEvent(new Event("change", { bubbles: true }));

  const submitCandidates = Array.from(document.querySelectorAll("input[type='submit'],input[type='button'],button,[role='button']"))
    .filter(isVisible);
  const submit = submitCandidates.find(element => {
    const text = textOf(element);
    return text.includes("next") ||
      text.includes("siguiente") ||
      text.includes("continue") ||
      text.includes("continuar") ||
      text.includes("submit") ||
      text.includes("verify") ||
      text.includes("verificar");
  }) || submitCandidates[0] || null;

  if (submit) {
    submit.click();
    return JSON.stringify({
      success: true,
      matchedBy: "deviceCodeSubmit",
      matchedText: textOf(submit),
      tagName: (submit.tagName || "").toLowerCase()
    });
  }

  if (input.form && input.form.requestSubmit) {
    input.form.requestSubmit();
    return JSON.stringify({
      success: true,
      matchedBy: "deviceCodeForm",
      matchedText: "",
      tagName: (input.form.tagName || "").toLowerCase()
    });
  }

  input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", code: "Enter", bubbles: true }));
  input.dispatchEvent(new KeyboardEvent("keyup", { key: "Enter", code: "Enter", bubbles: true }));
  return JSON.stringify({
    success: true,
    matchedBy: "deviceCodeEnter",
    matchedText: textOf(input),
    tagName: (input.tagName || "").toLowerCase()
  });
})()
""";
    }

    private static string BuildDeviceLoginAdvanceExpression()
    {
        return """
(() => {
  const normalize = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
  const isVisible = element => {
    if (!element || !element.getBoundingClientRect) {
      return false;
    }
    const style = window.getComputedStyle(element);
    if (!style || style.visibility === "hidden" || style.display === "none") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };
  const textOf = element => normalize(
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.placeholder ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  );
  const rawTextOf = element => (
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.placeholder ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  ).replace(/\s+/g, " ").trim();
  const actionableOf = element =>
    element?.closest?.("button,a,[role='button'],[role='option'],[role='menuitem'],[tabindex]") || element;
  const clickPointOf = element => {
    const target = actionableOf(element);
    if (!target || !target.getBoundingClientRect) {
      return null;
    }
    target.scrollIntoView?.({ block: "center", inline: "center" });
    const rect = target.getBoundingClientRect();
    const x = rect.left + Math.max(1, rect.width / 2);
    const y = rect.top + Math.max(1, rect.height / 2);
    return { x, y };
  };
  const body = normalize(document.body ? document.body.innerText || "" : "");
  if (body.includes("need admin approval") || body.includes("approval required")) {
    return JSON.stringify({ success: false, message: "Admin approval required." });
  }

  const passwordInput = Array.from(document.querySelectorAll("input[type='password']"))
    .filter(isVisible)
    .find(element => !element.disabled && !element.readOnly);
  const passwordValidation =
    body.includes("please enter your password") ||
    body.includes("escriba la contraseña") ||
    body.includes("escriba la contrasena");
  const passwordPage =
    passwordValidation ||
    body.includes("enter password") ||
    body.includes("forgot my password") ||
    body.includes("olvidé mi contraseña") ||
    body.includes("olvide mi contrasena");
  if ((passwordValidation || !passwordInput) && passwordPage) {
    return JSON.stringify({ success: false, message: "Password required." });
  }

  const candidates = Array.from(document.querySelectorAll("button,input[type='submit'],input[type='button'],a,[role='button'],[role='option'],[role='menuitem'],[tabindex],div"))
    .filter(isVisible)
    .map(element => ({ element, text: textOf(element), rawText: rawTextOf(element) }))
    .filter(candidate => candidate.text);
  const isBackOrOther = candidate =>
    candidate.text.includes("back") ||
    candidate.text.includes("atrás") ||
    candidate.text.includes("atras") ||
    candidate.text.includes("use another account") ||
    candidate.text.includes("usar otra cuenta");

  if (body.includes("pick an account") || body.includes("elige una cuenta") || body.includes("seleccionar una cuenta")) {
    const accounts = candidates
      .filter(candidate => !isBackOrOther(candidate))
      .filter(candidate =>
        candidate.text.includes("connected to windows") ||
        candidate.text.includes("conectado a windows") ||
        candidate.text.includes("@mineracentinela.cl") ||
        candidate.text.includes("@"));
    const preferred = accounts.find(candidate =>
      candidate.text.includes("connected to windows") ||
      candidate.text.includes("conectado a windows")) || accounts[0] || null;
    if (preferred) {
      const clickPoint = clickPointOf(preferred.element);
      return JSON.stringify({
        success: !!clickPoint,
        message: clickPoint ? "" : "No clickable account point.",
        matchedBy: "accountPicker",
        matchedText: preferred.rawText,
        tagName: (preferred.element.tagName || "").toLowerCase(),
        clickX: clickPoint?.x,
        clickY: clickPoint?.y
      });
    }
  }

  const actionCandidates = Array.from(document.querySelectorAll("button,input[type='submit'],input[type='button'],a,[role='button'],[role='option'],[role='menuitem'],[tabindex]"))
    .filter(isVisible)
    .map(element => ({ element, text: textOf(element), rawText: rawTextOf(element) }))
    .filter(candidate => candidate.text);
  const actions = actionCandidates
    .filter(candidate => !isBackOrOther(candidate))
    .filter(candidate =>
      candidate.text === "yes" ||
      candidate.text === "si" ||
      candidate.text === "sí" ||
      candidate.text === "sign in" ||
      candidate.text.includes("iniciar sesión") ||
      candidate.text.includes("iniciar sesion") ||
      candidate.text.includes("continue") ||
      candidate.text.includes("continuar") ||
      candidate.text.includes("accept") ||
      candidate.text.includes("aceptar") ||
      candidate.text.includes("allow") ||
      candidate.text.includes("permitir") ||
      candidate.text.includes("approve") ||
      candidate.text.includes("aprobar") ||
      candidate.text.includes("next") ||
      candidate.text.includes("siguiente"));
  const action = actions[0] || null;
  if (!action) {
    return JSON.stringify({ success: false, message: "No safe Microsoft auth action found." });
  }

  const clickPoint = clickPointOf(action.element);
  return JSON.stringify({
    success: !!clickPoint,
    message: clickPoint ? "" : "No clickable action point.",
    matchedBy: "authAction",
    matchedText: action.rawText,
    tagName: (action.element.tagName || "").toLowerCase(),
    clickX: clickPoint?.x,
    clickY: clickPoint?.y
  });
})()
""";
    }

    private static AuthorizeObservation ObserveAuthorizeState(
        DateTimeOffset startedAfterUtc,
        int startedProcessId,
        bool reuseExistingProfile,
        int devToolsPort,
        string authorizeUrl,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        AuthorizeObservation? last = null;
        do
        {
            var window = FindAuthorizeWindow(
                startedProcessId,
                startedAfterUtc,
                reuseExistingProfile,
                TimeSpan.Zero);
            var observedAtUtc = DateTimeOffset.UtcNow;
            var page = TryReadEdgePage(devToolsPort, authorizeUrl);
            var title = page?.Title ?? window?.Title;
            var text = window is { ProcessId: not 0 } ? ReadWindowText(window.Value.Hwnd) : string.Empty;
            var classification = ClassifyAuthorizeState(authorizeUrl, page?.Url, title, text);
            last = new AuthorizeObservation(
                classification.Status,
                classification.State,
                title,
                page?.Url,
                classification.Origin,
                classification.Error,
                classification.CodePresent,
                observedAtUtc);
            if (classification.Status is MicrosoftAuthorizeProbeStatus.RedirectObserved or MicrosoftAuthorizeProbeStatus.Failed)
            {
                return last.Value;
            }

            Thread.Sleep(500);
        }
        while (DateTimeOffset.UtcNow <= deadline);

        if (last is { Status: MicrosoftAuthorizeProbeStatus.NeedsUserAction or MicrosoftAuthorizeProbeStatus.RedirectObserved or MicrosoftAuthorizeProbeStatus.Failed })
        {
            return last.Value;
        }

        return last is { } observed
            ? observed with { Status = MicrosoftAuthorizeProbeStatus.TimedOut, State = "browser_observation_timed_out" }
            : new AuthorizeObservation(
                MicrosoftAuthorizeProbeStatus.TimedOut,
                "browser_state_not_observed",
                null,
                null,
                null,
                null,
                false,
                DateTimeOffset.UtcNow);
    }

    private static EdgePageState? TryReadEdgePage(int devToolsPort, string authorizeUrl)
    {
        try
        {
            using var response = DevToolsHttpClient.GetAsync($"http://127.0.0.1:{devToolsPort}/json/list").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            var candidates = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                : Enumerable.Empty<JsonElement>();
            EdgePageState? best = null;
            foreach (var candidate in candidates)
            {
                if (candidate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = candidate.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = candidate.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                var title = candidate.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                var state = new EdgePageState(title, url);
                if (best is null || ScoreEdgePage(state, authorizeUrl) > ScoreEdgePage(best.Value, authorizeUrl))
                {
                    best = state;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreEdgePage(EdgePageState page, string authorizeUrl)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(page.Url))
        {
            score += 10;
        }

        if (string.Equals(page.Url, authorizeUrl, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (Uri.TryCreate(page.Url, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, "devtools", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "edge", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            if (!IsMicrosoftAuthHost(uri.Host))
            {
                score += 100;
            }

            if (ContainsOAuthResult(uri))
            {
                score += 200;
            }
        }

        return score;
    }

    private static (MicrosoftAuthorizeProbeStatus Status, string State, string? Origin, string? Error, bool CodePresent) ClassifyAuthorizeState(
        string authorizeUrl,
        string? observedUrl,
        string? title,
        string? text)
    {
        Uri? observedUri = null;
        string? error = null;
        var codePresent = false;
        if (Uri.TryCreate(observedUrl, UriKind.Absolute, out var uri))
        {
            observedUri = uri;
            var parameters = ReadOAuthParameters(uri);
            codePresent = parameters.ContainsKey("code");
            if (parameters.TryGetValue("error", out var parsedError) && !string.IsNullOrWhiteSpace(parsedError))
            {
                error = parsedError;
            }

            if (codePresent)
            {
                return (MicrosoftAuthorizeProbeStatus.RedirectObserved, "redirect_code_observed", Origin(uri), error, true);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                return (MicrosoftAuthorizeProbeStatus.RedirectObserved, "redirect_error_observed", Origin(uri), error, false);
            }

            if (IsNativeClientRedirect(uri) || (!IsMicrosoftAuthHost(uri.Host) && !string.Equals(observedUrl, authorizeUrl, StringComparison.OrdinalIgnoreCase)))
            {
                return (MicrosoftAuthorizeProbeStatus.RedirectObserved, "redirect_observed", Origin(uri), null, false);
            }
        }

        var normalized = $"{title ?? string.Empty} {text ?? string.Empty}".Trim().ToLowerInvariant();
        if (ContainsAny(
            normalized,
            "need admin approval",
            "approval required",
            "requires approval",
            "consent on behalf of your organization"))
        {
            return (MicrosoftAuthorizeProbeStatus.NeedsUserAction, "browser_needs_admin_approval", observedUri is null ? null : Origin(observedUri), error, codePresent);
        }

        var browserError = ExtractBrowserError($"{title ?? string.Empty} {text ?? string.Empty}");
        if (!string.IsNullOrWhiteSpace(browserError))
        {
            return (MicrosoftAuthorizeProbeStatus.Failed, "browser_error_observed", observedUri is null ? null : Origin(observedUri), browserError, codePresent);
        }

        if (ContainsAny(
            normalized,
            "sign in",
            "iniciar sesión",
            "stay signed in",
            "are you trying to sign in",
            "approve",
            "password",
            "permissions requested",
            "consent",
            "continue",
            "siguiente",
            "contraseña"))
        {
            return (MicrosoftAuthorizeProbeStatus.NeedsUserAction, "browser_needs_user_action", observedUri is null ? null : Origin(observedUri), error, codePresent);
        }

        return (MicrosoftAuthorizeProbeStatus.Opened, "browser_opened", observedUri is null ? null : Origin(observedUri), error, codePresent);
    }

    private static string? ExtractBrowserError(string value)
    {
        var match = Regex.Match(
            value,
            @"(AADSTS\d+:[^\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    private static bool IsMicrosoftAuthHost(string host) =>
        host.EndsWith("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("login.live.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("microsoft.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("live.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsNativeClientRedirect(Uri uri) =>
        uri.AbsolutePath.Contains("/oauth2/nativeclient", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("/oauth20_desktop.srf", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsOAuthResult(Uri uri)
    {
        var parameters = ReadOAuthParameters(uri);
        return parameters.ContainsKey("code") || parameters.ContainsKey("error");
    }

    private static Dictionary<string, string> ReadOAuthParameters(Uri uri)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddParameters(values, uri.Query);
        AddParameters(values, uri.Fragment);
        return values;
    }

    private static void AddParameters(IDictionary<string, string> values, string raw)
    {
        var value = raw.TrimStart('?', '#');
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var parameterValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(parameterValue.Replace("+", " ", StringComparison.Ordinal));
        }
    }

    private static string Origin(Uri uri) => $"{uri.Scheme}://{uri.Authority}";

    private static string ReadWindowText(IntPtr hwnd)
    {
        try
        {
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(hwnd);
            if (root is null)
            {
                return string.Empty;
            }

            var names = new List<string>();
            CollectNames(root, names, maxDepth: 14, maxCount: 500);
            return string.Join(" ", names);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void CollectNames(AutomationElement element, List<string> names, int maxDepth, int maxCount)
    {
        if (names.Count >= maxCount || maxDepth < 0)
        {
            return;
        }

        var name = SafeName(element);
        if (!string.IsNullOrWhiteSpace(name) &&
            !names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(name);
        }

        if (names.Count >= maxCount || maxDepth == 0)
        {
            return;
        }

        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            CollectNames(child, names, maxDepth - 1, maxCount);
            if (names.Count >= maxCount)
            {
                return;
            }
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static (MicrosoftDeviceLoginStatus Status, string State) ClassifyBrowserState(string? title, string? text)
    {
        var value = $"{title ?? string.Empty} {text ?? string.Empty}".Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return (MicrosoftDeviceLoginStatus.Submitted, "browser_state_empty");
        }

        var normalized = value.ToLowerInvariant();
        if (ContainsAny(
            normalized,
            "code didn't work",
            "code does not exist",
            "code doesn't exist",
            "check the code and try again",
            "código no funcion",
            "codigo no funcion",
            "comprueba el código",
            "comprueba el codigo"))
        {
            return (MicrosoftDeviceLoginStatus.InvalidCode, "browser_invalid_code");
        }

        if (ContainsAny(
            normalized,
            "stay signed in",
            "keep me signed in",
            "mantener la sesión",
            "mantener la sesion",
            "permanecer conectado",
            "seguir conectado"))
        {
            return (MicrosoftDeviceLoginStatus.NeedsUserAction, "browser_needs_user_action");
        }

        if (ContainsAny(
            normalized,
            "please enter your password",
            "escriba la contraseña",
            "escriba la contrasena"))
        {
            return (MicrosoftDeviceLoginStatus.NeedsUserAction, "browser_needs_password");
        }

        if (ContainsAny(
            normalized,
            "you may now close",
            "you can close this window",
            "you have signed in",
            "you've signed in",
            "authentication complete",
            "sign in complete",
            "device login complete",
            "device login completed",
            "ha iniciado sesión correctamente",
            "has iniciado sesión correctamente",
            "puede cerrar",
            "puedes cerrar",
            "inicio de sesión completado"))
        {
            return (MicrosoftDeviceLoginStatus.BrowserAccepted, "browser_accepted");
        }

        if (ContainsAny(
            normalized,
            "sign in",
            "enter code",
            "are you trying to sign in",
            "approve",
            "password",
            "permissions requested",
            "stay signed in",
            "iniciar sesión",
            "escriba el código",
            "aprobar",
            "contraseña",
            "permisos solicitados",
            "mantener la sesión"))
        {
            return (MicrosoftDeviceLoginStatus.NeedsUserAction, "browser_needs_user_action");
        }

        return (MicrosoftDeviceLoginStatus.Submitted, "browser_observed");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);

    private static MicrosoftAuthCleanupResult CleanupAuthWindowsCore(MicrosoftAuthCleanupRequest request)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        var summary = CleanupAuthWindows(request, actions, errors);
        return new MicrosoftAuthCleanupResult(
            errors.Count == 0,
            summary.MatchedWindows,
            summary.ClosedWindows,
            summary.PreservedWindows,
            summary.FailedWindows,
            actions,
            errors,
            DateTimeOffset.UtcNow);
    }

    private static MicrosoftAuthCleanupSummary CleanupAuthWindows(
        MicrosoftAuthCleanupRequest request,
        List<string> actions,
        List<string>? errors)
    {
        if (!OperatingSystem.IsWindows())
        {
            actions.Add("cleanup_skipped:not_windows");
            return new MicrosoftAuthCleanupSummary(0, 0, 0, 0);
        }

        var preserveRecentSeconds = Math.Clamp(request.PreserveRecentSeconds, 0, 3600);
        var preserveAfterUtc = DateTimeOffset.UtcNow.AddSeconds(-preserveRecentSeconds);
        var matchedWindows = 0;
        var closedWindows = 0;
        var preservedWindows = 0;
        var failedWindows = 0;

        foreach (var window in EdgeWindows().OrderByDescending(candidate => candidate.StartedAtUtc))
        {
            var text = ReadWindowText(window.Hwnd);
            if (!LooksLikeMicrosoftAuthWindow(window.Title, text))
            {
                continue;
            }

            matchedWindows++;
            if (window.StartedAtUtc >= preserveAfterUtc)
            {
                preservedWindows++;
                continue;
            }

            if (request.DryRun)
            {
                continue;
            }

            if (TryCloseWindow(window))
            {
                closedWindows++;
                continue;
            }

            failedWindows++;
            errors?.Add($"Failed to close auth window hwnd={window.Hwnd} title='{window.Title}'.");
        }

        actions.Add($"auth_window_cleanup:matched={matchedWindows};closed={closedWindows};preserved={preservedWindows};failed={failedWindows}");
        if (request.DryRun)
        {
            actions.Add("cleanup_dry_run");
        }

        return new MicrosoftAuthCleanupSummary(matchedWindows, closedWindows, preservedWindows, failedWindows);
    }

    private static bool LooksLikeMicrosoftAuthWindow(string? title, string? text)
    {
        var normalized = $"{title ?? string.Empty} {text ?? string.Empty}".Trim().ToLowerInvariant();
        return ContainsAny(
            normalized,
            "sign in to your account",
            "enter code",
            "need admin approval",
            "permissions requested",
            "are you trying to sign in",
            "stay signed in",
            "you have signed in",
            "signed in to the",
            "wrong place",
            "wrongplace",
            "appverify",
            "aadsts",
            "contraseña",
            "permisos solicitados",
            "mantener la sesión",
            "mantener la sesion",
            "ha iniciado sesión",
            "has iniciado sesión",
            "has iniciado sesion",
            "escriba el código",
            "escriba el codigo");
    }

    private static bool TryCloseWindow(BrowserWindow window)
    {
        if (window.Hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            PostMessage(window.Hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow <= deadline)
            {
                if (!IsWindow(window.Hwnd))
                {
                    return true;
                }

                Thread.Sleep(100);
            }
        }
        catch
        {
            // Try process-level close below.
        }

        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            process.CloseMainWindow();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow <= deadline)
            {
                if (!IsWindow(window.Hwnd))
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

        return !IsWindow(window.Hwnd);
    }

    private static IEnumerable<BrowserWindow> EdgeWindows()
    {
        foreach (var process in Process.GetProcessesByName("msedge"))
        {
            using (process)
            {
                BrowserWindow? window = null;
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    window = new BrowserWindow(
                        process.Id,
                        process.MainWindowHandle,
                        process.MainWindowTitle,
                        new DateTimeOffset(process.StartTime.ToUniversalTime()));
                }
                catch
                {
                    continue;
                }

                yield return window.Value;
            }
        }
    }

    private static MicrosoftDeviceLoginResult ReadResult(string runId)
    {
        var normalized = string.Equals(runId, "latest", StringComparison.OrdinalIgnoreCase)
            ? ReadLatestRunId()
            : SafeFileName(runId, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Microsoft device login run id is required."));
        }

        var path = ResultPath(normalized);
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Microsoft device login result was not found: {normalized}"));
        }

        return JsonSerializer.Deserialize<MicrosoftDeviceLoginResult>(
            File.ReadAllText(path),
            OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Microsoft device login result JSON was invalid: {normalized}"));
    }

    private static MicrosoftAuthorizeProbeResult ReadAuthorizeProbeResult(string runId)
    {
        var normalized = string.Equals(runId, "latest", StringComparison.OrdinalIgnoreCase)
            ? ReadLatestAuthorizeProbeRunId()
            : SafeFileName(runId, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Microsoft authorize probe run id is required."));
        }

        var path = AuthorizeProbeResultPath(normalized);
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Microsoft authorize probe result was not found: {normalized}"));
        }

        return JsonSerializer.Deserialize<MicrosoftAuthorizeProbeResult>(
            File.ReadAllText(path),
            OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Microsoft authorize probe result JSON was invalid: {normalized}"));
    }

    private static string ReadLatestRunId()
    {
        var path = LatestPath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    private static string ReadLatestAuthorizeProbeRunId()
    {
        var path = LatestAuthorizeProbePath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    private static void WriteResult(string runId, MicrosoftDeviceLoginResult result)
    {
        var path = ResultPath(runId);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(result, OperatorJson.SerializerOptions));
        File.WriteAllText(LatestPath(), runId);
    }

    private static void WriteAuthorizeProbeResult(string runId, MicrosoftAuthorizeProbeResult result)
    {
        var path = AuthorizeProbeResultPath(runId);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(result, OperatorJson.SerializerOptions));
        File.WriteAllText(LatestAuthorizeProbePath(), runId);
    }

    private static string ResultPath(string runId) =>
        Path.Combine(RunRoot(runId), "result.json");

    private static string AuthorizeProbeResultPath(string runId) =>
        Path.Combine(AuthorizeProbeRunRoot(runId), "result.json");

    private static string LatestPath() =>
        Path.Combine(AuthRoot(), "latest.txt");

    private static string LatestAuthorizeProbePath() =>
        Path.Combine(AuthorizeProbeRoot(), "latest.txt");

    private static string RunRoot(string runId) =>
        Path.Combine(AuthRoot(), runId);

    private static string AuthorizeProbeRunRoot(string runId) =>
        Path.Combine(AuthorizeProbeRoot(), runId);

    private static string AuthRoot() =>
        Path.Combine(StateRoot(), "run", "auth", "microsoft-device-login");

    private static string AuthorizeProbeRoot() =>
        Path.Combine(StateRoot(), "run", "auth", "microsoft-authorize-probe");

    private static string StateRoot() =>
        Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOperator");

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static string SafeFileName(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length > 180 ? value[..180] : value;
    }

    private readonly record struct BrowserWindow(int ProcessId, IntPtr Hwnd, string? Title, DateTimeOffset StartedAtUtc);

    private readonly record struct EdgeProfileSelection(string? UserDataDir, string? ProfileDirectory)
    {
        public static EdgeProfileSelection Temp(string path) => new(path, null);
    }

    private readonly record struct BrowserObservation(
        MicrosoftDeviceLoginStatus Status,
        string State,
        string? Title,
        DateTimeOffset ObservedAtUtc);

    private readonly record struct EdgePageState(string? Title, string? Url);

    private readonly record struct AuthorizeObservation(
        MicrosoftAuthorizeProbeStatus Status,
        string State,
        string? Title,
        string? Url,
        string? Origin,
        string? Error,
        bool CodePresent,
        DateTimeOffset ObservedAtUtc);

    private readonly record struct MicrosoftAuthCleanupSummary(
        int MatchedWindows,
        int ClosedWindows,
        int PreservedWindows,
        int FailedWindows);
}
