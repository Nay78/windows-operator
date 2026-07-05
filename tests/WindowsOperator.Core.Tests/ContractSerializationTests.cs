using System.Text.Json;
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
            "values");
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
            "table");
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
}
