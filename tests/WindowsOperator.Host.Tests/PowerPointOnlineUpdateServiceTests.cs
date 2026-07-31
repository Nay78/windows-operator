using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

    public sealed class PowerPointOnlineUpdateServiceTests
{
    [Fact]
    public async Task UpdateAsync_RejectsExecutableJob_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var ex = await Assert.ThrowsAsync<WindowsOperator.Core.OperatorFailureException>(() =>
            service.UpdateAsync(
                CreateRequest() with
                {
                    AllowDeckMutation = false,
                },
                CancellationToken.None));

        Assert.Equal(WindowsOperator.Core.ErrorCodes.PowerPointValidationFailed, ex.Error.Code);
        Assert.Contains("allowDeckMutation", ex.Error.Details?["detail"]);
        Assert.Equal(0, online.StartCalls);
        Assert.Empty(jobs.EnqueuedJobs);
    }

    [Fact]
    public async Task UpdateAsync_AllowsValidateOnlyJob_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "failed") with
        {
            Job = CreateJob() with { ValidateOnly = true },
        });
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                AllowDeckMutation = false,
                Job = CreateJob() with { ValidateOnly = true },
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Failed, result.Status);
        Assert.Equal(1, online.StartCalls);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.True(jobs.EnqueuedJobs[0].ValidateOnly);
    }

    [Fact]
    public async Task UpdateAsync_AllowsReadTableJob_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        var readJob = CreateJob() with
        {
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "readTable",
                    TargetId = "DATA_TABLE",
                },
            },
        };
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded") with { Job = readJob });
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                AllowDeckMutation = false,
                Job = readJob,
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal("readTable", jobs.EnqueuedJobs[0].Operations.Single().Kind);
    }

    [Fact]
    public async Task UpdateAsync_AllowsGeometryReadJobs_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        var readJob = CreateJob() with
        {
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "readShapeBounds",
                    TargetId = "DATE_HIGHLIGHT_BOX",
                },
                new PowerPointUpdateOperation
                {
                    Kind = "readTableGeometry",
                    TargetId = "DATA_TABLE",
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
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded") with { Job = readJob });
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                AllowDeckMutation = false,
                Job = readJob,
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal(new[] { "readShapeBounds", "readTableGeometry", "findTableColumn" }, jobs.EnqueuedJobs[0].Operations.Select(operation => operation.Kind));
    }

    [Fact]
    public async Task UpdateAsync_RejectsSetShapeBounds_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        var mutationJob = CreateJob() with
        {
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "setShapeBounds",
                    TargetId = "DATE_HIGHLIGHT_BOX",
                    Left = 100,
                    Top = 20,
                    Width = 50,
                    Height = 400,
                },
            },
        };
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var ex = await Assert.ThrowsAsync<WindowsOperator.Core.OperatorFailureException>(() =>
            service.UpdateAsync(
                CreateRequest() with
                {
                    AllowDeckMutation = false,
                    Job = mutationJob,
                },
                CancellationToken.None));

        Assert.Equal(WindowsOperator.Core.ErrorCodes.PowerPointValidationFailed, ex.Error.Code);
        Assert.Contains("allowDeckMutation", ex.Error.Details?["detail"]);
        Assert.Empty(jobs.EnqueuedJobs);
    }

    [Fact]
    public async Task UpdateAsync_RejectsNamedTargetRepair_WhenDeckMutationNotAllowed()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        var readJob = CreateJob() with
        {
            BindNamedTargets = true,
            Operations = new[]
            {
                new PowerPointUpdateOperation
                {
                    Kind = "readTable",
                    TargetId = "DATA_TABLE",
                },
            },
        };
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var ex = await Assert.ThrowsAsync<WindowsOperator.Core.OperatorFailureException>(() =>
            service.UpdateAsync(
                CreateRequest() with
                {
                    AllowDeckMutation = false,
                    Job = readJob,
                    Capture = false,
                },
                CancellationToken.None));

        Assert.Equal(WindowsOperator.Core.ErrorCodes.PowerPointValidationFailed, ex.Error.Code);
        Assert.Contains("allowDeckMutation", ex.Error.Details?["detail"]);
        Assert.Equal(0, online.StartCalls);
        Assert.Empty(jobs.EnqueuedJobs);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsSucceeded_WhenJobCompletes()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "running"));
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result.Status);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier2SavedIndicator, result.SaveProofTier);
        Assert.Equal("succeeded", result.JobRecord.Status);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal(1, online.StartCalls);
        Assert.Equal(1, online.ProbeCalls);
        Assert.True(online.LastProbeActivateIfNeeded);
        Assert.Equal(5, online.LastProbeActivationTimeoutSeconds);
        Assert.Equal(5, online.LastProbeHostTimeoutSeconds);
        Assert.Equal(1, online.SaveWaitCalls);
        Assert.Equal(30, online.LastSaveTimeoutSeconds);
        Assert.Equal(1, online.LastSavePollSeconds);
        Assert.Equal(1, online.SelectCalls);
        Assert.Equal(1, online.LastSelectWaitSeconds);
        Assert.Equal(1, online.ScreenshotCalls);
        Assert.Contains("save_wait_requested", result.Actions);
        Assert.Contains("save_wait_observed:saved", result.Actions);
        Assert.Contains("addin_probe_requested", result.Actions);
        Assert.Contains("addin_host_probe_ok", result.Actions);
        Assert.Contains("slide_selected:7", result.Session.Actions);
        Assert.Contains("slide_selected:7", result.Actions);
        Assert.Single(result.Evidence);
        Assert.NotNull(result.PhaseTimings);
        Assert.NotNull(result.PhaseTimings!.OpenSessionMs);
        Assert.NotNull(result.PhaseTimings.AddInProbeMs);
        Assert.NotNull(result.PhaseTimings.JobMs);
        Assert.NotNull(result.PhaseTimings.SaveMs);
        Assert.NotNull(result.PhaseTimings.EvidenceMs);
        Assert.NotNull(result.PhaseTimings.TotalMs);
        Assert.Null(result.PhaseTimings.VerificationReopenMs);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedSession_WhenFinalEvidenceSlideSelectionMismatches()
    {
        var online = new FakePowerPointOnlineService
        {
            SelectResultFactory = (sessionId, request) =>
                CreateSession(PowerPointOnlineSessionStatus.Ready) with
                {
                    SessionId = sessionId,
                    CurrentSlide = request.SlideNumber - 1,
                    Actions = new[] { $"slide_select_verification_failed:{request.SlideNumber - 1}:{request.SlideNumber}" },
                },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedSession, result.Status);
        Assert.Equal("succeeded", result.JobRecord.Status);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal(1, online.SelectCalls);
        Assert.Equal(0, online.ScreenshotCalls);
        Assert.Contains("evidence_slide_select_failed", result.Actions);
        Assert.Contains("evidence_not_ready", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVerificationFailed_WhenReopenEvidenceSlideSelectionMismatches()
    {
        var selectAttempt = 0;
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened" },
            },
            SelectResultFactory = (sessionId, request) =>
            {
                selectAttempt++;
                if (selectAttempt == 2)
                {
                    return CreateSession(PowerPointOnlineSessionStatus.Ready) with
                    {
                        SessionId = sessionId,
                        CurrentSlide = request.SlideNumber + 1,
                        Actions = new[] { $"slide_select_verification_failed:{request.SlideNumber + 1}:{request.SlideNumber}" },
                    };
                }

                return CreateSession(PowerPointOnlineSessionStatus.Ready) with
                {
                    SessionId = sessionId,
                    CurrentSlide = request.SlideNumber,
                    Actions = new[] { $"slide_selected:{request.SlideNumber}" },
                };
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.VerificationFailed, result.Status);
        Assert.Equal("succeeded", result.JobRecord.Status);
        Assert.NotNull(result.VerificationSession);
        Assert.Equal(PowerPointOnlineSessionStatus.Failed, result.VerificationSession!.Status);
        Assert.Equal(2, online.SelectCalls);
        Assert.Equal(1, online.ScreenshotCalls);
        Assert.Contains("verification_evidence_not_ready", result.Actions);
        Assert.Contains("evidence_slide_select_failed", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_RunsPendingJobThroughPowerPointService()
    {
        var online = new FakePowerPointOnlineService
        {
            ProbeResult = CreateProbeResult(PowerPointOnlineAddInProbeStatus.Ready) with
            {
                MatchedElements = new[]
                {
                    CreateUiElement(
                        "run-pending-job",
                        "Run Pending Job",
                        "runPendingJob",
                        "Button",
                        new WindowBounds(100, 200, 80, 20)),
                },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var facade = new FakeOperatorFacade();
        var service = new PowerPointOnlineUpdateService(online, jobs, facade);

        var result = await service.UpdateAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, online.RunPendingJobCalls);
        Assert.Equal(0, online.LastRunPendingJobWaitSeconds);
        Assert.Empty(facade.ScreenClicks);
        Assert.Contains("addin_run_pending_job_command_requested", result.Actions);
        Assert.Contains("addin_run_pending_job_click_dispatched", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedAddIn_WhenRunPendingJobButtonFails()
    {
        var online = new FakePowerPointOnlineService
        {
            ProbeResult = CreateProbeResult(PowerPointOnlineAddInProbeStatus.Ready) with
            {
                MatchedElements = Array.Empty<UiElementRef>(),
            },
            RunPendingJobResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                Success = false,
                Actions = new[] { "addin_run_pending_job_button_not_found" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("Run Pending Job button is missing.") },
            },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedAddIn, result.Status);
        Assert.Equal(1, online.RunPendingJobCalls);
        Assert.Equal("failed", result.JobRecord.Status);
        Assert.Equal("ADDIN_RUN_COMMAND_FAILED", jobs.FailRequest?.Error.Code);
        Assert.Contains("addin_run_pending_job_command_requested", result.Actions);
        Assert.Contains("addin_run_pending_job_button_not_found", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReopensVerification_WhenRequested()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened" },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "running"));
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
                ReopenWaitSeconds = 17,
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result.Status);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier3ReopenVisual, result.SaveProofTier);
        Assert.NotNull(result.VerificationSession);
        Assert.Equal("ppt-session-verification", result.VerificationSession!.SessionId);
        Assert.Equal(2, online.StartCalls);
        Assert.Equal("ppt-session", online.LastCleanupSessionId);
        Assert.Equal("ppt-session-verification", online.LastStartSessionId);
        Assert.Equal(17, online.LastStartWaitSeconds);
        Assert.Equal(2, online.SelectCalls);
        Assert.Equal(2, online.ScreenshotCalls);
        Assert.Equal(2, result.Evidence.Count);
        Assert.Contains("verification_cleanup_requested", result.Actions);
        Assert.Contains("verification_reopen_requested", result.Actions);
        Assert.Contains("verification_reopen_ready", result.Actions);
        Assert.Equal(1, result.Actions.Count(action => action == "session_reopened"));
        Assert.NotNull(result.PhaseTimings);
        Assert.NotNull(result.PhaseTimings!.VerificationReopenMs);
        Assert.Null(result.PhaseTimings.SessionCleanupMs);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotClaimReopenVisualTier_WhenVerificationCaptureDisabled()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened" },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
                Capture = false,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result.Status);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier2SavedIndicator, result.SaveProofTier);
        Assert.NotNull(result.VerificationSession);
        Assert.Empty(result.Evidence);
        Assert.Equal(0, online.ScreenshotCalls);
        Assert.Contains("verification_reopen_ready", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVerificationFailed_WhenReopenSessionReadyButNotSuccessful()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                Success = false,
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened_not_successful" },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.VerificationFailed, result.Status);
        Assert.NotNull(result.VerificationSession);
        Assert.False(result.VerificationSession!.Success);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier2SavedIndicator, result.SaveProofTier);
        Assert.Equal(2, online.StartCalls);
        Assert.Equal(1, online.CleanupCalls);
        Assert.Equal(1, online.ScreenshotCalls);
        Assert.Contains("verification_reopen_failed", result.Actions);
        Assert.DoesNotContain("verification_reopen_ready", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_PreparesTemplateAndCleansAfterReopen_WhenRequested()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened" },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                Job = CreateJob() with
                {
                    Operations = new[]
                    {
                        new PowerPointUpdateOperation
                        {
                            Kind = "replaceText",
                            TargetId = "TITLE_MAIN",
                            Text = "Updated template proof title",
                            Mode = "plain",
                        },
                    },
                },
                PrepareTemplate = true,
                CleanupTemplate = true,
                VerifyReopen = true,
                EvidenceSlideNumber = 7,
                TemplateWaitSeconds = 4,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result.Status);
        Assert.NotNull(result.TemplatePreparationSession);
        Assert.NotNull(result.TemplateCleanupSession);
        Assert.Equal(1, online.PrepareTemplateCalls);
        Assert.Equal(1, online.CleanupTemplateCalls);
        Assert.Equal(4, online.LastPrepareTemplateWaitSeconds);
        Assert.Equal(4, online.LastCleanupTemplateWaitSeconds);
        Assert.True(online.LastPrepareTemplateAllowDeckMutation.GetValueOrDefault());
        Assert.True(online.LastCleanupTemplateAllowDeckMutation.GetValueOrDefault());
        Assert.Equal("ppt-session-verification", online.LastCleanupTemplateSessionId);
        Assert.Equal(new[] { 7, 7, 7, 7 }, online.SelectedSlides);
        Assert.True(online.Calls.IndexOf("select:7") < online.Calls.IndexOf("prepare-template"));
        Assert.Equal("TITLE_MAIN", Assert.Single(jobs.EnqueuedJobs).Operations.Single().TargetId);
        Assert.Equal(3, online.ProbeCalls);
        Assert.Equal(3, online.SaveWaitCalls);
        Assert.Contains("powerpoint-online-template-cleanup", online.ScreenshotLabels);
        Assert.Contains("template_prepare_slide_select_requested:7", result.Actions);
        Assert.Contains("template_prepare_requested", result.Actions);
        Assert.Contains("template_prepare_save_wait_requested", result.Actions);
        Assert.Contains("template_prepare_addin_reprobe_requested", result.Actions);
        Assert.Contains("template_cleanup_addin_probe_requested", result.Actions);
        Assert.Contains("template_cleanup_requested", result.Actions);
        Assert.Contains("template_cleanup_save_wait_requested", result.Actions);
        Assert.Contains("template_cleanup_evidence_requested", result.Actions);
        Assert.NotEmpty(result.TemplateCleanupSession!.Evidence);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotPrepareTemplate_WhenEvidenceSlideSelectionFails()
    {
        var online = new FakePowerPointOnlineService
        {
            SelectResultFactory = (sessionId, request) =>
                CreateSession(PowerPointOnlineSessionStatus.Ready) with
                {
                    SessionId = sessionId,
                    CurrentSlide = request.SlideNumber - 1,
                    Actions = new[] { $"slide_select_verification_failed:{request.SlideNumber - 1}:{request.SlideNumber}" },
                },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                PrepareTemplate = true,
                CleanupTemplate = true,
                CleanupSession = true,
                EvidenceSlideNumber = 7,
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedSession, result.Status);
        Assert.Equal("notQueued", result.JobRecord.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.Equal(1, online.SelectCalls);
        Assert.Equal(0, online.PrepareTemplateCalls);
        Assert.Equal(0, online.CleanupTemplateCalls);
        Assert.Equal(1, online.CleanupCalls);
        Assert.Contains("template_prepare_slide_select_requested:7", result.Actions);
        Assert.Contains("template_prepare_slide_select_failed", result.Actions);
        Assert.Contains("session_cleanup_requested", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_CleansFinalVerificationSession_WhenSessionCleanupRequested()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                SessionId = "ppt-session-verification",
                Actions = new[] { "session_reopened" },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                PrepareTemplate = true,
                CleanupTemplate = true,
                CleanupSession = true,
                VerifyReopen = true,
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.Succeeded, result.Status);
        Assert.NotNull(result.TemplateCleanupSession);
        Assert.NotNull(result.SessionCleanupSession);
        Assert.Equal(PowerPointOnlineSessionStatus.Closed, result.SessionCleanupSession!.Status);
        Assert.Equal(new[] { "ppt-session", "ppt-session-verification" }, online.CleanupSessionIds);
        Assert.Contains("session_cleanup_requested", result.Actions);
        Assert.DoesNotContain("session_cleanup_failed", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsSessionCleanupFailed_WhenFinalSessionCleanupFailsAfterSuccessfulJob()
    {
        var online = new FakePowerPointOnlineService
        {
            CleanupResult = CreateSession(PowerPointOnlineSessionStatus.Failed) with
            {
                Success = false,
                Actions = new[] { "session_cleanup_backend_failed" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("session cleanup failed") },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                CleanupSession = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.SessionCleanupFailed, result.Status);
        Assert.Equal("succeeded", result.JobRecord.Status);
        Assert.NotNull(result.SessionCleanupSession);
        Assert.Equal(1, online.CleanupCalls);
        Assert.Equal("ppt-session", Assert.Single(online.CleanupSessionIds));
        Assert.Contains("session_cleanup_requested", result.Actions);
        Assert.Contains("session_cleanup_failed", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsCleanupFailed_WhenTemplateCleanupFailsAfterSuccessfulJob()
    {
        var online = new FakePowerPointOnlineService
        {
            CleanupTemplateResult = CreateSession(PowerPointOnlineSessionStatus.Failed) with
            {
                Success = false,
                Actions = new[] { "template_cleanup_click_failed" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("cleanup failed") },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                CleanupTemplate = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.CleanupFailed, result.Status);
        Assert.Equal("succeeded", result.JobRecord.Status);
        Assert.NotNull(result.TemplateCleanupSession);
        Assert.Equal(1, online.CleanupTemplateCalls);
        Assert.Equal(1, online.SaveWaitCalls);
        Assert.Contains("template_cleanup_requested", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedAddIn_WhenTemplatePrepareFailsWithoutEnqueue()
    {
        var online = new FakePowerPointOnlineService
        {
            PrepareTemplateResult = CreateSession(PowerPointOnlineSessionStatus.Failed) with
            {
                Success = false,
                Actions = new[] { "template_prepare_click_failed" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("prepare failed") },
            },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                PrepareTemplate = true,
                Capture = false,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedAddIn, result.Status);
        Assert.Equal("notQueued", result.JobRecord.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.NotNull(result.TemplatePreparationSession);
        Assert.Equal(1, online.PrepareTemplateCalls);
        Assert.Equal(0, online.SaveWaitCalls);
        Assert.Contains("template_prepare_requested", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVerificationFailed_WhenCleanupFails()
    {
        var online = new FakePowerPointOnlineService
        {
            CleanupResult = CreateSession(PowerPointOnlineSessionStatus.Failed) with
            {
                Success = false,
                Actions = new[] { "cleanup_failed" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("cleanup failed") },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.VerificationFailed, result.Status);
        Assert.Null(result.VerificationSession);
        Assert.Equal(1, online.StartCalls);
        Assert.Equal(1, online.CleanupCalls);
        Assert.Contains("verification_cleanup_failed", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVerificationFailed_WhenReopenNotReady()
    {
        var online = new FakePowerPointOnlineService
        {
            ReopenStartResult = CreateSession(PowerPointOnlineSessionStatus.BlockedOfficeError) with
            {
                Success = false,
                SessionId = "ppt-session-verification",
                Actions = new[] { "reopen_blocked" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("reopen blocked") },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                VerifyReopen = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.VerificationFailed, result.Status);
        Assert.NotNull(result.VerificationSession);
        Assert.Equal(PowerPointOnlineSessionStatus.BlockedOfficeError, result.VerificationSession!.Status);
        Assert.Equal(2, online.StartCalls);
        Assert.Equal(1, online.CleanupCalls);
        Assert.Equal(1, online.ScreenshotCalls);
        Assert.Contains("verification_reopen_failed", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointUnavailable);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsSaveUnverified_WhenSaveWaitTimesOut()
    {
        var online = new FakePowerPointOnlineService
        {
            SaveWaitResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                Success = false,
                SaveState = "saving",
                Actions = new[] { "save_wait_timeout" },
                Warnings = new[] { "save_state_not_saved:saving" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("save not observed") },
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.SaveUnverified, result.Status);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier1OfficeJsSync, result.SaveProofTier);
        Assert.Equal(1, online.SaveWaitCalls);
        Assert.Equal("saving", result.Session.SaveState);
        Assert.Contains("save_wait_requested", result.Actions);
        Assert.Contains("save_wait_timeout", result.Actions);
        Assert.Contains("save_state_not_saved:saving", result.Warnings);
        Assert.Equal(WindowsOperator.Core.ErrorCodes.PowerPointUnavailable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedSession_WhenSessionNotReady()
    {
        var blockedSession = CreateSession(PowerPointOnlineSessionStatus.BlockedOfficeError) with
        {
            Success = false,
            Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("office banner observed") },
        };
        var online = new FakePowerPointOnlineService { StartResult = blockedSession };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedSession, result.Status);
        Assert.Equal(PowerPointOnlineSaveProofTier.Tier0VisualOpen, result.SaveProofTier);
        Assert.Equal("notQueued", result.JobRecord.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.Equal(blockedSession.Errors, result.Errors);
    }

    [Fact]
    public async Task UpdateAsync_TimesOutAndFailsQueuedJob()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "queued" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "queued"));
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "queued"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                JobTimeoutSeconds = 0,
                PollSeconds = 0,
                EvidenceSlideNumber = 7,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedAddIn, result.Status);
        Assert.Equal("failed", result.JobRecord.Status);
        Assert.NotNull(jobs.FailRequest);
        Assert.Equal("job-1", jobs.FailRequest!.Value.JobId);
        Assert.Equal("ADDIN_TIMEOUT", jobs.FailRequest.Value.Error.Code);
        Assert.Equal(1, online.ProbeCalls);
        Assert.Equal(1, online.SelectCalls);
        Assert.Equal(1, online.ScreenshotCalls);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedAddInWithoutEnqueue_WhenProbeBlocked()
    {
        var online = new FakePowerPointOnlineService
        {
            ProbeResult = CreateProbeResult(PowerPointOnlineAddInProbeStatus.BlockedActivation, success: false) with
            {
                Actions = new[] { "addin_host_probe_ok", "addin_taskpane_not_visible" },
                Errors = new[] { WindowsOperator.Core.OperatorErrors.PowerPointUnavailable("task pane not visible") },
            },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(CreateRequest() with { Capture = false }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedAddIn, result.Status);
        Assert.Equal("notQueued", result.JobRecord.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.Equal(1, online.ProbeCalls);
        Assert.Contains("addin_probe_requested", result.Actions);
        Assert.Contains("addin_host_probe_ok", result.Actions);
        Assert.Contains("addin_taskpane_not_visible", result.Actions);
        Assert.Contains("addin_probe_blocked:blockedActivation", result.Actions);
        Assert.DoesNotContain("job_enqueued", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_PassesActivationFlags_ToAddInProbe()
    {
        var online = new FakePowerPointOnlineService();
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        await service.UpdateAsync(
            CreateRequest() with
            {
                JobTimeoutSeconds = 77,
            },
            CancellationToken.None);

        Assert.True(online.LastProbeActivateIfNeeded);
        Assert.Equal(60, online.LastProbeActivationTimeoutSeconds);
        Assert.Equal(60, online.LastProbeHostTimeoutSeconds);
    }

    [Fact]
    public async Task UpdateAsync_CollectsEvidenceSlide_WhenProbeBlocked()
    {
        var online = new FakePowerPointOnlineService
        {
            ProbeResult = CreateProbeResult(PowerPointOnlineAddInProbeStatus.BlockedActivation, success: false) with
            {
                Actions = new[] { "addin_host_probe_ok", "addin_taskpane_not_visible" },
            },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                EvidenceSlideNumber = 7,
                Capture = true,
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedAddIn, result.Status);
        Assert.Equal("notQueued", result.JobRecord.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.Equal(1, online.SelectCalls);
        Assert.Equal(1, online.ScreenshotCalls);
        Assert.Contains("slide_selected:7", result.Actions);
        Assert.Single(result.Evidence);
    }

    [Fact]
    public async Task UpdateAsync_BindsMissingExpectedDocumentUrl_ToSessionCanonicalUrl()
    {
        var online = new FakePowerPointOnlineService
        {
            StartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                CanonicalUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D",
                CurrentUrl = "https://tenant.sharepoint.com/personalized/view",
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded") with
        {
            Job = CreateJob() with { ExpectedDocumentUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D" },
        });
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                Job = CreateJob() with { ExpectedDocumentUrl = null },
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal(
            "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D",
            jobs.EnqueuedJobs[0].ExpectedDocumentUrl);
        Assert.Contains("job_bound_to_session", result.Actions);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsBlockedSession_WhenExpectedDocumentUrlMismatchesSession()
    {
        var online = new FakePowerPointOnlineService
        {
            StartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                CanonicalUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D",
                CurrentUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D",
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck-a.pptx?web=1",
            },
        };
        var jobs = new FakePowerPointJobService();
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                Job = CreateJob() with
                {
                    ExpectedDocumentUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Bdef%7D",
                },
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PowerPointOnlineUpdateStatus.BlockedSession, result.Status);
        Assert.Empty(jobs.EnqueuedJobs);
        Assert.Contains("job_document_mismatch", result.Actions);
        Assert.Contains(result.Errors, error => error.Code == WindowsOperator.Core.ErrorCodes.PowerPointValidationFailed);
    }

    [Fact]
    public async Task UpdateAsync_Enqueues_WhenExpectedDocumentUrlMatchesSessionIdentity()
    {
        var online = new FakePowerPointOnlineService
        {
            StartResult = CreateSession(PowerPointOnlineSessionStatus.Ready) with
            {
                CanonicalUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?action=edit&sourcedoc=%7Babc%7D",
                CurrentUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Babc%7D&action=edit",
                DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            },
        };
        var jobs = new FakePowerPointJobService();
        jobs.EnqueueResultFactory = record => record with { Status = "running" };
        jobs.GetResults.Enqueue(CreateJobRecord("job-1", "succeeded"));
        var service = new PowerPointOnlineUpdateService(online, jobs);

        var result = await service.UpdateAsync(
            CreateRequest() with
            {
                Job = CreateJob() with
                {
                    ExpectedDocumentUrl = "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Babc%7D&action=edit",
                },
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(jobs.EnqueuedJobs);
        Assert.Equal(
            "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Babc%7D&action=edit",
            jobs.EnqueuedJobs[0].ExpectedDocumentUrl);
        Assert.DoesNotContain("job_document_mismatch", result.Actions);
    }

    private static PowerPointOnlineUpdateRequest CreateRequest() =>
        new()
        {
            DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            Job = CreateJob(),
            Capture = true,
            OpenWaitSeconds = 5,
            JobTimeoutSeconds = 5,
            PollSeconds = 0,
            AllowDeckMutation = true,
        };

    private static PowerPointUpdateJob CreateJob() =>
        new()
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
        };

    private static UiElementRef CreateUiElement(
        string runtimeId,
        string name,
        string automationId,
        string controlType,
        WindowBounds bounds) =>
        new(
            runtimeId,
            name,
            automationId,
            controlType,
            true,
            false,
            bounds);

    private static PowerPointOnlineSessionResult CreateSession(PowerPointOnlineSessionStatus status) =>
        new()
        {
            Success = status == PowerPointOnlineSessionStatus.Ready,
            SessionId = "ppt-session",
            Status = status,
            DeckUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CanonicalUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
            CurrentTitle = "Deck - PowerPoint",
            BrowserSessionId = "edge-session",
            Hwnd = 42,
            ArtifactRoot = null,
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "session_state_observed" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
        };

    private static PowerPointOnlineAddInProbeResult CreateProbeResult(
        PowerPointOnlineAddInProbeStatus status,
        bool success = true) =>
        new()
        {
            Success = success,
            Status = status,
            Session = CreateSession(PowerPointOnlineSessionStatus.Ready),
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
            TaskPaneVisible = status == PowerPointOnlineAddInProbeStatus.Ready,
            CommandVisible = true,
            MatchedElements = Array.Empty<UiElementRef>(),
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = new[] { "addin_taskpane_probe_ok", "addin_manifest_probe_ok", "addin_host_probe_ok" },
            Warnings = Array.Empty<string>(),
            Errors = Array.Empty<OperatorError>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:00Z"),
        };

    private static PowerPointJobRecord CreateJobRecord(string jobId, string status) =>
        new()
        {
            JobId = jobId,
            Status = status,
            Job = CreateJob(),
            EnqueuedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2026-07-03T12:00:01Z"),
        };

    private sealed class FakePowerPointOnlineService : IPowerPointOnlineService
    {
        public PowerPointOnlineSessionResult StartResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Ready);

        public PowerPointOnlineSessionResult? ReopenStartResult { get; set; }

        public PowerPointOnlineAddInProbeResult ProbeResult { get; set; } = CreateProbeResult(PowerPointOnlineAddInProbeStatus.Ready);

        public int StartCalls { get; private set; }

        public int ProbeCalls { get; private set; }

        public string? LastStartSessionId { get; private set; }

        public int? LastStartWaitSeconds { get; private set; }

        public int? LastProbeHostTimeoutSeconds { get; private set; }

        public bool LastProbeActivateIfNeeded { get; private set; }

        public int? LastProbeActivationTimeoutSeconds { get; private set; }

        public int SelectCalls { get; private set; }

        public int? LastSelectWaitSeconds { get; private set; }

        public List<int> SelectedSlides { get; } = new();

        public List<string> Calls { get; } = new();

        public Func<string, PowerPointOnlineSlideSelectRequest, PowerPointOnlineSessionResult>? SelectResultFactory { get; set; }

        public int SaveWaitCalls { get; private set; }

        public int? LastSaveTimeoutSeconds { get; private set; }

        public int? LastSavePollSeconds { get; private set; }

        public int PrepareTemplateCalls { get; private set; }

        public int? LastPrepareTemplateWaitSeconds { get; private set; }

        public bool? LastPrepareTemplateAllowDeckMutation { get; private set; }

        public int CleanupTemplateCalls { get; private set; }

        public string? LastCleanupTemplateSessionId { get; private set; }

        public int? LastCleanupTemplateWaitSeconds { get; private set; }

        public bool? LastCleanupTemplateAllowDeckMutation { get; private set; }

        public int RunPendingJobCalls { get; private set; }

        public int? LastRunPendingJobWaitSeconds { get; private set; }

        public int ScreenshotCalls { get; private set; }

        public List<string> ScreenshotLabels { get; } = new();

        public int CleanupCalls { get; private set; }

        public string? LastCleanupSessionId { get; private set; }

        public List<string> CleanupSessionIds { get; } = new();

        public PowerPointOnlineSessionResult SaveWaitResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Ready) with
        {
            SaveState = "saved",
            Actions = new[] { "save_wait_observed:saved" },
        };

        public PowerPointOnlineSessionResult PrepareTemplateResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Ready) with
        {
            Actions = new[] { "template_prepare_clicked" },
        };

        public PowerPointOnlineSessionResult CleanupTemplateResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Ready) with
        {
            Actions = new[] { "template_cleanup_clicked" },
        };

        public PowerPointOnlineSessionResult RunPendingJobResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Ready) with
        {
            Actions = new[] { "addin_run_pending_job_click_dispatched" },
        };

        public PowerPointOnlineSessionResult CleanupResult { get; set; } = CreateSession(PowerPointOnlineSessionStatus.Closed) with
        {
            Success = true,
        };

        public Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(PowerPointOnlineSessionStartRequest request, CancellationToken cancellationToken)
        {
            StartCalls++;
            Calls.Add("start");
            LastStartSessionId = request.SessionId;
            LastStartWaitSeconds = request.WaitSeconds;
            return Task.FromResult(StartCalls == 1 || ReopenStartResult is null ? StartResult : ReopenStartResult);
        }

        public Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(StartResult with { SessionId = sessionId });

        public Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(string sessionId, PowerPointOnlineSlideSelectRequest request, CancellationToken cancellationToken)
        {
            SelectCalls++;
            SelectedSlides.Add(request.SlideNumber);
            Calls.Add($"select:{request.SlideNumber}");
            LastSelectWaitSeconds = request.WaitSeconds;
            if (SelectResultFactory is not null)
            {
                return Task.FromResult(SelectResultFactory(sessionId, request));
            }

            return Task.FromResult(
                CreateSession(PowerPointOnlineSessionStatus.Ready) with
                {
                    SessionId = sessionId,
                    CurrentSlide = request.SlideNumber,
                    Actions = new[] { $"slide_selected:{request.SlideNumber}" },
                });
        }

        public Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(string sessionId, PowerPointOnlineAddInProbeRequest request, CancellationToken cancellationToken)
        {
            ProbeCalls++;
            Calls.Add("probe");
            LastProbeActivateIfNeeded = request.ActivateIfNeeded;
            LastProbeActivationTimeoutSeconds = request.ActivationTimeoutSeconds;
            LastProbeHostTimeoutSeconds = request.HostTimeoutSeconds;
            return Task.FromResult(
                ProbeResult with
                {
                    AddInBaseUrl = request.AddInBaseUrl,
                    Session = ProbeResult.Session with { SessionId = sessionId },
                });
        }

        public Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(string sessionId, PowerPointOnlineSaveWaitRequest request, CancellationToken cancellationToken)
        {
            SaveWaitCalls++;
            Calls.Add("save-wait");
            LastSaveTimeoutSeconds = request.TimeoutSeconds;
            LastSavePollSeconds = request.PollSeconds;
            return Task.FromResult(SaveWaitResult with { SessionId = sessionId });
        }

        public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(
            string sessionId,
            PowerPointOnlineTemplateRequest request,
            CancellationToken cancellationToken)
        {
            PrepareTemplateCalls++;
            Calls.Add("prepare-template");
            LastPrepareTemplateWaitSeconds = request.WaitSeconds;
            LastPrepareTemplateAllowDeckMutation = request.AllowDeckMutation;
            return Task.FromResult(PrepareTemplateResult with { SessionId = sessionId });
        }

        public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(
            string sessionId,
            PowerPointOnlineTemplateRequest request,
            CancellationToken cancellationToken)
        {
            CleanupTemplateCalls++;
            Calls.Add("cleanup-template");
            LastCleanupTemplateSessionId = sessionId;
            LastCleanupTemplateWaitSeconds = request.WaitSeconds;
            LastCleanupTemplateAllowDeckMutation = request.AllowDeckMutation;
            return Task.FromResult(CleanupTemplateResult with { SessionId = sessionId });
        }

        public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(
            string sessionId,
            PowerPointOnlineAddInCommandRequest request,
            CancellationToken cancellationToken)
        {
            RunPendingJobCalls++;
            Calls.Add("run-pending-job");
            LastRunPendingJobWaitSeconds = request.WaitSeconds;
            return Task.FromResult(RunPendingJobResult with { SessionId = sessionId });
        }

        public Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(string sessionId, PowerPointOnlineSessionScreenshotRequest request, CancellationToken cancellationToken)
        {
            ScreenshotCalls++;
            Calls.Add("screenshot");
            ScreenshotLabels.Add(request.Label ?? "");
            return Task.FromResult(
                CreateSession(PowerPointOnlineSessionStatus.Ready) with
                {
                    SessionId = sessionId,
                    Actions = new[] { "screenshot_requested" },
                    Evidence = new[] { CreateScreenshotResult() },
                });
        }

        public Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            CleanupCalls++;
            Calls.Add("cleanup-session");
            LastCleanupSessionId = sessionId;
            CleanupSessionIds.Add(sessionId);
            return Task.FromResult(CleanupResult with { SessionId = sessionId });
        }
    }

    private sealed class FakeOperatorFacade : IOperatorFacade
    {
        public IReadOnlyList<UiElementRef> QueryUiResult { get; set; } = Array.Empty<UiElementRef>();

        public Queue<IReadOnlyList<UiElementRef>> QueryUiResults { get; } = new();

        public int QueryUiCalls { get; private set; }

        public UiQuery? LastQuery { get; private set; }

        public List<ScreenClickRequest> ScreenClicks { get; } = new();

        public Task<HealthResult> GetHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<WindowRef>> ListWindowsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ActionResult> ActivateWindowAsync(long hwnd, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ScreenshotResult> CaptureWindowAsync(long hwnd, ScreenshotFormat? format, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UiElementRef>> QueryUiAsync(UiQuery query, CancellationToken cancellationToken)
        {
            QueryUiCalls++;
            LastQuery = query;
            return Task.FromResult(QueryUiResults.Count > 0 ? QueryUiResults.Dequeue() : QueryUiResult);
        }

        public Task<ActionResult> ClickUiAsync(UiaClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ActionResult> TypeUiAsync(UiaTypeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken)
        {
            ScreenClicks.Add(request);
            return Task.FromResult(new ActionResult(true, "clicked"));
        }

        public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeResetResult> ResetEdgeBrowserAsync(BrowserEdgeResetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> StartEdgeBrowserSessionAsync(BrowserEdgeSessionStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> GetEdgeBrowserSessionStateAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> NavigateEdgeBrowserSessionAsync(string sessionId, BrowserEdgeSessionNavigateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionDomActionResult> ClickEdgeBrowserDomAsync(string sessionId, BrowserEdgeSessionDomClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionDomActionResult> FillEdgeBrowserDomAsync(string sessionId, BrowserEdgeSessionDomFillRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserEdgeSessionStateResult> CloseEdgeBrowserSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MicrosoftAuthCleanupResult> CleanupMicrosoftAuthWindowsAsync(MicrosoftAuthCleanupRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MicrosoftAuthorizeProbeResult> StartMicrosoftAuthorizeProbeAsync(MicrosoftAuthorizeProbeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MicrosoftAuthorizeProbeResult> GetMicrosoftAuthorizeProbeStatusAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MicrosoftDeviceLoginResult> StartMicrosoftDeviceLoginAsync(MicrosoftDeviceLoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MicrosoftDeviceLoginResult> GetMicrosoftDeviceLoginStatusAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailFoldersResult> ListMailFoldersAsync(MailListFoldersRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailSearchResult> SearchMailMessagesAsync(MailSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailDownloadResult> DownloadMailAttachmentsAsync(MailDownloadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailDownloadResult> GetMailRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MailStatusResult> GetMailStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePowerPointJobService : IPowerPointJobService
    {
        public List<PowerPointUpdateJob> EnqueuedJobs { get; } = new();

        public Queue<PowerPointJobRecord> GetResults { get; } = new();

        public Func<PowerPointJobRecord, PowerPointJobRecord>? EnqueueResultFactory { get; set; }

        public (string JobId, PowerPointUpdateError Error)? FailRequest { get; private set; }

        public Task<PowerPointJobRecord> EnqueueAsync(PowerPointUpdateJob job, CancellationToken cancellationToken)
        {
            EnqueuedJobs.Add(job);
            var record = new PowerPointJobRecord
            {
                JobId = job.JobId,
                Status = "queued",
                Job = job,
                EnqueuedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            return Task.FromResult(EnqueueResultFactory?.Invoke(record) ?? record);
        }

        public Task<PowerPointUpdateJob?> ClaimNextAsync(PowerPointClaimJobRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<PowerPointUpdateJob?>(null);

        public Task<PowerPointJobRecord> CompleteAsync(string jobId, PowerPointUpdateResult result, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PowerPointJobRecord> FailAsync(string jobId, PowerPointUpdateError error, CancellationToken cancellationToken)
        {
            FailRequest = (jobId, error);
            return Task.FromResult(
                new PowerPointJobRecord
                {
                    JobId = jobId,
                    Status = "failed",
                    Job = EnqueuedJobs.Single(job => job.JobId == jobId),
                    Error = error,
                    EnqueuedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                });
        }

        public Task<PowerPointJobRecord> GetAsync(string jobId, CancellationToken cancellationToken)
        {
            if (GetResults.Count == 0)
            {
                return Task.FromResult(CreateJobRecord(jobId, "queued"));
            }

            return Task.FromResult(GetResults.Dequeue());
        }

        public Task<PowerPointArtifactContent> GetArtifactAsync(string jobId, string artifactId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static DesktopScreenshotResult CreateScreenshotResult() =>
        new(
            true,
            new WorkbenchArtifactRef(
                @"Z:\operator-exchange\runs\ppt\screenshots\shot.png",
                "runs/ppt/screenshots/shot.png",
                "/var/lib/windows-server/shared/operator-exchange/runs/ppt/screenshots/shot.png",
                "image/png",
                3),
            new WindowRef(
                1,
                2,
                "PowerPoint",
                "Chrome_WidgetWin_1",
                new WindowBounds(0, 0, 1, 1),
                1,
                DateTimeOffset.UnixEpoch,
                true,
                false),
            1,
            1,
            "test",
            DateTimeOffset.UnixEpoch,
            [],
            []);
}
