using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WindowsOperator.Capture.Services;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Core.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void WindowRef_Serializes_WithRequiredFields()
    {
        var window = new WindowRef(
            42,
            84,
            "Notepad",
            "Notepad",
            new WindowBounds(10, 20, 640, 480),
            1.25,
            DateTimeOffset.Parse("2026-04-22T18:00:00Z"),
            true,
            false);

        var json = JsonSerializer.Serialize(window, OperatorJson.SerializerOptions);

        Assert.Contains("\"hwnd\":42", json);
        Assert.Contains("\"processId\":84", json);
        Assert.Contains("\"dpiScale\":1.25", json);
        Assert.Contains("\"capturedAtUtc\":\"2026-04-22T18:00:00+00:00\"", json);
    }

    [Fact]
    public void MicrosoftDeviceLoginResult_Serializes_StatusAsCamelCase()
    {
        var result = new MicrosoftDeviceLoginResult(
            true,
            "https://microsoft.com/devicelogin",
            false,
            new[] { "device_code_submitted" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:13:00Z"),
            "run-1",
            MicrosoftDeviceLoginStatus.NeedsUserAction,
            "browser_title_needs_user_action",
            "Sign in to your account - Microsoft Edge",
            DateTimeOffset.Parse("2026-04-26T20:13:01Z"),
            @"C:\state\result.json");

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"runId\":\"run-1\"", json);
        Assert.Contains("\"status\":\"needsUserAction\"", json);
        Assert.Contains("\"browserState\":\"browser_title_needs_user_action\"", json);
    }

    [Theory]
    [InlineData(MicrosoftDeviceLoginStatus.DryRun, true)]
    [InlineData(MicrosoftDeviceLoginStatus.BrowserAccepted, true)]
    [InlineData(MicrosoftDeviceLoginStatus.Submitted, false)]
    [InlineData(MicrosoftDeviceLoginStatus.NeedsUserAction, false)]
    [InlineData(MicrosoftDeviceLoginStatus.InvalidCode, false)]
    [InlineData(MicrosoftDeviceLoginStatus.Failed, false)]
    [InlineData(MicrosoftDeviceLoginStatus.TimedOut, false)]
    public void MicrosoftDeviceLoginOutcomes_OnlyAcceptedOrDryRunSucceed(
        MicrosoftDeviceLoginStatus status,
        bool expected) =>
        Assert.Equal(expected, MicrosoftDeviceLoginOutcomes.IsSuccess(status));

    [Fact]
    public void MicrosoftAuthorizeProbeResult_Serializes_StatusAsCamelCase()
    {
        var result = new MicrosoftAuthorizeProbeResult(
            true,
            "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
            false,
            new[] { "edge_opened", "observed_url" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-18T01:15:00Z"),
            "probe-1",
            MicrosoftAuthorizeProbeStatus.RedirectObserved,
            "redirect_code_observed",
            "Continue to app - Microsoft Edge",
            "https://localhost/callback?code=abc",
            "https://localhost",
            null,
            true,
            DateTimeOffset.Parse("2026-05-18T01:15:01Z"),
            @"C:\state\auth-probe\result.json");

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"runId\":\"probe-1\"", json);
        Assert.Contains("\"status\":\"redirectObserved\"", json);
        Assert.Contains("\"observedCodePresent\":true", json);
    }

    [Fact]
    public void MailMessageRef_Serializes_ModifiedTime()
    {
        var message = new MailMessageRef(
            "message-1",
            "mailbox/Alimentacion",
            "Daily report",
            DateTimeOffset.Parse("2026-05-17T18:00:00Z"),
            DateTimeOffset.Parse("2026-05-17T22:00:00Z"),
            1,
            new[] { new MailAttachmentRef(1, "report.pdf", ".pdf", 1234) });

        var json = JsonSerializer.Serialize(message, OperatorJson.SerializerOptions);

        Assert.Contains("\"receivedTime\":\"2026-05-17T18:00:00+00:00\"", json);
        Assert.Contains("\"modifiedTime\":\"2026-05-17T22:00:00+00:00\"", json);
    }

    [Fact]
    public void PowerPointOnlineSessionResult_Serializes_StatusAsCamelCase()
    {
        var result = new PowerPointOnlineSessionResult
        {
            Success = false,
            SessionId = "ppt-session",
            Status = PowerPointOnlineSessionStatus.BlockedOfficeError,
            DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentTitle = "PowerPoint",
            CurrentSlide = 4,
            SlideCount = 71,
            EditMode = "editing",
            SaveState = "saved",
            BrowserSessionId = "edge-session",
            Hwnd = 888,
            ArtifactRoot = new WorkbenchRunRef(
                "ppt-session",
                @"Z:\operator-exchange\runs\ppt-session",
                "runs/ppt-session",
                "/host-exchange/runs/ppt-session"),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "session_started" },
            Warnings = new[] { "office_banner_observed" },
            Errors = new[] { OperatorErrors.PowerPointUnavailable("office error banner") },
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"status\":\"blockedOfficeError\"", json);
        Assert.Contains("\"browserSessionId\":\"edge-session\"", json);
        Assert.Contains("\"currentSlide\":4", json);
        Assert.Contains("\"slideCount\":71", json);
        Assert.Contains("\"editMode\":\"editing\"", json);
        Assert.Contains("\"saveState\":\"saved\"", json);
    }

    [Fact]
    public void OperatorError_Serializes_BranchableFields()
    {
        var error = OperatorErrors.PowerPointValidationFailed("missing title") with
        {
            CorrelationId = "corr-1",
        };

        var json = JsonSerializer.Serialize(error, OperatorJson.SerializerOptions);
        var roundTrip = JsonSerializer.Deserialize<OperatorError>(json, OperatorJson.SerializerOptions);

        Assert.Contains("\"code\":\"powerpoint_validation_failed\"", json);
        Assert.Contains("\"retryable\":false", json);
        Assert.Contains("\"category\":\"validation\"", json);
        Assert.Contains("\"correlationId\":\"corr-1\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(OperatorErrorCategory.Validation, roundTrip!.Category);
        Assert.False(roundTrip.Retryable);
    }

    [Fact]
    public void ArtifactRef_UsesOpaqueId_ForRelativePath()
    {
        var artifact = ArtifactRef.Create(
            "runs/run-one/screenshots/final.jpg",
            "image/jpeg",
            123,
            "abc123",
            DateTimeOffset.Parse("2026-07-06T12:00:00Z"));

        Assert.NotEqual("runs/run-one/screenshots/final.jpg", artifact.ArtifactId);
        Assert.StartsWith("/v1/artifacts/", artifact.Href, StringComparison.Ordinal);
        Assert.True(ArtifactIds.TryGetRelativePath(artifact.ArtifactId, out var relativePath));
        Assert.Equal("runs/run-one/screenshots/final.jpg", relativePath);
    }

    [Fact]
    public void CapabilitiesResult_Serializes_FeatureMap()
    {
        var result = new CapabilitiesResult(
            "0.1.0",
            new RuntimeBuildIdentity("1.0.0+abcdef123456", "1.0.0.0", "abcdef123456"),
            new CapabilityHost("ok", "headless-host", "http://127.0.0.1:43117", "ok"),
            new Dictionary<string, CapabilityFeature>(StringComparer.Ordinal)
            {
                ["powerpoint.online.update"] = new(true, "stable"),
                ["mail.outlook.download"] = new(false, "stable", "Desktop Agent unavailable."),
            },
            DateTimeOffset.Parse("2026-07-06T12:00:00Z"));

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"contractVersion\":\"0.1.0\"", json);
        Assert.Contains("\"sourceRevision\":\"abcdef123456\"", json);
        Assert.Contains("\"runtimeMode\":\"headless-host\"", json);
        Assert.Contains("\"powerpoint.online.update\"", json);
        Assert.Contains("\"available\":true", json);
        Assert.Contains("\"reason\":\"Desktop Agent unavailable.\"", json);
    }

    [Fact]
    public void DevScriptResult_Serializes_StatusAsCamelCase()
    {
        var result = new DevScriptResult
        {
            Success = false,
            Status = DevScriptStatus.ResultTooLarge,
            SessionId = "ppt-session",
            ScriptId = "ppt.dom.snapshot",
            Target = "powerpoint-page",
            TargetUrl = "https://powerpoint.office.com/edit",
            ResultJson = "{\"ok\":true}",
            Actions = new[] { "dev_script_requested" },
            Warnings = new[] { "result_capped" },
            Errors = new[] { "Result exceeded cap." },
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-04T10:00:00Z"),
            EvidencePath = "/host/runs/dev/result.json",
            SourceSha256 = "abc123",
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"status\":\"resultTooLarge\"", json);
        Assert.Contains("\"scriptId\":\"ppt.dom.snapshot\"", json);
        Assert.Contains("\"sourceSha256\":\"abc123\"", json);
    }

    [Fact]
    public void PowerPointOnlineUpdateResult_Serializes_StatusAsCamelCase()
    {
        var result = new PowerPointOnlineUpdateResult
        {
            Success = false,
            Status = PowerPointOnlineUpdateStatus.VerificationFailed,
            SaveProofTier = PowerPointOnlineSaveProofTier.Tier3ReopenVisual,
            Session = new PowerPointOnlineSessionResult
            {
                Success = true,
                SessionId = "ppt-session",
                Status = PowerPointOnlineSessionStatus.Ready,
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentTitle = "PowerPoint",
                CurrentSlide = 4,
                SlideCount = 71,
                EditMode = "editing",
                SaveState = "saved",
                BrowserSessionId = "edge-session",
                Hwnd = 888,
                ArtifactRoot = null,
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = Array.Empty<string>(),
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
            },
            VerificationSession = new PowerPointOnlineSessionResult
            {
                Success = false,
                SessionId = "ppt-session-verification",
                Status = PowerPointOnlineSessionStatus.BlockedOfficeError,
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentTitle = "PowerPoint",
                CurrentSlide = 4,
                SlideCount = 71,
                EditMode = "editing",
                SaveState = "saved",
                BrowserSessionId = "edge-session",
                Hwnd = 999,
                ArtifactRoot = null,
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = Array.Empty<string>(),
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
            },
            JobRecord = new PowerPointJobRecord
            {
                JobId = "job-1",
                Status = "failed",
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
                Error = new PowerPointUpdateError("ADDIN_TIMEOUT", true, "timed out"),
                EnqueuedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
                UpdatedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:02Z"),
            },
            PhaseTimings = new PowerPointOnlineUpdatePhaseTimings
            {
                TotalMs = 12345,
                OpenSessionMs = 1500,
                AddInProbeMs = 1200,
                JobMs = 4200,
                SaveMs = 900,
                EvidenceMs = 600,
                VerificationReopenMs = 3100,
                SessionCleanupMs = 250,
            },
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "job_timed_out" },
            Warnings = Array.Empty<string>(),
            Errors = new[] { OperatorErrors.PowerPointUnavailable("timed out") },
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:03Z"),
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"status\":\"verificationFailed\"", json);
        Assert.Contains("\"saveProofTier\":\"tier3ReopenVisual\"", json);
        Assert.Contains("\"jobRecord\":", json);
        Assert.Contains("\"verificationSession\":", json);
        Assert.Contains("\"phaseTimings\":", json);
        Assert.Contains("\"verificationReopenMs\":3100", json);
    }

    [Fact]
    public void PowerPointUpdateJob_Serializes_ValidateOnly()
    {
        var job = new PowerPointUpdateJob
        {
            JobId = "job-validate",
            ExpectedDocumentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            DiscoverTargets = true,
            BindNamedTargets = true,
            ValidateOnly = true,
            RequestedBy = "test",
            CreatedAt = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "replaceText",
                    TargetId = "summary-status",
                    Mode = "plain",
                },
            },
        };

        var json = JsonSerializer.Serialize(job, OperatorJson.SerializerOptions);

        Assert.Contains("\"validateOnly\":true", json);
        Assert.Contains("\"discoverTargets\":true", json);
        Assert.Contains("\"bindNamedTargets\":true", json);
    }

    [Fact]
    public void PowerPointUpdateJob_Serializes_ValidateOnlyDefaultFalse()
    {
        var job = new PowerPointUpdateJob
        {
            JobId = "job-default",
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
        };

        var json = JsonSerializer.Serialize(job, OperatorJson.SerializerOptions);

        Assert.Contains("\"validateOnly\":false", json);
        Assert.Contains("\"discoverTargets\":false", json);
        Assert.Contains("\"bindNamedTargets\":false", json);
    }

    [Fact]
    public void PowerPointTargetResult_Serializes_InspectionFields()
    {
        var result = new PowerPointTargetResult(
            "summary-status",
            "replaceText",
            "skipped",
            null,
            true,
            true,
            "text",
            "Binding resolved.",
            "TARGET_SUMMARY_STATUS",
            "binding",
            true,
            true);

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"found\":true", json);
        Assert.Contains("\"editable\":true", json);
        Assert.Contains("\"type\":\"text\"", json);
        Assert.Contains("\"message\":\"Binding resolved.\"", json);
        Assert.Contains("\"shapeName\":\"TARGET_SUMMARY_STATUS\"", json);
        Assert.Contains("\"source\":\"binding\"", json);
        Assert.Contains("\"bound\":true", json);
        Assert.Contains("\"tagged\":true", json);
    }

    [Fact]
    public void PowerPointTableOperation_Serializes_TablePayload()
    {
        var job = new PowerPointUpdateJob
        {
            JobId = "job-table",
            ExpectedDocumentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            RequestedBy = "test",
            CreatedAt = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "replaceTableRange",
                    TargetId = "DATA_TABLE",
                    StartRowIndex = 1,
                    StartColumnIndex = 1,
                    Values = new[]
                    {
                        new[] { "42", "43" },
                        new[] { "98%", "99%" },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(job, OperatorJson.SerializerOptions);
        var roundTrip = JsonSerializer.Deserialize<PowerPointUpdateJob>(json, OperatorJson.SerializerOptions);

        Assert.Contains("\"kind\":\"replaceTableRange\"", json);
        Assert.Contains("\"targetId\":\"DATA_TABLE\"", json);
        Assert.Contains("\"startRowIndex\":1", json);
        Assert.Contains("\"startColumnIndex\":1", json);
        Assert.Contains("\"values\":[[\"42\",\"43\"],[\"98%\",\"99%\"]]", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("replaceTableRange", Assert.Single(roundTrip!.Operations).Kind);
        Assert.Equal("43", roundTrip.Operations[0].Values![0][1]);
    }

    [Fact]
    public void PowerPointTargetResult_Serializes_TableSnapshot()
    {
        var result = new PowerPointTargetResult(
            "DATA_TABLE",
            "readTable",
            "succeeded",
            Type: "table",
            Table: new PowerPointTableSnapshot(
                2,
                2,
                new[]
                {
                    new[] { "Metric", "Plan" },
                    new[] { "Tonnes", "42" },
                }));

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"operationKind\":\"readTable\"", json);
        Assert.Contains("\"type\":\"table\"", json);
        Assert.Contains("\"table\":", json);
        Assert.Contains("\"rowCount\":2", json);
        Assert.Contains("\"columnCount\":2", json);
        Assert.Contains("\"values\":[[\"Metric\",\"Plan\"],[\"Tonnes\",\"42\"]]", json);
    }

    [Fact]
    public void PowerPointGeometryOperations_Serialize()
    {
        var job = new PowerPointUpdateJob
        {
            JobId = "job-geometry",
            RequestedBy = "test",
            CreatedAt = DateTimeOffset.Parse("2026-07-09T12:00:00Z"),
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "setShapeBounds",
                    TargetId = "DATE_HIGHLIGHT_BOX",
                    Left = 120.5,
                    Top = 60,
                    Width = 48.25,
                    Height = 400,
                },
                new PowerPointUpdateOperation
                {
                    Kind = "findTableColumn",
                    TargetId = "DATA_TABLE",
                    RowIndex = 0,
                    Text = "08-jul",
                },
            },
        };

        var json = JsonSerializer.Serialize(job, OperatorJson.SerializerOptions);
        var roundTrip = JsonSerializer.Deserialize<PowerPointUpdateJob>(json, OperatorJson.SerializerOptions);

        Assert.Contains("\"kind\":\"setShapeBounds\"", json);
        Assert.Contains("\"left\":120.5", json);
        Assert.Contains("\"top\":60", json);
        Assert.Contains("\"width\":48.25", json);
        Assert.Contains("\"height\":400", json);
        Assert.Contains("\"kind\":\"findTableColumn\"", json);
        Assert.Contains("\"text\":\"08-jul\"", json);
        Assert.Equal(120.5, roundTrip!.Operations[0].Left);
        Assert.Equal("08-jul", roundTrip.Operations[1].Text);
    }

    [Fact]
    public void PowerPointTargetResult_Serializes_GeometryPayloads()
    {
        var result = new PowerPointTargetResult(
            "DATA_TABLE",
            "readTableGeometry",
            "succeeded",
            Type: "table",
            Table: new PowerPointTableSnapshot(
                1,
                2,
                new[]
                {
                    new[] { "07-jul", "08-jul" },
                },
                new PowerPointTableGeometry(
                    new PowerPointShapeBounds(10, 20, 200, 40),
                    new[]
                    {
                        new PowerPointTableGeometryColumn(0, 10, 100, 110),
                        new PowerPointTableGeometryColumn(1, 110, 100, 210),
                    },
                    new[]
                    {
                        new PowerPointTableGeometryRow(0, 20, 40, 60),
                    })),
            Bounds: new PowerPointShapeBounds(10, 20, 200, 40),
            TableMatch: new PowerPointTableMatch(0, 1, "08-jul"));

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"bounds\":{\"left\":10,\"top\":20,\"width\":200,\"height\":40}", json);
        Assert.Contains("\"geometry\":", json);
        Assert.Contains("\"columnIndex\":1", json);
        Assert.Contains("\"tableMatch\":{\"rowIndex\":0,\"columnIndex\":1,\"text\":\"08-jul\"}", json);
    }

    [Fact]
    public void PowerPointUpdateResult_Serializes_DiscoveredTargets()
    {
        var result = new PowerPointUpdateResult
        {
            JobId = "job-discover",
            Status = "succeeded",
            StartedAt = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
            Targets = Array.Empty<PowerPointTargetResult>(),
            DiscoveredTargets = new[]
            {
                new PowerPointDiscoveredTarget("TITLE_MAIN", true, "text", "Tagged binding.", "TARGET_TITLE_MAIN", "binding", true, true),
            },
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"discoveredTargets\":[", json);
        Assert.Contains("\"targetId\":\"TITLE_MAIN\"", json);
        Assert.Contains("\"editable\":true", json);
        Assert.Contains("\"type\":\"text\"", json);
        Assert.Contains("\"message\":\"Tagged binding.\"", json);
        Assert.Contains("\"shapeName\":\"TARGET_TITLE_MAIN\"", json);
        Assert.Contains("\"source\":\"binding\"", json);
        Assert.Contains("\"bound\":true", json);
        Assert.Contains("\"tagged\":true", json);
    }

    [Fact]
    public void PowerPointOnlineSaveWaitRequest_Serializes_Defaults()
    {
        var request = new PowerPointOnlineSaveWaitRequest();

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);

        Assert.Contains("\"timeoutSeconds\":30", json);
        Assert.Contains("\"pollSeconds\":1", json);
        Assert.Contains("\"capture\":false", json);
    }

    [Fact]
    public void PowerPointOnlineTemplateRequest_Serializes_MutationApprovalDefault()
    {
        var request = new PowerPointOnlineTemplateRequest();

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);

        Assert.Contains("\"waitSeconds\":2", json);
        Assert.Contains("\"allowDeckMutation\":false", json);
        Assert.Contains("\"namedOnly\":false", json);
    }

    [Fact]
    public void PowerPointOnlineAddInCommandRequest_Serializes_Defaults()
    {
        var request = new PowerPointOnlineAddInCommandRequest();

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);

        Assert.Contains("\"capture\":false", json);
        Assert.Contains("\"waitSeconds\":2", json);
    }

    [Fact]
    public void PowerPointOnlineUpdateRequest_Serializes_SaveWaitDefaults()
    {
        var request = new PowerPointOnlineUpdateRequest
        {
            SessionId = "ppt-session",
            Job = new PowerPointUpdateJob
            {
                JobId = "job-1",
                RequestedBy = "test",
                CreatedAt = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
                Operations = Array.Empty<PowerPointUpdateOperation>(),
            },
        };

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);

        Assert.Contains("\"saveTimeoutSeconds\":30", json);
        Assert.Contains("\"savePollSeconds\":1", json);
        Assert.Contains("\"verifyReopen\":false", json);
        Assert.Contains("\"reopenWaitSeconds\":30", json);
        Assert.Contains("\"prepareTemplate\":false", json);
        Assert.Contains("\"cleanupTemplate\":false", json);
        Assert.Contains("\"cleanupTemplateOnFailure\":true", json);
        Assert.Contains("\"templateWaitSeconds\":2", json);
        Assert.Contains("\"allowDeckMutation\":false", json);
        Assert.Contains("\"cleanupSession\":false", json);
    }

    [Fact]
    public void PowerPointOnlineUpdateRequest_Serializes_FinalMutationProofShape()
    {
        var request = new PowerPointOnlineUpdateRequest
        {
            DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            SessionId = "ppt-mutation-proof-20260704t0950z",
            Job = new PowerPointUpdateJob
            {
                JobId = "ppt-mutation-proof-20260704t0950z",
                DiscoverTargets = true,
                ValidateOnly = false,
                RequestedBy = "codex-live-mutation-proof",
                CreatedAt = DateTimeOffset.Parse("2026-07-04T09:50:00Z"),
                Operations = new[]
                {
                    new PowerPointUpdateOperation
                    {
                        Kind = "replaceText",
                        TargetId = "TITLE_MAIN",
                        Mode = "plain",
                        Text = "Windows Operator live edit proof 2026-07-04 09:50 UTC",
                    },
                },
            },
            EvidenceSlideNumber = 4,
            Capture = true,
            AllowDeckMutation = true,
            PrepareTemplate = true,
            CleanupTemplate = true,
            CleanupTemplateOnFailure = true,
            TemplateWaitSeconds = 2,
            VerifyReopen = true,
            ReopenWaitSeconds = 40,
            CleanupSession = true,
            OpenWaitSeconds = 40,
            JobTimeoutSeconds = 60,
            SaveTimeoutSeconds = 30,
            SavePollSeconds = 1,
        };

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);
        var roundTrip = JsonSerializer.Deserialize<PowerPointOnlineUpdateRequest>(json, OperatorJson.SerializerOptions);

        Assert.Contains("\"deckUrl\":\"https://tenant.sharepoint.com/sites/team/deck.pptx?web=1\"", json);
        Assert.Contains("\"sessionId\":\"ppt-mutation-proof-20260704t0950z\"", json);
        Assert.Contains("\"discoverTargets\":true", json);
        Assert.Contains("\"validateOnly\":false", json);
        Assert.Contains("\"kind\":\"replaceText\"", json);
        Assert.Contains("\"targetId\":\"TITLE_MAIN\"", json);
        Assert.Contains("\"evidenceSlideNumber\":4", json);
        Assert.Contains("\"allowDeckMutation\":true", json);
        Assert.Contains("\"prepareTemplate\":true", json);
        Assert.Contains("\"cleanupTemplate\":true", json);
        Assert.Contains("\"verifyReopen\":true", json);
        Assert.Contains("\"cleanupSession\":true", json);
        Assert.NotNull(roundTrip);
        Assert.True(roundTrip!.AllowDeckMutation);
        Assert.True(roundTrip.PrepareTemplate);
        Assert.True(roundTrip.CleanupTemplate);
        Assert.True(roundTrip.VerifyReopen);
        Assert.True(roundTrip.CleanupSession);
        Assert.Equal("TITLE_MAIN", Assert.Single(roundTrip.Job.Operations).TargetId);
    }

    [Fact]
    public void OperatorOpenApi_Exposes_PowerPointOnlineUpdateProofFields()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var root = document.RootElement;
        var updatePost = root
            .GetProperty("paths")
            .GetProperty("/v1/powerpoint/online/updates")
            .GetProperty("post");
        var runPendingJobPost = root
            .GetProperty("paths")
            .GetProperty("/v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job")
            .GetProperty("post");

        Assert.Equal("updatePowerPointOnlinePresentation", updatePost.GetProperty("operationId").GetString());
        Assert.Equal("runPowerPointOnlinePendingJob", runPendingJobPost.GetProperty("operationId").GetString());
        Assert.Equal(
            "#/components/schemas/PowerPointOnlineAddInCommandRequest",
            runPendingJobPost
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/PowerPointOnlineUpdateRequest",
            updatePost
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/PowerPointOnlineUpdateResult",
            updatePost
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertHasProperties(
            schemas.GetProperty("PowerPointOnlineAddInCommandRequest").GetProperty("properties"),
            "capture",
            "waitSeconds",
            "label");
        AssertHasProperties(
            schemas.GetProperty("PowerPointOnlineTemplateRequest").GetProperty("properties"),
            "capture",
            "waitSeconds",
            "allowDeckMutation",
            "namedOnly",
            "label");
        AssertHasProperties(
            schemas.GetProperty("PowerPointUpdateJob").GetProperty("properties"),
            "jobId",
            "expectedDocumentUrl",
            "discoverTargets",
            "bindNamedTargets",
            "validateOnly",
            "operations",
            "requestedBy",
            "createdAt");
        AssertHasProperties(
            schemas.GetProperty("PowerPointOnlineUpdateRequest").GetProperty("properties"),
            "deckUrl",
            "sessionId",
            "job",
            "evidenceSlideNumber",
            "capture",
            "allowDeckMutation",
            "prepareTemplate",
            "cleanupTemplate",
            "cleanupTemplateOnFailure",
            "templateWaitSeconds",
            "verifyReopen",
            "reopenWaitSeconds",
            "cleanupSession",
            "openWaitSeconds",
            "jobTimeoutSeconds",
            "pollSeconds",
            "saveTimeoutSeconds",
            "savePollSeconds");
        AssertHasProperties(
            schemas.GetProperty("PowerPointOnlineUpdateResult").GetProperty("properties"),
            "success",
            "status",
            "saveProofTier",
            "session",
            "verificationSession",
            "templatePreparationSession",
            "templateCleanupSession",
            "sessionCleanupSession",
            "jobRecord",
            "phaseTimings",
            "evidence",
            "actions",
            "warnings",
            "errors",
            "observedAtUtc");
        AssertHasProperties(
            schemas.GetProperty("PowerPointOnlineUpdatePhaseTimings").GetProperty("properties"),
            "totalMs",
            "openSessionMs",
            "addInProbeMs",
            "templatePreparationMs",
            "jobMs",
            "saveMs",
            "evidenceMs",
            "verificationReopenMs",
            "templateCleanupMs",
            "sessionCleanupMs");
        AssertHasProperties(
            schemas.GetProperty("PowerPointUpdateOperation").GetProperty("properties"),
            "kind",
            "targetId",
            "text",
            "mode",
            "allowEmpty",
            "artifact",
            "altText",
            "fit",
            "rowIndex",
            "columnIndex",
            "startRowIndex",
            "startColumnIndex",
            "values",
            "left",
            "top",
            "width",
            "height");
        AssertHasProperties(
            schemas.GetProperty("PowerPointTargetResult").GetProperty("properties"),
            "targetId",
            "operationKind",
            "status",
            "error",
            "found",
            "editable",
            "type",
            "message",
            "shapeName",
            "source",
            "bound",
            "tagged",
            "table",
            "bounds",
            "tableMatch");
        AssertHasProperties(
            schemas.GetProperty("PowerPointTableSnapshot").GetProperty("properties"),
            "rowCount",
            "columnCount",
            "values",
            "geometry");
        AssertHasProperties(
            schemas.GetProperty("PowerPointTableGeometry").GetProperty("properties"),
            "bounds",
            "columns",
            "rows");
        AssertHasProperties(
            schemas.GetProperty("PowerPointDiscoveredTarget").GetProperty("properties"),
            "targetId",
            "editable",
            "type",
            "message",
            "shapeName",
            "source",
            "bound",
            "tagged");
    }

    [Fact]
    public void OperatorOpenApi_Uses_ExplicitContractVersionSource()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));

        Assert.Equal(
            OperatorContractVersion.Value,
            document.RootElement.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public void OperatorOpenApi_Requires_DeclaredRequestMembers_AndCoreErrorFields()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        AssertRequiredProperties(schemas.GetProperty("HotkeyRequest"), "keys");
        AssertRequiredProperties(schemas.GetProperty("ScreenClickRequest"), "x", "y");
        AssertRequiredProperties(schemas.GetProperty("UiaClickRequest"), "query");
        AssertRequiredProperties(schemas.GetProperty("UiaTypeRequest"), "query", "text");
        AssertRequiredProperties(schemas.GetProperty("BrowserEdgeOpenUrlRequest"), "url");
        AssertRequiredProperties(schemas.GetProperty("PowerPointOnlineUpdateRequest"), "job");
        AssertRequiredProperties(schemas.GetProperty("OperatorError"), "code", "message", "remediation");

        Assert.DoesNotContain(
            "doubleClick",
            schemas.GetProperty("UiaClickRequest")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));

        var processId = schemas.GetProperty("WindowRef")
            .GetProperty("properties")
            .GetProperty("processId");
        Assert.Equal("integer", processId.GetProperty("type").GetString());
        Assert.Equal("int64", processId.GetProperty("format").GetString());
        Assert.Equal(0, processId.GetProperty("minimum").GetInt32());
    }

    [Fact]
    public void OperatorOpenApi_Projects_EveryRequestBody_AsInput()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var contractTypes = typeof(OperatorOpenApi).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace == typeof(OperatorOpenApi).Namespace)
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        var requestBodyCount = 0;

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("requestBody", out var requestBody))
                {
                    continue;
                }

                requestBodyCount++;
                var reference = requestBody
                    .GetProperty("content")
                    .GetProperty("application/json")
                    .GetProperty("schema")
                    .GetProperty("$ref")
                    .GetString()!;
                var schemaName = reference.Split('/')[^1];
                var typeName = schemaName.EndsWith("Input", StringComparison.Ordinal)
                    ? schemaName[..^"Input".Length]
                    : schemaName;

                Assert.True(
                    contractTypes.TryGetValue(typeName, out var contractType),
                    $"{operation.Value.GetProperty("operationId").GetString()} request schema '{schemaName}' has no contract type.");

                var expectedRequired = contractType!
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property =>
                        property.IsDefined(typeof(RequiredMemberAttribute), inherit: true) &&
                        !property.IsDefined(typeof(OperatorInternalAttribute), inherit: true))
                    .Select(JsonPropertyName)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var actualRequired = RequiredProperties(schemas.GetProperty(schemaName))
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                Assert.Equal(expectedRequired, actualRequired);
            }
        }

        Assert.Equal(39, requestBodyCount);
    }

    [Fact]
    public void PublicHttpContract_Omits_InternalMachinePaths()
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.DoesNotContain(
            "hostPath",
            schemas.GetProperty("WorkbenchArtifactRef").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "statePath",
            schemas.GetProperty("BrowserEdgeSessionStateResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "statusPath",
            schemas.GetProperty("MicrosoftAuthorizeProbeResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "absolutePath",
            schemas.GetProperty("MailSavedAttachment").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "runRoot",
            schemas.GetProperty("MailDownloadResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "lastWorkerError",
            schemas.GetProperty("MailStatusResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "evidencePath",
            schemas.GetProperty("DevScriptResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "statePath",
            schemas.GetProperty("PowerAutomateMcpStartResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "nodePath",
            schemas.GetProperty("PowerAutomateMcpStatusResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));
        Assert.DoesNotContain(
            "extensionPath",
            schemas.GetProperty("PowerAutomateMcpEdgeResult").GetProperty("properties").EnumerateObject().Select(item => item.Name));

        var value = new WorkbenchArtifactRef(
            @"C:\local\capture.png",
            "runs/test/capture.png",
            "/host/runs/test/capture.png",
            "image/png",
            42);
        var httpOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        OperatorJson.ConfigureHttp(httpOptions);

        var publicJson = JsonSerializer.Serialize(value, httpOptions);
        Assert.DoesNotContain("hostPath", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("relativePath", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"path\"", publicJson, StringComparison.Ordinal);

        var mailStatus = new MailStatusResult(
            true,
            1,
            0,
            @"Mail worker failed under C:\Users\operator\AppData\Local\WindowsOperator.",
            DateTimeOffset.Parse("2026-07-23T16:42:40Z"));
        var publicMailStatusJson = JsonSerializer.Serialize(mailStatus, httpOptions);
        Assert.DoesNotContain("lastWorkerError", publicMailStatusJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users", publicMailStatusJson, StringComparison.Ordinal);

        var persistedJson = JsonSerializer.Serialize(value, OperatorJson.SerializerOptions);
        Assert.Contains("hostPath", persistedJson, StringComparison.Ordinal);
        Assert.Contains("relativePath", persistedJson, StringComparison.Ordinal);
        Assert.Contains("\"path\"", persistedJson, StringComparison.Ordinal);

        var persistedMailStatusJson = JsonSerializer.Serialize(mailStatus, OperatorJson.SerializerOptions);
        Assert.Contains("lastWorkerError", persistedMailStatusJson, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorOpenApi_Separates_PowerPointJobInput_FromOutput()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        Assert.Equal(
            "#/components/schemas/PowerPointUpdateJobInput",
            schemas.GetProperty("PowerPointOnlineUpdateRequest")
                .GetProperty("properties")
                .GetProperty("job")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/PowerPointUpdateOperationInput",
            schemas.GetProperty("PowerPointUpdateJobInput")
                .GetProperty("properties")
                .GetProperty("operations")
                .GetProperty("items")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            new[] { "jobId", "requestedBy" },
            RequiredProperties(schemas.GetProperty("PowerPointUpdateJobInput"))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                "bindNamedTargets",
                "createdAt",
                "discoverTargets",
                "jobId",
                "operations",
                "requestedBy",
                "validateOnly",
            },
            RequiredProperties(schemas.GetProperty("PowerPointUpdateJob"))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            "#/components/schemas/PowerPointUpdateJob",
            root.GetProperty("paths")
                .GetProperty("/v1/powerpoint/jobs/claim")
                .GetProperty("post")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
    }

    [Fact]
    public void OperatorOpenApi_Emits_RuntimeInputDefaults()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var edgeProperties = schemas.GetProperty("BrowserEdgeOpenUrlRequest").GetProperty("properties");
        var updateProperties = schemas.GetProperty("PowerPointOnlineUpdateRequest").GetProperty("properties");
        var queryProperties = schemas.GetProperty("UiQueryInput").GetProperty("properties");
        var jobProperties = schemas.GetProperty("PowerPointUpdateJobInput").GetProperty("properties");

        Assert.Equal("work", edgeProperties.GetProperty("profileMode").GetProperty("default").GetString());
        Assert.Equal(12, edgeProperties.GetProperty("waitSeconds").GetProperty("default").GetInt32());
        Assert.False(edgeProperties.GetProperty("capture").GetProperty("default").GetBoolean());
        Assert.True(updateProperties.GetProperty("capture").GetProperty("default").GetBoolean());
        Assert.Equal(30, updateProperties.GetProperty("openWaitSeconds").GetProperty("default").GetInt32());
        Assert.True(updateProperties.GetProperty("cleanupTemplateOnFailure").GetProperty("default").GetBoolean());
        Assert.Equal(25, queryProperties.GetProperty("maxResults").GetProperty("default").GetInt32());
        Assert.False(jobProperties.GetProperty("discoverTargets").GetProperty("default").GetBoolean());
        Assert.False(jobProperties.GetProperty("createdAt").TryGetProperty("default", out _));
        Assert.False(jobProperties.GetProperty("jobId").TryGetProperty("default", out _));
    }

    [Fact]
    public void PowerPointOnlineAddInProbeRequest_Serializes_ActivationDefaults()
    {
        var request = new PowerPointOnlineAddInProbeRequest();

        var json = JsonSerializer.Serialize(request, OperatorJson.SerializerOptions);

        Assert.Contains("\"activateIfNeeded\":false", json);
        Assert.Contains("\"activationTimeoutSeconds\":10", json);
        Assert.Contains("\"hostTimeoutSeconds\":10", json);
    }

    [Fact]
    public void PowerPointOnlineAddInProbeResult_Serializes_StatusAsCamelCase()
    {
        var result = new PowerPointOnlineAddInProbeResult
        {
            Success = false,
            Status = PowerPointOnlineAddInProbeStatus.BlockedActivation,
            Session = new PowerPointOnlineSessionResult
            {
                Success = true,
                SessionId = "ppt-session",
                Status = PowerPointOnlineSessionStatus.Ready,
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                CurrentTitle = "PowerPoint",
                CurrentSlide = 4,
                SlideCount = 71,
                EditMode = "editing",
                SaveState = "saved",
                BrowserSessionId = "edge-session",
                Hwnd = 888,
                ArtifactRoot = null,
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = Array.Empty<string>(),
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
            },
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
            TaskPaneVisible = false,
            CommandVisible = false,
            MatchedElements = Array.Empty<UiElementRef>(),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "addin_host_probe_ok" },
            Warnings = Array.Empty<string>(),
            Errors = new[] { OperatorErrors.PowerPointUnavailable("task pane hidden") },
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:03Z"),
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"status\":\"blockedActivation\"", json);
        Assert.Contains("\"addInBaseUrl\":\"https://localhost:3003\"", json);
        Assert.Contains("\"hostReachable\":true", json);
        Assert.Contains("\"taskPaneUrl\":\"https://localhost:3003/taskpane.html\"", json);
        Assert.Contains("\"manifestReachable\":true", json);
        Assert.Contains("\"manifestDisplayName\":\"Windows Operator PowerPoint\"", json);
    }

    [Fact]
    public async Task ScreenshotEncoding_UsesJpegDefaults_AndResizesLongestEdge()
    {
        using var image = new Image<Rgba32>(3200, 1800, new Rgba32(20, 40, 60));
        var frame = new RawCaptureFrame(
            image,
            new WindowBounds(0, 0, 3200, 1800),
            1.0,
            "Synthetic",
            DateTimeOffset.UtcNow);
        var service = new ImageEncodingService(
            Options.Create(
                new OperatorOptions
                {
                    Screenshot = new ScreenshotOptions
                    {
                        DefaultFormat = ScreenshotFormat.Jpeg,
                        JpegQuality = 85,
                        LongestEdge = 1600,
                    },
                }));

        var result = await service.EncodeAsync(frame, null, CancellationToken.None);

        Assert.Equal("image/jpeg", result.MediaType);
        Assert.Equal(1600, result.PixelWidth);
        Assert.Equal(900, result.PixelHeight);
        Assert.Equal(85, result.JpegQuality);
    }

    private static void AssertHasProperties(JsonElement properties, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.True(properties.TryGetProperty(name, out _), $"Missing OpenAPI property '{name}'.");
        }
    }

    private static void AssertRequiredProperties(JsonElement schema, params string[] names)
    {
        var required = RequiredProperties(schema).ToHashSet(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Assert.Contains(name, required);
        }
    }

    private static IEnumerable<string> RequiredProperties(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(item => item.GetString()!)
            : Array.Empty<string>();

    private static string JsonPropertyName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
        JsonNamingPolicy.CamelCase.ConvertName(property.Name);
}
