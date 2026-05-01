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

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionResult(true, string.Join("+", request.Keys)));

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MicrosoftDeviceLoginResult(
            true,
            request.LoginUrl,
            request.InPrivate,
            new[] { request.DryRun ? "dry_run" : "device_code_submitted" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:13:00Z")));

    public Task<IReadOnlyList<MailFolderRef>> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailFolderRef>>(
            new[]
            {
                new MailFolderRef(0, "mailbox", "mailbox", 1),
                new MailFolderRef(1, "mailbox/Bandeja de entrada", "Bandeja de entrada", 0),
            });

    public Task<IReadOnlyList<MailMessageRef>> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailMessageRef>>(
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
            string.IsNullOrWhiteSpace(request.Mode) ? "basic" : request.Mode,
            true,
            new[] { "fake_recovery" },
            Array.Empty<string>(),
            0,
            0,
            DateTimeOffset.Parse("2026-04-26T20:17:00Z")));
}
