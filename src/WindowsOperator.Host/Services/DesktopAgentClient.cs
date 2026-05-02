using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Host.Services;

public sealed class DesktopAgentClient
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

    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/v1/input/hotkey", request, cancellationToken);

    public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MicrosoftDeviceLoginResult>(HttpMethod.Post, "/v1/auth/microsoft/device-login", request, cancellationToken);

    public Task<PowerPointInspectResult> InspectPowerPointAsync(
        PowerPointInspectRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointInspectResult>(HttpMethod.Post, "/v1/powerpoint/inspect", request, cancellationToken);

    public Task<PowerPointEditResult> EditPowerPointAsync(
        PowerPointEditRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<PowerPointEditResult>(HttpMethod.Post, "/v1/powerpoint/edit", request, cancellationToken);

    public Task<PowerPointEditResult> GetPowerPointJobAsync(string jobId, CancellationToken cancellationToken) =>
        SendAsync<PowerPointEditResult>(HttpMethod.Get, $"/v1/powerpoint/jobs/{Uri.EscapeDataString(jobId)}", null, cancellationToken);

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
            var result = await response.Content.ReadFromJsonAsync<T>(OperatorJson.SerializerOptions, cancellationToken);
            return result ?? throw new OperatorFailureException(
                OperatorErrors.LockedDesktop("Desktop agent returned an empty response."));
        }

        var error = await response.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions, cancellationToken);
        if (error is not null)
        {
            throw new OperatorFailureException(error);
        }

        throw new OperatorFailureException(
            OperatorErrors.LockedDesktop($"Desktop agent returned HTTP {(int)response.StatusCode}."));
    }
}
