using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests.Fakes;

internal sealed class FakeOperatorFacade : IOperatorFacade
{
    public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            new HealthResult(
                "ok",
                "interactive-user",
                "Test",
                "http://127.0.0.1:43117",
                "Fake.UIA3",
                new[] { "Synthetic" },
                true,
                DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WindowRef>>(
            new[]
            {
                new WindowRef(
                    101,
                    202,
                    "Fake",
                    "FakeWindow",
                    new WindowBounds(0, 0, 1200, 900),
                    1.0,
                    DateTimeOffset.UtcNow,
                    true,
                    false),
            });

    public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, $"activated:{hwnd}"));

    public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken) =>
        Task.FromResult(
            new ScreenshotResult(
                format == ScreenshotFormat.Png ? "image/png" : "image/jpeg",
                Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                100,
                80,
                new WindowBounds(0, 0, 100, 80),
                1.0,
                DateTimeOffset.UtcNow,
                "Synthetic",
                1600,
                format == ScreenshotFormat.Png ? null : 85,
                format == ScreenshotFormat.Png));

    public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UiElementRef>>(
            new[]
            {
                new UiElementRef("1", query.Name ?? "Name", query.AutomationId ?? "Id", query.ControlType ?? "Edit", true, false, new WindowBounds(0, 0, 20, 20)),
            });

    public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, "clicked"));

    public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, $"typed:{request.Text}"));

    public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, $"screen-clicked:{request.X},{request.Y}"));

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, string.Join("+", request.Keys)));

    public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeResetResult(
            true,
            2,
            request.DryRun ? 0 : 2,
            new[] { request.DryRun ? "edge_reset_dry_run" : "edge_reset:matched=2;killed=2;failed=0" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:00:00Z")));

    public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionStateResult(
            true,
            request.SessionId ?? "edge-session-run",
            request.ProfileMode,
            request.InPrivate,
            !request.DryRun,
            new[] { request.DryRun ? "browser_session_dry_run" : "session_started" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:01:00Z"),
            request.DryRun ? null : 777,
            request.DryRun ? null : 888L,
            request.DryRun ? null : "Enter code - Microsoft Edge",
            request.StartUrl,
            request.DryRun ? null : "Use a code",
            request.DryRun ? null : new[] { new BrowserEdgeSessionElementRef("input", "text", "code", "code", "otc", "otc") },
            request.DryRun ? null : 9222,
            request.DryRun ? "dry_run" : "page_ready",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{request.SessionId ?? "edge-session-run"}\state.json"));

    public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionStateResult(
            true,
            sessionId,
            BrowserEdgeProfileMode.Work,
            false,
            true,
            new[] { "session_state_observed" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:02:00Z"),
            777,
            888L,
            "Enter code - Microsoft Edge",
            "https://microsoft.com/devicelogin",
            "Use a code",
            new[] { new BrowserEdgeSessionElementRef("button", "submit", "Next", null, null, null) },
            9222,
            "page_ready",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionStateResult(
            true,
            sessionId,
            BrowserEdgeProfileMode.Work,
            false,
            true,
            new[] { "navigate_requested", "navigate_dispatched", "navigation_observed" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:03:00Z"),
            777,
            888L,
            "Navigate - Microsoft Edge",
            request.Url,
            "Target page",
            Array.Empty<BrowserEdgeSessionElementRef>(),
            9222,
            "page_ready",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionDomActionResult(
            true,
            sessionId,
            "click",
            new[] { "click_requested", "click_dispatched" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:04:00Z"),
            request.Selector is null ? "visibleText" : "selector",
            request.VisibleText ?? request.Selector,
            "button",
            "https://microsoft.com/devicelogin",
            "Enter code - Microsoft Edge",
            "Use a code",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionDomActionResult(
            true,
            sessionId,
            "fill",
            new[] { "fill_requested", "fill_dispatched" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:05:00Z"),
            request.Selector is null ? "labelText" : "selector",
            request.LabelText ?? request.Selector,
            "input",
            "https://microsoft.com/devicelogin",
            "Enter code - Microsoft Edge",
            request.Value,
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BrowserEdgeSessionStateResult(
            true,
            sessionId,
            BrowserEdgeProfileMode.Work,
            false,
            false,
            new[] { "session_window_closed" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-25T12:06:00Z"),
            777,
            888L,
            "Enter code - Microsoft Edge",
            "https://microsoft.com/devicelogin",
            null,
            null,
            9222,
            "session_closed",
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\{sessionId}\state.json"));

    public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftAuthCleanupResult(
            true,
            3,
            request.DryRun ? 0 : 3,
            0,
            0,
            new[] { request.DryRun ? "cleanup_dry_run" : "auth_window_cleanup:matched=3;closed=3;preserved=0;failed=0" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-22T12:00:00Z")));

    public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftAuthorizeProbeResult(
            true,
            request.AuthorizeUrl,
            request.InPrivate,
            new[] { request.DryRun ? "dry_run" : "edge_opened" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-18T01:10:00Z"),
            request.RunId ?? "auth-probe-run",
            request.DryRun ? MicrosoftAuthorizeProbeStatus.DryRun : MicrosoftAuthorizeProbeStatus.Opened,
            request.DryRun ? "dry_run" : "browser_opened",
            request.DryRun ? null : "Sign in - Microsoft Edge",
            request.DryRun ? null : request.AuthorizeUrl,
            request.DryRun ? null : "https://login.microsoftonline.com",
            null,
            false,
            DateTimeOffset.Parse("2026-05-18T01:10:01Z"),
            @"C:\Users\fake\AppData\Local\WindowsOperator\run\auth\microsoft-authorize-probe\auth-probe-run\result.json"));

    public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftAuthorizeProbeResult(
            true,
            "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
            false,
            new[] { "edge_opened", "observed_url", "browser_observed:Opened" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-18T01:11:00Z"),
            runId,
            MicrosoftAuthorizeProbeStatus.Opened,
            "browser_opened",
            "Sign in - Microsoft Edge",
            "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
            "https://login.microsoftonline.com",
            null,
            false,
            DateTimeOffset.Parse("2026-05-18T01:11:01Z"),
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\auth\microsoft-authorize-probe\{runId}\result.json"));

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftDeviceLoginResult(
            true,
            request.LoginUrl,
            request.InPrivate,
            new[] { request.DryRun ? "dry_run" : "device_code_submitted" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:13:00Z"),
            request.RunId ?? "fake-run",
            request.DryRun ? MicrosoftDeviceLoginStatus.DryRun : MicrosoftDeviceLoginStatus.Submitted,
            request.DryRun ? "dry_run" : "browser_title_observed",
            request.DryRun ? null : "Enter code - Microsoft Edge",
            DateTimeOffset.Parse("2026-04-26T20:13:01Z"),
            @"C:\Users\fake\AppData\Local\WindowsOperator\run\auth\microsoft-device-login\fake-run\result.json"));

    public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftDeviceLoginResult(
            true,
            "https://microsoft.com/devicelogin",
            false,
            new[] { "device_code_submitted", "browser_observed:Submitted" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:14:00Z"),
            runId,
            MicrosoftDeviceLoginStatus.Submitted,
            "browser_title_observed",
            "Enter code - Microsoft Edge",
            DateTimeOffset.Parse("2026-04-26T20:14:01Z"),
            $@"C:\Users\fake\AppData\Local\WindowsOperator\run\auth\microsoft-device-login\{runId}\result.json"));

    public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new MailFoldersResult(
            true,
            new[]
            {
                new MailFolderRef(0, "mailbox", "mailbox", 1),
                new MailFolderRef(1, "mailbox/Bandeja de entrada", "Bandeja de entrada", 0),
            },
            new[] { "attached_existing_outlook", "folders_read" },
            Array.Empty<string>(),
            Array.Empty<MailRunError>(),
            DateTimeOffset.Parse("2026-04-26T20:16:30Z"),
            false,
            DateTimeOffset.Parse("2026-04-26T20:16:31Z")));

    public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new MailSearchResult(
            true,
            new[]
            {
                new MailMessageRef(
                    "message-1",
                    request.FolderPath ?? "mailbox/Bandeja de entrada",
                    request.SubjectContains ?? "Daily report",
                    DateTimeOffset.Parse("2026-04-26T20:14:25Z"),
                    DateTimeOffset.Parse("2026-04-26T20:16:15Z"),
                    1,
                    new[] { new MailAttachmentRef(1, "report.pdf", ".pdf", 1234) }),
            },
            new[] { "attached_existing_outlook", "messages_searched" },
            Array.Empty<string>(),
            Array.Empty<MailRunError>(),
            DateTimeOffset.Parse("2026-04-26T20:16:30Z"),
            false,
            DateTimeOffset.Parse("2026-04-26T20:16:32Z")));

    public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(
            new MailDownloadResult(
                true,
                request.RunId ?? "mail-download-test",
                "/exchange/runs/mail-download-test",
                "/exchange/downloads/mail",
                1,
                1,
                request.DryRun ? 0 : 1,
                request.DryRun ? 1 : 0,
                request.DryRun
                    ? Array.Empty<MailSavedAttachment>()
                    : new[]
                    {
                        new MailSavedAttachment(
                            "message-1",
                            request.FolderPath ?? "mailbox/Bandeja de entrada",
                            "Daily report",
                            DateTimeOffset.Parse("2026-04-26T20:14:25Z"),
                            1,
                            "report.pdf",
                            "downloads/mail/default/2026-04-26/report.pdf",
                            "/exchange/downloads/mail/default/2026-04-26/report.pdf",
                            1234,
                            false),
                    },
                request.DryRun
                    ? new[]
                    {
                        new MailSkippedAttachment(
                            "message-1",
                            request.FolderPath ?? "mailbox/Bandeja de entrada",
                            "Daily report",
                            DateTimeOffset.Parse("2026-04-26T20:14:25Z"),
                            1,
                            "report.pdf",
                            "dry_run"),
                    }
                    : Array.Empty<MailSkippedAttachment>(),
                new[] { "attached_existing_outlook", "attachments_downloaded" },
                Array.Empty<string>(),
                Array.Empty<MailRunError>(),
                DateTimeOffset.Parse("2026-04-26T20:16:30Z"),
                false,
                DateTimeOffset.Parse("2026-04-26T20:15:00Z")));

    public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
        DownloadMailAttachmentsAsync(new MailDownloadRequest { RunId = runId }, cancellationToken);

    public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MailStatusResult(true, 0, 0, null, DateTimeOffset.Parse("2026-04-26T20:16:00Z")));
}
