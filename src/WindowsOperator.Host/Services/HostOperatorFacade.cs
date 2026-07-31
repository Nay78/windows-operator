using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class HostOperatorFacade : IOperatorFacade
{
    private readonly DesktopAgentClient _desktopAgent;
    private readonly RuntimeBuildIdentity _buildIdentity;
    private readonly IOptions<OperatorOptions> _options;
    private readonly IOptions<DesktopAgentOptions> _desktopOptions;
    private readonly IOptions<PowerPointAddInOptions> _powerPointAddInOptions;

    public HostOperatorFacade(
        DesktopAgentClient desktopAgent,
        RuntimeBuildIdentity buildIdentity,
        IOptions<OperatorOptions> options,
        IOptions<DesktopAgentOptions> desktopOptions,
        IOptions<PowerPointAddInOptions> powerPointAddInOptions)
    {
        _desktopAgent = desktopAgent;
        _buildIdentity = buildIdentity;
        _options = options;
        _desktopOptions = desktopOptions;
        _powerPointAddInOptions = powerPointAddInOptions;
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

    private async Task<HealthResult?> ProbeDesktopAgentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _desktopAgent.GetHealthAsync(cancellationToken);
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
