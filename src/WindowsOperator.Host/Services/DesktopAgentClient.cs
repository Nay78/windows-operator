using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class DesktopAgentClient : IWorkbenchService, IPowerPointOnlineService, IDevAutomationService, IPowerAutomateMcpService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<DesktopAgentOptions> _options;

    public DesktopAgentClient(HttpClient httpClient, IOptions<DesktopAgentOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken) =>
        SendAsync<HealthResult>(HttpMethod.Get, "/v1/health", null, cancellationToken);

    public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<WindowRef>>(HttpMethod.Get, "/v1/windows", null, cancellationToken);

    public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, $"/v1/windows/{hwnd}/activate", null, cancellationToken);

    public Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken) =>
        SendAsync<WindowRef>(HttpMethod.Get, "/v1/desktop/foreground", null, cancellationToken);

    public Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DesktopScreenshotResult>(HttpMethod.Post, "/v1/desktop/screenshot", request, cancellationToken);

    public Task<WorkbenchSessionResult> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<WorkbenchSessionResult>(
            HttpMethod.Get,
            $"/v1/sessions/{Uri.EscapeDataString(sessionId)}",
            null,
            cancellationToken);

    public Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DesktopScreenshotResult>(
            HttpMethod.Post,
            $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/screenshot",
            request,
            cancellationToken);

    public Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<WorkbenchSessionCleanupResult>(
            HttpMethod.Post,
            $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/cleanup",
            null,
            cancellationToken);

    public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken)
    {
        var path = $"/v1/windows/{hwnd}/screenshot";
        if (format is not null)
        {
            path += $"?format={format.Value.ToString().ToLowerInvariant()}";
        }

        return SendAsync<ScreenshotResult>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<UiElementRef>>(HttpMethod.Post, "/v1/uia/query", query, cancellationToken);

    public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/v1/uia/click", request, cancellationToken);

    public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/v1/uia/type", request, cancellationToken);

    public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/v1/input/click", request, cancellationToken);

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/v1/input/hotkey", request, cancellationToken);

    public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeResetResult>(HttpMethod.Post, "/v1/browser/edge/reset", request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionStateResult>(HttpMethod.Post, "/v1/browser/edge/session/start", request, cancellationToken);

    public Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
        BrowserEdgeOpenUrlRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeOpenUrlResult>(HttpMethod.Post, "/v1/browser/edge/open-url", request, cancellationToken);

    public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionStateResult>(
            HttpMethod.Get,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/state",
            null,
            cancellationToken);

    public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionStateResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/navigate",
            request,
            cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionDomActionResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/dom/click",
            request,
            cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionDomActionResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/dom/fill",
            request,
            cancellationToken);

    public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionStateResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/close",
            null,
            cancellationToken);

    public Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DesktopScreenshotResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/screenshot",
            request,
            cancellationToken);

    public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<BrowserEdgeSessionStateResult>(
            HttpMethod.Post,
            $"/v1/browser/edge/session/{Uri.EscapeDataString(sessionId)}/cleanup",
            null,
            cancellationToken);

    public Task<DevScriptResult> EvaluateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeDevEvalRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DevScriptResult>(
            HttpMethod.Post,
            $"/v1/dev/browser/edge/sessions/{Uri.EscapeDataString(sessionId)}/eval",
            request,
            cancellationToken);

    public Task<PowerAutomateMcpStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpStatusResult>(HttpMethod.Get, "/v1/power-automate/mcp/status", null, cancellationToken);

    public Task<PowerAutomateMcpStartResult> StartBridgeAsync(
        PowerAutomateMcpStartRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpStartResult>(HttpMethod.Post, "/v1/power-automate/mcp/start", request, cancellationToken);

    public Task<PowerAutomateMcpEdgeResult> OpenEdgeAsync(
        PowerAutomateMcpEdgeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpEdgeResult>(HttpMethod.Post, "/v1/power-automate/mcp/edge", request, cancellationToken);

    public Task<PowerAutomateMcpEdgeCleanupResult> CleanupEdgeAsync(CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpEdgeCleanupResult>(HttpMethod.Post, "/v1/power-automate/mcp/edge/cleanup", null, cancellationToken);

    public Task<PowerAutomateMcpFlowReadResult> ReadFlowAsync(
        PowerAutomateMcpFlowReadRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpFlowReadResult>(HttpMethod.Post, "/v1/power-automate/mcp/flows/read", request, cancellationToken);

    public Task<PowerAutomateMcpFlowUpdateResult> UpdateFlowAsync(
        PowerAutomateMcpFlowUpdateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerAutomateMcpFlowUpdateResult>(HttpMethod.Post, "/v1/power-automate/mcp/flows/update", request, cancellationToken);

    public Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(
        PowerPointOnlineSessionStartRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            "/v1/powerpoint/online/sessions",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Get,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}",
            null,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(
        string sessionId,
        PowerPointOnlineSlideSelectRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/slides/select",
            request,
            cancellationToken);

    public Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(
        string sessionId,
        PowerPointOnlineAddInProbeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineAddInProbeResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/addin/probe",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(
        string sessionId,
        PowerPointOnlineSaveWaitRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/save/wait",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/template/prepare",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/template/cleanup",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(
        string sessionId,
        PowerPointOnlineAddInCommandRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/addin/run-pending-job",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(
        string sessionId,
        PowerPointOnlineSessionScreenshotRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/screenshot",
            request,
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointOnlineSessionResult>(
            HttpMethod.Post,
            $"/v1/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/cleanup",
            null,
            cancellationToken);

    public Task<DevScriptResult> RunPowerPointOnlineScriptAsync(
        string sessionId,
        PowerPointDevScriptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DevScriptResult>(
            HttpMethod.Post,
            $"/v1/dev/powerpoint/online/sessions/{Uri.EscapeDataString(sessionId)}/script",
            request,
            cancellationToken);

    public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftAuthCleanupResult>(HttpMethod.Post, "/v1/auth/microsoft/cleanup", request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftAuthorizeProbeResult>(HttpMethod.Post, "/v1/auth/microsoft/authorize-probe", request, cancellationToken);

    public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftAuthorizeProbeResult>(
            HttpMethod.Get,
            $"/v1/auth/microsoft/authorize-probe/status/{Uri.EscapeDataString(runId)}",
            null,
            cancellationToken);

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftDeviceLoginResult>(HttpMethod.Post, "/v1/auth/microsoft/device-login", request, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftDeviceLoginResult>(
            HttpMethod.Get,
            $"/v1/auth/microsoft/device-login/status/{Uri.EscapeDataString(runId)}",
            null,
            cancellationToken);

    public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) =>
        SendAsync<MailFoldersResult>(HttpMethod.Post, "/v1/mail/folders", request, cancellationToken);

    public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) =>
        SendAsync<MailStatusResult>(HttpMethod.Get, "/v1/mail/status", null, cancellationToken);

    public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) =>
        SendAsync<MailSearchResult>(HttpMethod.Post, "/v1/mail/messages/search", request, cancellationToken);

    public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) =>
        SendAsync<MailDownloadResult>(HttpMethod.Post, "/v1/mail/attachments/download", request, cancellationToken);

    public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) =>
        SendAsync<MailDownloadResult>(HttpMethod.Get, $"/v1/mail/runs/{Uri.EscapeDataString(runId)}", null, cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.Value.BaseUrl), path));
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: OperatorJson.SerializerOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.LockedDesktop($"Desktop agent unavailable at {_options.Value.BaseUrl}: {ex.Message}"));
        }

        using var _ = response;
        if (response.IsSuccessStatusCode)
        {
            T? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<T>(OperatorJson.SerializerOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new OperatorFailureException(
                    OperatorErrors.LockedDesktop($"Desktop agent returned an invalid or empty response: {ex.Message}"));
            }

            return result ?? throw new OperatorFailureException(
                OperatorErrors.LockedDesktop("Desktop agent returned an empty response."));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var error = JsonSerializer.Deserialize<OperatorError>(body, OperatorJson.SerializerOptions);
                if (error is not null)
                {
                    throw new OperatorFailureException(error);
                }
            }
            catch (JsonException)
            {
            }
        }

        throw new OperatorFailureException(
            OperatorErrors.LockedDesktop(DescribeAgentFailure(response, body)));
    }

    private static string DescribeAgentFailure(HttpResponseMessage response, string body)
    {
        var detail = $"Desktop agent returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        if (string.IsNullOrWhiteSpace(body))
        {
            return detail;
        }

        var normalized = body.ReplaceLineEndings(" ").Trim();
        if (normalized.Length > 200)
        {
            normalized = normalized[..200] + "...";
        }

        return $"{detail} Body: {normalized}";
    }
}
