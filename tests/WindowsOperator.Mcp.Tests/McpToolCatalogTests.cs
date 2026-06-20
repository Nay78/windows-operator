using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Mcp.Protocol;

namespace WindowsOperator.Mcp.Tests;

public sealed class McpToolCatalogTests
{
    [Fact]
    public void ListTools_ReturnsStableBaseCatalog()
    {
        var catalog = new McpToolCatalog(new FakeOperatorFacade());

        var tools = catalog.ListTools();

        Assert.Equal(
            new[]
            {
                "operator_health",
                "window_list",
                "window_activate",
                "window_screenshot",
                "uia_query",
                "uia_click",
                "uia_type",
                "input_hotkey",
                "browser_edge_reset",
                "browser_edge_session_start",
                "browser_edge_session_state",
                "browser_edge_session_navigate",
                "browser_edge_session_dom_click",
                "browser_edge_session_dom_fill",
                "browser_edge_session_close",
                "auth_microsoft_cleanup",
                "auth_microsoft_authorize_probe",
                "auth_microsoft_authorize_probe_status",
                "auth_microsoft_device_login",
                "auth_microsoft_device_login_status",
                "mail_list_folders",
                "mail_status",
                "mail_search_messages",
                "mail_download_attachments",
                "mail_get_run",
            },
            tools.Select(tool => tool.Name));
    }

