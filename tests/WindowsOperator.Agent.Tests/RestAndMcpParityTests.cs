using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Agent.Hosting;
using WindowsOperator.Agent.Tests.Fakes;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Mcp.Protocol;

namespace WindowsOperator.Agent.Tests;

public sealed class RestAndMcpParityTests
{
    [Fact]
    public async Task WindowList_RestAndMcp_ReturnSameContracts()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var rest = await client.GetFromJsonAsync<IReadOnlyList<WindowRef>>("/v1/windows", OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var mcpNode = await catalog.ExecuteToolAsync("window_list", new JsonObject(), CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<IReadOnlyList<WindowRef>>(mcpNode!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.NotNull(rest);
        Assert.NotNull(mcp);
        Assert.Single(rest);
        Assert.Single(mcp);
        Assert.Equal(rest[0].Hwnd, mcp[0].Hwnd);
        Assert.Equal(rest[0].ProcessId, mcp[0].ProcessId);
        Assert.Equal(rest[0].Title, mcp[0].Title);
        Assert.Equal(rest[0].ClassName, mcp[0].ClassName);
        Assert.Equal(rest[0].Bounds, mcp[0].Bounds);
        Assert.Equal(rest[0].DpiScale, mcp[0].DpiScale);
        Assert.Equal(rest[0].IsForeground, mcp[0].IsForeground);
        Assert.Equal(rest[0].IsMinimized, mcp[0].IsMinimized);
    }

    [Fact]
    public async Task Hotkey_RestAndMcp_UseSameResultShape()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new HotkeyRequest { Keys = new[] { "ctrl", "shift", "p" } };

        var response = await client.PostAsJsonAsync("/v1/input/hotkey", request, OperatorJson.SerializerOptions);
        var rest = await response.Content.ReadFromJsonAsync<ActionResult>(OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var node = await catalog.ExecuteToolAsync(
            "input_hotkey",
            JsonSerializer.SerializeToNode(request, OperatorJson.SerializerOptions)!.AsObject(),
            CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<ActionResult>(node!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.Equal(rest, mcp);
    }

    [Fact]
    public async Task BrowserEdgeSessionStart_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new BrowserEdgeSessionStartRequest
        {
            SessionId = "entra-session",
            StartUrl = "https://microsoft.com/devicelogin",
            ProfileMode = BrowserEdgeProfileMode.Work,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/browser/edge/session/start",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<BrowserEdgeSessionStateResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("entra-session", result.SessionId);
        Assert.True(result.IsAlive);
    }

    [Fact]
    public async Task DesktopForeground_RestEndpoint_ReturnsWindow()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var result = await client.GetFromJsonAsync<WindowRef>(
            "/v1/desktop/foreground",
            OperatorJson.SerializerOptions);

        Assert.NotNull(result);
        Assert.True(result!.IsForeground);
        Assert.Equal(101, result.Hwnd);
    }

    [Fact]
    public async Task DesktopScreenshot_RestEndpoint_ReturnsArtifactRef()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/desktop/screenshot",
            new DesktopScreenshotRequest { Target = "foreground", Label = "foreground" },
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<DesktopScreenshotResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("image/png", result.Artifact.MediaType);
        Assert.Equal("runs/workbench-test/screenshots/foreground.png", result.Artifact.RelativePath);
        Assert.DoesNotContain("base64", JsonSerializer.Serialize(result, OperatorJson.SerializerOptions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserEdgeOpenUrl_RestEndpoint_ReturnsStateAndOptionalScreenshot()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new BrowserEdgeOpenUrlRequest
        {
            Url = "https://example.com",
            SessionId = "example-session",
            Capture = true,
            Label = "edge-open",
        };

        var response = await client.PostAsJsonAsync(
            "/v1/browser/edge/open-url",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<BrowserEdgeOpenUrlResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("example-session", result.State.SessionId);
        Assert.Equal("https://example.com", result.State.Url);
        Assert.NotNull(result.Screenshot);
        Assert.Equal("runs/workbench-test/screenshots/edge-open.png", result.Screenshot!.Artifact.RelativePath);
    }

    [Fact]
    public async Task BrowserEdgeSessionScreenshot_RestEndpoint_ReturnsArtifactRef()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/browser/edge/session/example-session/screenshot",
            new DesktopScreenshotRequest { Label = "edge-session" },
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<DesktopScreenshotResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("runs/workbench-test/screenshots/edge-session.png", result.Artifact.RelativePath);
    }

    [Fact]
    public async Task BrowserEdgeSessionCleanup_RestEndpoint_ReturnsClosedState()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync("/v1/browser/edge/session/example-session/cleanup", null);
        var result = await response.Content.ReadFromJsonAsync<BrowserEdgeSessionStateResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.False(result!.IsAlive);
        Assert.Contains("session_window_closed", result.Actions);
    }

    [Fact]
    public async Task PowerPointOnlineSessionStart_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions",
            new PowerPointOnlineSessionStartRequest
            {
                DeckUrl = "https://example.sharepoint.com/deck.pptx?web=1",
                SessionId = "ppt-session",
                Capture = true,
            },
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.Equal("ppt-session", result!.SessionId);
        Assert.Equal(PowerPointOnlineSessionStatus.Ready, result.Status);
    }

    [Fact]
    public async Task PowerPointOnlineSessionRoutes_ReturnStateSlideSelectScreenshotAndCleanup()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var session = await client.GetFromJsonAsync<PowerPointOnlineSessionResult>(
            "/v1/powerpoint/online/sessions/ppt-session",
            OperatorJson.SerializerOptions);
        var slideSelectResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/slides/select",
            new PowerPointOnlineSlideSelectRequest { SlideNumber = 4, Capture = false, WaitSeconds = 0 },
            OperatorJson.SerializerOptions);
        var slideSelect = await slideSelectResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var saveWaitResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/save/wait",
            new PowerPointOnlineSaveWaitRequest { TimeoutSeconds = 3, PollSeconds = 1, Capture = false },
            OperatorJson.SerializerOptions);
        var saveWait = await saveWaitResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var prepareResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/template/prepare",
            new PowerPointOnlineTemplateRequest { Capture = true, WaitSeconds = 1, AllowDeckMutation = true, Label = "template-prepare" },
            OperatorJson.SerializerOptions);
        var prepare = await prepareResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var templateCleanupResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/template/cleanup",
            new PowerPointOnlineTemplateRequest { Capture = true, WaitSeconds = 1, AllowDeckMutation = true, Label = "template-cleanup" },
            OperatorJson.SerializerOptions);
        var templateCleanup = await templateCleanupResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var runPendingJobResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/addin/run-pending-job",
            new PowerPointOnlineAddInCommandRequest { Capture = true, WaitSeconds = 1, Label = "run-pending-job" },
            OperatorJson.SerializerOptions);
        var runPendingJob = await runPendingJobResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var screenshotResponse = await client.PostAsJsonAsync(
            "/v1/powerpoint/online/sessions/ppt-session/screenshot",
            new PowerPointOnlineSessionScreenshotRequest { Label = "ppt-online-shot" },
            OperatorJson.SerializerOptions);
        var screenshot = await screenshotResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);
        var cleanupResponse = await client.PostAsync("/v1/powerpoint/online/sessions/ppt-session/cleanup", null);
        var cleanup = await cleanupResponse.Content.ReadFromJsonAsync<PowerPointOnlineSessionResult>(OperatorJson.SerializerOptions);

        Assert.NotNull(session);
        Assert.Equal(PowerPointOnlineSessionStatus.Ready, session!.Status);
        slideSelectResponse.EnsureSuccessStatusCode();
        Assert.Contains("slide_selected:4", slideSelect!.Actions);
        saveWaitResponse.EnsureSuccessStatusCode();
        Assert.Equal("saved", saveWait!.SaveState);
        prepareResponse.EnsureSuccessStatusCode();
        Assert.Contains("template_prepare_click_dispatched", prepare!.Actions);
        Assert.Equal("runs/workbench-test/screenshots/template-prepare.png", Assert.Single(prepare.Evidence).Artifact.RelativePath);
        templateCleanupResponse.EnsureSuccessStatusCode();
        Assert.Contains("template_cleanup_click_dispatched", templateCleanup!.Actions);
        Assert.Equal("runs/workbench-test/screenshots/template-cleanup.png", Assert.Single(templateCleanup.Evidence).Artifact.RelativePath);
        runPendingJobResponse.EnsureSuccessStatusCode();
        Assert.Contains("addin_run_pending_job_click_dispatched", runPendingJob!.Actions);
        Assert.Equal("runs/workbench-test/screenshots/run-pending-job.png", Assert.Single(runPendingJob.Evidence).Artifact.RelativePath);
        screenshotResponse.EnsureSuccessStatusCode();
        Assert.Equal("runs/workbench-test/screenshots/ppt-online-shot.png", Assert.Single(screenshot!.Evidence).Artifact.RelativePath);
        cleanupResponse.EnsureSuccessStatusCode();
        Assert.Equal(PowerPointOnlineSessionStatus.Closed, cleanup!.Status);
    }

    [Fact]
    public async Task WorkbenchSessionRoutes_ReturnStateScreenshotAndCleanup()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var session = await client.GetFromJsonAsync<WorkbenchSessionResult>(
            "/v1/sessions/example-session",
            OperatorJson.SerializerOptions);
        var screenshotResponse = await client.PostAsJsonAsync(
            "/v1/sessions/example-session/screenshot",
            new DesktopScreenshotRequest { Label = "generic-session" },
            OperatorJson.SerializerOptions);
        var screenshot = await screenshotResponse.Content.ReadFromJsonAsync<DesktopScreenshotResult>(OperatorJson.SerializerOptions);
        var cleanupResponse = await client.PostAsync("/v1/sessions/example-session/cleanup", null);
        var cleanup = await cleanupResponse.Content.ReadFromJsonAsync<WorkbenchSessionCleanupResult>(OperatorJson.SerializerOptions);

        Assert.NotNull(session);
        Assert.Equal("browser.edge", session!.Kind);
        Assert.Equal("runs/workbench-test", session.ArtifactRoot.RelativePath);
        screenshotResponse.EnsureSuccessStatusCode();
        Assert.Equal("runs/workbench-test/screenshots/generic-session.png", screenshot!.Artifact.RelativePath);
        cleanupResponse.EnsureSuccessStatusCode();
        Assert.True(cleanup!.Success);
        Assert.Equal(1, cleanup.ClosedWindows);
    }

    [Fact]
    public async Task MicrosoftDeviceLogin_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new MicrosoftDeviceLoginRequest
        {
            DeviceCode = "ABCD-EFGH",
            DryRun = true,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/auth/microsoft/device-login",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MicrosoftDeviceLoginResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Contains("dry_run", result.Actions);
        Assert.Equal(MicrosoftDeviceLoginStatus.DryRun, result.Status);
        Assert.NotNull(result.RunId);
    }

    [Fact]
    public async Task MicrosoftAuthCleanup_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new MicrosoftAuthCleanupRequest
        {
            DryRun = true,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/auth/microsoft/cleanup",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MicrosoftAuthCleanupResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Contains("cleanup_dry_run", result.Actions);
        Assert.Equal(3, result.MatchedWindows);
    }

    [Fact]
    public async Task MicrosoftAuthorizeProbe_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new MicrosoftAuthorizeProbeRequest
        {
            AuthorizeUrl = "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
            DryRun = true,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/auth/microsoft/authorize-probe",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MicrosoftAuthorizeProbeResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Contains("dry_run", result.Actions);
        Assert.Equal(MicrosoftAuthorizeProbeStatus.DryRun, result.Status);
        Assert.NotNull(result.RunId);
    }

    [Fact]
    public async Task MicrosoftDeviceLoginStatus_RestAndMcp_UseSameResultShape()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var rest = await client.GetFromJsonAsync<MicrosoftDeviceLoginResult>(
            "/v1/auth/microsoft/device-login/status/fake-run",
            OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var node = await catalog.ExecuteToolAsync(
            "auth_microsoft_device_login_status",
            new JsonObject { ["runId"] = "fake-run" },
            CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<MicrosoftDeviceLoginResult>(node!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.Equal(
            JsonSerializer.Serialize(rest, OperatorJson.SerializerOptions),
            JsonSerializer.Serialize(mcp, OperatorJson.SerializerOptions));
        Assert.Equal(MicrosoftDeviceLoginStatus.Submitted, rest!.Status);
    }

    [Fact]
    public async Task MicrosoftAuthorizeProbeStatus_RestAndMcp_UseSameResultShape()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var rest = await client.GetFromJsonAsync<MicrosoftAuthorizeProbeResult>(
            "/v1/auth/microsoft/authorize-probe/status/fake-run",
            OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var node = await catalog.ExecuteToolAsync(
            "auth_microsoft_authorize_probe_status",
            new JsonObject { ["runId"] = "fake-run" },
            CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<MicrosoftAuthorizeProbeResult>(node!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.Equal(
            JsonSerializer.Serialize(rest, OperatorJson.SerializerOptions),
            JsonSerializer.Serialize(mcp, OperatorJson.SerializerOptions));
        Assert.Equal(MicrosoftAuthorizeProbeStatus.Opened, rest!.Status);
    }

    [Fact]
    public async Task MailFolders_RestEndpoint_ReturnsEnvelope()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/mail/folders",
            new MailListFoldersRequest(),
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MailFoldersResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.NotEmpty(result.Actions);
        Assert.Contains(result.Folders, folder => folder.Name == "Bandeja de entrada");
    }

    [Fact]
    public async Task MailLegacySyncRecoverAndGetFoldersRoutes_AreRemoved()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/v1/mail/folders")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/mail/sync", new { })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/mail/recover", new { })).StatusCode);
    }

    private static void ReplaceOperatorFacade(IServiceCollection services)
    {
        var existing = services.Single(descriptor => descriptor.ServiceType == typeof(IOperatorFacade));
        services.Remove(existing);
        services.AddSingleton<IOperatorFacade, FakeOperatorFacade>();

        var existingWorkbench = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkbenchService));
        services.Remove(existingWorkbench);
        services.AddSingleton<IWorkbenchService, FakeWorkbenchService>();

        var existingPowerPointOnline = services.Single(descriptor => descriptor.ServiceType == typeof(IPowerPointOnlineService));
        services.Remove(existingPowerPointOnline);
        services.AddSingleton<IPowerPointOnlineService, FakePowerPointOnlineService>();
    }
}
