using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsOperator.Automation.Interop;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class PowerAutomateMcpService : IPowerAutomateMcpService, IHostedService
{
    private const string DefaultPackageSpec = "@kaael1/mcp-power-automate@0.4.1";
    private const string DefaultBridgeHost = "127.0.0.1";
    private const int DefaultBridgePort = 17373;
    private const string DefaultPowerAutomateUrl = "https://make.powerautomate.com/";
    private const int DefaultEdgeIdleTtlSeconds = 15 * 60;
    private const int MaxEdgeIdleTtlSeconds = 60 * 60;
    private const int JanitorIntervalSeconds = 60;
    private const uint WmClose = 0x0010;
    private static readonly TimeSpan ProcessProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExtensionPathTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BridgeProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PowerAutomateApiTimeout = TimeSpan.FromSeconds(60);
    private static readonly HttpClient BridgeClient = new() { Timeout = BridgeProbeTimeout };
    private static readonly HttpClient PowerAutomateApiClient = new() { Timeout = PowerAutomateApiTimeout };
    private static readonly object BridgeProcessLock = new();
    private static readonly object BridgeLogLock = new();
    private static Process? heldBridgeProcess;
    private static StreamWriter? heldBridgeInput;

    private readonly PowerAutomateMcpOptions _options;
    private readonly IPowerAutomateMcpRuntime _runtime;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _janitorCts;
    private Task? _janitorTask;

    public PowerAutomateMcpService(IOptions<PowerAutomateMcpOptions>? options = null)
        : this(options?.Value ?? new PowerAutomateMcpOptions(), new PowerAutomateMcpRuntime())
    {
    }

    internal PowerAutomateMcpService(PowerAutomateMcpOptions options, IPowerAutomateMcpRuntime runtime)
    {
        _options = options ?? new PowerAutomateMcpOptions();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_janitorTask is not null)
        {
            return Task.CompletedTask;
        }

        _janitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _janitorTask = RunJanitorLoopAsync(_janitorCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_janitorCts is null || _janitorTask is null)
        {
            return;
        }

        _janitorCts.Cancel();
        await Task.WhenAny(_janitorTask, Task.Delay(Timeout.Infinite, cancellationToken));
        _janitorCts.Dispose();
        _janitorCts = null;
        _janitorTask = null;
    }

    public Task<PowerAutomateMcpStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.Run(() => BuildStatus(new StatusOptions(), cancellationToken), cancellationToken);

    public Task<PowerAutomateMcpStartResult> StartBridgeAsync(
        PowerAutomateMcpStartRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(() => StartBridgeCore(request ?? new PowerAutomateMcpStartRequest(), cancellationToken), cancellationToken);

    public Task<PowerAutomateMcpEdgeResult> OpenEdgeAsync(
        PowerAutomateMcpEdgeRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(() => OpenEdgeCore(request ?? new PowerAutomateMcpEdgeRequest(), cancellationToken), cancellationToken);

    public Task<PowerAutomateMcpEdgeCleanupResult> CleanupEdgeAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CleanupEdgeCore("power_automate_mcp_edge_cleanup_requested", false, cancellationToken), cancellationToken);

    public Task<PowerAutomateMcpFlowReadResult> ReadFlowAsync(
        PowerAutomateMcpFlowReadRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(() => ReadFlowCore(request ?? new PowerAutomateMcpFlowReadRequest(), cancellationToken), cancellationToken);

    public Task<PowerAutomateMcpFlowUpdateResult> UpdateFlowAsync(
        PowerAutomateMcpFlowUpdateRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(() => UpdateFlowCore(request ?? new PowerAutomateMcpFlowUpdateRequest(), cancellationToken), cancellationToken);

    internal Task<PowerAutomateMcpEdgeCleanupResult> CleanupExpiredEdgeAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CleanupEdgeCore("power_automate_mcp_edge_cleanup_expired", true, cancellationToken), cancellationToken);

    private async Task RunJanitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(JanitorIntervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await CleanupExpiredEdgeAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Best-effort janitor only.
            }
        }
    }

    private PowerAutomateMcpStartResult StartBridgeCore(
        PowerAutomateMcpStartRequest request,
        CancellationToken cancellationToken)
    {
        var packageSpec = NormalizePackageSpec(request.PackageSpec);
        var host = NormalizeBridgeHost(request.BridgeHost);
        var port = NormalizeBridgePort(request.BridgePort);
        var waitSeconds = Math.Clamp(request.WaitSeconds, 0, 60);
        var actions = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        string? extensionPath = null;

        if (request.ResolveExtensionPath && !request.DryRun)
        {
            extensionPath = TryResolveExtensionPath(packageSpec, actions, warnings, cancellationToken);
        }
        else if (request.ResolveExtensionPath && request.DryRun)
        {
            actions.Add("power_automate_mcp_extension_path_resolution_skipped_dry_run");
        }

        if (request.DryRun)
        {
            actions.Add("power_automate_mcp_start_dry_run");
            var dryRunStatus = BuildStatus(new StatusOptions(packageSpec, host, port, extensionPath), cancellationToken);
            return new PowerAutomateMcpStartResult
            {
                Success = true,
                StatePath = StatePath(),
                LogPath = BridgeLogPath(),
                Status = dryRunStatus,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = _runtime.UtcNow,
            };
        }

        if (!_runtime.IsWindows)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate MCP bridge start requires Windows desktop session."));
        }

        var currentStatus = BuildStatus(new StatusOptions(packageSpec, host, port, extensionPath), cancellationToken);
        if (currentStatus.BridgeListening)
        {
            actions.Add("power_automate_mcp_bridge_already_listening");
            return new PowerAutomateMcpStartResult
            {
                Success = currentStatus.BridgeHealthy,
                ProcessId = currentStatus.BridgeProcessId,
                StatePath = currentStatus.StatePath,
                LogPath = currentStatus.LogPath,
                Status = currentStatus,
                Actions = actions,
                Warnings = warnings.Concat(currentStatus.Warnings).ToArray(),
                Errors = currentStatus.Errors,
                ObservedAtUtc = _runtime.UtcNow,
            };
        }

        var npxPath = ResolveExecutable("npx.cmd", KnownNpxPaths());
        var nodePath = ResolveExecutable("node.exe", KnownNodePaths());
        var serverPath = ResolveInstalledServerPath(packageSpec);
        if (string.IsNullOrWhiteSpace(serverPath) && string.IsNullOrWhiteSpace(npxPath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate MCP package is not installed and npx.cmd was not found."));
        }

        if (!string.IsNullOrWhiteSpace(serverPath) && string.IsNullOrWhiteSpace(nodePath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate MCP package is installed but node.exe was not found."));
        }

        if (string.IsNullOrWhiteSpace(extensionPath))
        {
            extensionPath = ResolveInstalledExtensionPath(packageSpec);
            if (!string.IsNullOrWhiteSpace(extensionPath))
            {
                actions.Add("power_automate_mcp_extension_path_resolved_installed");
            }
        }

        Directory.CreateDirectory(StateRoot());
        var logPath = BridgeLogPath();
        var useInstalledServer = !string.IsNullOrWhiteSpace(serverPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = useInstalledServer ? nodePath! : npxPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = StateRoot(),
        };
        if (useInstalledServer)
        {
            startInfo.ArgumentList.Add(serverPath!);
        }
        else
        {
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(packageSpec);
        }

        startInfo.Environment["POWER_AUTOMATE_BRIDGE_HOST"] = host;
        startInfo.Environment["POWER_AUTOMATE_BRIDGE_PORT"] = port.ToString();
        startInfo.Environment["POWER_AUTOMATE_DATA_DIR"] = BridgeDataRoot();

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable($"Failed to start Power Automate MCP bridge: {ex.Message}"));
        }

        if (process is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Failed to start Power Automate MCP bridge process."));
        }

        actions.Add("power_automate_mcp_bridge_process_started");
        actions.Add($"power_automate_mcp_bridge_port:{port}");
        actions.Add(useInstalledServer
            ? "power_automate_mcp_bridge_started_installed_server"
            : "power_automate_mcp_bridge_started_npx");
        HoldBridgeProcess(process);
        StartLogPump(process, logPath);
        UpdateState(state =>
            state with
            {
                Bridge = new BridgeProcessState(
                    packageSpec,
                    host,
                    port,
                    process.Id,
                    logPath,
                    extensionPath,
                    _runtime.UtcNow),
            });

        var deadline = _runtime.UtcNow.AddSeconds(waitSeconds);
        PowerAutomateMcpStatusResult status;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = BuildStatus(new StatusOptions(packageSpec, host, port, extensionPath), cancellationToken);
            if (status.BridgeListening)
            {
                break;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }
        while (_runtime.UtcNow < deadline);

        status = BuildStatus(new StatusOptions(packageSpec, host, port, extensionPath), cancellationToken);
        if (!status.BridgeListening)
        {
            errors.Add("Power Automate MCP bridge did not open the loopback port before timeout.");
        }

        if (status.BridgeListening && !status.BridgeHealthy)
        {
            warnings.Add("Power Automate MCP bridge is listening but /health did not return ok.");
        }

        return new PowerAutomateMcpStartResult
        {
            Success = errors.Count == 0 && status.BridgeListening,
            ProcessId = process.Id,
            StatePath = StatePath(),
            LogPath = logPath,
            Status = status,
            Actions = actions,
            Warnings = warnings.Concat(status.Warnings).ToArray(),
            Errors = errors.Concat(status.Errors).ToArray(),
            ObservedAtUtc = _runtime.UtcNow,
        };
    }

    private PowerAutomateMcpEdgeResult OpenEdgeCore(
        PowerAutomateMcpEdgeRequest request,
        CancellationToken cancellationToken)
    {
        var actions = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var now = _runtime.UtcNow;
        var ttlSeconds = ResolveIdleTtlSeconds(request.IdleTtlSeconds);
        var url = NormalizePowerAutomateUrl(request.Url);
        var packageSpec = NormalizePackageSpec(request.PackageSpec);
        var waitSeconds = Math.Clamp(request.WaitSeconds, 0, 30);

        string? extensionPath = request.ExtensionPath;
        if (string.IsNullOrWhiteSpace(extensionPath))
        {
            extensionPath = ReadState().Bridge?.ExtensionPath;
        }

        var edgePath = ResolveEdgePath();
        if (string.IsNullOrWhiteSpace(edgePath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Microsoft Edge executable was not found."));
        }

        if (request.DryRun)
        {
            actions.Add("power_automate_mcp_edge_dry_run");
            if (string.IsNullOrWhiteSpace(extensionPath))
            {
                actions.Add("power_automate_mcp_extension_path_resolution_skipped_dry_run");
            }
            else
            {
                extensionPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(extensionPath));
                if (!Directory.Exists(extensionPath))
                {
                    warnings.Add($"extension_path_missing_dry_run:{extensionPath}");
                }
            }

            return new PowerAutomateMcpEdgeResult
            {
                Success = true,
                Url = url,
                ProfileMode = request.ProfileMode,
                EdgePath = edgePath,
                ExtensionPath = extensionPath,
                TtlSeconds = ttlSeconds,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = now,
            };
        }

        if (string.IsNullOrWhiteSpace(extensionPath))
        {
            extensionPath = TryResolveExtensionPath(packageSpec, actions, warnings, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(extensionPath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Could not resolve @kaael1/mcp-power-automate extension path."));
        }

        extensionPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(extensionPath));
        if (!Directory.Exists(extensionPath))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed($"Extension path does not exist: {extensionPath}"));
        }

        if (!_runtime.IsWindows)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate MCP Edge launch requires Windows desktop session."));
        }

        var state = ReadState();
        var trackedEdge = state.OwnedEdge;
        if (trackedEdge is not null)
        {
            if (IsOwnedEdgeAlive(trackedEdge) && trackedEdge.LeaseExpiresAtUtc >= now)
            {
                var renewed = trackedEdge with
                {
                    LastUsedAtUtc = now,
                    LeaseExpiresAtUtc = now.AddSeconds(ttlSeconds),
                    TtlSeconds = ttlSeconds,
                    ClosedAtUtc = null,
                };
                UpdateState(current => current with { OwnedEdge = renewed });
                actions.Add("power_automate_mcp_edge_reused");
                actions.Add("power_automate_mcp_edge_lease_renewed");
                if (!string.Equals(trackedEdge.Url, url, StringComparison.Ordinal))
                {
                    warnings.Add($"tracked_edge_url_differs:{trackedEdge.Url}");
                }

                return CreateEdgeResult(renewed, true, ResolveEdgePath(), actions, warnings, errors, now);
            }

            if (IsOwnedEdgeAlive(trackedEdge))
            {
                var cleanup = CleanupEdgeCore("power_automate_mcp_edge_cleanup_expired", true, cancellationToken);
                actions.AddRange(cleanup.Actions);
                warnings.AddRange(cleanup.Warnings);
                errors.AddRange(cleanup.Errors);
                if (cleanup.Alive)
                {
                    throw new OperatorFailureException(
                        OperatorErrors.PowerAutomateMcpUnavailable("Expired owned Power Automate MCP Edge lease could not be closed cleanly."));
                }
            }
        }

        Directory.CreateDirectory(StateRoot());
        var userDataDir = request.ProfileMode == BrowserEdgeProfileMode.Temp
            ? Path.Combine(StateRoot(), "edge-profile")
            : null;
        if (!string.IsNullOrWhiteSpace(userDataDir))
        {
            Directory.CreateDirectory(userDataDir);
        }

        var launch = _runtime.LaunchEdge(new EdgeLaunchSpec(
            edgePath,
            url,
            extensionPath,
            request.ProfileMode,
            userDataDir,
            StateRoot()));
        if (launch.Process is not null)
        {
            StartLogPump(launch.Process, BridgeLogPath());
        }

        actions.Add("power_automate_mcp_edge_started");
        actions.Add("power_automate_mcp_extension_loaded_argument");
        actions.Add(request.ProfileMode == BrowserEdgeProfileMode.Temp ? "edge_profile_mode:temp" : "edge_profile_mode:work");
        if (request.ProfileMode == BrowserEdgeProfileMode.Work)
        {
            warnings.Add("If Edge is already running with the work profile, Chromium may open a tab without loading the unpacked extension. Close Edge first or retry with profileMode=temp.");
        }

        if (waitSeconds > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(waitSeconds));
        }

        var discovery = _runtime.TryFindPowerAutomateEdgeWindow(
            launch.ProcessId,
            url,
            TimeSpan.FromSeconds(Math.Max(waitSeconds, 1)));
        if (discovery is null)
        {
            actions.Add("power_automate_mcp_edge_window_not_found");
            errors.Add("Power Automate Edge window was not found after launch; no owned Edge lease was persisted.");
            warnings.Add("power_automate_mcp_edge_untracked_after_launch");
            return new PowerAutomateMcpEdgeResult
            {
                Success = false,
                Url = url,
                ProfileMode = request.ProfileMode,
                ProcessId = launch.ProcessId,
                Alive = false,
                EdgePath = edgePath,
                ExtensionPath = extensionPath,
                TtlSeconds = ttlSeconds,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = _runtime.UtcNow,
            };
        }

        actions.Add("power_automate_mcp_edge_window_discovered");
        if (discovery.ProcessId != launch.ProcessId)
        {
            actions.Add("power_automate_mcp_edge_reused_existing_window");
        }

        var lease = new OwnedEdgeLeaseState(
            discovery.ProcessId,
            discovery.Hwnd,
            request.ProfileMode,
            url,
            extensionPath,
            now,
            now,
            now.AddSeconds(ttlSeconds),
            null,
            ttlSeconds);
        UpdateState(current => current with { OwnedEdge = lease });
        actions.Add("power_automate_mcp_edge_lease_renewed");

        return CreateEdgeResult(lease, true, edgePath, actions, warnings, errors, _runtime.UtcNow);
    }

    private PowerAutomateMcpFlowReadResult ReadFlowCore(
        PowerAutomateMcpFlowReadRequest request,
        CancellationToken cancellationToken)
    {
        var actions = new List<string> { "power_automate_mcp_flow_read_requested" };
        var warnings = new List<string>();
        var errors = new List<string>();
        var context = LoadFlowApiContext(
            request.FlowId,
            request.BridgeHost,
            request.BridgePort,
            actions,
            warnings,
            cancellationToken);
        var flow = FetchFlowWithFallback(context, actions, warnings, cancellationToken);
        actions.Add($"power_automate_mcp_flow_read_source:{flow.Source}");
        return CreateFlowReadResult(flow, actions, warnings, errors, _runtime.UtcNow);
    }

    private PowerAutomateMcpFlowUpdateResult UpdateFlowCore(
        PowerAutomateMcpFlowUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var actions = new List<string> { "power_automate_mcp_flow_update_requested" };
        var warnings = new List<string>();
        var errors = new List<string>();
        var candidateFlow = ParseFlowContent(request.FlowJson);
        var context = LoadFlowApiContext(
            request.FlowId,
            request.BridgeHost,
            request.BridgePort,
            actions,
            warnings,
            cancellationToken);

        if (request.Create)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new OperatorFailureException(
                    OperatorErrors.PowerAutomateMcpValidationFailed("DisplayName is required when Create is true."));
            }

            var createDisplayName = request.DisplayName.Trim();
            if (request.ValidateBefore)
            {
                warnings.Add("power_automate_mcp_create_validate_before_skipped");
            }

            if (request.DryRun)
            {
                var dryRunFlow = new NormalizedPowerAutomateFlow(
                    context.EnvId,
                    string.Empty,
                    createDisplayName,
                    CloneObject(candidateFlow),
                    null,
                    "dry-run");
                actions.Add("power_automate_mcp_flow_create_dry_run_no_post");
                return new PowerAutomateMcpFlowUpdateResult
                {
                    Success = true,
                    Status = PowerAutomateMcpFlowUpdateStatus.DryRun,
                    DryRun = true,
                    After = CreateFlowReadResult(dryRunFlow, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
                    Actions = actions,
                    Warnings = warnings,
                    Errors = errors,
                    ObservedAtUtc = _runtime.UtcNow,
                };
            }

            var created = CreateFlowLegacy(context, candidateFlow, createDisplayName, cancellationToken);
            PowerAutomateMcpFlowValidationResult? createdValidation = null;
            if (request.ValidateAfter)
            {
                createdValidation = ValidateFlow(context with { FlowId = created.FlowId }, created.Flow, cancellationToken);
                actions.Add("power_automate_mcp_flow_validated_after");
            }

            actions.Add("power_automate_mcp_flow_created_legacy_api");
            return new PowerAutomateMcpFlowUpdateResult
            {
                Success = true,
                Status = PowerAutomateMcpFlowUpdateStatus.Succeeded,
                DryRun = false,
                After = CreateFlowReadResult(created, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
                AfterValidation = createdValidation,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = _runtime.UtcNow,
            };
        }

        var before = FetchFlowWithFallback(context, actions, warnings, cancellationToken);
        PowerAutomateMcpFlowValidationResult? beforeValidation = null;
        PowerAutomateMcpFlowValidationResult? afterValidation = null;

        if (request.ValidateBefore)
        {
            beforeValidation = ValidateFlow(context, before.Flow, cancellationToken);
            actions.Add("power_automate_mcp_flow_validated_before");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? before.DisplayName
            : request.DisplayName.Trim();

        if (request.DryRun)
        {
            var dryRunAfter = before with
            {
                DisplayName = displayName,
                Flow = CloneObject(candidateFlow),
                Source = "dry-run",
            };
            if (request.ValidateAfter)
            {
                afterValidation = ValidateFlow(context, candidateFlow, cancellationToken);
                actions.Add("power_automate_mcp_flow_validated_dry_run_after");
            }

            actions.Add("power_automate_mcp_flow_update_dry_run_no_patch");
            return new PowerAutomateMcpFlowUpdateResult
            {
                Success = true,
                Status = PowerAutomateMcpFlowUpdateStatus.DryRun,
                DryRun = true,
                Before = CreateFlowReadResult(before, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
                After = CreateFlowReadResult(dryRunAfter, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
                BeforeValidation = beforeValidation,
                AfterValidation = afterValidation,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = _runtime.UtcNow,
            };
        }

        var after = PatchFlowWithFallback(context, before, candidateFlow, displayName, actions, warnings, cancellationToken);
        if (request.ValidateAfter)
        {
            afterValidation = ValidateFlow(context, after.Flow, cancellationToken);
            actions.Add("power_automate_mcp_flow_validated_after");
        }

        actions.Add($"power_automate_mcp_flow_update_source:{after.Source}");
        return new PowerAutomateMcpFlowUpdateResult
        {
            Success = true,
            Status = PowerAutomateMcpFlowUpdateStatus.Succeeded,
            DryRun = false,
            Before = CreateFlowReadResult(before, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
            After = CreateFlowReadResult(after, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _runtime.UtcNow),
            BeforeValidation = beforeValidation,
            AfterValidation = afterValidation,
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = _runtime.UtcNow,
        };
    }

    private FlowApiContext LoadFlowApiContext(
        string? requestedFlowId,
        string? requestedBridgeHost,
        int? requestedBridgePort,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var bridgeHost = NormalizeBridgeHost(requestedBridgeHost ?? DefaultBridgeHost);
        var bridgePort = NormalizeBridgePort(requestedBridgePort ?? DefaultBridgePort);
        var status = BuildStatus(new StatusOptions(BridgeHost: bridgeHost, BridgePort: bridgePort), cancellationToken);
        if (!status.BridgeListening || !status.BridgeHealthy)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate API flow edit requires a healthy local MCP bridge with browser-captured tokens."));
        }

        var session = ReadBridgeDataObject("session.json");
        if (session is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate API flow edit requires a captured browser session. Open or refresh the target flow with the MCP extension enabled."));
        }

        var envId = ReadRequiredString(session, "envId", "session.envId");
        var sessionFlowId = ReadOptionalString(session, "flowId");
        var activeTarget = ReadBridgeDataObject("active-target.json");
        var activeFlowId = activeTarget is not null &&
            string.Equals(ReadOptionalString(activeTarget, "envId"), envId, StringComparison.Ordinal)
                ? ReadOptionalString(activeTarget, "flowId")
                : null;
        var flowId = FirstNonBlank(requestedFlowId, activeFlowId, sessionFlowId);
        if (string.IsNullOrWhiteSpace(flowId))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Flow id was not supplied and no active flow target was captured."));
        }

        var apiUrl = ReadRequiredString(session, "apiUrl", "session.apiUrl");
        var apiToken = ReadRequiredString(session, "apiToken", "session.apiToken");
        var legacyApiUrl = ReadOptionalString(session, "legacyApiUrl");
        var legacyToken = ReadOptionalString(session, "legacyToken");
        var legacySource = !string.IsNullOrWhiteSpace(legacyToken) ? "captured-legacy-session" : null;
        if (string.IsNullOrWhiteSpace(legacyToken))
        {
            var tokenAuditLegacy = TryReadLegacyTokenFromAudit();
            legacyApiUrl = tokenAuditLegacy?.BaseUrl ?? legacyApiUrl;
            legacyToken = tokenAuditLegacy?.Token;
            legacySource = tokenAuditLegacy?.Source;
        }

        actions.Add("power_automate_mcp_flow_context_loaded");
        if (string.IsNullOrWhiteSpace(legacyToken))
        {
            warnings.Add("power_automate_mcp_legacy_token_unavailable");
        }

        return new FlowApiContext(
            envId,
            flowId.Trim(),
            apiUrl,
            apiToken,
            legacyApiUrl,
            legacyToken,
            legacySource,
            ReadOptionalString(activeTarget, "displayName"));
    }

    private NormalizedPowerAutomateFlow FetchFlowWithFallback(
        FlowApiContext context,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var modern = FetchFlowModern(context, cancellationToken);
            actions.Add("power_automate_mcp_flow_read_modern_api");
            return modern;
        }
        catch (Exception ex) when (ex is not OperatorFailureException { Error.Code: ErrorCodes.PowerAutomateMcpValidationFailed })
        {
            warnings.Add($"power_automate_mcp_flow_read_modern_failed:{SanitizedOneLine(ex.Message)}");
            var legacy = FetchFlowLegacy(context, cancellationToken);
            actions.Add("power_automate_mcp_flow_read_legacy_api");
            return legacy;
        }
    }

    private NormalizedPowerAutomateFlow PatchFlowWithFallback(
        FlowApiContext context,
        NormalizedPowerAutomateFlow before,
        JsonObject flow,
        string displayName,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.Equals(before.Source, "modern-api", StringComparison.Ordinal))
        {
            try
            {
                var modern = PatchFlowModern(context, before, flow, displayName, cancellationToken);
                actions.Add("power_automate_mcp_flow_patch_modern_api");
                return modern;
            }
            catch (Exception ex) when (ex is not OperatorFailureException { Error.Code: ErrorCodes.PowerAutomateMcpValidationFailed })
            {
                warnings.Add($"power_automate_mcp_flow_patch_modern_failed:{SanitizedOneLine(ex.Message)}");
            }
        }

        var legacy = PatchFlowLegacy(context, before, flow, displayName, cancellationToken);
        actions.Add("power_automate_mcp_flow_patch_legacy_api");
        return legacy;
    }

    private NormalizedPowerAutomateFlow FetchFlowModern(
        FlowApiContext context,
        CancellationToken cancellationToken)
    {
        var response = RequestPowerAutomateJson(
            context.ApiUrl,
            $"powerautomate/flows/{Uri.EscapeDataString(context.FlowId)}",
            "1",
            HttpMethod.Get,
            context.ApiToken,
            null,
            cancellationToken);
        return NormalizeFlow(context, response, "modern-api");
    }

    private NormalizedPowerAutomateFlow FetchFlowLegacy(
        FlowApiContext context,
        CancellationToken cancellationToken)
    {
        var legacy = RequireLegacyApi(context);
        var response = RequestPowerAutomateJson(
            legacy.BaseUrl,
            LegacyFlowPath(context.EnvId, context.FlowId),
            "2016-11-01",
            HttpMethod.Get,
            legacy.Token,
            null,
            cancellationToken);
        return NormalizeFlow(context, response, "legacy-api");
    }

    private NormalizedPowerAutomateFlow PatchFlowModern(
        FlowApiContext context,
        NormalizedPowerAutomateFlow before,
        JsonObject flow,
        string displayName,
        CancellationToken cancellationToken)
    {
        var response = RequestPowerAutomateJson(
            context.ApiUrl,
            $"powerautomate/flows/{Uri.EscapeDataString(context.FlowId)}",
            "1",
            HttpMethod.Patch,
            context.ApiToken,
            BuildFlowPatchBody(before, flow, displayName),
            cancellationToken);
        return NormalizeFlow(context, response, "modern-api");
    }

    private NormalizedPowerAutomateFlow PatchFlowLegacy(
        FlowApiContext context,
        NormalizedPowerAutomateFlow before,
        JsonObject flow,
        string displayName,
        CancellationToken cancellationToken)
    {
        var legacy = RequireLegacyApi(context);
        var response = RequestPowerAutomateJson(
            legacy.BaseUrl,
            LegacyFlowPath(context.EnvId, context.FlowId),
            "2016-11-01",
            HttpMethod.Patch,
            legacy.Token,
            BuildFlowPatchBody(before, flow, displayName),
            cancellationToken);
        return NormalizeFlow(context, response, "legacy-api");
    }

    private NormalizedPowerAutomateFlow CreateFlowLegacy(
        FlowApiContext context,
        JsonObject flow,
        string displayName,
        CancellationToken cancellationToken)
    {
        var legacy = RequireLegacyApi(context);
        var response = RequestPowerAutomateJson(
            legacy.BaseUrl,
            $"providers/Microsoft.ProcessSimple/environments/{Uri.EscapeDataString(context.EnvId)}/flows",
            "2016-11-01",
            HttpMethod.Post,
            legacy.Token,
            BuildFlowCreateBody(flow, displayName),
            cancellationToken);
        return NormalizeFlow(context, response, "legacy-api");
    }

    private PowerAutomateMcpFlowValidationResult ValidateFlow(
        FlowApiContext context,
        JsonObject flow,
        CancellationToken cancellationToken)
    {
        var legacy = TryGetLegacyApi(context);
        if (legacy is null)
        {
            return new PowerAutomateMcpFlowValidationResult
            {
                Available = false,
                Message = "Legacy validation is unavailable because no flow-compatible legacy token was captured.",
            };
        }

        var requestBody = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["definition"] = RequireFlowProperty(flow, "definition").DeepClone(),
            },
        };
        var basePath = LegacyFlowPath(context.EnvId, context.FlowId);
        var errors = NormalizeIssues(RequestPowerAutomateJson(
            legacy.BaseUrl,
            $"{basePath}/checkFlowErrors",
            "2016-11-01",
            HttpMethod.Post,
            legacy.Token,
            requestBody,
            cancellationToken));
        var warnings = NormalizeIssues(RequestPowerAutomateJson(
            legacy.BaseUrl,
            $"{basePath}/checkFlowWarnings",
            "2016-11-01",
            HttpMethod.Post,
            legacy.Token,
            requestBody,
            cancellationToken));
        return new PowerAutomateMcpFlowValidationResult
        {
            Available = true,
            Source = legacy.Source,
            ErrorCount = errors.Count,
            WarningCount = warnings.Count,
            ErrorsJson = errors.ToJsonString(OperatorJson.SerializerOptions),
            WarningsJson = warnings.ToJsonString(OperatorJson.SerializerOptions),
        };
    }

    private JsonObject? ReadBridgeDataObject(string fileName)
    {
        var path = Path.Combine(BridgeDataRoot(), fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private CapturedLegacyToken? TryReadLegacyTokenFromAudit()
    {
        var audit = ReadBridgeDataObject("token-audit.json");
        if (audit is null ||
            !audit.TryGetPropertyValue("candidates", out var candidatesNode) ||
            candidatesNode is not JsonArray candidates)
        {
            return null;
        }

        foreach (var candidate in candidates.OfType<JsonObject>())
        {
            var audience = ReadOptionalString(candidate, "aud");
            if (!string.Equals(audience, "https://service.flow.microsoft.com/", StringComparison.Ordinal) &&
                !string.Equals(audience, "https://service.powerapps.com/", StringComparison.Ordinal))
            {
                continue;
            }

            var token = ReadOptionalString(candidate, "token");
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            return new CapturedLegacyToken(
                "https://api.flow.microsoft.com/",
                token,
                ReadOptionalString(candidate, "source") ?? "token-audit");
        }

        return null;
    }

    private static JsonObject ParseFlowContent(string flowJson)
    {
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("FlowJson must contain a JSON object with connectionReferences and definition."));
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(flowJson) as JsonObject
                ?? throw new JsonException("Root value is not an object.");
        }
        catch (Exception ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed($"FlowJson is not valid JSON: {SanitizedOneLine(ex.Message)}"));
        }

        if (root.TryGetPropertyValue("flow", out var nestedFlow) && nestedFlow is JsonObject nestedFlowObject)
        {
            root = nestedFlowObject;
        }

        _ = RequireFlowProperty(root, "connectionReferences");
        _ = RequireFlowProperty(root, "definition");
        return CloneObject(root);
    }

    private static JsonNode RequestPowerAutomateJson(
        string baseUrl,
        string resourcePath,
        string apiVersion,
        HttpMethod method,
        string token,
        JsonObject? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildPowerAutomateUri(baseUrl, resourcePath, apiVersion));
        request.Headers.TryAddWithoutValidation("Authorization", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(OperatorJson.SerializerOptions), Encoding.UTF8, "application/json");
        }

        using var response = PowerAutomateApiClient.SendAsync(request, cancellationToken).GetAwaiter().GetResult();
        var responseBody = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(responseBody)
                ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
                : $"{(int)response.StatusCode} {response.ReasonPhrase}: {SanitizedOneLine(responseBody)}";
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable($"Power Automate API request failed: {detail}"));
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(responseBody) ?? new JsonObject();
        }
        catch (Exception ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable($"Power Automate API returned invalid JSON: {SanitizedOneLine(ex.Message)}"));
        }
    }

    private static Uri BuildPowerAutomateUri(string baseUrl, string resourcePath, string apiVersion)
    {
        var baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/", UriKind.Absolute);
        var uri = new Uri(baseUri, resourcePath);
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri($"{uri}{separator}api-version={Uri.EscapeDataString(apiVersion)}", UriKind.Absolute);
    }

    private static JsonObject BuildFlowPatchBody(
        NormalizedPowerAutomateFlow before,
        JsonObject flow,
        string displayName)
    {
        var properties = new JsonObject
        {
            ["connectionReferences"] = RequireFlowProperty(flow, "connectionReferences").DeepClone(),
            ["definition"] = RequireFlowProperty(flow, "definition").DeepClone(),
            ["displayName"] = displayName,
        };
        if (before.Environment is not null)
        {
            properties["environment"] = before.Environment.DeepClone();
        }

        return new JsonObject
        {
            ["properties"] = properties,
        };
    }

    private static JsonObject BuildFlowCreateBody(JsonObject flow, string displayName) =>
        new()
        {
            ["properties"] = new JsonObject
            {
                ["connectionReferences"] = RequireFlowProperty(flow, "connectionReferences").DeepClone(),
                ["definition"] = RequireFlowProperty(flow, "definition").DeepClone(),
                ["displayName"] = displayName,
            },
        };

    private static NormalizedPowerAutomateFlow NormalizeFlow(
        FlowApiContext context,
        JsonNode response,
        string source)
    {
        if (response is not JsonObject root ||
            !root.TryGetPropertyValue("properties", out var propertiesNode) ||
            propertiesNode is not JsonObject properties)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Power Automate API response did not include flow properties."));
        }

        var flow = new JsonObject
        {
            ["connectionReferences"] = (properties["connectionReferences"] ?? new JsonObject()).DeepClone(),
            ["definition"] = (properties["definition"] ?? new JsonObject()).DeepClone(),
        };
        return new NormalizedPowerAutomateFlow(
            context.EnvId,
            ReadOptionalString(root, "name") ?? context.FlowId,
            ReadOptionalString(properties, "displayName") ?? context.TargetDisplayName ?? string.Empty,
            flow,
            properties["environment"]?.DeepClone(),
            source);
    }

    private static PowerAutomateMcpFlowReadResult CreateFlowReadResult(
        NormalizedPowerAutomateFlow flow,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc) =>
        new()
        {
            Success = errors.Count == 0,
            EnvId = flow.EnvId,
            FlowId = flow.FlowId,
            DisplayName = flow.DisplayName,
            FlowJson = flow.Flow.ToJsonString(OperatorJson.SerializerOptions),
            Source = flow.Source,
            Summary = SummarizeFlow(flow.Flow),
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = observedAtUtc,
        };

    private static PowerAutomateMcpFlowSummary SummarizeFlow(JsonObject flow)
    {
        var definition = flow["definition"] as JsonObject;
        var triggers = definition?["triggers"] as JsonObject;
        var actions = definition?["actions"] as JsonObject;
        var triggerNames = triggers?.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        var actionNames = actions?.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        return new PowerAutomateMcpFlowSummary
        {
            TriggerCount = triggerNames.Length,
            ActionCount = actionNames.Length,
            TriggerNames = triggerNames,
            ActionNames = actionNames,
        };
    }

    private static JsonArray NormalizeIssues(JsonNode issues)
    {
        if (issues is JsonArray array)
        {
            return CloneArray(array);
        }

        if (issues is JsonObject obj &&
            obj.TryGetPropertyValue("value", out var valueNode) &&
            valueNode is JsonArray valueArray)
        {
            return CloneArray(valueArray);
        }

        return new JsonArray();
    }

    private static JsonNode RequireFlowProperty(JsonObject flow, string propertyName)
    {
        if (!flow.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed($"FlowJson must include '{propertyName}'."));
        }

        return value;
    }

    private static LegacyPowerAutomateApi RequireLegacyApi(FlowApiContext context) =>
        TryGetLegacyApi(context)
            ?? throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("No flow-compatible legacy Power Automate token is available. Refresh the flow page with the MCP extension enabled."));

    private static LegacyPowerAutomateApi? TryGetLegacyApi(FlowApiContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LegacyToken))
        {
            return null;
        }

        return new LegacyPowerAutomateApi(
            string.IsNullOrWhiteSpace(context.LegacyApiUrl)
                ? "https://api.flow.microsoft.com/"
                : context.LegacyApiUrl,
            context.LegacyToken,
            context.LegacySource ?? "captured-legacy-session");
    }

    private static string LegacyFlowPath(string envId, string flowId) =>
        $"providers/Microsoft.ProcessSimple/environments/{Uri.EscapeDataString(envId)}/flows/{Uri.EscapeDataString(flowId)}";

    private static string ReadRequiredString(JsonObject obj, string propertyName, string label)
    {
        var value = ReadOptionalString(obj, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable($"Power Automate captured context is missing {label}."));
        }

        return value;
    }

    private static string? ReadOptionalString(JsonObject? obj, string propertyName)
    {
        if (obj is null ||
            !obj.TryGetPropertyValue(propertyName, out var value) ||
            value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static JsonObject CloneObject(JsonObject value) =>
        JsonNode.Parse(value.ToJsonString(OperatorJson.SerializerOptions)) as JsonObject ?? new JsonObject();

    private static JsonArray CloneArray(JsonArray value) =>
        JsonNode.Parse(value.ToJsonString(OperatorJson.SerializerOptions)) as JsonArray ?? new JsonArray();

    private PowerAutomateMcpEdgeCleanupResult CleanupEdgeCore(
        string cleanupAction,
        bool onlyIfExpired,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actions = new List<string> { cleanupAction };
        var warnings = new List<string>();
        var errors = new List<string>();
        var now = _runtime.UtcNow;
        var state = ReadState();
        var lease = state.OwnedEdge;
        if (lease is null)
        {
            actions.Add("power_automate_mcp_edge_cleanup_no_owned_edge");
            return new PowerAutomateMcpEdgeCleanupResult
            {
                Success = true,
                Actions = actions,
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = now,
            };
        }

        var expired = lease.LeaseExpiresAtUtc <= now;
        if (onlyIfExpired && !expired)
        {
            actions.Add("power_automate_mcp_edge_cleanup_skipped_not_expired");
            return CreateCleanupResult(lease, IsOwnedEdgeAlive(lease), actions, warnings, errors, now);
        }

        var workingLease = lease;
        var trackedWindowPresent = IsTrackedEdgeWindowPresent(workingLease);
        var alive = IsOwnedEdgeAlive(workingLease);
        if (!alive && !trackedWindowPresent)
        {
            actions.Add("power_automate_mcp_edge_cleanup_already_closed");
            workingLease = workingLease with { ClosedAtUtc = workingLease.ClosedAtUtc ?? now };
            UpdateState(current => current with { OwnedEdge = workingLease });
            return CreateCleanupResult(workingLease, false, actions, warnings, errors, now);
        }

        if (workingLease.Hwnd is { } hwnd)
        {
            if (_runtime.TryCloseWindow(hwnd, workingLease.ProcessId))
            {
                actions.Add("power_automate_mcp_edge_closed_hwnd");
            }
            else
            {
                warnings.Add($"power_automate_mcp_edge_close_hwnd_failed_or_mismatched:{hwnd}");
            }
        }
        else
        {
            warnings.Add("power_automate_mcp_edge_cleanup_skipped_no_tracked_hwnd");
        }

        alive = IsOwnedEdgeAlive(workingLease);
        if (!alive)
        {
            workingLease = workingLease with { ClosedAtUtc = now };
            actions.Add("power_automate_mcp_edge_cleanup_completed");
        }
        else
        {
            errors.Add("Owned Power Automate MCP Edge lease is still alive after cleanup.");
        }

        UpdateState(current => current with { OwnedEdge = workingLease });
        return CreateCleanupResult(workingLease, IsOwnedEdgeAlive(workingLease), actions, warnings, errors, now);
    }

    private PowerAutomateMcpStatusResult BuildStatus(
        StatusOptions options,
        CancellationToken cancellationToken)
    {
        var packageSpec = NormalizePackageSpec(options.PackageSpec ?? DefaultPackageSpec);
        var host = NormalizeBridgeHost(options.BridgeHost ?? DefaultBridgeHost);
        var port = NormalizeBridgePort(options.BridgePort ?? DefaultBridgePort);
        var actions = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var state = ReadState();
        var bridgeState = state.Bridge;
        var edgeState = state.OwnedEdge;
        string? extensionPath = options.ExtensionPath ?? bridgeState?.ExtensionPath;
        var nodePath = ResolveExecutable(_runtime.IsWindows ? "node.exe" : "node", KnownNodePaths());
        var npmPath = ResolveExecutable(_runtime.IsWindows ? "npm.cmd" : "npm", KnownNpmPaths());
        var npxPath = ResolveExecutable(_runtime.IsWindows ? "npx.cmd" : "npx", KnownNpxPaths());
        var edgePath = ResolveEdgePath();

        var nodeVersion = TryRunVersion(nodePath, "--version", cancellationToken);
        var npmVersion = TryRunVersion(npmPath, "--version", cancellationToken);
        var bridgeListening = IsTcpListening(host, port);
        var health = bridgeListening ? ProbeBridgeHealth(host, port, cancellationToken) : BridgeHealthProbe.NotListening();
        var contextAvailable = bridgeListening && ProbeBridgeContext(host, port, cancellationToken);
        var processAlive = bridgeState?.ProcessId is { } processId && IsProcessAlive(processId);
        var edgeAlive = edgeState is not null && IsOwnedEdgeAlive(edgeState);
        var edgeTtlSeconds = edgeState?.TtlSeconds ?? ResolveIdleTtlSeconds(null);

        if (string.IsNullOrWhiteSpace(nodePath))
        {
            warnings.Add("node_not_found");
        }

        if (string.IsNullOrWhiteSpace(npmPath))
        {
            warnings.Add("npm_not_found");
        }

        if (string.IsNullOrWhiteSpace(npxPath))
        {
            warnings.Add("npx_not_found");
        }

        if (string.IsNullOrWhiteSpace(edgePath))
        {
            warnings.Add("edge_not_found");
        }

        if (!bridgeListening)
        {
            actions.Add("power_automate_mcp_bridge_not_listening");
        }

        if (!health.Healthy && !string.IsNullOrWhiteSpace(health.Error))
        {
            warnings.Add(health.Error);
        }

        if (bridgeState is not null && !processAlive)
        {
            warnings.Add($"bridge_state_process_not_alive:{bridgeState.ProcessId}");
        }

        if (edgeState is not null)
        {
            if (edgeAlive)
            {
                actions.Add(edgeState.LeaseExpiresAtUtc <= _runtime.UtcNow
                    ? "power_automate_mcp_edge_lease_expired"
                    : "power_automate_mcp_edge_lease_active");
            }
            else if (edgeState.ClosedAtUtc is null)
            {
                warnings.Add($"power_automate_mcp_edge_process_not_alive:{edgeState.ProcessId}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new PowerAutomateMcpStatusResult
        {
            Success = bridgeListening && health.Healthy,
            BridgeHost = host,
            BridgePort = port,
            PackageSpec = packageSpec,
            BridgeListening = bridgeListening,
            BridgeHealthy = health.Healthy,
            ContextAvailable = contextAvailable,
            BridgeMode = health.BridgeMode,
            BridgeVersion = health.Version,
            BridgeProcessId = health.ProcessId ?? bridgeState?.ProcessId,
            NodePath = nodePath,
            NodeVersion = nodeVersion,
            NpmPath = npmPath,
            NpmVersion = npmVersion,
            NpxPath = npxPath,
            EdgePath = edgePath,
            ExtensionPath = extensionPath,
            ExtensionPathResolved = !string.IsNullOrWhiteSpace(extensionPath) && Directory.Exists(Environment.ExpandEnvironmentVariables(extensionPath)),
            EdgeSessionAlive = edgeAlive,
            EdgeProcessId = edgeState?.ProcessId,
            EdgeHwnd = edgeState?.Hwnd,
            EdgeStartedAtUtc = edgeState?.StartedAtUtc,
            EdgeLastUsedAtUtc = edgeState?.LastUsedAtUtc,
            EdgeLeaseExpiresAtUtc = edgeState?.LeaseExpiresAtUtc,
            EdgeClosedAtUtc = edgeState?.ClosedAtUtc,
            EdgeIdleTtlSeconds = edgeTtlSeconds,
            EdgeProfileMode = edgeState?.ProfileMode,
            EdgeUrl = edgeState?.Url,
            StatePath = StatePath(),
            LogPath = bridgeState?.LogPath ?? BridgeLogPath(),
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = _runtime.UtcNow,
        };
    }

    private string? TryResolveExtensionPath(
        string packageSpec,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var installedPath = ResolveInstalledExtensionPath(packageSpec);
        if (!string.IsNullOrWhiteSpace(installedPath))
        {
            actions.Add("power_automate_mcp_extension_path_resolved_installed");
            UpdateState(state =>
                state with
                {
                    Bridge = state.Bridge is null
                        ? null
                        : state.Bridge with { ExtensionPath = installedPath },
                });
            return installedPath;
        }

        var npxPath = ResolveExecutable(_runtime.IsWindows ? "npx.cmd" : "npx", KnownNpxPaths());
        if (string.IsNullOrWhiteSpace(npxPath))
        {
            warnings.Add("extension_path_not_resolved:npx_not_found");
            return null;
        }

        var result = RunProcessCapture(
            npxPath,
            new[] { "-y", packageSpec, "extension-path" },
            StateRoot(),
            ExtensionPathTimeout,
            cancellationToken);
        if (!result.Success)
        {
            warnings.Add($"extension_path_not_resolved:{result.Error ?? "command_failed"}");
            return null;
        }

        var extensionPath = result.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(extensionPath))
        {
            warnings.Add("extension_path_not_resolved:empty_output");
            return null;
        }

        actions.Add("power_automate_mcp_extension_path_resolved");
        UpdateState(state =>
            state with
            {
                Bridge = state.Bridge is null
                    ? null
                    : state.Bridge with { ExtensionPath = extensionPath },
            });
        return extensionPath;
    }

    private static ProcessCaptureResult RunProcessCapture(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workingDirectory);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new ProcessCaptureResult(false, string.Empty, "process_start_failed");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                TryKill(process);
                return new ProcessCaptureResult(false, string.Empty, "timeout");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0
                ? new ProcessCaptureResult(true, output, null)
                : new ProcessCaptureResult(false, output, string.IsNullOrWhiteSpace(error) ? $"exit_code:{process.ExitCode}" : SanitizedOneLine(error));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessCaptureResult(false, string.Empty, SanitizedOneLine(ex.Message));
        }
    }

    private string? TryRunVersion(string? fileName, string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var result = RunProcessCapture(fileName, new[] { argument }, StateRoot(), ProcessProbeTimeout, cancellationToken);
        return result.Success
            ? result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            : null;
    }

    private static bool IsTcpListening(string host, int port)
    {
        using var client = new TcpClient();
        try
        {
            return client.ConnectAsync(host, port).Wait(BridgeProbeTimeout);
        }
        catch
        {
            return false;
        }
    }

    private static BridgeHealthProbe ProbeBridgeHealth(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = BridgeClient.GetAsync($"http://{host}:{port}/health", cancellationToken).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return new BridgeHealthProbe(false, null, null, null, $"bridge_health_http:{(int)response.StatusCode}");
            }

            using var stream = response.Content.ReadAsStream(cancellationToken);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okProperty) && okProperty.ValueKind == JsonValueKind.True;
            var version = root.TryGetProperty("version", out var versionProperty) && versionProperty.ValueKind == JsonValueKind.String
                ? versionProperty.GetString()
                : null;
            var mode = root.TryGetProperty("bridgeMode", out var modeProperty) && modeProperty.ValueKind == JsonValueKind.String
                ? modeProperty.GetString()
                : null;
            int? pid = root.TryGetProperty("pid", out var pidProperty) && pidProperty.TryGetInt32(out var parsedPid)
                ? parsedPid
                : null;

            return new BridgeHealthProbe(ok, version, mode, pid, ok ? null : "bridge_health_not_ok");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BridgeHealthProbe(false, null, null, null, $"bridge_health_failed:{SanitizedOneLine(ex.Message)}");
        }
    }

    private static bool ProbeBridgeContext(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = BridgeClient.GetAsync($"http://{host}:{port}/context", cancellationToken).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private int ResolveIdleTtlSeconds(int? requestedSeconds)
    {
        var raw = requestedSeconds ?? _options.EdgeIdleTtlSeconds;
        return Math.Clamp(raw == 0 ? 0 : raw, 0, MaxEdgeIdleTtlSeconds);
    }

    private static string NormalizePackageSpec(string? packageSpec)
    {
        if (string.IsNullOrWhiteSpace(packageSpec))
        {
            return DefaultPackageSpec;
        }

        var trimmed = packageSpec.Trim();
        if (trimmed.Contains(' ') || trimmed.Contains('"') || trimmed.Contains('\''))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Package spec must be a single npm package spec without quotes or spaces."));
        }

        return trimmed;
    }

    private static string NormalizeBridgeHost(string? host)
    {
        var trimmed = string.IsNullOrWhiteSpace(host) ? DefaultBridgeHost : host.Trim();
        if (!string.Equals(trimmed, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Power Automate MCP bridge host must be loopback."));
        }

        return trimmed;
    }

    private static int NormalizeBridgePort(int port)
    {
        var normalized = port == 0 ? DefaultBridgePort : port;
        if (normalized is < 1024 or > 65535)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Power Automate MCP bridge port must be between 1024 and 65535."));
        }

        return normalized;
    }

    private static string NormalizePowerAutomateUrl(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? DefaultPowerAutomateUrl : raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Power Automate URL must be an absolute HTTP or HTTPS URL."));
        }

        var host = uri.Host.ToLowerInvariant();
        if (!host.EndsWith("powerautomate.com", StringComparison.Ordinal) &&
            !host.EndsWith("flow.microsoft.com", StringComparison.Ordinal) &&
            !host.EndsWith("powerapps.com", StringComparison.Ordinal) &&
            !host.EndsWith("powerplatform.microsoft.com", StringComparison.Ordinal))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpValidationFailed("Power Automate MCP Edge launch is restricted to Power Platform URLs."));
        }

        return uri.ToString();
    }

    private static string? ResolveEdgePath() =>
        ResolveExecutable("msedge.exe", new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        });

    private static string? ResolveExecutable(string executable, IEnumerable<string> knownPaths)
    {
        foreach (var path in knownPaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void HoldBridgeProcess(Process process)
    {
        lock (BridgeProcessLock)
        {
            if (heldBridgeProcess is { HasExited: false } previous)
            {
                TryKill(previous);
            }

            heldBridgeInput?.Dispose();
            heldBridgeProcess?.Dispose();
            heldBridgeProcess = process;
            heldBridgeInput = process.StandardInput;
        }
    }

    private static void StartLogPump(Process process, string logPath)
    {
        _ = Task.Run(() => PumpLog(process.StandardOutput, logPath));
        _ = Task.Run(() => PumpLog(process.StandardError, logPath));
    }

    private static void PumpLog(StreamReader reader, string logPath)
    {
        try
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lock (BridgeLogLock)
                {
                    File.AppendAllText(logPath, $"[{DateTimeOffset.UtcNow:O}] {line}{Environment.NewLine}");
                }
            }
        }
        catch
        {
            // Best-effort bridge diagnostics only.
        }
    }

    private static IReadOnlyList<string> KnownNodePaths() =>
        OperatingSystem.IsWindows()
            ? new[] { @"C:\Program Files\nodejs\node.exe" }
            : Array.Empty<string>();

    private static IReadOnlyList<string> KnownNpmPaths() =>
        OperatingSystem.IsWindows()
            ? new[] { @"C:\Program Files\nodejs\npm.cmd" }
            : Array.Empty<string>();

    private static IReadOnlyList<string> KnownNpxPaths() =>
        OperatingSystem.IsWindows()
            ? new[] { @"C:\Program Files\nodejs\npx.cmd" }
            : Array.Empty<string>();

    private bool IsProcessAlive(int processId) => _runtime.IsProcessAlive(processId);

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort timeout cleanup only.
        }
    }

    private string StateRoot()
    {
        var localAppData = _runtime.LocalAppDataRoot;
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "WindowsOperator", "run", "power-automate-mcp");
    }

    private string StatePath() => Path.Combine(StateRoot(), "bridge-state.json");

    private string BridgeLogPath() => Path.Combine(StateRoot(), "bridge.log");

    private string BridgeDataRoot() => Path.Combine(StateRoot(), "data");

    private string NpmRuntimeRoot() => Path.Combine(StateRoot(), "npm-runtime");

    private static bool IsDefaultPowerAutomatePackage(string packageSpec) =>
        packageSpec.StartsWith("@kaael1/mcp-power-automate", StringComparison.OrdinalIgnoreCase);

    private string? ResolveInstalledServerPath(string packageSpec)
    {
        if (!IsDefaultPowerAutomatePackage(packageSpec))
        {
            return null;
        }

        var path = Path.Combine(
            NpmRuntimeRoot(),
            "node_modules",
            "@kaael1",
            "mcp-power-automate",
            "dist",
            "server",
            "index.js");
        return File.Exists(path) ? path : null;
    }

    private string? ResolveInstalledExtensionPath(string packageSpec)
    {
        if (!IsDefaultPowerAutomatePackage(packageSpec))
        {
            return null;
        }

        var path = Path.Combine(
            NpmRuntimeRoot(),
            "node_modules",
            "@kaael1",
            "mcp-power-automate",
            "dist",
            "extension");
        return Directory.Exists(path) ? path : null;
    }

    private PowerAutomateMcpState ReadState()
    {
        var path = StatePath();
        if (!File.Exists(path))
        {
            return new PowerAutomateMcpState();
        }

        lock (_stateLock)
        {
            try
            {
                var json = File.ReadAllText(path);
                var current = JsonSerializer.Deserialize<PowerAutomateMcpState>(json, OperatorJson.SerializerOptions);
                if (current is not null)
                {
                    return current;
                }

                var legacyBridge = JsonSerializer.Deserialize<BridgeProcessState>(json, OperatorJson.SerializerOptions);
                return legacyBridge is null ? new PowerAutomateMcpState() : new PowerAutomateMcpState(legacyBridge, null);
            }
            catch
            {
                return new PowerAutomateMcpState();
            }
        }
    }

    private void UpdateState(Func<PowerAutomateMcpState, PowerAutomateMcpState> update)
    {
        lock (_stateLock)
        {
            var next = update(ReadState());
            Directory.CreateDirectory(StateRoot());
            File.WriteAllText(StatePath(), JsonSerializer.Serialize(next, OperatorJson.SerializerOptions));
        }
    }

    private bool IsOwnedEdgeAlive(OwnedEdgeLeaseState lease)
    {
        return lease.Hwnd is { } hwnd && _runtime.IsWindowForProcess(hwnd, lease.ProcessId);
    }

    private bool IsTrackedEdgeWindowPresent(OwnedEdgeLeaseState lease) =>
        lease.Hwnd is { } hwnd && _runtime.IsWindow(hwnd);

    private PowerAutomateMcpEdgeResult CreateEdgeResult(
        OwnedEdgeLeaseState lease,
        bool success,
        string? edgePath,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc) =>
        new()
        {
            Success = success && !errors.Any(),
            Url = lease.Url,
            ProfileMode = lease.ProfileMode,
            ProcessId = lease.ProcessId,
            Hwnd = lease.Hwnd,
            Alive = IsOwnedEdgeAlive(lease),
            EdgePath = edgePath,
            ExtensionPath = lease.ExtensionPath,
            StartedAtUtc = lease.StartedAtUtc,
            LastUsedAtUtc = lease.LastUsedAtUtc,
            LeaseExpiresAtUtc = lease.LeaseExpiresAtUtc,
            ClosedAtUtc = lease.ClosedAtUtc,
            TtlSeconds = lease.TtlSeconds,
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = observedAtUtc,
        };

    private PowerAutomateMcpEdgeCleanupResult CreateCleanupResult(
        OwnedEdgeLeaseState lease,
        bool alive,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        DateTimeOffset observedAtUtc) =>
        new()
        {
            Success = !alive && !errors.Any(),
            Alive = alive,
            ProcessId = lease.ProcessId,
            Hwnd = lease.Hwnd,
            ProfileMode = lease.ProfileMode,
            Url = lease.Url,
            ExtensionPath = lease.ExtensionPath,
            StartedAtUtc = lease.StartedAtUtc,
            LastUsedAtUtc = lease.LastUsedAtUtc,
            LeaseExpiresAtUtc = lease.LeaseExpiresAtUtc,
            ClosedAtUtc = lease.ClosedAtUtc,
            TtlSeconds = lease.TtlSeconds,
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = observedAtUtc,
        };

    private static string SanitizedOneLine(string value)
    {
        var sanitized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return sanitized.Length > 240 ? sanitized[..240] : sanitized;
    }

    private sealed record StatusOptions(
        string? PackageSpec = null,
        string? BridgeHost = null,
        int? BridgePort = null,
        string? ExtensionPath = null);

    private sealed record FlowApiContext(
        string EnvId,
        string FlowId,
        string ApiUrl,
        string ApiToken,
        string? LegacyApiUrl,
        string? LegacyToken,
        string? LegacySource,
        string? TargetDisplayName);

    private sealed record LegacyPowerAutomateApi(
        string BaseUrl,
        string Token,
        string Source);

    private sealed record CapturedLegacyToken(
        string BaseUrl,
        string Token,
        string Source);

    private sealed record NormalizedPowerAutomateFlow(
        string EnvId,
        string FlowId,
        string DisplayName,
        JsonObject Flow,
        JsonNode? Environment,
        string Source);

    private sealed record PowerAutomateMcpState(
        BridgeProcessState? Bridge = null,
        OwnedEdgeLeaseState? OwnedEdge = null);

    private sealed record BridgeProcessState(
        string PackageSpec,
        string BridgeHost,
        int BridgePort,
        int ProcessId,
        string LogPath,
        string? ExtensionPath,
        DateTimeOffset StartedAtUtc);

    private sealed record OwnedEdgeLeaseState(
        int? ProcessId,
        long? Hwnd,
        BrowserEdgeProfileMode ProfileMode,
        string Url,
        string ExtensionPath,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset LastUsedAtUtc,
        DateTimeOffset LeaseExpiresAtUtc,
        DateTimeOffset? ClosedAtUtc,
        int TtlSeconds);

    private sealed record BridgeHealthProbe(
        bool Healthy,
        string? Version,
        string? BridgeMode,
        int? ProcessId,
        string? Error)
    {
        public static BridgeHealthProbe NotListening() => new(false, null, null, null, null);
    }

    private sealed record ProcessCaptureResult(
        bool Success,
        string Output,
        string? Error);
}

