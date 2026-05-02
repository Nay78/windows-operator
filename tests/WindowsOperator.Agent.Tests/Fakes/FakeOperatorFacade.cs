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

    public Task<PowerPointInspectResult> InspectPowerPointAsync(
        PowerPointInspectRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PowerPointInspectResult(
            true,
            new PowerPointPresentationRef("report.pptx", request.PresentationPath ?? request.PresentationUrl ?? request.ExchangePath, 1),
            new[]
            {
                new PowerPointSlideRef(
                    1,
                    257,
                    "Executive Summary",
                    new Dictionary<string, string> { ["WO_SLIDE"] = "EXEC_SUMMARY" },
                    new[]
                    {
                        new PowerPointShapeRef(
                            12,
                            "Summary.Status",
                            "TextBox",
                            null,
                            0,
                            new Dictionary<string, string> { ["WO_FIELD"] = "STATUS" },
                            null,
                            true,
                            false,
                            false,
                            true,
                            request.IncludeText ? "{{STATUS}}" : null),
                    }),
            },
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:18:00Z")));

    public Task<PowerPointEditResult> EditPowerPointAsync(
        PowerPointEditRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PowerPointEditResult(
            true,
            request.DryRun,
            "powerpoint-test",
            request.PresentationPath ?? request.PresentationUrl ?? request.ExchangePath,
            request.OutputPath,
            request.Edits.Select(edit => new PowerPointEditOutcome(
                edit.Id,
                edit.Op,
                1,
                request.DryRun ? 0 : 1,
                "{{STATUS}}",
                edit.Value,
                Array.Empty<string>(),
                Array.Empty<string>())).ToArray(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:19:00Z")));

    public Task<PowerPointEditResult> GetPowerPointJobAsync(string jobId, CancellationToken cancellationToken) =>
        Task.FromResult(new PowerPointEditResult(
            true,
            true,
            jobId,
            @"C:\Reports\report.pptx",
            null,
            Array.Empty<PowerPointEditOutcome>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:20:00Z")));

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
