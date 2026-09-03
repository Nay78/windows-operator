using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Host.Api;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class HostOperatorEndpointsTests
{
    [Theory]
    [InlineData(ErrorCodes.AuthRunNotFound)]
    [InlineData(ErrorCodes.BrowserSessionNotFound)]
    [InlineData(ErrorCodes.WorkbenchSessionNotFound)]
    [InlineData(ErrorCodes.PowerPointSessionNotFound)]
    public void ResourceNotFound_MapsToHttpNotFound(string errorCode)
    {
        Assert.Equal(
            (int)HttpStatusCode.NotFound,
            HostOperatorHttp.MapStatusCode(errorCode));
    }

    [Fact]
    public async Task UnknownRoute_ReturnsTypedRouteNotFound()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().GetAsync("/v1/does-not-exist");

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorCodes.RouteNotFound,
            OperatorErrorCategory.NotFound,
            retryable: false);
    }

    [Fact]
    public async Task WrongMethod_ReturnsTypedMethodNotAllowed()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().PostAsync("/v1/health", content: null);

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            ErrorCodes.MethodNotAllowed,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task MalformedJson_ReturnsTypedInvalidRequest()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));
        var client = app.GetTestClient();
        using var content = new StringContent(
            "{\"keys\":",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/v1/input/hotkey", content);

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task InvalidScreenshotFormat_ReturnsTypedInvalidRequest()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/windows/42/screenshot?format=gif");

        await AssertTypedErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest,
            OperatorErrorCategory.Validation,
            retryable: false);
    }

    [Fact]
    public async Task UnexpectedEndpointException_ReturnsSafeTypedInternalError()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/health");

        var error = await AssertTypedErrorAsync(
            response,
            HttpStatusCode.InternalServerError,
            ErrorCodes.InternalError,
            OperatorErrorCategory.Internal,
            retryable: true);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(nameof(NotSupportedException), body, StringComparison.Ordinal);
        Assert.DoesNotContain("HostOperatorEndpointsTests", body, StringComparison.Ordinal);
        Assert.Equal("Unhandled endpoint exception.", error.Details!["detail"]);
    }

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
    public async Task PowerAutomateMcpRoutes_MapRequestsAndResponses()
    {
        var powerAutomate = new FakePowerAutomateMcpService();
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
            powerAutomateMcp: powerAutomate);
        var client = app.GetTestClient();

        var status = await client.GetFromJsonAsync<PowerAutomateMcpStatusResult>(
            "/v1/power-automate/mcp/status",
            OperatorJson.SerializerOptions);
        var startResponse = await client.PostAsJsonAsync(
            "/v1/power-automate/mcp/start",
            new PowerAutomateMcpStartRequest { BridgePort = 17373, DryRun = true },
            OperatorJson.SerializerOptions);
        var edgeResponse = await client.PostAsJsonAsync(
            "/v1/power-automate/mcp/edge",
            new PowerAutomateMcpEdgeRequest
            {
                DryRun = true,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            OperatorJson.SerializerOptions);
        var cleanupResponse = await client.PostAsync(
            "/v1/power-automate/mcp/edge/cleanup",
            content: null);
        var readResponse = await client.PostAsJsonAsync(
            "/v1/power-automate/mcp/flows/read",
            new PowerAutomateMcpFlowReadRequest { FlowId = "flow-1" },
            OperatorJson.SerializerOptions);
        var updateResponse = await client.PostAsJsonAsync(
            "/v1/power-automate/mcp/flows/update",
            new PowerAutomateMcpFlowUpdateRequest
            {
                FlowId = "flow-1",
                FlowJson = "{\"connectionReferences\":{},\"definition\":{}}",
                DryRun = true,
            },
            OperatorJson.SerializerOptions);

        Assert.NotNull(status);
        Assert.True(status!.BridgeHealthy);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edgeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cleanupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(17373, powerAutomate.LastStartRequest!.BridgePort);
        Assert.True(powerAutomate.LastStartRequest.DryRun);
        Assert.NotNull(powerAutomate.LastEdgeRequest);
        Assert.Equal(BrowserEdgeProfileMode.Temp, powerAutomate.LastEdgeRequest.ProfileMode);
        Assert.Equal(1, powerAutomate.CleanupCalls);
        Assert.Equal("flow-1", powerAutomate.LastReadRequest!.FlowId);
        Assert.Equal("flow-1", powerAutomate.LastUpdateRequest!.FlowId);
        Assert.True(powerAutomate.LastUpdateRequest.DryRun);
    }

    [Fact]
    public async Task CapabilitiesRoute_ReturnsContractAndFeatures()
    {
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
        }));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CapabilitiesResult>(OperatorJson.SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal("0.1.0", result!.ContractVersion);
        Assert.Equal("1.0.0+abcdef123456", result.Build.InformationalVersion);
        Assert.Equal("1.0.0.0", result.Build.AssemblyVersion);
        Assert.Equal("abcdef123456", result.Build.SourceRevision);
        Assert.Equal("headless-host", result.Host.RuntimeMode);
        Assert.True(result.Features["powerpoint.online.update"].Available);
        Assert.True(result.Features["mail.outlook.download"].Available);
        Assert.True(result.Features["power-automate.mcp"].Available);
        Assert.Equal("diagnostic", result.Features["power-automate.mcp"].Surface);
    }

    [Fact]
    public async Task ArtifactRoutes_ListAndFetchRunArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-operator-host-artifacts", Guid.NewGuid().ToString("N"));
        try
        {
            var runId = "external-smoke";
            var artifactRoot = Path.Combine(root, "runs", runId, "screenshots");
            Directory.CreateDirectory(artifactRoot);
            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "proof.txt"), "artifact proof");

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
                artifacts: new ExchangeArtifactService(Options.Create(new WorkbenchOptions
                {
                    ExchangeRoot = root,
                })));
            var client = app.GetTestClient();

            var list = await client.GetFromJsonAsync<ArtifactListResult>(
                $"/v1/runs/{runId}/artifacts",
                OperatorJson.SerializerOptions);

            Assert.NotNull(list);
            var artifact = Assert.Single(list!.Artifacts);
            Assert.Equal("text/plain", artifact.MediaType);
            Assert.Equal(14, artifact.Bytes);
            Assert.StartsWith("/v1/artifacts/", artifact.Href, StringComparison.Ordinal);

            var response = await client.GetAsync(artifact.Href);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("artifact proof", await response.Content.ReadAsStringAsync());
            Assert.NotNull(response.Headers.ETag);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ArtifactRoute_NotFound_ReturnsBranchableOperatorError()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-operator-host-artifacts", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
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
                artifacts: new ExchangeArtifactService(Options.Create(new WorkbenchOptions
                {
                    ExchangeRoot = root,
                })));
            var client = app.GetTestClient();

            var response = await client.GetAsync("/v1/artifacts/%2A%2A%2A");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
            Assert.NotNull(error);
            Assert.Equal("artifact_not_found", error!.Code);
            Assert.Equal(OperatorErrorCategory.NotFound, error.Category);
            Assert.False(error.Retryable);
            Assert.False(string.IsNullOrWhiteSpace(error.CorrelationId));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void OpenApi_IncludesPowerPointOnlineUpdatesPath()
    {
        var json = JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions);

        Assert.Contains("\"/openapi.json\"", json);
        Assert.Contains("\"getOpenApiDocument\"", json);
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
        Assert.Contains("\"/v1/power-automate/mcp/status\"", json);
        Assert.Contains("\"getPowerAutomateMcpStatus\"", json);
        Assert.Contains("\"/v1/power-automate/mcp/start\"", json);
        Assert.Contains("\"startPowerAutomateMcpBridge\"", json);
        Assert.Contains("\"/v1/power-automate/mcp/edge\"", json);
        Assert.Contains("\"openPowerAutomateMcpEdge\"", json);
        Assert.Contains("\"/v1/power-automate/mcp/edge/cleanup\"", json);
        Assert.Contains("\"cleanupPowerAutomateMcpEdge\"", json);
        Assert.Contains("\"/v1/power-automate/mcp/flows/read\"", json);
        Assert.Contains("\"readPowerAutomateMcpFlow\"", json);
        Assert.Contains("\"/v1/power-automate/mcp/flows/update\"", json);
        Assert.Contains("\"updatePowerAutomateMcpFlow\"", json);
        Assert.Contains("\"/v1/capabilities\"", json);
        Assert.Contains("\"getCapabilities\"", json);
        Assert.Contains("\"/v1/artifacts/{artifactId}\"", json);
        Assert.Contains("\"getArtifact\"", json);
        Assert.Contains("\"/v1/runs/{runId}/artifacts\"", json);
        Assert.Contains("\"listRunArtifacts\"", json);
    }

    [Fact]
    public async Task OneDriveConfigRoute_MapsToHostFacade()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().GetAsync("/v1/files/onedrive/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OneDriveConfigResult>(OperatorJson.SerializerOptions);
        Assert.NotNull(result);
        Assert.Equal("etag-onedrive-test", result!.ETag);
        Assert.Contains("geosupport", result.Config.Roots.Keys);
    }

    [Theory]
    [InlineData(true, 2, "clearConfiguration is not supported")]
    [InlineData(false, -1, "targetSessionId must be zero")]
    public async Task OneDriveRuntimeRecovery_RejectsUnsafeRequestShape(
        bool clearConfiguration,
        int targetSessionId,
        string expectedDetail)
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/v1/files/onedrive/runtime/recover",
            new OneDriveConfigurationRecoveryRequest
            {
                ClearConfiguration = clearConfiguration,
                TargetSessionId = targetSessionId,
            },
            OperatorJson.SerializerOptions);

        var error = await AssertTypedErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest,
            OperatorErrorCategory.Validation,
            retryable: false);
        Assert.Contains(expectedDetail, error!.Details!["detail"]);
    }

    [Fact]
    public void OneDriveRuntimeRecovery_IsEnabledOnlyForExactVm()
    {
        Assert.True(OneDriveConfigurationRecoveryService.IsRecoveryEnabled(
            "WIN-UUKQS009K4J",
            "WIN-UUKQS009K4J"));
        Assert.False(OneDriveConfigurationRecoveryService.IsRecoveryEnabled(
            "LEGION",
            "LEGION"));
        Assert.False(OneDriveConfigurationRecoveryService.IsRecoveryEnabled(
            "OTHER-WINDOWS",
            "WIN-UUKQS009K4J"));
    }

    [Theory]
    [InlineData("WIN-UUKQS009K4J", "WIN-UUKQS009K4J", 2, "Administrator", "disconnected", 2, true)]
    [InlineData("WIN-UUKQS009K4J", "WIN-UUKQS009K4J", 0, "Administrator", "disconnected", 2, false)]
    [InlineData("WIN-UUKQS009K4J", "WIN-UUKQS009K4J", 2, "Administrator", "active", 2, false)]
    [InlineData("WIN-UUKQS009K4J", "WIN-UUKQS009K4J", 2, "OtherUser", "disconnected", 2, false)]
    [InlineData("LEGION", "LEGION", 2, "Administrator", "disconnected", 2, false)]
    [InlineData("WIN-UUKQS009K4J", "WIN-UUKQS009K4J", 2, "Administrator", "disconnected", 1, false)]
    public void OneDriveRuntimeRecovery_TransfersOnlyAllowlistedDisconnectedAdministratorSession(
        string computerName,
        string allowedComputer,
        int sessionId,
        string userName,
        string sessionState,
        ushort protocol,
        bool expected)
    {
        Assert.Equal(expected, OneDriveConfigurationRecoveryService.ShouldTransferDisconnectedSession(
            computerName,
            allowedComputer,
            sessionId,
            userName,
            sessionState,
            protocol));
    }

    [Theory]
    [InlineData("Administrator", "active", 2, true)]
    [InlineData("Administrator", "disconnected", 2, false)]
    [InlineData("Administrator", "active", 0, true)]
    [InlineData("OtherUser", "active", 2, false)]
    public void OneDriveRuntimeRecovery_RequiresActiveAdministratorDesktopSession(
        string userName,
        string sessionState,
        ushort protocol,
        bool expected)
    {
        Assert.Equal(expected, OneDriveConfigurationRecoveryService.IsTargetSessionEligible(
            userName,
            sessionState,
            protocol));
    }

    [Theory]
    [InlineData(1, 2, @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", true)]
    [InlineData(2, 2, @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", false)]
    [InlineData(1, 2, null, @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", false)]
    [InlineData(1, 2, @"C:\Other\OneDrive.exe", @"C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\OneDrive.exe", false)]
    public void OneDriveRuntimeRecovery_StopsOnlyVerifiedStaleTargetUserRuntime(
        int processSessionId,
        int targetSessionId,
        string? processPath,
        string expectedPath,
        bool expected)
    {
        Assert.Equal(expected, OneDriveConfigurationRecoveryService.ShouldStopStaleProcess(
            processSessionId,
            targetSessionId,
            processPath,
            expectedPath));
    }

    [Theory]
    [InlineData(1, 2, "dotnet", true)]
    [InlineData(1, 2, "WindowsOperator.Agent", true)]
    [InlineData(2, 2, "dotnet", false)]
    [InlineData(1, 2, "other-process", false)]
    public void OneDriveRuntimeRecovery_MigratesOnlyVerifiedWrongSessionAgentListener(
        int processSessionId,
        int targetSessionId,
        string processName,
        bool expected)
    {
        Assert.Equal(expected, OneDriveConfigurationRecoveryService.IsDesktopAgentListenerEligibleForStop(
            processSessionId,
            targetSessionId,
            processName));
    }

    [Fact]
    public void OneDriveRuntimeSupervisorState_SurvivesStoreRestartAndBacksOffFailures()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var statePath = Path.Combine(stateRoot.FullName, "onedrive-runtime.json");
        try
        {
            var first = new OneDriveRuntimeStateStore(statePath);
            first.BeginAttempt("WIN-UUKQS009K4J", true, 2);
            first.BeginAttempt("WIN-UUKQS009K4J", true);
            Assert.Equal(2, first.Read()!.TargetSessionId);
            first.RecordFailure(OperatorErrors.OneDriveUnavailable(
                "target_rdp_session_not_ready;session=2;state=disconnected",
                OneDriveConfigurationRecoveryService.BuildRuntimeEvidence(
                    "target_rdp_session_not_ready;session=2;state=disconnected",
                    "WIN-UUKQS009K4J",
                    "WIN-UUKQS009K4J",
                    "Administrator",
                    "disconnected",
                    2,
                    (321, 2))));

            var second = new OneDriveRuntimeStateStore(statePath);
            var restored = second.Read();

            Assert.NotNull(restored);
            Assert.Equal("waiting_for_session", restored!.State);
            Assert.Equal("disconnected", restored.SessionState);
            Assert.Equal(2, restored.AttemptCount);
            Assert.Equal(1, restored.ConsecutiveFailureCount);
            Assert.True(restored.NextAttemptAtUtc > restored.LastAttemptAtUtc);
            Assert.False(second.ShouldAttempt(restored.ObservedAtUtc));
        }
        finally
        {
            stateRoot.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("OtherUser", "active", 2)]
    [InlineData("Administrator", "active", 1)]
    public void OneDriveRuntimeRecovery_ReportsActualIneligibleSessionEvidence(
        string userName,
        string sessionState,
        ushort protocol)
    {
        var runtime = OneDriveConfigurationRecoveryService.BuildRuntimeEvidence(
            $"target_rdp_session_not_ready;session=2;user={userName};state={sessionState};protocol={protocol}",
            "WIN-UUKQS009K4J",
            "WIN-UUKQS009K4J",
            userName,
            sessionState,
            protocol,
            (1234, 2));
        var error = OperatorErrors.OneDriveUnavailable("target_rdp_session_not_ready", runtime);

        Assert.Equal(userName, runtime.InteractiveUser);
        Assert.Equal(sessionState, runtime.InteractiveSessionState);
        Assert.Equal(protocol, runtime.InteractiveSessionProtocol);
        Assert.Null(runtime.ActiveInteractiveSessionId);
        Assert.Equal(userName, error.Details!["interactiveUser"]);
        Assert.Equal(sessionState, error.Details["interactiveSessionState"]);
        Assert.Equal(protocol.ToString(), error.Details["interactiveSessionProtocol"]);
        Assert.Equal("Open the Administrator desktop session on the allowlisted VM, then retry.", error.Remediation);
    }

    [Fact]
    public void OneDriveRuntimeRecovery_ReportsConsoleTransferFailureEvidence()
    {
        var runtime = OneDriveConfigurationRecoveryService.BuildRuntimeEvidence(
            "target_rdp_session_console_transfer_failed;session=2;exitCode=1",
            "WIN-UUKQS009K4J",
            "WIN-UUKQS009K4J",
            "Administrator",
            "disconnected",
            2,
            (1234, 2),
            2);
        var error = OperatorErrors.OneDriveUnavailable(
            "target_rdp_session_console_transfer_failed;session=2;exitCode=1",
            runtime);

        Assert.Equal(ErrorCodes.OneDriveUnavailable, error.Code);
        Assert.Equal("target_rdp_session_console_transfer_failed", runtime.ProviderReason);
        Assert.Equal("2", error.Details!["configuredSessionId"]);
        Assert.Equal("operator_retry_administrator_console_transfer_2", error.Details["actions"]);
    }

    [Fact]
    public async Task OneDriveRuntimeRecovery_AcceptsNonClearingShapeBeforeMachineGate()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/v1/files/onedrive/runtime/recover",
            new OneDriveConfigurationRecoveryRequest
            {
                ClearConfiguration = false,
                TargetSessionId = 0,
            },
            OperatorJson.SerializerOptions);

        var error = await AssertTypedErrorAsync(
            response,
            HttpStatusCode.Locked,
            ErrorCodes.OneDriveUnavailable,
            OperatorErrorCategory.Unavailable,
            retryable: true);
        Assert.Contains("onedrive_recovery_denied", error!.Details!["detail"]);
        Assert.Equal("false", error.Details["recoveryAllowed"]);
        Assert.Equal("false", error.Details["authenticationRequired"]);
        Assert.Equal("operator_inspect_onedrive_runtime", error.Details["actions"]);
    }

    [Fact]
    public async Task OneDriveRelease_ReturnsAcceptedWhileReleaseIsPending()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        var response = await app.GetTestClient().PostAsync("/v1/files/onedrive/leases/od-pending/release", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/v1/files/onedrive/leases/od-pending", response.Headers.Location?.OriginalString);
        var result = await response.Content.ReadFromJsonAsync<OneDriveLeaseResult>(OperatorJson.SerializerOptions);
        Assert.Equal(OneDriveLeaseState.Releasing, result!.State);
    }

    [Fact]
    public async Task OneDriveDiagnosticNamespace_ListsExpectedPublicSurface()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));

        using var document = await GetJsonDocumentAsync(
            app.GetTestClient(),
            "/openapi/namespaces/files.onedrive.json?surface=diagnostic");
        var paths = document.RootElement.GetProperty("paths");
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["/v1/files/onedrive/list"] = ["post"],
            ["/v1/files/onedrive/download"] = ["post"],
            ["/v1/files/onedrive/leases"] = ["post"],
            ["/v1/files/onedrive/leases/{leaseId}"] = ["get"],
            ["/v1/files/onedrive/leases/{leaseId}/renew"] = ["post"],
            ["/v1/files/onedrive/leases/{leaseId}/release"] = ["post"],
            ["/v1/files/onedrive/status"] = ["get"],
            ["/v1/files/onedrive/runtime/recover"] = ["post"],
            ["/v1/files/onedrive/config"] = ["get", "put"],
            ["/v1/files/onedrive/reclaims"] = ["post"],
            ["/v1/files/onedrive/reclaims/{runId}"] = ["get"],
        };

        Assert.Equal(expected.Keys.Order(), paths.EnumerateObject().Select(item => item.Name).Order());
        foreach (var (path, methods) in expected)
        {
            Assert.Equal(methods.Order(), paths.GetProperty(path).EnumerateObject().Select(item => item.Name).Order());
        }
    }

    [Fact]
    public void OpenApi_ClaimPowerPointJob_DocumentsEmptyQueue()
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/v1/powerpoint/jobs/claim")
            .GetProperty("post");

        Assert.Equal(
            "claimPowerPointJob",
            operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("204", out var noContent));
        Assert.Equal("No queued job is available.", noContent.GetProperty("description").GetString());
    }

    [Fact]
    public void OpenApi_Operations_ExposeKnownSurfaceMetadata()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var paths = document.RootElement.GetProperty("paths");
        var allowedSurfaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "stable",
            "diagnostic",
            "development",
        };
        var allowedNamespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "system",
            "desktop",
            "sessions",
            "uia",
            "input",
            "browser.edge",
            "auth.microsoft",
            "power-automate.mcp",
            "powerpoint.online",
            "powerpoint.jobs",
            "artifacts",
            "mail.outlook",
            "files.onedrive",
        };

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var surface = operation.Value.GetProperty("x-windows-operator-surface").GetString();
                Assert.NotNull(surface);
                Assert.Contains(surface!, allowedSurfaces);

                var @namespace = operation.Value.GetProperty("x-windows-operator-namespace").GetString();
                Assert.NotNull(@namespace);
                Assert.Contains(@namespace!, allowedNamespaces);

                var tags = operation.Value.GetProperty("tags");
                Assert.Equal(JsonValueKind.Array, tags.ValueKind);
                Assert.Equal(@namespace, Assert.Single(tags.EnumerateArray()).GetString());
            }
        }

        Assert.Equal(
            "stable",
            paths.GetProperty("/v1/windows").GetProperty("get").GetProperty("x-windows-operator-surface").GetString());
        Assert.Equal(
            "desktop",
            paths.GetProperty("/v1/windows").GetProperty("get").GetProperty("x-windows-operator-namespace").GetString());
        Assert.Equal(
            "stable",
            paths.GetProperty("/v1/health").GetProperty("get").GetProperty("x-windows-operator-surface").GetString());
        Assert.Equal(
            "mail.outlook",
            paths.GetProperty("/v1/mail/messages/search").GetProperty("post").GetProperty("x-windows-operator-namespace").GetString());
        Assert.Equal(
            "stable",
            paths.GetProperty("/v1/mail/messages/search").GetProperty("post").GetProperty("x-windows-operator-surface").GetString());
        Assert.Equal(
            "powerpoint.online",
            paths
                .GetProperty("/v1/powerpoint/online/sessions/{sessionId}/addin/probe")
                .GetProperty("post")
                .GetProperty("x-windows-operator-namespace")
                .GetString());
        Assert.Equal(
            "diagnostic",
            paths
                .GetProperty("/v1/powerpoint/online/sessions/{sessionId}/addin/probe")
                .GetProperty("post")
                .GetProperty("x-windows-operator-surface")
                .GetString());
        Assert.Equal(
            "browser.edge",
            paths
                .GetProperty("/v1/dev/browser/edge/sessions/{sessionId}/eval")
                .GetProperty("post")
                .GetProperty("x-windows-operator-namespace")
                .GetString());
        Assert.Equal(
            "development",
            paths
                .GetProperty("/v1/dev/browser/edge/sessions/{sessionId}/eval")
                .GetProperty("post")
                .GetProperty("x-windows-operator-surface")
                .GetString());
        Assert.Equal(
            "power-automate.mcp",
            paths
                .GetProperty("/v1/power-automate/mcp/status")
                .GetProperty("get")
                .GetProperty("x-windows-operator-namespace")
                .GetString());
        Assert.Equal(
            "diagnostic",
            paths
                .GetProperty("/v1/power-automate/mcp/edge")
                .GetProperty("post")
                .GetProperty("x-windows-operator-surface")
                .GetString());
        Assert.Equal(
            "diagnostic",
            paths
                .GetProperty("/v1/power-automate/mcp/edge/cleanup")
                .GetProperty("post")
                .GetProperty("x-windows-operator-surface")
                .GetString());

        var statusProperties = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PowerAutomateMcpStatusResult")
            .GetProperty("properties");
        Assert.True(statusProperties.TryGetProperty("edgeSessionAlive", out _));
        Assert.True(statusProperties.TryGetProperty("edgeProcessId", out _));
        Assert.True(statusProperties.TryGetProperty("edgeHwnd", out _));
        Assert.True(statusProperties.TryGetProperty("edgeLastUsedAtUtc", out _));
        Assert.True(statusProperties.TryGetProperty("edgeLeaseExpiresAtUtc", out _));
        Assert.True(statusProperties.TryGetProperty("edgeIdleTtlSeconds", out _));
    }

    [Fact]
    public async Task OpenApi_Operations_MatchRuntimeEndpointMethodsAndRoutes()
    {
        await using var app = await CreateAppAsync(new FakeUpdateService(null!));
        var runtimeOperations = app.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ??
                    Array.Empty<string>();
                var route = NormalizeRoute(endpoint.RoutePattern.RawText!);
                return methods.Select(method => $"{method.ToUpperInvariant()} {route}");
            })
            .ToHashSet(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var contractOperations = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "patch" or "delete")
                .Select(operation =>
                    $"{operation.Name.ToUpperInvariant()} {NormalizeRoute(path.Name)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            contractOperations.OrderBy(operation => operation, StringComparer.Ordinal),
            runtimeOperations.OrderBy(operation => operation, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OpenApiNamespaceRoutes_ReturnBoundedDocumentsAndErrors()
    {
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
        }));
        var client = app.GetTestClient();

        var discovery = await client.GetFromJsonAsync<OpenApiNamespaceDiscoveryResult>(
            "/openapi/namespaces",
            OperatorJson.SerializerOptions);

        Assert.NotNull(discovery);
        Assert.Equal("0.1.0", discovery!.ContractVersion);
        var mailNamespace = Assert.Single(discovery.Namespaces, item => item.Name == "mail.outlook");
        Assert.Equal("/openapi/namespaces/mail.outlook.json", mailNamespace.Href);
        Assert.Equal(new[] { "stable" }, mailNamespace.Surfaces);
        Assert.Equal(5, mailNamespace.PathCount);
        Assert.Equal(5, mailNamespace.OperationCount);
        var powerAutomateNamespace = Assert.Single(discovery.Namespaces, item => item.Name == "power-automate.mcp");
        Assert.Equal("/openapi/namespaces/power-automate.mcp.json", powerAutomateNamespace.Href);
        Assert.Equal(new[] { "diagnostic" }, powerAutomateNamespace.Surfaces);
        Assert.Equal(6, powerAutomateNamespace.PathCount);
        Assert.Equal(6, powerAutomateNamespace.OperationCount);

        using var mail = await GetJsonDocumentAsync(client, "/openapi/namespaces/mail.outlook.json");
        var mailPaths = mail.RootElement.GetProperty("paths").EnumerateObject().Select(item => item.Name).ToArray();
        Assert.NotEmpty(mailPaths);
        Assert.All(mailPaths, path => Assert.StartsWith("/v1/mail/", path, StringComparison.Ordinal));

        using var powerAutomate = await GetJsonDocumentAsync(client, "/openapi/namespaces/power-automate.mcp.json?surface=diagnostic");
        var powerAutomatePaths = powerAutomate.RootElement.GetProperty("paths").EnumerateObject().Select(item => item.Name).ToArray();
        Assert.Equal(6, powerAutomatePaths.Length);
        Assert.All(powerAutomatePaths, path => Assert.StartsWith("/v1/power-automate/mcp/", path, StringComparison.Ordinal));

        using var powerpointStable = await GetJsonDocumentAsync(client, "/openapi/namespaces/powerpoint.online.json");
        Assert.True(powerpointStable.RootElement.GetProperty("paths").TryGetProperty("/v1/powerpoint/online/updates", out _));
        Assert.False(powerpointStable.RootElement.GetProperty("paths").TryGetProperty("/v1/powerpoint/online/sessions/{sessionId}/addin/probe", out _));
        Assert.False(powerpointStable.RootElement.GetProperty("paths").TryGetProperty("/v1/dev/powerpoint/online/sessions/{sessionId}/script", out _));

        using var powerpointDiagnostic = await GetJsonDocumentAsync(client, "/openapi/namespaces/powerpoint.online.json?surface=stable,diagnostic");
        Assert.True(powerpointDiagnostic.RootElement.GetProperty("paths").TryGetProperty("/v1/powerpoint/online/sessions/{sessionId}/addin/probe", out _));
        Assert.False(powerpointDiagnostic.RootElement.GetProperty("paths").TryGetProperty("/v1/dev/powerpoint/online/sessions/{sessionId}/script", out _));

        using var powerpointAll = await GetJsonDocumentAsync(client, "/openapi/namespaces/powerpoint.online.json?surface=all");
        Assert.True(powerpointAll.RootElement.GetProperty("paths").TryGetProperty("/v1/dev/powerpoint/online/sessions/{sessionId}/script", out _));

        var missing = await client.GetAsync("/openapi/namespaces/missing.json");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var missingError = await missing.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
        Assert.Equal("openapi_namespace_not_found", missingError!.Code);

        var invalidSurface = await client.GetAsync("/openapi/namespaces/mail.outlook.json?surface=unsafe");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidSurface.StatusCode);
        var invalidSurfaceError = await invalidSurface.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
        Assert.Equal("openapi_surface_invalid", invalidSurfaceError!.Code);

        var mixedAllSurface = await client.GetAsync("/openapi/namespaces/mail.outlook.json?surface=all,stable");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mixedAllSurface.StatusCode);
        var mixedAllSurfaceError = await mixedAllSurface.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
        Assert.Equal("openapi_surface_invalid", mixedAllSurfaceError!.Code);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IPowerPointOnlineUpdateService updates,
        IPowerPointOnlineService? powerpointOnline = null,
        IDevAutomationService? devAutomation = null,
        IPowerAutomateMcpService? powerAutomateMcp = null,
        IArtifactService? artifacts = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<JsonOptions>(options => OperatorJson.ConfigureHttp(options.SerializerOptions));
        builder.Services.AddSingleton(updates);
        builder.Services.AddSingleton<IOperatorFacade, UnusedFacade>();
        builder.Services.AddSingleton<IWorkbenchService, UnusedWorkbenchService>();
        builder.Services.AddSingleton<IPowerPointOnlineService>(powerpointOnline ?? new UnusedPowerPointOnlineService());
        builder.Services.AddSingleton<IDevAutomationService>(devAutomation ?? new UnusedDevAutomationService());
        builder.Services.AddSingleton<IPowerAutomateMcpService>(powerAutomateMcp ?? new UnusedPowerAutomateMcpService());
        builder.Services.AddSingleton<IPowerPointJobService, UnusedPowerPointJobService>();
        builder.Services.AddSingleton(artifacts ?? new UnusedArtifactService());
        builder.Services.AddSingleton<OneDriveConfigurationRecoveryService>();
        builder.Services.AddSingleton(new OneDriveRuntimeStateStore(
            Path.Combine(Path.GetTempPath(), $"host-endpoint-onedrive-{Guid.NewGuid():N}.json")));

        var app = builder.Build();
        app.UseHostOperatorErrorHandling();
        app.MapHostOperatorEndpoints();
        await app.StartAsync();
        return app;
    }

    private static string NormalizeRoute(string route)
    {
        var segments = route.Split('/', StringSplitOptions.None);
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index].StartsWith('{') && segments[index].EndsWith('}'))
            {
                var suffix = segments[index].EndsWith("}.json", StringComparison.Ordinal)
                    ? ".json"
                    : string.Empty;
                segments[index] = "{}" + suffix;
            }
            else if (segments[index].Contains('{', StringComparison.Ordinal))
            {
                var open = segments[index].IndexOf('{', StringComparison.Ordinal);
                var close = segments[index].IndexOf('}', open);
                if (close >= 0)
                {
                    segments[index] = segments[index][..open] + "{}" + segments[index][(close + 1)..];
                }
            }
        }

        return string.Join('/', segments);
    }

    private static async Task<OperatorError> AssertTypedErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        OperatorErrorCategory expectedCategory,
        bool retryable)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<OperatorError>(OperatorJson.SerializerOptions);
        Assert.NotNull(error);
        Assert.Equal(expectedCode, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
        Assert.False(string.IsNullOrWhiteSpace(error.CorrelationId));
        Assert.Equal(expectedCategory, error.Category);
        Assert.Equal(retryable, error.Retryable);
        return error;
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

    private static OneDriveConfigResult CreateOneDriveConfigResult() =>
        new()
        {
            Config = new OneDriveConfig(),
            ETag = "etag-onedrive-test",
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-06T12:00:00Z"),
        };

    private static async Task<JsonDocument> GetJsonDocumentAsync(HttpClient client, string requestUri)
    {
        var response = await client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

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
        public Task<CapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilitiesResult(
                "0.1.0",
                new RuntimeBuildIdentity("1.0.0+abcdef123456", "1.0.0.0", "abcdef123456"),
                new CapabilityHost("ok", "headless-host", "http://127.0.0.1:43117", "ok"),
                new Dictionary<string, CapabilityFeature>(StringComparer.Ordinal)
                {
                    ["powerpoint.online.update"] = new(true, "stable"),
                    ["mail.outlook.download"] = new(true, "stable"),
                    ["power-automate.mcp"] = new(true, "diagnostic"),
                },
                DateTimeOffset.Parse("2026-07-06T12:00:00Z")));
        public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveLeaseResult> AcquireOneDriveLeaseAsync(OneDriveLeaseRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<OneDriveFileEntry>> ListOneDriveFilesAsync(OneDriveListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveLeaseStatusResult> GetOneDriveLeaseAsync(string leaseId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveLeaseResult> RenewOneDriveLeaseAsync(string leaseId, OneDriveLeaseRenewRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveLeaseResult> ReleaseOneDriveLeaseAsync(string leaseId, CancellationToken cancellationToken) =>
            Task.FromResult(new OneDriveLeaseResult
            {
                Success = false,
                LeaseId = leaseId,
                RootId = "test",
                RelativePath = "file.txt",
                State = OneDriveLeaseState.Releasing,
                CreatedAtUtc = DateTimeOffset.Parse("2026-08-07T00:00:00Z"),
                Actions = new[] { "release_started" },
            });
        public Task<OneDriveFilesOnDemandStatusResult> GetOneDriveStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveConfigResult> GetOneDriveConfigAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CreateOneDriveConfigResult());
        public Task<OneDriveConfigResult> UpdateOneDriveConfigAsync(OneDriveConfigUpdateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateOneDriveConfigResult() with { Config = request.Config });
        public Task<OneDriveReclaimResult> StartOneDriveReclaimAsync(OneDriveReclaimRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OneDriveReclaimResult> GetOneDriveReclaimAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakePowerAutomateMcpService : IPowerAutomateMcpService
    {
        public PowerAutomateMcpStartRequest? LastStartRequest { get; private set; }

        public PowerAutomateMcpEdgeRequest? LastEdgeRequest { get; private set; }

        public PowerAutomateMcpFlowReadRequest? LastReadRequest { get; private set; }

        public PowerAutomateMcpFlowUpdateRequest? LastUpdateRequest { get; private set; }

        public int CleanupCalls { get; private set; }

        public Task<PowerAutomateMcpStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PowerAutomateMcpStatusResult
            {
                Success = true,
                BridgeListening = true,
                BridgeHealthy = true,
                ContextAvailable = true,
                BridgeVersion = "0.4.1",
                EdgeSessionAlive = true,
                EdgeProcessId = 5678,
                EdgeHwnd = 4321,
                EdgeIdleTtlSeconds = 900,
                Actions = new[] { "status_observed" },
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-06T12:00:00Z"),
            });

        public Task<PowerAutomateMcpStartResult> StartBridgeAsync(PowerAutomateMcpStartRequest request, CancellationToken cancellationToken)
        {
            LastStartRequest = request;
            return Task.FromResult(new PowerAutomateMcpStartResult
            {
                Success = true,
                ProcessId = 1234,
                Status = new PowerAutomateMcpStatusResult
                {
                    Success = true,
                    BridgeListening = true,
                    BridgeHealthy = true,
                },
                Actions = new[] { "bridge_started" },
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-06T12:00:01Z"),
            });
        }

        public Task<PowerAutomateMcpEdgeResult> OpenEdgeAsync(PowerAutomateMcpEdgeRequest request, CancellationToken cancellationToken)
        {
            LastEdgeRequest = request;
            return Task.FromResult(new PowerAutomateMcpEdgeResult
            {
                Success = true,
                Url = request.Url,
                ProfileMode = request.ProfileMode,
                ProcessId = 5678,
                Hwnd = 4321,
                Alive = true,
                TtlSeconds = 900,
                Actions = new[] { "edge_opened" },
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-06T12:00:02Z"),
            });
        }

        public Task<PowerAutomateMcpEdgeCleanupResult> CleanupEdgeAsync(CancellationToken cancellationToken)
        {
            CleanupCalls++;
            return Task.FromResult(new PowerAutomateMcpEdgeCleanupResult
            {
                Success = true,
                Alive = false,
                ProcessId = 5678,
                Hwnd = 4321,
                TtlSeconds = 900,
                Actions = new[] { "edge_cleaned" },
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-06T12:00:03Z"),
            });
        }

        public Task<PowerAutomateMcpFlowReadResult> ReadFlowAsync(PowerAutomateMcpFlowReadRequest request, CancellationToken cancellationToken)
        {
            LastReadRequest = request;
            return Task.FromResult(new PowerAutomateMcpFlowReadResult
            {
                Success = true,
                EnvId = "env-1",
                FlowId = request.FlowId ?? "flow-1",
                DisplayName = "Flow",
                FlowJson = "{\"connectionReferences\":{},\"definition\":{}}",
                Source = "modern-api",
            });
        }

        public Task<PowerAutomateMcpFlowUpdateResult> UpdateFlowAsync(PowerAutomateMcpFlowUpdateRequest request, CancellationToken cancellationToken)
        {
            LastUpdateRequest = request;
            return Task.FromResult(new PowerAutomateMcpFlowUpdateResult
            {
                Success = true,
                Status = request.DryRun ? PowerAutomateMcpFlowUpdateStatus.DryRun : PowerAutomateMcpFlowUpdateStatus.Succeeded,
                DryRun = request.DryRun,
            });
        }
    }

    private sealed class UnusedPowerAutomateMcpService : IPowerAutomateMcpService
    {
        public Task<PowerAutomateMcpStatusResult> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerAutomateMcpStartResult> StartBridgeAsync(PowerAutomateMcpStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerAutomateMcpEdgeResult> OpenEdgeAsync(PowerAutomateMcpEdgeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerAutomateMcpEdgeCleanupResult> CleanupEdgeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerAutomateMcpFlowReadResult> ReadFlowAsync(PowerAutomateMcpFlowReadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerAutomateMcpFlowUpdateResult> UpdateFlowAsync(PowerAutomateMcpFlowUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class UnusedArtifactService : IArtifactService
    {
        public Task<ArtifactContent> GetArtifactAsync(string artifactId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ArtifactListResult> ListRunArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