internal interface IPowerAutomateMcpRuntime
{
    DateTimeOffset UtcNow { get; }

    bool IsWindows { get; }

    string LocalAppDataRoot { get; }

    bool IsProcessAlive(int processId);

    bool IsWindow(long hwnd);

    bool TryCloseWindow(long hwnd, int? processId);

    bool IsWindowForProcess(long hwnd, int? processId);

    EdgeWindowDiscovery? TryFindPowerAutomateEdgeWindow(int launchedProcessId, string url, TimeSpan timeout);

    EdgeLaunchResult LaunchEdge(EdgeLaunchSpec spec);
}

internal sealed record EdgeLaunchSpec(
    string EdgePath,
    string Url,
    string ExtensionPath,
    BrowserEdgeProfileMode ProfileMode,
    string? UserDataDir,
    string WorkingDirectory);

internal sealed record EdgeLaunchResult(
    int ProcessId,
    long? Hwnd,
    Process? Process);

internal sealed record EdgeWindowDiscovery(
    long Hwnd,
    int ProcessId,
    string? Title);

internal sealed class PowerAutomateMcpRuntime : IPowerAutomateMcpRuntime
{
    private const uint WmClose = 0x0010;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public bool IsWindows => OperatingSystem.IsWindows();

    public string LocalAppDataRoot
    {
        get
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return localAppData;
            }

            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(localAppData) ? Path.GetTempPath() : localAppData;
        }
    }

    public bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public bool IsWindow(long hwnd) => User32.IsWindow(new IntPtr(hwnd));

    public bool IsWindowForProcess(long hwnd, int? processId)
    {
        var handle = new IntPtr(hwnd);
        if (!User32.IsWindow(handle))
        {
            return false;
        }

        if (processId is null)
        {
            return true;
        }

        User32.GetWindowThreadProcessId(handle, out var actualProcessId);
        return actualProcessId == processId;
    }

    public bool TryCloseWindow(long hwnd, int? processId)
    {
        if (hwnd == 0)
        {
            return false;
        }

        try
        {
            var handle = new IntPtr(hwnd);
            if (!User32.IsWindow(handle))
            {
                return true;
            }

            if (processId is { } expectedProcessId)
            {
                User32.GetWindowThreadProcessId(handle, out var actualProcessId);
                if (actualProcessId != expectedProcessId)
                {
                    return false;
                }
            }

            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow <= deadline)
            {
                if (!User32.IsWindow(handle))
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

        return !User32.IsWindow(new IntPtr(hwnd));
    }

    public EdgeWindowDiscovery? TryFindPowerAutomateEdgeWindow(int launchedProcessId, string url, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            var window = FindPowerAutomateEdgeWindow(launchedProcessId);
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(250);
        }

        return FindPowerAutomateEdgeWindow(launchedProcessId);
    }

    public EdgeLaunchResult LaunchEdge(EdgeLaunchSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.EdgePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = spec.WorkingDirectory,
        };
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add($"--load-extension={spec.ExtensionPath}");

        if (spec.ProfileMode == BrowserEdgeProfileMode.Temp)
        {
            startInfo.ArgumentList.Add($"--user-data-dir={spec.UserDataDir}");
        }
        else
        {
            startInfo.ArgumentList.Add("--profile-directory=Default");
        }

        startInfo.ArgumentList.Add(spec.Url);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable($"Failed to launch Edge with Power Automate MCP extension: {ex.Message}"));
        }

        if (process is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerAutomateMcpUnavailable("Failed to launch Edge with Power Automate MCP extension."));
        }

        return new EdgeLaunchResult(process.Id, null, process);
    }

    private static EdgeWindowDiscovery? FindPowerAutomateEdgeWindow(int launchedProcessId)
    {
        var candidates = new List<(EdgeWindowDiscovery Window, DateTimeOffset? ProcessStartUtc, bool IsLaunchedProcess)>();
        User32.EnumWindows((hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd))
            {
                return true;
            }

            User32.GetWindowThreadProcessId(hwnd, out var candidateProcessId);
            var processId = unchecked((int)candidateProcessId);
            string processName;
            DateTimeOffset? processStartUtc = null;
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }

                processName = process.ProcessName;
                try
                {
                    processStartUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                }
                catch
                {
                    processStartUtc = null;
                }
            }
            catch
            {
                return true;
            }

            if (!string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var title = GetWindowText(hwnd);
            if (!LooksLikePowerAutomateWindow(title))
            {
                return true;
            }

            candidates.Add((
                new EdgeWindowDiscovery(hwnd.ToInt64(), processId, title),
                processStartUtc,
                processId == launchedProcessId));
            return true;
        }, IntPtr.Zero);

        return candidates
            .OrderByDescending(item => item.IsLaunchedProcess)
            .ThenByDescending(item => item.ProcessStartUtc)
            .Select(item => item.Window)
            .FirstOrDefault();
    }

    private static bool LooksLikePowerAutomateWindow(string title) =>
        title.Contains("Power Automate", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("make.powerautomate.com", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Power Platform", StringComparison.OrdinalIgnoreCase);

    private static string GetWindowText(IntPtr hwnd)
    {
        var builder = new StringBuilder(512);
        _ = User32.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
