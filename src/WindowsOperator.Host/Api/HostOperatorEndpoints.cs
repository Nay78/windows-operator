using Microsoft.AspNetCore.Http.HttpResults;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Api;

public static class HostOperatorEndpoints
{
    public static IEndpointRouteBuilder MapHostOperatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1");

        group.MapGet("/health", async Task<Results<Ok<HealthResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.GetHealthAsync(cancellationToken)));

        group.MapGet("/windows", async Task<Results<Ok<IReadOnlyList<WindowRef>>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.ListWindowsAsync(cancellationToken)));

        group.MapPost("/windows/{id:long}/activate", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            long id,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.ActivateWindowAsync(id, cancellationToken)));

        group.MapGet("/windows/{id:long}/screenshot", async Task<Results<Ok<ScreenshotResult>, JsonHttpResult<OperatorError>>> (
            long id,
            string? format,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.CaptureWindowAsync(id, ParseFormat(format), cancellationToken)));

        group.MapPost("/uia/query", async Task<Results<Ok<IReadOnlyList<UiElementRef>>, JsonHttpResult<OperatorError>>> (
            UiQuery request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.QueryUiAsync(request, cancellationToken)));

        group.MapPost("/uia/click", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            UiaClickRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.ClickUiAsync(request, cancellationToken)));

        group.MapPost("/uia/type", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            UiaTypeRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.TypeUiAsync(request, cancellationToken)));

        group.MapPost("/input/hotkey", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            HotkeyRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.SendHotkeyAsync(request, cancellationToken)));

        group.MapPost("/auth/microsoft/device-login", async Task<Results<Ok<MicrosoftDeviceLoginResult>, JsonHttpResult<OperatorError>>> (
            MicrosoftDeviceLoginRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.StartMicrosoftDeviceLoginAsync(request, cancellationToken)));

        group.MapPost("/powerpoint/inspect", async Task<Results<Ok<PowerPointInspectResult>, JsonHttpResult<OperatorError>>> (
            PowerPointInspectRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.InspectPowerPointAsync(request, cancellationToken)));

        group.MapPost("/powerpoint/edit", async Task<Results<Ok<PowerPointEditResult>, JsonHttpResult<OperatorError>>> (
            PowerPointEditRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.EditPowerPointAsync(request, cancellationToken)));

        group.MapGet("/powerpoint/jobs/{jobId}", async Task<Results<Ok<PowerPointEditResult>, JsonHttpResult<OperatorError>>> (
            string jobId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.GetPowerPointJobAsync(jobId, cancellationToken)));

        group.MapPost("/mail/folders", async Task<Results<Ok<MailFoldersResult>, JsonHttpResult<OperatorError>>> (
            MailListFoldersRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.ListMailFoldersAsync(request, cancellationToken)));

        group.MapGet("/mail/status", async Task<Results<Ok<MailStatusResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.GetMailStatusAsync(cancellationToken)));

        group.MapPost("/mail/messages/search", async Task<Results<Ok<MailSearchResult>, JsonHttpResult<OperatorError>>> (
            MailSearchRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.SearchMailMessagesAsync(request, cancellationToken)));

        group.MapPost("/mail/attachments/download", async Task<Results<Ok<MailDownloadResult>, JsonHttpResult<OperatorError>>> (
            MailDownloadRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.DownloadMailAttachmentsAsync(request, cancellationToken)));

        group.MapGet("/mail/runs/{runId}", async Task<Results<Ok<MailDownloadResult>, JsonHttpResult<OperatorError>>> (
            string runId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await HostOperatorHttp.ExecuteAsync(
                () => facade.GetMailRunAsync(runId, cancellationToken)));

        endpoints.MapGet("/openapi.json", () => OperatorOpenApi.Document);

        return endpoints;
    }

    private static ScreenshotFormat? ParseFormat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Enum.Parse<ScreenshotFormat>(raw, true);
    }
}
