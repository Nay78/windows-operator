using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Host.Api;

namespace WindowsOperator.Host.Tests;

public sealed class HostOperatorEndpointsTests
{
    [Fact]
    public async Task PowerPointOnlineUpdatesRoute_MapsRequestAndResponse()
    {
        var expected = new PowerPointOnlineUpdateResult
        {
            Success = true,
            Status = PowerPointOnlineUpdateStatus.Succeeded,
            SaveProofTier = PowerPointOnlineSaveProofTier.Tier2SavedIndicator,
            Session = CreateSession(),
            JobRecord = CreateJobRecord(),
            PhaseTimings = new PowerPointOnlineUpdatePhaseTimings
            {
                TotalMs = 4000,
                OpenSessionMs = 500,
                JobMs = 1200,
                SessionCleanupMs = 250,
            },
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "job_enqueued" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:05Z"),
        };

        await using var app = await CreateAppAsync(new FakeUpdateService(expected));
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/updates",
            new PowerPointOnlineUpdateRequest
            {
                SessionId = "ppt-session",
                Job = CreateJobRecord().Job,
                Capture = false,
            },
            OperatorJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PowerPointOnlineUpdateResult>(OperatorJson.SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result!.Status);
        Assert.Equal("job-1", result.JobRecord.JobId);
        Assert.Equal("ppt-session", result.Session.SessionId);
        Assert.Equal(4000, result.PhaseTimings!.TotalMs);
    }

