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
                "mail_list_folders",
                "mail_status",
                "mail_sync",
                "mail_recover",
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
        Assert.NotNull(schemas["mail_list_folders"]["properties"]!["syncBeforeRead"]);
        Assert.NotNull(schemas["mail_search_messages"]["properties"]!["folderPath"]);
        Assert.NotNull(schemas["mail_search_messages"]["properties"]!["syncBeforeRead"]);
        Assert.NotNull(schemas["mail_sync"]["properties"]!["waitSeconds"]);
        Assert.NotNull(schemas["mail_recover"]["properties"]!["mode"]);
        Assert.NotNull(schemas["mail_download_attachments"]["properties"]!["messageIds"]);
        Assert.NotNull(schemas["mail_download_attachments"]["properties"]!["syncBeforeRead"]);
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
        var messages = node!.Deserialize<IReadOnlyList<MailMessageRef>>(OperatorJson.SerializerOptions);

        Assert.Equal("mailbox/Bandeja de entrada", facade.LastMailSearch?.FolderPath);
        Assert.Equal("Daily", facade.LastMailSearch?.SubjectContains);
        Assert.True(facade.LastMailSearch?.HasAttachments);
        Assert.Single(messages!);
    }

    private sealed class FakeOperatorFacade : IOperatorFacade
    {
        public long? LastScreenshotHwnd { get; private set; }

        public ScreenshotFormat? LastScreenshotFormat { get; private set; }

        public IReadOnlyList<string>? LastHotkeyKeys { get; private set; }

        public MailSearchRequest? LastMailSearch { get; private set; }

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

        public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken)
        {
            LastHotkeyKeys = request.Keys;
            return Task.FromResult(new ActionResult(true, string.Join("+", request.Keys)));
        }

        public Task<IReadOnlyList<MailFolderRef>> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailFolderRef>>(
                new[] { new MailFolderRef(0, "mailbox", "mailbox", 1) });

        public Task<IReadOnlyList<MailMessageRef>> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken)
        {
            LastMailSearch = request;
            return Task.FromResult<IReadOnlyList<MailMessageRef>>(
                new[]
                {
                    new MailMessageRef(
                        "message-1",
                        request.FolderPath ?? "mailbox/Bandeja de entrada",
                        request.SubjectContains ?? "Daily report",
                        DateTimeOffset.Parse("2026-04-26T20:14:25Z"),
                        1,
                        new[] { new MailAttachmentRef(1, "report.pdf", ".pdf", 1234) }),
                });
        }

        public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(
                new MailDownloadResult(
                    request.RunId ?? "mail-download-test",
                    "/exchange/runs/mail-download-test",
                    "/exchange/downloads/mail",
                    1,
                    1,
                    request.DryRun ? 0 : 1,
                    request.DryRun ? 1 : 0,
                    Array.Empty<MailSavedAttachment>(),
                    Array.Empty<MailSkippedAttachment>(),
                    Array.Empty<MailRunError>(),
                    DateTimeOffset.Parse("2026-04-26T20:15:00Z")));

        public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
            DownloadMailAttachmentsAsync(new MailDownloadRequest { RunId = runId }, cancellationToken);

        public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MailStatusResult(true, 0, 0, null, null, DateTimeOffset.Parse("2026-04-26T20:16:00Z")));

        public Task<MailSyncResult> SyncMailAsync(MailSyncRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MailSyncResult(
                true,
                1,
                request.WaitSeconds,
                new[] { "fake_sync" },
                Array.Empty<string>(),
                DateTimeOffset.Parse("2026-04-26T20:16:30Z")));

        public Task<MailRecoveryResult> RecoverMailAsync(MailRecoveryRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MailRecoveryResult(
                request.Mode,
                true,
                new[] { "fake_recovery" },
                Array.Empty<string>(),
                0,
                0,
                DateTimeOffset.Parse("2026-04-26T20:17:00Z")));
    }
}
