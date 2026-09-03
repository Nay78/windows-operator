using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class HostOperatorFacade : IOperatorFacade
{
    private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly DesktopAgentClient _desktopAgent;
    private readonly RuntimeBuildIdentity _buildIdentity;
    private readonly IOptions<OperatorOptions> _options;
    private readonly IOptions<DesktopAgentOptions> _desktopOptions;
    private readonly IOptions<PowerPointAddInOptions> _powerPointAddInOptions;
    private readonly OneDriveRuntimeStateStore _oneDriveRuntimeState;

    public HostOperatorFacade(
        DesktopAgentClient desktopAgent,
        RuntimeBuildIdentity buildIdentity,
        IOptions<OperatorOptions> options,
        IOptions<DesktopAgentOptions> desktopOptions,
        IOptions<PowerPointAddInOptions> powerPointAddInOptions,
        OneDriveRuntimeStateStore oneDriveRuntimeState)
    {
        _desktopAgent = desktopAgent;
        _buildIdentity = buildIdentity;
        _options = options;
        _desktopOptions = desktopOptions;
        _powerPointAddInOptions = powerPointAddInOptions;
        _oneDriveRuntimeState = oneDriveRuntimeState;
    }

    public async Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var desktopStatus = await ProbeDesktopAgentAsync(cancellationToken);
        return new HealthResult(
            desktopStatus is null ? "degraded" : "ok",
            "headless-host",
            Environment.OSVersion.VersionString,
            options.RestBaseUrl,
            desktopStatus?.UiBackend ?? $"DesktopAgentProxy:{_desktopOptions.Value.BaseUrl}",
            desktopStatus?.CaptureBackends ?? new[] { "DesktopAgentProxy" },
            options.EnableMcpStdio,
            DateTimeOffset.UtcNow);
    }

    public async Task<CapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var health = await GetHealthAsync(cancellationToken);
        var desktopAvailable = string.Equals(health.Status, "ok", StringComparison.Ordinal);
        var addInEnabled = _powerPointAddInOptions.Value.Enabled;
        var features = new Dictionary<string, CapabilityFeature>(StringComparer.Ordinal)
        {
            ["host.openapi"] = Available("stable"),
            ["desktop.window"] = AgentBacked(desktopAvailable, "stable"),
            ["desktop.screenshot"] = AgentBacked(desktopAvailable, "stable"),
            ["browser.edge.session"] = AgentBacked(desktopAvailable, "stable"),
            ["power-automate.mcp"] = AgentBacked(
                desktopAvailable,
                "diagnostic",
                desktopAvailable
                    ? "Browser-token/API bridge bootstrap only; Power Automate writes must not fall back to designer UI automation."
                    : "Desktop Agent is unavailable."),
            ["powerpoint.online.update"] = desktopAvailable && addInEnabled
                ? Available("stable")
                : Unavailable(
                    "stable",
                    desktopAvailable
                        ? "PowerPoint add-in host is not enabled."
                        : "Desktop Agent is unavailable.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["addInEnabled"] = addInEnabled ? "true" : "false",
                    }),
            ["mail.outlook.download"] = AgentBacked(desktopAvailable, "stable"),
            ["files.onedrive"] = AgentBacked(
                desktopAvailable,
                "diagnostic",
                desktopAvailable
                    ? "Files-On-Demand lease and local reclaim surface; stable promotion requires live Windows proof."
                    : "Desktop Agent is unavailable."),
        };

        return new CapabilitiesResult(
            OperatorContractVersion.Value,
            _buildIdentity,
            new CapabilityHost(
                health.Status,
                health.RuntimeMode,
                health.RestBaseUrl,
                desktopAvailable ? "ok" : "unavailable"),
            features,
            DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) =>
        _desktopAgent.ListWindowsAsync(cancellationToken);

    public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) =>
        _desktopAgent.ActivateWindowAsync(hwnd, cancellationToken);

    public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken) =>
        _desktopAgent.CaptureWindowAsync(hwnd, format, cancellationToken);

    public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) =>
        _desktopAgent.QueryUiAsync(query, cancellationToken);

    public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.ClickUiAsync(request, cancellationToken);

    public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.TypeUiAsync(request, cancellationToken);

    public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.ClickScreenAsync(request, cancellationToken);

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.SendHotkeyAsync(request, cancellationToken);

    public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.ResetEdgeBrowserAsync(request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.StartEdgeBrowserSessionAsync(request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _desktopAgent.GetEdgeBrowserSessionStateAsync(sessionId, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.NavigateEdgeBrowserSessionAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.ClickEdgeBrowserDomAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.FillEdgeBrowserDomAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _desktopAgent.CloseEdgeBrowserSessionAsync(sessionId, cancellationToken);

    public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.CleanupMicrosoftAuthWindowsAsync(request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.StartMicrosoftAuthorizeProbeAsync(request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _desktopAgent.GetMicrosoftAuthorizeProbeStatusAsync(runId, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.StartMicrosoftDeviceLoginAsync(request, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _desktopAgent.GetMicrosoftDeviceLoginStatusAsync(runId, cancellationToken);

    public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.ListMailFoldersAsync(request, cancellationToken);

    public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.SearchMailMessagesAsync(request, cancellationToken);

    public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        _desktopAgent.DownloadMailAttachmentsAsync(request, cancellationToken);

    public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
        _desktopAgent.GetMailRunAsync(runId, cancellationToken);

    public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
        _desktopAgent.GetMailStatusAsync(cancellationToken);

    public async Task<OneDriveLeaseResult> AcquireOneDriveLeaseAsync(
        OneDriveLeaseRequest request,
        CancellationToken cancellationToken)
    {
        EnsureOneDriveSupervisorReady();
        return await _desktopAgent.AcquireLeaseAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<OneDriveFileEntry>> ListOneDriveFilesAsync(
        OneDriveListRequest request,
        CancellationToken cancellationToken)
    {
        EnsureOneDriveSupervisorReady();
        return await _desktopAgent.ListOneDriveFilesAsync(request, cancellationToken);
    }

    public Task<OneDriveLeaseStatusResult> GetOneDriveLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken) =>
        _desktopAgent.GetLeaseAsync(leaseId, cancellationToken);

    public Task<OneDriveLeaseResult> RenewOneDriveLeaseAsync(
        string leaseId,
        OneDriveLeaseRenewRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.RenewLeaseAsync(leaseId, request, cancellationToken);

    public Task<OneDriveLeaseResult> ReleaseOneDriveLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken) =>
        _desktopAgent.ReleaseLeaseAsync(leaseId, cancellationToken);

    public async Task<OneDriveFilesOnDemandStatusResult> GetOneDriveStatusAsync(CancellationToken cancellationToken)
    {
        var supervisor = _oneDriveRuntimeState.Read();
        if (supervisor is not null && supervisor.RecoveryAllowed && !IsOneDriveSupervisorReady(supervisor))
        {
            var unavailable = new OneDriveFilesOnDemandStatusResult
            {
                Available = false,
                Runtime = new OneDriveRuntimeEvidence
                {
                    ComputerName = supervisor.ComputerName,
                    RecoveryAllowed = supervisor.RecoveryAllowed,
                    ConfiguredSessionId = supervisor.TargetSessionId,
                    ProcessPresent = supervisor.ProcessSessionId is not null,
                    ProcessSessionId = supervisor.ProcessSessionId,
                    InteractiveUser = "Administrator",
                    InteractiveSessionState = supervisor.SessionState,
                    ProviderReady = false,
                    ProviderReason = supervisor.Reason ?? "target_rdp_session_not_ready",
                    RecoveryActions = supervisor.Actions,
                },
                RuntimeSupervisor = supervisor,
                ProviderReadinessReason = supervisor.Reason ?? "target_rdp_session_not_ready",
                Warnings = new[]
                {
                    $"Host runtime supervisor is not ready: {supervisor.Reason ?? "target_rdp_session_not_ready"}.",
                },
            };
            return unavailable;
        }

        var status = await _desktopAgent.GetOneDriveStatusAsync(cancellationToken);
        supervisor = _oneDriveRuntimeState.Read();
        if (supervisor is null || !supervisor.RecoveryAllowed || IsOneDriveSupervisorReady(supervisor))
        {
            return status with { RuntimeSupervisor = supervisor };
        }

        var runtime = ApplySupervisorState(status.Runtime, supervisor);
        return status with
        {
            Available = false,
            Runtime = runtime,
            RuntimeSupervisor = supervisor,
            ProviderReadinessReason = supervisor.Reason ?? "target_rdp_session_not_ready",
            Warnings = status.Warnings
                .Append($"Host runtime supervisor is not ready: {supervisor.Reason ?? "target_rdp_session_not_ready"}.")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public Task<OneDriveConfigResult> GetOneDriveConfigAsync(CancellationToken cancellationToken) =>
        _desktopAgent.GetConfigAsync(cancellationToken);

    public Task<OneDriveConfigResult> UpdateOneDriveConfigAsync(
        OneDriveConfigUpdateRequest request,
        CancellationToken cancellationToken) =>
        _desktopAgent.UpdateConfigAsync(request, cancellationToken);

    public async Task<OneDriveReclaimResult> StartOneDriveReclaimAsync(
        OneDriveReclaimRequest request,
        CancellationToken cancellationToken)
    {
        EnsureOneDriveSupervisorReady();
        return await _desktopAgent.StartReclaimAsync(request, cancellationToken);
    }

    public Task<OneDriveReclaimResult> GetOneDriveReclaimAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _desktopAgent.GetReclaimAsync(runId, cancellationToken);

    private void EnsureOneDriveSupervisorReady()
    {
        var supervisor = _oneDriveRuntimeState.Read();
        if (supervisor is null || !supervisor.RecoveryAllowed || IsOneDriveSupervisorReady(supervisor))
        {
            return;
        }

        var runtime = ApplySupervisorState(new OneDriveRuntimeEvidence
        {
            ComputerName = supervisor.ComputerName,
            RecoveryAllowed = supervisor.RecoveryAllowed,
            ConfiguredSessionId = supervisor.TargetSessionId,
        }, supervisor);
        throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
            $"Host runtime supervisor is not ready;reason={supervisor.Reason ?? supervisor.State}",
            runtime));
    }

    private static bool IsOneDriveSupervisorReady(OneDriveRuntimeSupervisorState supervisor) =>
        string.Equals(supervisor.State, "ready", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(supervisor.SessionState, "active", StringComparison.OrdinalIgnoreCase) &&
        supervisor.TargetSessionId > 0 &&
        supervisor.ProcessSessionId == supervisor.TargetSessionId;

    private static OneDriveRuntimeEvidence ApplySupervisorState(
        OneDriveRuntimeEvidence runtime,
        OneDriveRuntimeSupervisorState supervisor)
    {
        var ready = IsOneDriveSupervisorReady(supervisor);
        return runtime with
        {
            ComputerName = supervisor.ComputerName,
            RecoveryAllowed = supervisor.RecoveryAllowed,
            ProcessPresent = supervisor.ProcessSessionId is not null,
            ProcessSessionId = supervisor.ProcessSessionId,
            ConfiguredSessionId = supervisor.TargetSessionId,
            ActiveInteractiveSessionId = ready ? supervisor.TargetSessionId : null,
            InteractiveUser = "Administrator",
            InteractiveSessionState = supervisor.SessionState,
            InteractiveSessionProtocol = ready ? 2 : 0,
            ProviderReady = ready && runtime.ProviderReady,
            ProviderReason = supervisor.Reason ?? (ready ? runtime.ProviderReason : "target_rdp_session_not_ready"),
            RecoveryActions = supervisor.Actions,
        };
    }

    private async Task<HealthResult?> ProbeDesktopAgentAsync(CancellationToken cancellationToken)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(HealthProbeTimeout);

        try
        {
            return await _desktopAgent.GetHealthAsync(probeCts.Token);
        }
        catch
        {
            return null;
        }
    }

    private static CapabilityFeature AgentBacked(bool desktopAvailable, string surface, string? availableReason = null) =>
        desktopAvailable
            ? Available(surface, availableReason)
            : Unavailable(surface, "Desktop Agent is unavailable.");

    private static CapabilityFeature Available(string surface, string? reason = null) =>
        new(true, surface, reason);

    private static CapabilityFeature Unavailable(
        string surface,
        string reason,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(false, surface, reason, details);
}