    [Fact]
    public async Task PowerPointOnlineAddInProbeRoute_MapsRequestAndResponse()
    {
        var expected = new PowerPointOnlineAddInProbeResult
        {
            Success = true,
            Status = PowerPointOnlineAddInProbeStatus.Ready,
            Session = CreateSession(),
            AddInBaseUrl = "https://localhost:3003",
            HostReachable = true,
            TaskPaneUrl = "https://localhost:3003/taskpane.html",
            TaskPaneReachable = true,
            ManifestUrl = "https://localhost:3003/manifest.xml",
            ManifestReachable = true,
            ManifestId = "6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7",
            ManifestVersion = "1.0.0.0",
            ManifestDisplayName = "Windows Operator PowerPoint",
            ManifestSourceLocation = "https://localhost:3003/taskpane.html",
            TaskPaneVisible = true,
            CommandVisible = true,
            MatchedElements = Array.Empty<UiElementRef>(),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "addin_taskpane_probe_ok", "addin_manifest_probe_ok", "addin_host_probe_ok" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:04Z"),
        };

        await using var app = await CreateAppAsync(new FakeUpdateService(new PowerPointOnlineUpdateResult
        {
            Success = true,
            Status = PowerPointOnlineUpdateStatus.Succeeded,
            SaveProofTier = PowerPointOnlineSaveProofTier.Tier2SavedIndicator,
            Session = CreateSession(),
            JobRecord = CreateJobRecord(),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:05Z"),
        }), new FakePowerPointOnlineService(expected));
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/addin/probe",
            new PowerPointOnlineAddInProbeRequest
            {
                AddInBaseUrl = "https://localhost:3003",
                Capture = true,
                HostTimeoutSeconds = 10,
            },
            OperatorJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PowerPointOnlineAddInProbeResult>(OperatorJson.SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal(PowerPointOnlineAddInProbeStatus.Ready, result!.Status);
        Assert.True(result.HostReachable);
        Assert.Equal("ppt-session", result.Session.SessionId);
    }

    [Fact]
    public async Task DevAutomationRoutes_MapRequestAndResponse()
    {
        var expected = new DevScriptResult
        {
            Success = true,
            Status = DevScriptStatus.Succeeded,
            SessionId = "ppt-session",
            ScriptId = "ppt.dom.snapshot",
            Target = "powerpoint-page",
            ResultJson = "{\"ok\":true}",
            Actions = new[] { "dev_script_evaluated" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<string>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-04T10:00:00Z"),
            EvidencePath = "/host/runs/dev/result.json",
        };

        await using var app = await CreateAppAsync(
            new FakeUpdateService(new PowerPointOnlineUpdateResult
            {
                Success = true,
                Status = PowerPointOnlineUpdateStatus.Succeeded,
                SaveProofTier = PowerPointOnlineSaveProofTier.Tier2SavedIndicator,
                Session = CreateSession(),
                JobRecord = CreateJobRecord(),
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = Array.Empty<string>(),
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:05Z"),
            }),
            devAutomation: new FakeDevAutomationService(expected));
        var client = app.GetTestClient();

        var scriptResponse = await client.PostAsJsonAsync(
            "/v1/dev/powerpoint/online/sessions/ppt-session/script",
            new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
            OperatorJson.SerializerOptions);
        var scriptResult = await scriptResponse.Content.ReadFromJsonAsync<DevScriptResult>(OperatorJson.SerializerOptions);
        var evalResponse = await client.PostAsJsonAsync(
            "/v1/dev/browser/edge/sessions/ppt-session/eval",
            new BrowserEdgeDevEvalRequest { Source = "document.title", AllowUnsafeRawJs = true },
            OperatorJson.SerializerOptions);
        var evalResult = await evalResponse.Content.ReadFromJsonAsync<DevScriptResult>(OperatorJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Equal(DevScriptStatus.Succeeded, scriptResult!.Status);
        Assert.Equal(HttpStatusCode.OK, evalResponse.StatusCode);
        Assert.Equal(DevScriptStatus.Succeeded, evalResult!.Status);
    }

    [Fact]
    public void OpenApi_IncludesPowerPointOnlineUpdatesPath()
    {
        var json = JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions);

        Assert.Contains("\"/v1/powerpoint/online/updates\"", json);
        Assert.Contains("\"updatePowerPointOnlinePresentation\"", json);
        Assert.Contains("\"/v1/powerpoint/online/sessions/{sessionId}/addin/probe\"", json);
        Assert.Contains("\"probePowerPointOnlineAddIn\"", json);
        Assert.Contains("\"/v1/powerpoint/online/sessions/{sessionId}/save/wait\"", json);
        Assert.Contains("\"waitPowerPointOnlineSave\"", json);
        Assert.Contains("\"/v1/dev/powerpoint/online/sessions/{sessionId}/script\"", json);
        Assert.Contains("\"runPowerPointOnlineDevScript\"", json);
        Assert.Contains("\"/v1/dev/browser/edge/sessions/{sessionId}/eval\"", json);
        Assert.Contains("\"evaluateEdgeBrowserDevScript\"", json);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IPowerPointOnlineUpdateService updates,
        IPowerPointOnlineService? powerpointOnline = null,
        IDevAutomationService? devAutomation = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<JsonOptions>(options => OperatorJson.Configure(options.SerializerOptions));
        builder.Services.AddSingleton(updates);
        builder.Services.AddSingleton<IOperatorFacade, UnusedFacade>();
        builder.Services.AddSingleton<IWorkbenchService, UnusedWorkbenchService>();
        builder.Services.AddSingleton<IPowerPointOnlineService>(powerpointOnline ?? new UnusedPowerPointOnlineService());
        builder.Services.AddSingleton<IDevAutomationService>(devAutomation ?? new UnusedDevAutomationService());
        builder.Services.AddSingleton<IPowerPointJobService, UnusedPowerPointJobService>();

        var app = builder.Build();
        app.MapHostOperatorEndpoints();
        await app.StartAsync();
        return app;
    }

    private static PowerPointOnlineSessionResult CreateSession() =>
        new()
        {
            Success = true,
            SessionId = "ppt-session",
            Status = PowerPointOnlineSessionStatus.Ready,
            DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentTitle = "Deck - PowerPoint",
            BrowserSessionId = "edge-session",
            Hwnd = 42,
            ArtifactRoot = null,
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
        };

    private static PowerPointJobRecord CreateJobRecord() =>
        new()
        {
            JobId = "job-1",
            Status = "succeeded",
            Job = new PowerPointUpdateJob
            {
                JobId = "job-1",
                ExpectedDocumentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                RequestedBy = "test",
                CreatedAt = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
                Operations = new[]
                {
                    new PowerPointUpdateOperation
                    {
                        Kind = "replaceText",
                        TargetId = "summary-status",
                        Text = "Updated",
                        Mode = "plain",
                    },
                },
            },
            EnqueuedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:02Z"),
        };

    private sealed class FakeUpdateService : IPowerPointOnlineUpdateService
    {
        private readonly PowerPointOnlineUpdateResult _result;

        public FakeUpdateService(PowerPointOnlineUpdateResult result)
        {
            _result = result;
        }

        public Task<PowerPointOnlineUpdateResult> UpdateAsync(PowerPointOnlineUpdateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class FakePowerPointOnlineService : IPowerPointOnlineService
    {
        private readonly PowerPointOnlineAddInProbeResult _probeResult;

        public FakePowerPointOnlineService(PowerPointOnlineAddInProbeResult probeResult)
        {
            _probeResult = probeResult;
        }

        public Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(PowerPointOnlineSessionStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(string sessionId, PowerPointOnlineSlideSelectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(string sessionId, PowerPointOnlineAddInProbeRequest request, CancellationToken cancellationToken) => Task.FromResult(_probeResult);
        public Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(string sessionId, PowerPointOnlineSaveWaitRequest request, CancellationToken cancellationToken) => Task.FromResult(CreateSession() with { SaveState = "saved" });
        public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(string sessionId, PowerPointOnlineTemplateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(string sessionId, PowerPointOnlineTemplateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(string sessionId, PowerPointOnlineAddInCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(string sessionId, PowerPointOnlineSessionScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDevAutomationService : IDevAutomationService
    {
        private readonly DevScriptResult _result;

        public FakeDevAutomationService(DevScriptResult result)
        {
            _result = result;
        }

        public Task<DevScriptResult> EvaluateEdgeBrowserSessionAsync(string sessionId, BrowserEdgeDevEvalRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_result with { SessionId = sessionId, ScriptId = "raw.browser.eval" });

        public Task<DevScriptResult> RunPowerPointOnlineScriptAsync(string sessionId, PowerPointDevScriptRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_result with { SessionId = sessionId, ScriptId = request.ScriptId });
    }

    private sealed class UnusedFacade : IOperatorFacade
    {
        public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(string sessionId, BrowserEdgeSessionDomClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(string sessionId, BrowserEdgeSessionNavigateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(BrowserEdgeResetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(MicrosoftAuthorizeProbeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(MicrosoftDeviceLoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(BrowserEdgeSessionStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(MicrosoftAuthCleanupRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(string sessionId, BrowserEdgeSessionDomFillRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedWorkbenchService : IWorkbenchService
    {
        public Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(BrowserEdgeOpenUrlRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkbenchSessionResult> GetSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(string sessionId, DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(string sessionId, DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedPowerPointOnlineService : IPowerPointOnlineService
    {
        public Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(PowerPointOnlineSessionStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(string sessionId, PowerPointOnlineSlideSelectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(string sessionId, PowerPointOnlineAddInProbeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(string sessionId, PowerPointOnlineSaveWaitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(string sessionId, PowerPointOnlineTemplateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(string sessionId, PowerPointOnlineTemplateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(string sessionId, PowerPointOnlineAddInCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(string sessionId, PowerPointOnlineSessionScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedDevAutomationService : IDevAutomationService
    {
        public Task<DevScriptResult> EvaluateEdgeBrowserSessionAsync(string sessionId, BrowserEdgeDevEvalRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DevScriptResult> RunPowerPointOnlineScriptAsync(string sessionId, PowerPointDevScriptRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedPowerPointJobService : IPowerPointJobService
    {
        public Task<PowerPointUpdateJob?> ClaimNextAsync(PowerPointClaimJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointJobRecord> CompleteAsync(string jobId, PowerPointUpdateResult result, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointJobRecord> EnqueueAsync(PowerPointUpdateJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointJobRecord> FailAsync(string jobId, PowerPointUpdateError error, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointArtifactContent> GetArtifactAsync(string jobId, string artifactId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerPointJobRecord> GetAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
