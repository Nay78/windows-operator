using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public sealed class OperatorFacade : IOperatorFacade
{
    private readonly IEdgeBrowserService _edgeBrowserService;
    private readonly IInputService _inputService;
    private readonly IMailService _mailService;
    private readonly IOneDriveFilesOnDemandService _oneDriveService;
    private readonly IMicrosoftAuthService _microsoftAuthService;
    private readonly RuntimeBuildIdentity _buildIdentity;
    private readonly IUiAutomationService _uiAutomationService;
    private readonly IOptions<OperatorOptions> _options;
    private readonly IScreenshotService _screenshotService;
    private readonly IWindowActivationService _windowActivationService;
    private readonly IWindowCatalogService _windowCatalogService;

    public OperatorFacade(
        IWindowCatalogService windowCatalogService,
        IWindowActivationService windowActivationService,
        IUiAutomationService uiAutomationService,
        IScreenshotService screenshotService,
        IInputService inputService,
        IEdgeBrowserService edgeBrowserService,
        IMailService mailService,
        IOneDriveFilesOnDemandService oneDriveService,
        IMicrosoftAuthService microsoftAuthService,
        RuntimeBuildIdentity buildIdentity,
        IOptions<OperatorOptions> options)
    {
        _windowCatalogService = windowCatalogService;
        _windowActivationService = windowActivationService;
        _uiAutomationService = uiAutomationService;
        _screenshotService = screenshotService;
        _inputService = inputService;
        _edgeBrowserService = edgeBrowserService;
        _mailService = mailService;
        _oneDriveService = oneDriveService;
        _microsoftAuthService = microsoftAuthService;
        _buildIdentity = buildIdentity;
        _options = options;
    }

    public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var result = new HealthResult(
            "ok",
            "interactive-user",
            Environment.OSVersion.VersionString,
            options.RestBaseUrl,
            options.UiBackend,
            new[] { "WindowsGraphicsCapture", "PrintWindow", "GdiBitBlt" },
            options.EnableMcpStdio,
            DateTimeOffset.UtcNow);

        return Task.FromResult(result);
    }

    public async Task<CapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var health = await GetHealthAsync(cancellationToken);
        var features = new Dictionary<string, CapabilityFeature>(StringComparer.Ordinal)
        {
            ["desktop.window"] = new(true, "stable"),
            ["desktop.screenshot"] = new(true, "stable"),
            ["browser.edge.session"] = new(true, "stable"),
            ["power-automate.mcp"] = new(
                true,
                "diagnostic",
                "Browser-token/API bridge bootstrap only; Power Automate writes must not fall back to designer UI automation."),
            ["powerpoint.online.session"] = new(true, "stable"),
            ["powerpoint.online.update"] = new(
                false,
                "stable",
                "PowerPoint update orchestration is owned by the Headless Host."),
            ["mail.outlook.download"] = new(true, "stable"),
            ["files.onedrive"] = new(true, "diagnostic", "Files-On-Demand lease and local reclaim surface; stable promotion requires live Windows proof."),
        };

        return new CapabilitiesResult(
            OperatorContractVersion.Value,
            _buildIdentity,
            new CapabilityHost(health.Status, health.RuntimeMode, health.RestBaseUrl, "ok"),
            features,
            DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) =>
        _windowCatalogService.ListAsync(cancellationToken);

    public async Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken)
    {
        var window = await RequireWindowAsync(hwnd, cancellationToken);
        return await _windowActivationService.ActivateAsync(window, cancellationToken);
    }

    public async Task<ScreenshotResult> CaptureWindowAsync(
        long hwnd,
        ScreenshotFormat? format,
        CancellationToken cancellationToken)
    {
        var window = await RequireWindowAsync(hwnd, cancellationToken);
        return await _screenshotService.CaptureAsync(window, format, cancellationToken);
    }

    public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) =>
        _uiAutomationService.QueryAsync(query, cancellationToken);

    public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
        _uiAutomationService.ClickAsync(request, cancellationToken);

    public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
        _uiAutomationService.TypeAsync(request, cancellationToken);

    public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) =>
        _inputService.ClickScreenAsync(request, cancellationToken);

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        _inputService.SendHotkeyAsync(request, cancellationToken);

    public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.ResetAsync(request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.StartSessionAsync(request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.GetSessionStateAsync(sessionId, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.NavigateSessionAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.ClickDomAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.FillDomAsync(sessionId, request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.CloseSessionAsync(sessionId, cancellationToken);

    public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken) =>
        _microsoftAuthService.CleanupAuthWindowsAsync(request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken) =>
        _microsoftAuthService.StartAuthorizeProbeAsync(request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _microsoftAuthService.GetAuthorizeProbeStatusAsync(runId, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        _microsoftAuthService.StartDeviceLoginAsync(request, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _microsoftAuthService.GetDeviceLoginStatusAsync(runId, cancellationToken);

    public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        _mailService.ListFoldersAsync(request, cancellationToken);

    public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        _mailService.SearchMessagesAsync(request, cancellationToken);

    public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        _mailService.DownloadAttachmentsAsync(request, cancellationToken);

    public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
        _mailService.GetRunAsync(runId, cancellationToken);

    public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
        _mailService.GetStatusAsync(cancellationToken);

    public Task<OneDriveLeaseResult> AcquireOneDriveLeaseAsync(
        OneDriveLeaseRequest request,
        CancellationToken cancellationToken) =>
        _oneDriveService.AcquireLeaseAsync(request, cancellationToken);

    public Task<IReadOnlyList<OneDriveFileEntry>> ListOneDriveFilesAsync(
        OneDriveListRequest request,
        CancellationToken cancellationToken) =>
        _oneDriveService.ListFilesAsync(request, cancellationToken);

    public Task<OneDriveLeaseStatusResult> GetOneDriveLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken) =>
        _oneDriveService.GetLeaseAsync(leaseId, cancellationToken);

    public Task<OneDriveLeaseResult> RenewOneDriveLeaseAsync(
        string leaseId,
        OneDriveLeaseRenewRequest request,
        CancellationToken cancellationToken) =>
        _oneDriveService.RenewLeaseAsync(leaseId, request, cancellationToken);

    public Task<OneDriveLeaseResult> ReleaseOneDriveLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken) =>
        _oneDriveService.ReleaseLeaseAsync(leaseId, cancellationToken);

    public Task<OneDriveFilesOnDemandStatusResult> GetOneDriveStatusAsync(CancellationToken cancellationToken) =>
        _oneDriveService.GetStatusAsync(cancellationToken);

    public Task<OneDriveConfigResult> GetOneDriveConfigAsync(CancellationToken cancellationToken) =>
        _oneDriveService.GetConfigAsync(cancellationToken);

    public Task<OneDriveConfigResult> UpdateOneDriveConfigAsync(
        OneDriveConfigUpdateRequest request,
        CancellationToken cancellationToken) =>
        _oneDriveService.UpdateConfigAsync(request, cancellationToken);

    public Task<OneDriveReclaimResult> StartOneDriveReclaimAsync(
        OneDriveReclaimRequest request,
        CancellationToken cancellationToken) =>
        _oneDriveService.StartReclaimAsync(request, cancellationToken);

    public Task<OneDriveReclaimResult> GetOneDriveReclaimAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _oneDriveService.GetReclaimAsync(runId, cancellationToken);

    private async Task<WindowRef> RequireWindowAsync(long hwnd, CancellationToken cancellationToken)
    {
        var window = await _windowCatalogService.GetAsync(hwnd, cancellationToken);
        if (window is not null)
        {
            return window;
        }

        throw new OperatorFailureException(
            OperatorErrors.WindowNotFound($"hwnd={hwnd}"));
    }
}