    [Fact]
    public void ListTools_ExposesConcreteInputSchemas()
    {
        var catalog = new McpToolCatalog(new FakeOperatorFacade());
        var schemas = catalog.ListTools().ToDictionary(tool => tool.Name, tool => tool.InputSchema, StringComparer.Ordinal);

        Assert.Empty(schemas["operator_health"]["properties"]!.AsObject());
        Assert.Equal("object", schemas["uia_query"]["type"]!.GetValue<string>());
        Assert.NotNull(schemas["uia_query"]["properties"]!["windowHwnd"]);
        Assert.NotNull(schemas["uia_query"]["properties"]!["maxResults"]);
        Assert.Contains("hwnd", schemas["window_screenshot"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("png", schemas["window_screenshot"]["properties"]!["format"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("keys", schemas["input_hotkey"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.NotNull(schemas["browser_edge_reset"]["properties"]!["dryRun"]);
        Assert.NotNull(schemas["browser_edge_session_start"]["properties"]!["profileMode"]);
        Assert.Contains("sessionId", schemas["browser_edge_session_state"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("url", schemas["browser_edge_session_navigate"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.NotNull(schemas["browser_edge_session_dom_click"]["properties"]!["visibleText"]);
        Assert.Contains("value", schemas["browser_edge_session_dom_fill"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.NotNull(schemas["auth_microsoft_cleanup"]["properties"]!["preserveRecentSeconds"]);
        Assert.NotNull(schemas["auth_microsoft_cleanup"]["properties"]!["dryRun"]);
        Assert.Contains("authorizeUrl", schemas["auth_microsoft_authorize_probe"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.NotNull(schemas["auth_microsoft_authorize_probe"]["properties"]!["observationTimeoutSeconds"]);
        Assert.NotNull(schemas["auth_microsoft_authorize_probe"]["properties"]!["reuseExistingProfile"]);
        Assert.NotNull(schemas["auth_microsoft_authorize_probe_status"]["properties"]!["runId"]);
        Assert.Contains("deviceCode", schemas["auth_microsoft_device_login"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.NotNull(schemas["auth_microsoft_device_login"]["properties"]!["verificationWaitSeconds"]);
        Assert.NotNull(schemas["auth_microsoft_device_login"]["properties"]!["reuseExistingProfile"]);
        Assert.NotNull(schemas["auth_microsoft_device_login_status"]["properties"]!["runId"]);
        Assert.NotNull(schemas["mail_list_folders"]["properties"]!["freshness"]);
        Assert.NotNull(schemas["mail_search_messages"]["properties"]!["folderPath"]);
        Assert.NotNull(schemas["mail_search_messages"]["properties"]!["freshness"]);
        Assert.NotNull(schemas["mail_download_attachments"]["properties"]!["messageIds"]);
        Assert.NotNull(schemas["mail_download_attachments"]["properties"]!["freshness"]);
        Assert.Contains("runId", schemas["mail_get_run"]["required"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task OperatorHealth_ReturnsHealthContract()
    {
        var catalog = new McpToolCatalog(new FakeOperatorFacade());

        var node = await catalog.ExecuteToolAsync("operator_health", new JsonObject(), CancellationToken.None);
        var health = node!.Deserialize<HealthResult>(OperatorJson.SerializerOptions);

        Assert.Equal("ok", health!.Status);
        Assert.Equal("Fake.UIA3", health.UiBackend);
        Assert.True(health.McpEnabled);
    }

    [Fact]
    public async Task WindowScreenshot_DeserializesHwndAndFormat()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        var node = await catalog.ExecuteToolAsync(
            "window_screenshot",
            new JsonObject
            {
                ["hwnd"] = 42,
                ["format"] = "png",
            },
            CancellationToken.None);
        var screenshot = node!.Deserialize<ScreenshotResult>(OperatorJson.SerializerOptions);

        Assert.Equal(42, facade.LastScreenshotHwnd);
        Assert.Equal(ScreenshotFormat.Png, facade.LastScreenshotFormat);
        Assert.Equal("image/png", screenshot!.MediaType);
    }

    [Fact]
    public async Task InputHotkey_DeserializesKeys()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        await catalog.ExecuteToolAsync(
            "input_hotkey",
            new JsonObject
            {
                ["keys"] = new JsonArray("ctrl", "shift", "p"),
            },
            CancellationToken.None);

        Assert.Equal(new[] { "ctrl", "shift", "p" }, facade.LastHotkeyKeys);
    }

    [Fact]
    public async Task MicrosoftAuthorizeProbe_DeserializesRequest()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        var node = await catalog.ExecuteToolAsync(
            "auth_microsoft_authorize_probe",
            new JsonObject
            {
                ["authorizeUrl"] = "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
                ["observationTimeoutSeconds"] = 45,
                ["reuseExistingProfile"] = true,
            },
            CancellationToken.None);
        var result = node!.Deserialize<MicrosoftAuthorizeProbeResult>(OperatorJson.SerializerOptions);

        Assert.Equal("https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize", facade.LastAuthorizeProbe?.AuthorizeUrl);
        Assert.Equal(45, facade.LastAuthorizeProbe?.ObservationTimeoutSeconds);
        Assert.True(facade.LastAuthorizeProbe?.ReuseExistingProfile);
        Assert.Equal(MicrosoftAuthorizeProbeStatus.Opened, result!.Status);
    }

    [Fact]
    public async Task MicrosoftAuthCleanup_DeserializesRequest()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        var node = await catalog.ExecuteToolAsync(
            "auth_microsoft_cleanup",
            new JsonObject
            {
                ["preserveRecentSeconds"] = 45,
                ["dryRun"] = true,
            },
            CancellationToken.None);
        var result = node!.Deserialize<MicrosoftAuthCleanupResult>(OperatorJson.SerializerOptions);

        Assert.Equal(45, facade.LastAuthCleanup?.PreserveRecentSeconds);
        Assert.True(facade.LastAuthCleanup?.DryRun);
        Assert.Equal(3, result!.MatchedWindows);
    }

    [Fact]
    public async Task BrowserEdgeSessionStart_DeserializesRequest()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        var node = await catalog.ExecuteToolAsync(
            "browser_edge_session_start",
            new JsonObject
            {
                ["sessionId"] = "entra-session",
                ["startUrl"] = "https://microsoft.com/devicelogin",
                ["profileMode"] = "work",
            },
            CancellationToken.None);
        var result = node!.Deserialize<BrowserEdgeSessionStateResult>(OperatorJson.SerializerOptions);

        Assert.Equal("entra-session", facade.LastBrowserSessionStart?.SessionId);
        Assert.Equal(BrowserEdgeProfileMode.Work, facade.LastBrowserSessionStart?.ProfileMode);
        Assert.True(result!.IsAlive);
    }

    [Fact]
    public async Task ExecuteToolAsync_UnknownTool_ThrowsMethodNotFound()
    {
        var catalog = new McpToolCatalog(new FakeOperatorFacade());

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => catalog.ExecuteToolAsync("missing_tool", new JsonObject(), CancellationToken.None));

        Assert.Equal(-32601, error.Code);
    }

    [Fact]
    public async Task ExecuteToolAsync_InvalidArgs_ThrowsInvalidParams()
    {
        var catalog = new McpToolCatalog(new FakeOperatorFacade());

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => catalog.ExecuteToolAsync("window_screenshot", new JsonObject { ["format"] = "gif" }, CancellationToken.None));

        Assert.Equal(-32602, error.Code);
        Assert.Contains("hwnd", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailSearch_DeserializesRequest()
    {
        var facade = new FakeOperatorFacade();
        var catalog = new McpToolCatalog(facade);

        var node = await catalog.ExecuteToolAsync(
            "mail_search_messages",
            new JsonObject
            {
                ["folderPath"] = "mailbox/Bandeja de entrada",
                ["subjectContains"] = "Daily",
                ["hasAttachments"] = true,
                ["maxResults"] = 5,
            },
            CancellationToken.None);
        var result = node!.Deserialize<MailSearchResult>(OperatorJson.SerializerOptions);

        Assert.Equal("mailbox/Bandeja de entrada", facade.LastMailSearch?.FolderPath);
        Assert.Equal("Daily", facade.LastMailSearch?.SubjectContains);
        Assert.True(facade.LastMailSearch?.HasAttachments);
        Assert.Single(result!.Messages);
    }

    private sealed class FakeOperatorFacade : IOperatorFacade
    {
        public long? LastScreenshotHwnd { get; private set; }

        public ScreenshotFormat? LastScreenshotFormat { get; private set; }

        public IReadOnlyList<string>? LastHotkeyKeys { get; private set; }

        public MailSearchRequest? LastMailSearch { get; private set; }

        public MicrosoftAuthorizeProbeRequest? LastAuthorizeProbe { get; private set; }

        public MicrosoftAuthCleanupRequest? LastAuthCleanup { get; private set; }

        public BrowserEdgeSessionStartRequest? LastBrowserSessionStart { get; private set; }

        public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                new HealthResult(
                    "ok",
                    "interactive-user",
                    "Test OS",
                    "http://127.0.0.1:43117",
                    "Fake.UIA3",
                    new[] { "Synthetic" },
                    true,
                    DateTimeOffset.Parse("2026-04-26T00:00:00Z")));

        public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WindowRef>>(Array.Empty<WindowRef>());

        public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionResult(true, $"activated:{hwnd}"));

        public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken)
        {
            LastScreenshotHwnd = hwnd;
            LastScreenshotFormat = format;
            return Task.FromResult(
                new ScreenshotResult(
                    format == ScreenshotFormat.Png ? "image/png" : "image/jpeg",
                    Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                    1,
                    1,
                    new WindowBounds(0, 0, 1, 1),
                    1,
                    DateTimeOffset.Parse("2026-04-26T00:00:00Z"),
                    "Synthetic",
                    1600,
                    format == ScreenshotFormat.Png ? null : 85,
                    format == ScreenshotFormat.Png));
        }

        public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UiElementRef>>(Array.Empty<UiElementRef>());

        public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionResult(true, "clicked"));

        public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionResult(true, "typed"));

        public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionResult(true, $"screen-clicked:{request.X},{request.Y}"));

        public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken)
        {
            LastHotkeyKeys = request.Keys;
            return Task.FromResult(new ActionResult(true, string.Join("+", request.Keys)));
        }

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
            CancellationToken cancellationToken)
        {
            LastBrowserSessionStart = request;
            return Task.FromResult(new BrowserEdgeSessionStateResult(
                true,
                request.SessionId ?? "edge-session-run",
                request.ProfileMode,
                request.InPrivate,
                !request.DryRun,
                new[] { request.DryRun ? "browser_session_dry_run" : "session_started" },
                Array.Empty<string>(),
                DateTimeOffset.Parse("2026-05-25T12:01:00Z"),
                777,
                888L,
                "Enter code - Microsoft Edge",
                request.StartUrl,
                "Use a code",
                new[] { new BrowserEdgeSessionElementRef("input", "text", "code", "code", "otc", "otc") },
                9222,
                request.DryRun ? "dry_run" : "page_ready",
                @"C:\Users\fake\AppData\Local\WindowsOperator\run\browser\edge-sessions\edge-session-run\state.json"));
        }

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
                Array.Empty<BrowserEdgeSessionElementRef>(),
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
            CancellationToken cancellationToken)
        {
            LastAuthCleanup = request;
            return Task.FromResult(new MicrosoftAuthCleanupResult(
                true,
                3,
                request.DryRun ? 0 : 3,
                0,
                0,
                new[] { request.DryRun ? "cleanup_dry_run" : "auth_window_cleanup:matched=3;closed=3;preserved=0;failed=0" },
                Array.Empty<string>(),
                DateTimeOffset.Parse("2026-05-22T12:00:00Z")));
        }

        public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
            MicrosoftAuthorizeProbeRequest request,
            CancellationToken cancellationToken)
        {
            LastAuthorizeProbe = request;
            return Task.FromResult(new MicrosoftAuthorizeProbeResult(
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
        }

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
                new[] { new MailFolderRef(0, "mailbox", "mailbox", 1) },
                new[] { "attached_existing_outlook", "folders_read" },
                Array.Empty<string>(),
                Array.Empty<MailRunError>(),
                DateTimeOffset.Parse("2026-04-26T20:16:30Z"),
                false,
                DateTimeOffset.Parse("2026-04-26T20:16:31Z")));

        public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken)
        {
            LastMailSearch = request;
            return Task.FromResult(new MailSearchResult(
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
        }

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
                    Array.Empty<MailSavedAttachment>(),
                    Array.Empty<MailSkippedAttachment>(),
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
}
