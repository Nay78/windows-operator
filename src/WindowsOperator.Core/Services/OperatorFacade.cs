using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public sealed class OperatorFacade : IOperatorFacade
{
    private readonly IInputService _inputService;
    private readonly IMailService _mailService;
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
        IMailService mailService,
        IOptions<OperatorOptions> options)
    {
        _windowCatalogService = windowCatalogService;
        _windowActivationService = windowActivationService;
        _uiAutomationService = uiAutomationService;
        _screenshotService = screenshotService;
        _inputService = inputService;
        _mailService = mailService;
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

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        _inputService.SendHotkeyAsync(request, cancellationToken);

    public Task<IReadOnlyList<MailFolderRef>> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        _mailService.ListFoldersAsync(request, cancellationToken);

    public Task<IReadOnlyList<MailMessageRef>> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        _mailService.SearchMessagesAsync(request, cancellationToken);

    public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        _mailService.DownloadAttachmentsAsync(request, cancellationToken);

    public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
        _mailService.GetRunAsync(runId, cancellationToken);

    public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
        _mailService.GetStatusAsync(cancellationToken);

    public Task<MailSyncResult> SyncMailAsync(MailSyncRequest request, CancellationToken cancellationToken) =>
        _mailService.SyncAsync(request, cancellationToken);

    public Task<MailRecoveryResult> RecoverMailAsync(MailRecoveryRequest request, CancellationToken cancellationToken) =>
        _mailService.RecoverAsync(request, cancellationToken);

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
