using Microsoft.AspNetCore.Http.HttpResults;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Api;

public static class OperatorEndpoints
{
    public static IEndpointRouteBuilder MapOperatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1");

        group.MapGet("/health", async Task<Results<Ok<HealthResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetHealthAsync(cancellationToken)));

        group.MapGet("/windows", async Task<Results<Ok<IReadOnlyList<WindowRef>>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ListWindowsAsync(cancellationToken)));

        group.MapGet("/desktop/foreground", async Task<Results<Ok<WindowRef>, JsonHttpResult<OperatorError>>> (
            IWorkbenchService workbench,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => workbench.GetForegroundWindowAsync(cancellationToken)));

        group.MapPost("/desktop/screenshot", async Task<Results<Ok<DesktopScreenshotResult>, JsonHttpResult<OperatorError>>> (
            DesktopScreenshotRequest request,
            IWorkbenchService workbench,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => workbench.CaptureDesktopScreenshotAsync(request, cancellationToken)));

        group.MapPost("/windows/{id:long}/activate", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            long id,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ActivateWindowAsync(id, cancellationToken)));

        group.MapGet("/windows/{id:long}/screenshot", async Task<Results<Ok<ScreenshotResult>, JsonHttpResult<OperatorError>>> (
            long id,
            string? format,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.CaptureWindowAsync(id, ParseFormat(format), cancellationToken)));

        group.MapPost("/uia/query", async Task<Results<Ok<IReadOnlyList<UiElementRef>>, JsonHttpResult<OperatorError>>> (
            UiQuery request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.QueryUiAsync(request, cancellationToken)));

        group.MapPost("/uia/click", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            UiaClickRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ClickUiAsync(request, cancellationToken)));

        group.MapPost("/uia/type", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            UiaTypeRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.TypeUiAsync(request, cancellationToken)));

        group.MapPost("/input/click", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            ScreenClickRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ClickScreenAsync(request, cancellationToken)));

        group.MapPost("/input/hotkey", async Task<Results<Ok<ActionResult>, JsonHttpResult<OperatorError>>> (
            HotkeyRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.SendHotkeyAsync(request, cancellationToken)));

        group.MapPost("/browser/edge/reset", async Task<Results<Ok<BrowserEdgeResetResult>, JsonHttpResult<OperatorError>>> (
            BrowserEdgeResetRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ResetEdgeBrowserAsync(request, cancellationToken)));

        group.MapPost("/browser/edge/session/start", async Task<Results<Ok<BrowserEdgeSessionStateResult>, JsonHttpResult<OperatorError>>> (
            BrowserEdgeSessionStartRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.StartEdgeBrowserSessionAsync(request, cancellationToken)));

        group.MapPost("/browser/edge/open-url", async Task<Results<Ok<BrowserEdgeOpenUrlResult>, JsonHttpResult<OperatorError>>> (
            BrowserEdgeOpenUrlRequest request,
            IWorkbenchService workbench,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => workbench.OpenEdgeUrlAsync(request, cancellationToken)));

        group.MapGet("/browser/edge/session/{sessionId}/state", async Task<Results<Ok<BrowserEdgeSessionStateResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetEdgeBrowserSessionStateAsync(sessionId, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/navigate", async Task<Results<Ok<BrowserEdgeSessionStateResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            BrowserEdgeSessionNavigateRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.NavigateEdgeBrowserSessionAsync(sessionId, request, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/dom/click", async Task<Results<Ok<BrowserEdgeSessionDomActionResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            BrowserEdgeSessionDomClickRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ClickEdgeBrowserDomAsync(sessionId, request, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/dom/fill", async Task<Results<Ok<BrowserEdgeSessionDomActionResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            BrowserEdgeSessionDomFillRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.FillEdgeBrowserDomAsync(sessionId, request, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/close", async Task<Results<Ok<BrowserEdgeSessionStateResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.CloseEdgeBrowserSessionAsync(sessionId, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/screenshot", async Task<Results<Ok<DesktopScreenshotResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            DesktopScreenshotRequest request,
            IWorkbenchService workbench,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => workbench.CaptureEdgeSessionScreenshotAsync(sessionId, request, cancellationToken)));

        group.MapPost("/browser/edge/session/{sessionId}/cleanup", async Task<Results<Ok<BrowserEdgeSessionStateResult>, JsonHttpResult<OperatorError>>> (
            string sessionId,
            IWorkbenchService workbench,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => workbench.CleanupEdgeSessionAsync(sessionId, cancellationToken)));

        group.MapPost("/auth/microsoft/cleanup", async Task<Results<Ok<MicrosoftAuthCleanupResult>, JsonHttpResult<OperatorError>>> (
            MicrosoftAuthCleanupRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.CleanupMicrosoftAuthWindowsAsync(request, cancellationToken)));

        group.MapPost("/auth/microsoft/authorize-probe", async Task<Results<Ok<MicrosoftAuthorizeProbeResult>, JsonHttpResult<OperatorError>>> (
            MicrosoftAuthorizeProbeRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.StartMicrosoftAuthorizeProbeAsync(request, cancellationToken)));

        group.MapGet("/auth/microsoft/authorize-probe/status/latest", async Task<Results<Ok<MicrosoftAuthorizeProbeResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetMicrosoftAuthorizeProbeStatusAsync("latest", cancellationToken)));

        group.MapGet("/auth/microsoft/authorize-probe/status/{runId}", async Task<Results<Ok<MicrosoftAuthorizeProbeResult>, JsonHttpResult<OperatorError>>> (
            string runId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetMicrosoftAuthorizeProbeStatusAsync(runId, cancellationToken)));

        group.MapPost("/auth/microsoft/device-login", async Task<Results<Ok<MicrosoftDeviceLoginResult>, JsonHttpResult<OperatorError>>> (
            MicrosoftDeviceLoginRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.StartMicrosoftDeviceLoginAsync(request, cancellationToken)));

        group.MapGet("/auth/microsoft/device-login/status/latest", async Task<Results<Ok<MicrosoftDeviceLoginResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetMicrosoftDeviceLoginStatusAsync("latest", cancellationToken)));

        group.MapGet("/auth/microsoft/device-login/status/{runId}", async Task<Results<Ok<MicrosoftDeviceLoginResult>, JsonHttpResult<OperatorError>>> (
            string runId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetMicrosoftDeviceLoginStatusAsync(runId, cancellationToken)));

        group.MapPost("/mail/folders", async Task<Results<Ok<MailFoldersResult>, JsonHttpResult<OperatorError>>> (
            MailListFoldersRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.ListMailFoldersAsync(request, cancellationToken)));

        group.MapGet("/mail/status", async Task<Results<Ok<MailStatusResult>, JsonHttpResult<OperatorError>>> (
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.GetMailStatusAsync(cancellationToken)));

        group.MapPost("/mail/messages/search", async Task<Results<Ok<MailSearchResult>, JsonHttpResult<OperatorError>>> (
            MailSearchRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.SearchMailMessagesAsync(request, cancellationToken)));

        group.MapPost("/mail/attachments/download", async Task<Results<Ok<MailDownloadResult>, JsonHttpResult<OperatorError>>> (
            MailDownloadRequest request,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
                () => facade.DownloadMailAttachmentsAsync(request, cancellationToken)));

        group.MapGet("/mail/runs/{runId}", async Task<Results<Ok<MailDownloadResult>, JsonHttpResult<OperatorError>>> (
            string runId,
            IOperatorFacade facade,
            CancellationToken cancellationToken) =>
            await OperatorHttp.ExecuteAsync(
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
