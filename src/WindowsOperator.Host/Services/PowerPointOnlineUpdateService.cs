using System.Diagnostics;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class PowerPointOnlineUpdateService : IPowerPointOnlineUpdateService
{
    private const string Queued = "queued";
    private const string Running = "running";
    private const string Succeeded = "succeeded";
    private const string Failed = "failed";
    private const string NotQueued = "notQueued";
    private const string AddInTimeoutCode = "ADDIN_TIMEOUT";
    private const string AddInRunCommandFailedCode = "ADDIN_RUN_COMMAND_FAILED";
    private const int EvidenceSlideSelectWaitSeconds = 1;
    private const string SavedState = "saved";

    private readonly IPowerPointOnlineService _powerPointOnline;
    private readonly IPowerPointJobService _jobs;

    public PowerPointOnlineUpdateService(
        IPowerPointOnlineService powerPointOnline,
        IPowerPointJobService jobs)
    {
        _powerPointOnline = powerPointOnline;
        _jobs = jobs;
    }

    public async Task<PowerPointOnlineUpdateResult> UpdateAsync(
        PowerPointOnlineUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var phaseTimings = new PhaseTimingAccumulator();
        var totalStarted = Stopwatch.GetTimestamp();
        var actions = new List<string>();
        PowerPointOnlineSessionResult? templatePreparationSession = null;
        PowerPointOnlineSessionResult session;
        if (!string.IsNullOrWhiteSpace(request.DeckUrl))
        {
            session = await MeasureAsync(
                phaseTimings.AddOpenSession,
                () => _powerPointOnline.StartOnlineSessionAsync(
                    new PowerPointOnlineSessionStartRequest
                    {
                        DeckUrl = request.DeckUrl!,
                        SessionId = request.SessionId,
                        Capture = false,
                        WaitSeconds = request.OpenWaitSeconds,
                    },
                    cancellationToken));
            actions.Add("session_started");
        }
        else
        {
            session = await MeasureAsync(
                phaseTimings.AddOpenSession,
                () => _powerPointOnline.GetOnlineSessionAsync(request.SessionId!, cancellationToken));
            actions.Add("session_loaded");
        }

        if (session.Status != PowerPointOnlineSessionStatus.Ready)
        {
            actions.Add("session_blocked");
            return await BuildResultWithCleanupAsync(
                false,
                PowerPointOnlineUpdateStatus.BlockedSession,
                session,
                null,
                null,
                CreateNotQueuedJobRecord(request.Job),
                actions,
                session.Warnings,
                session.Errors,
                session.Evidence,
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        var boundJob = TryBindJobToSession(request.Job, session, actions, out var bindError);
        if (bindError is not null)
        {
            return await BuildResultWithCleanupAsync(
                false,
                PowerPointOnlineUpdateStatus.BlockedSession,
                session,
                null,
                null,
                CreateNotQueuedJobRecord(request.Job),
                actions,
                session.Warnings,
                Merge(session.Errors, new[] { bindError }),
                session.Evidence,
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        actions.Add("addin_probe_requested");
        var probe = await MeasureAsync(
            phaseTimings.AddAddInProbe,
            () => _powerPointOnline.ProbeOnlineAddInAsync(
                session.SessionId,
                new PowerPointOnlineAddInProbeRequest
                {
                    Capture = false,
                    ActivateIfNeeded = true,
                    ActivationTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                    HostTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                },
                cancellationToken));
        actions.AddRange(probe.Actions);
        session = probe.Session;
        if (!probe.Success || probe.Status != PowerPointOnlineAddInProbeStatus.Ready)
        {
            var evidenceSession = await CollectEvidenceAsync(session, request, phaseTimings, cancellationToken);
            actions.Add(BuildAddInProbeBlockedAction(probe.Status));
            actions.AddRange(evidenceSession.Actions);
            return await BuildResultWithCleanupAsync(
                false,
                PowerPointOnlineUpdateStatus.BlockedAddIn,
                evidenceSession,
                null,
                null,
                CreateNotQueuedJobRecord(boundJob!),
                actions,
                Merge(Merge(session.Warnings, probe.Warnings), evidenceSession.Warnings),
                Merge(Merge(session.Errors, probe.Errors), evidenceSession.Errors),
                Merge(probe.Evidence, evidenceSession.Evidence),
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        if (request.PrepareTemplate)
        {
            if (request.EvidenceSlideNumber is int templateSlideNumber)
            {
                actions.Add($"template_prepare_slide_select_requested:{templateSlideNumber}");
                var selected = await MeasureAsync(
                    phaseTimings.AddTemplatePreparation,
                    () => _powerPointOnline.SelectOnlineSlideAsync(
                        session.SessionId,
                        new PowerPointOnlineSlideSelectRequest
                        {
                            SlideNumber = templateSlideNumber,
                            Capture = false,
                            WaitSeconds = EvidenceSlideSelectWaitSeconds,
                            Label = "template-prepare-slide",
                        },
                        cancellationToken));
                actions.AddRange(selected.Actions);
                session = MergeSessionObservations(session, selected);
                var slideSelectionError = BuildTemplatePreparationSlideError(selected, templateSlideNumber);
                if (slideSelectionError is not null)
                {
                    actions.Add("template_prepare_slide_select_failed");
                    return await BuildResultWithCleanupAsync(
                        false,
                        PowerPointOnlineUpdateStatus.BlockedSession,
                        session,
                        null,
                        null,
                        CreateNotQueuedJobRecord(boundJob!),
                        actions,
                        session.Warnings,
                        Merge(session.Errors, new[] { slideSelectionError }),
                        session.Evidence,
                        request with { CleanupTemplate = false },
                        phaseTimings,
                        totalStarted,
                        cancellationToken);
                }
            }

            actions.Add("template_prepare_requested");
            templatePreparationSession = await MeasureAsync(
                phaseTimings.AddTemplatePreparation,
                () => _powerPointOnline.PrepareOnlineTemplateAsync(
                    session.SessionId,
                    new PowerPointOnlineTemplateRequest
                    {
                        Capture = false,
                        WaitSeconds = request.TemplateWaitSeconds,
                        AllowDeckMutation = true,
                        Label = "template-prepare",
                    },
                    cancellationToken));
            actions.AddRange(templatePreparationSession.Actions);
            session = templatePreparationSession;
            if (!templatePreparationSession.Success ||
                templatePreparationSession.Status != PowerPointOnlineSessionStatus.Ready)
            {
                var evidenceSession = await CollectEvidenceAsync(templatePreparationSession, request, phaseTimings, cancellationToken);
                actions.AddRange(evidenceSession.Actions);
                return await BuildResultWithCleanupAsync(
                    false,
                    PowerPointOnlineUpdateStatus.BlockedAddIn,
                    evidenceSession,
                    null,
                    templatePreparationSession,
                    CreateNotQueuedJobRecord(boundJob!),
                    actions,
                    Merge(templatePreparationSession.Warnings, evidenceSession.Warnings),
                    Merge(templatePreparationSession.Errors, evidenceSession.Errors),
                    Merge(templatePreparationSession.Evidence, evidenceSession.Evidence),
                    request,
                    phaseTimings,
                    totalStarted,
                    cancellationToken);
            }

            actions.Add("template_prepare_save_wait_requested");
            var templateSaveSession = await MeasureAsync(
                phaseTimings.AddTemplatePreparation,
                () => _powerPointOnline.WaitForOnlineSaveAsync(
                    session.SessionId,
                    new PowerPointOnlineSaveWaitRequest
                    {
                        TimeoutSeconds = request.SaveTimeoutSeconds,
                        PollSeconds = request.SavePollSeconds,
                        Capture = false,
                    },
                    cancellationToken));
            actions.AddRange(templateSaveSession.Actions);
            templatePreparationSession = templateSaveSession;
            session = templateSaveSession;
            if (!templateSaveSession.Success ||
                !string.Equals(templateSaveSession.SaveState, SavedState, StringComparison.OrdinalIgnoreCase))
            {
                var evidenceSession = await CollectEvidenceAsync(templateSaveSession, request, phaseTimings, cancellationToken);
                actions.AddRange(evidenceSession.Actions);
                return await BuildResultWithCleanupAsync(
                    false,
                    PowerPointOnlineUpdateStatus.SaveUnverified,
                    evidenceSession,
                    null,
                    templatePreparationSession,
                    CreateNotQueuedJobRecord(boundJob!),
                    actions,
                    Merge(templateSaveSession.Warnings, evidenceSession.Warnings),
                    Merge(templateSaveSession.Errors, evidenceSession.Errors),
                    Merge(templateSaveSession.Evidence, evidenceSession.Evidence),
                    request,
                    phaseTimings,
                    totalStarted,
                    cancellationToken);
            }

            actions.Add("template_prepare_addin_reprobe_requested");
            probe = await MeasureAsync(
                phaseTimings.AddAddInProbe,
                () => _powerPointOnline.ProbeOnlineAddInAsync(
                    session.SessionId,
                    new PowerPointOnlineAddInProbeRequest
                    {
                        Capture = false,
                        ActivateIfNeeded = true,
                        ActivationTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                        HostTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                    },
                    cancellationToken));
            actions.AddRange(probe.Actions);
            session = probe.Session;
            if (!probe.Success || probe.Status != PowerPointOnlineAddInProbeStatus.Ready)
            {
                var evidenceSession = await CollectEvidenceAsync(session, request, phaseTimings, cancellationToken);
                actions.Add(BuildAddInProbeBlockedAction(probe.Status));
                actions.AddRange(evidenceSession.Actions);
                return await BuildResultWithCleanupAsync(
                    false,
                    PowerPointOnlineUpdateStatus.BlockedAddIn,
                    evidenceSession,
                    null,
                    templatePreparationSession,
                    CreateNotQueuedJobRecord(boundJob!),
                    actions,
                    Merge(Merge(session.Warnings, probe.Warnings), evidenceSession.Warnings),
                    Merge(Merge(session.Errors, probe.Errors), evidenceSession.Errors),
                    Merge(probe.Evidence, evidenceSession.Evidence),
                    request,
                    phaseTimings,
                    totalStarted,
                    cancellationToken);
            }
        }

        var jobOutcome = await MeasureAsync(
            phaseTimings.AddJob,
            async () =>
            {
                var queuedJob = await _jobs.EnqueueAsync(boundJob!, cancellationToken);
                actions.Add("job_enqueued");
                actions.Add("addin_run_pending_job_command_requested");
                var runPendingSession = await _powerPointOnline.RunOnlinePendingJobAsync(
                    session.SessionId,
                    new PowerPointOnlineAddInCommandRequest
                    {
                        Capture = false,
                        WaitSeconds = 0,
                    },
                    cancellationToken);
                actions.AddRange(runPendingSession.Actions);
                session = MergeSessionObservations(session, runPendingSession);
                if (!runPendingSession.Success || runPendingSession.Status != PowerPointOnlineSessionStatus.Ready)
                {
                    var failedJob = await _jobs.FailAsync(
                        queuedJob.JobId,
                        new PowerPointUpdateError(
                            AddInRunCommandFailedCode,
                            true,
                            "PowerPoint add-in run command could not be clicked.",
                            "Office.js task pane did not expose or accept the Run Pending Job command after the job was queued."),
                        cancellationToken);
                    return new JobOutcome(runPendingSession, failedJob, false);
                }

                var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(request.JobTimeoutSeconds);
                while (true)
                {
                    var jobRecord = await _jobs.GetAsync(queuedJob.JobId, cancellationToken);
                    if (IsTerminal(jobRecord.Status))
                    {
                        return new JobOutcome(session, jobRecord, false);
                    }

                    if (DateTimeOffset.UtcNow >= deadlineUtc)
                    {
                        jobRecord = await HandleTimeoutAsync(queuedJob.JobId, cancellationToken);
                        return new JobOutcome(session, jobRecord, true);
                    }

                    if (request.PollSeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(request.PollSeconds), cancellationToken);
                    }
                }
            });

        session = jobOutcome.Session;
        var jobRecord = jobOutcome.JobRecord;
        if (string.Equals(jobRecord.Error?.Code, AddInRunCommandFailedCode, StringComparison.OrdinalIgnoreCase))
        {
            var evidenceSession = await CollectEvidenceAsync(jobOutcome.Session, request, phaseTimings, cancellationToken);
            actions.AddRange(evidenceSession.Actions);
            return await BuildResultWithCleanupAsync(
                false,
                PowerPointOnlineUpdateStatus.BlockedAddIn,
                evidenceSession,
                null,
                templatePreparationSession,
                jobRecord,
                actions,
                Merge(jobOutcome.Session.Warnings, evidenceSession.Warnings),
                Merge(jobOutcome.Session.Errors, evidenceSession.Errors),
                Merge(jobOutcome.Session.Evidence, evidenceSession.Evidence),
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        if (jobOutcome.TimedOut &&
            string.Equals(jobRecord.Error?.Code, AddInTimeoutCode, StringComparison.OrdinalIgnoreCase))
        {
            var timeoutSession = await CollectEvidenceAsync(session, request, phaseTimings, cancellationToken);
            actions.Add("job_timed_out");
            actions.AddRange(timeoutSession.Actions);
            return await BuildResultWithCleanupAsync(
                false,
                PowerPointOnlineUpdateStatus.BlockedAddIn,
                timeoutSession,
                null,
                templatePreparationSession,
                jobRecord,
                actions,
                timeoutSession.Warnings,
                BuildErrors(timeoutSession.Errors, jobRecord),
                timeoutSession.Evidence,
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        if (string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            actions.Add("save_wait_requested");
            var saveWaitSession = await MeasureAsync(
                phaseTimings.AddSave,
                () => _powerPointOnline.WaitForOnlineSaveAsync(
                    session.SessionId,
                    new PowerPointOnlineSaveWaitRequest
                    {
                        TimeoutSeconds = request.SaveTimeoutSeconds,
                        PollSeconds = request.SavePollSeconds,
                        Capture = false,
                    },
                    cancellationToken));
            actions.AddRange(saveWaitSession.Actions);
            if (!saveWaitSession.Success || !string.Equals(saveWaitSession.SaveState, SavedState, StringComparison.OrdinalIgnoreCase))
            {
                var evidenceSession = await CollectEvidenceAsync(saveWaitSession, request, phaseTimings, cancellationToken);
                actions.AddRange(evidenceSession.Actions);
                return await BuildResultWithCleanupAsync(
                    false,
                    PowerPointOnlineUpdateStatus.SaveUnverified,
                    evidenceSession,
                    null,
                    templatePreparationSession,
                    jobRecord,
                    actions,
                    evidenceSession.Warnings,
                    evidenceSession.Errors,
                    evidenceSession.Evidence,
                    request,
                    phaseTimings,
                    totalStarted,
                    cancellationToken);
            }

            session = saveWaitSession;
        }

        var finalSession = await CollectEvidenceAsync(session, request, phaseTimings, cancellationToken);
        actions.AddRange(finalSession.Actions);

        if (string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase) &&
            !EvidenceCollectionSucceeded(finalSession))
        {
            actions.Add("evidence_not_ready");
            return await BuildResultWithCleanupAsync(
                false,
                request.VerifyReopen
                    ? PowerPointOnlineUpdateStatus.VerificationFailed
                    : PowerPointOnlineUpdateStatus.BlockedSession,
                finalSession,
                null,
                templatePreparationSession,
                jobRecord,
                actions,
                finalSession.Warnings,
                BuildErrors(finalSession.Errors, jobRecord),
                finalSession.Evidence,
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        if (request.VerifyReopen && string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            var verification = await VerifyReopenAsync(finalSession, request, phaseTimings, cancellationToken);
            actions.AddRange(verification.Actions);

            var warnings = Merge(finalSession.Warnings, verification.Warnings);
            var errors = BuildErrors(Merge(finalSession.Errors, verification.Errors), jobRecord);
            var evidence = Merge(finalSession.Evidence, verification.Evidence);
            if (!verification.Success)
            {
                return await BuildResultWithCleanupAsync(
                    false,
                    PowerPointOnlineUpdateStatus.VerificationFailed,
                    finalSession,
                    verification.Session,
                    templatePreparationSession,
                    jobRecord,
                    actions,
                    warnings,
                    errors,
                    evidence,
                    request,
                    phaseTimings,
                    totalStarted,
                    cancellationToken);
            }

            return await BuildResultWithCleanupAsync(
                true,
                PowerPointOnlineUpdateStatus.Succeeded,
                finalSession,
                verification.Session,
                templatePreparationSession,
                jobRecord,
                actions,
                warnings,
                errors,
                evidence,
                request,
                phaseTimings,
                totalStarted,
                cancellationToken);
        }

        return await BuildResultWithCleanupAsync(
            string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase),
            string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase)
                ? PowerPointOnlineUpdateStatus.Succeeded
                : PowerPointOnlineUpdateStatus.Failed,
            finalSession,
            null,
            templatePreparationSession,
            jobRecord,
            actions,
            finalSession.Warnings,
            BuildErrors(finalSession.Errors, jobRecord),
            finalSession.Evidence,
            request,
            phaseTimings,
            totalStarted,
            cancellationToken);
    }

    private async Task<PowerPointJobRecord> HandleTimeoutAsync(string jobId, CancellationToken cancellationToken)
    {
        var latest = await _jobs.GetAsync(jobId, cancellationToken);
        if (IsTerminal(latest.Status))
        {
            return latest;
        }

        return await _jobs.FailAsync(
            jobId,
            new PowerPointUpdateError(
                AddInTimeoutCode,
                true,
                "PowerPoint add-in did not complete the queued update before timeout.",
                "Office.js task pane did not claim or finish the queued job before the orchestration timeout elapsed."),
            cancellationToken);
    }

    private async Task<VerificationAttemptResult> VerifyReopenAsync(
        PowerPointOnlineSessionResult session,
        PowerPointOnlineUpdateRequest request,
        PhaseTimingAccumulator phaseTimings,
        CancellationToken cancellationToken) =>
        await MeasureAsync(
            phaseTimings.AddVerificationReopen,
            async () =>
            {
                var actions = new List<string> { "verification_cleanup_requested" };
                var cleanupSession = await _powerPointOnline.CleanupOnlineSessionAsync(session.SessionId, cancellationToken);
                actions.AddRange(cleanupSession.Actions);
                if (!cleanupSession.Success || cleanupSession.Status != PowerPointOnlineSessionStatus.Closed)
                {
                    actions.Add("verification_cleanup_failed");
                    return new VerificationAttemptResult(
                        false,
                        null,
                        actions,
                        cleanupSession.Warnings,
                        cleanupSession.Errors,
                        cleanupSession.Evidence);
                }

                actions.Add("verification_reopen_requested");
                var verificationSession = await _powerPointOnline.StartOnlineSessionAsync(
                    new PowerPointOnlineSessionStartRequest
                    {
                        DeckUrl = ResolveSessionDocumentUrl(session) ?? session.DeckUrl,
                        SessionId = BuildVerificationSessionId(session.SessionId),
                        Capture = false,
                        WaitSeconds = request.ReopenWaitSeconds,
                    },
                    cancellationToken);
                if (!verificationSession.Success || verificationSession.Status != PowerPointOnlineSessionStatus.Ready)
                {
                    actions.AddRange(verificationSession.Actions);
                    actions.Add("verification_reopen_failed");
                    return new VerificationAttemptResult(
                        false,
                        verificationSession,
                        actions,
                        Merge(cleanupSession.Warnings, verificationSession.Warnings),
                        Merge(cleanupSession.Errors, verificationSession.Errors),
                        Merge(cleanupSession.Evidence, verificationSession.Evidence));
                }

                var evidenceSession = await CollectEvidenceAsync(verificationSession, request, phaseTimings, cancellationToken);
                actions.AddRange(evidenceSession.Actions);
                if (!EvidenceCollectionSucceeded(evidenceSession))
                {
                    actions.Add("verification_evidence_not_ready");
                    return new VerificationAttemptResult(
                        false,
                        evidenceSession,
                        actions,
                        Merge(cleanupSession.Warnings, evidenceSession.Warnings),
                        Merge(cleanupSession.Errors, evidenceSession.Errors),
                        Merge(cleanupSession.Evidence, evidenceSession.Evidence));
                }

                actions.Add("verification_reopen_ready");
                return new VerificationAttemptResult(
                    true,
                    evidenceSession,
                    actions,
                    Merge(cleanupSession.Warnings, evidenceSession.Warnings),
                    Merge(cleanupSession.Errors, evidenceSession.Errors),
                    Merge(cleanupSession.Evidence, evidenceSession.Evidence));
            });

    private async Task<PowerPointOnlineSessionResult> CollectEvidenceAsync(
        PowerPointOnlineSessionResult session,
        PowerPointOnlineUpdateRequest request,
        PhaseTimingAccumulator phaseTimings,
        CancellationToken cancellationToken,
        string screenshotLabel = "powerpoint-online-update") =>
        await MeasureAsync(
            phaseTimings.AddEvidence,
            async () =>
            {
                var current = session;
                if (request.EvidenceSlideNumber is int slideNumber)
                {
                    var selected = await _powerPointOnline.SelectOnlineSlideAsync(
                        session.SessionId,
                        new PowerPointOnlineSlideSelectRequest
                        {
                            SlideNumber = slideNumber,
                            Capture = false,
                            // PowerPoint Online slide navigation settles asynchronously; wait briefly before capture.
                            WaitSeconds = EvidenceSlideSelectWaitSeconds,
                        },
                        cancellationToken);
                    current = MergeSessionObservations(current, selected);
                    var slideSelectionError = BuildSlideSelectionError(selected, slideNumber, "for evidence capture");
                    if (slideSelectionError is not null)
                    {
                        return current with
                        {
                            Success = false,
                            Status = PowerPointOnlineSessionStatus.Failed,
                            Actions = Merge(current.Actions, new[] { "evidence_slide_select_failed" }),
                            Errors = Merge(current.Errors, new[] { slideSelectionError }),
                        };
                    }
                }

                if (!request.Capture)
                {
                    return current with { Evidence = Array.Empty<DesktopScreenshotResult>() };
                }

                var screenshot = await _powerPointOnline.CaptureOnlineSessionScreenshotAsync(
                    session.SessionId,
                    new PowerPointOnlineSessionScreenshotRequest
                    {
                        Label = screenshotLabel,
                    },
                    cancellationToken);

                return MergeSessionObservations(current, screenshot);
            });

    private static PowerPointOnlineSessionResult MergeSessionObservations(
        PowerPointOnlineSessionResult baseline,
        PowerPointOnlineSessionResult observed) =>
        observed with
        {
            DeckUrl = string.IsNullOrWhiteSpace(observed.DeckUrl) ? baseline.DeckUrl : observed.DeckUrl,
            CurrentTitle = observed.CurrentTitle ?? baseline.CurrentTitle,
            CurrentUrl = observed.CurrentUrl ?? baseline.CurrentUrl,
            CanonicalUrl = observed.CanonicalUrl ?? baseline.CanonicalUrl,
            CurrentSlide = observed.CurrentSlide ?? baseline.CurrentSlide,
            SlideCount = observed.SlideCount ?? baseline.SlideCount,
            EditMode = observed.EditMode ?? baseline.EditMode,
            SaveState = observed.SaveState ?? baseline.SaveState,
            BrowserSessionId = observed.BrowserSessionId ?? baseline.BrowserSessionId,
            Hwnd = observed.Hwnd ?? baseline.Hwnd,
            ArtifactRoot = observed.ArtifactRoot ?? baseline.ArtifactRoot,
            Actions = Merge(baseline.Actions, observed.Actions),
            Warnings = Merge(baseline.Warnings, observed.Warnings),
            Errors = Merge(baseline.Errors, observed.Errors),
            Evidence = Merge(baseline.Evidence, observed.Evidence),
        };

    private static IReadOnlyList<OperatorError> BuildErrors(
        IReadOnlyList<OperatorError> sessionErrors,
        PowerPointJobRecord jobRecord)
    {
        var errors = new List<OperatorError>(sessionErrors);
        if (jobRecord.Error is not null)
        {
            errors.Add(OperatorErrors.PowerPointUnavailable(jobRecord.Error.OperatorMessage));
        }

        return errors;
    }

    private static OperatorError? BuildTemplatePreparationSlideError(
        PowerPointOnlineSessionResult selected,
        int requestedSlide)
    {
        if (!selected.Success || selected.Status != PowerPointOnlineSessionStatus.Ready)
        {
            return OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online could not select slide {requestedSlide} before template preparation.");
        }

        if (selected.CurrentSlide != requestedSlide)
        {
            var observed = selected.CurrentSlide?.ToString() ?? "unknown";
            return OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online selected slide {observed}, not requested slide {requestedSlide}, before template preparation.");
        }

        return null;
    }

    private static OperatorError? BuildSlideSelectionError(
        PowerPointOnlineSessionResult selected,
        int requestedSlide,
        string context)
    {
        if (!selected.Success || selected.Status != PowerPointOnlineSessionStatus.Ready)
        {
            return OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online could not select slide {requestedSlide} {context}.");
        }

        if (selected.CurrentSlide != requestedSlide)
        {
            var observed = selected.CurrentSlide?.ToString() ?? "unknown";
            return OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online selected slide {observed}, not requested slide {requestedSlide}, {context}.");
        }

        return null;
    }

    private static PowerPointUpdateJob? TryBindJobToSession(
        PowerPointUpdateJob job,
        PowerPointOnlineSessionResult session,
        ICollection<string> actions,
        out OperatorError? error)
    {
        error = null;
        var sessionUrl = ResolveSessionDocumentUrl(session);
        if (string.IsNullOrWhiteSpace(sessionUrl))
        {
            actions.Add("job_document_mismatch");
            error = OperatorErrors.PowerPointUnavailable("PowerPoint Online session did not expose a document URL for queue binding.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(job.ExpectedDocumentUrl))
        {
            actions.Add("job_bound_to_session");
            return job with { ExpectedDocumentUrl = sessionUrl };
        }

        var expectedIdentity = NormalizeDocumentIdentity(job.ExpectedDocumentUrl);
        var sessionIdentities = new[]
            {
                session.CanonicalUrl,
                session.CurrentUrl,
                session.DeckUrl,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeDocumentIdentity(value!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (sessionIdentities.Contains(expectedIdentity, StringComparer.Ordinal))
        {
            return job;
        }

        actions.Add("job_document_mismatch");
        error = OperatorErrors.PowerPointValidationFailed(
            $"Job expectedDocumentUrl does not match session document identity. expected={job.ExpectedDocumentUrl}; sessionCanonical={session.CanonicalUrl ?? ""}; sessionCurrent={session.CurrentUrl ?? ""}; sessionDeck={session.DeckUrl}");
        return null;
    }

    private async Task<PowerPointOnlineUpdateResult> BuildResultWithCleanupAsync(
        bool success,
        PowerPointOnlineUpdateStatus status,
        PowerPointOnlineSessionResult session,
        PowerPointOnlineSessionResult? verificationSession,
        PowerPointOnlineSessionResult? templatePreparationSession,
        PowerPointJobRecord jobRecord,
        List<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<OperatorError> errors,
        IReadOnlyList<DesktopScreenshotResult> evidence,
        PowerPointOnlineUpdateRequest request,
        PhaseTimingAccumulator phaseTimings,
        long totalStarted,
        CancellationToken cancellationToken)
    {
        PowerPointOnlineSessionResult? templateCleanupSession = null;
        PowerPointOnlineSessionResult? sessionCleanupSession = null;
        if (request.CleanupTemplate && (success || request.CleanupTemplateOnFailure))
        {
            var cleanupTarget = verificationSession?.Status == PowerPointOnlineSessionStatus.Ready
                ? verificationSession
                : session;
            templateCleanupSession = await CleanupTemplateAsync(cleanupTarget, request, actions, phaseTimings, cancellationToken);
            warnings = Merge(warnings, templateCleanupSession.Warnings);
            errors = Merge(errors, templateCleanupSession.Errors);
            evidence = Merge(evidence, templateCleanupSession.Evidence);

            if (success && !TemplateCleanupSucceeded(templateCleanupSession))
            {
                success = false;
                status = PowerPointOnlineUpdateStatus.CleanupFailed;
            }
        }

        if (request.CleanupSession)
        {
            var sessionCleanupTarget = templateCleanupSession ?? verificationSession ?? session;
            sessionCleanupSession = await CleanupSessionAsync(sessionCleanupTarget, actions, phaseTimings, cancellationToken);
            warnings = Merge(warnings, sessionCleanupSession.Warnings);
            errors = Merge(errors, sessionCleanupSession.Errors);
            evidence = Merge(evidence, sessionCleanupSession.Evidence);

            if (success && !SessionCleanupSucceeded(sessionCleanupSession))
            {
                success = false;
                status = PowerPointOnlineUpdateStatus.SessionCleanupFailed;
            }
        }

        return BuildResult(
            success,
            status,
            session,
            verificationSession,
            templatePreparationSession,
            templateCleanupSession,
            sessionCleanupSession,
            jobRecord,
            BuildPhaseTimings(phaseTimings, totalStarted),
            actions,
            warnings,
            errors,
            evidence);
    }

    private async Task<PowerPointOnlineSessionResult> CleanupTemplateAsync(
        PowerPointOnlineSessionResult target,
        PowerPointOnlineUpdateRequest request,
        List<string> actions,
        PhaseTimingAccumulator phaseTimings,
        CancellationToken cancellationToken) =>
        await MeasureAsync(
            phaseTimings.AddTemplateCleanup,
            async () =>
            {
                if (target.Status != PowerPointOnlineSessionStatus.Ready)
                {
                    actions.Add("template_cleanup_skipped:session_not_ready");
                    return target with
                    {
                        Success = false,
                        Actions = Merge(target.Actions, new[] { "template_cleanup_skipped:session_not_ready" }),
                        Errors = Merge(target.Errors, new[]
                        {
                            OperatorErrors.PowerPointUnavailable("PowerPoint Online session was not ready for template cleanup."),
                        }),
                    };
                }

                actions.Add("template_cleanup_addin_probe_requested");
                var probe = await _powerPointOnline.ProbeOnlineAddInAsync(
                    target.SessionId,
                    new PowerPointOnlineAddInProbeRequest
                    {
                        Capture = false,
                        ActivateIfNeeded = true,
                        ActivationTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                        HostTimeoutSeconds = Math.Clamp(request.JobTimeoutSeconds, 1, 60),
                    },
                    cancellationToken);
                actions.AddRange(probe.Actions);
                if (!probe.Success || probe.Status != PowerPointOnlineAddInProbeStatus.Ready)
                {
                    var blockedAction = $"template_cleanup_addin_probe_blocked:{ToCamelCase(probe.Status.ToString())}";
                    actions.Add(blockedAction);
                    return probe.Session with
                    {
                        Success = false,
                        Actions = Merge(Merge(probe.Session.Actions, probe.Actions), new[] { blockedAction }),
                        Warnings = Merge(probe.Session.Warnings, probe.Warnings),
                        Errors = Merge(probe.Session.Errors, probe.Errors),
                        Evidence = Merge(probe.Session.Evidence, probe.Evidence),
                    };
                }

                actions.Add("template_cleanup_requested");
                var cleanupSession = await _powerPointOnline.CleanupOnlineTemplateAsync(
                    probe.Session.SessionId,
                    new PowerPointOnlineTemplateRequest
                    {
                        Capture = false,
                        WaitSeconds = request.TemplateWaitSeconds,
                        AllowDeckMutation = true,
                        Label = "template-cleanup",
                    },
                    cancellationToken);
                actions.AddRange(cleanupSession.Actions);
                if (!cleanupSession.Success || cleanupSession.Status != PowerPointOnlineSessionStatus.Ready)
                {
                    return cleanupSession with
                    {
                        Warnings = Merge(Merge(probe.Warnings, probe.Session.Warnings), cleanupSession.Warnings),
                        Errors = Merge(Merge(probe.Errors, probe.Session.Errors), cleanupSession.Errors),
                        Evidence = Merge(Merge(probe.Evidence, probe.Session.Evidence), cleanupSession.Evidence),
                    };
                }

                actions.Add("template_cleanup_save_wait_requested");
                var saveWaitSession = await _powerPointOnline.WaitForOnlineSaveAsync(
                    cleanupSession.SessionId,
                    new PowerPointOnlineSaveWaitRequest
                    {
                        TimeoutSeconds = request.SaveTimeoutSeconds,
                        PollSeconds = request.SavePollSeconds,
                        Capture = false,
                    },
                    cancellationToken);
                actions.AddRange(saveWaitSession.Actions);
                if (!TemplateCleanupSucceeded(saveWaitSession))
                {
                    actions.Add("template_cleanup_save_unverified");
                }

                var result = saveWaitSession with
                {
                    Warnings = Merge(Merge(probe.Warnings, cleanupSession.Warnings), saveWaitSession.Warnings),
                    Errors = Merge(Merge(probe.Errors, cleanupSession.Errors), saveWaitSession.Errors),
                    Evidence = Merge(Merge(probe.Evidence, cleanupSession.Evidence), saveWaitSession.Evidence),
                };

                if (!request.Capture || !TemplateCleanupSucceeded(result))
                {
                    return result;
                }

                actions.Add("template_cleanup_evidence_requested");
                return await CollectEvidenceAsync(
                    result,
                    request,
                    phaseTimings,
                    cancellationToken,
                    "powerpoint-online-template-cleanup");
            });

    private async Task<PowerPointOnlineSessionResult> CleanupSessionAsync(
        PowerPointOnlineSessionResult target,
        List<string> actions,
        PhaseTimingAccumulator phaseTimings,
        CancellationToken cancellationToken) =>
        await MeasureAsync(
            phaseTimings.AddSessionCleanup,
            async () =>
            {
                actions.Add("session_cleanup_requested");
                var cleanupSession = await _powerPointOnline.CleanupOnlineSessionAsync(target.SessionId, cancellationToken);
                actions.AddRange(cleanupSession.Actions);
                if (!SessionCleanupSucceeded(cleanupSession))
                {
                    actions.Add("session_cleanup_failed");
                }

                return cleanupSession;
            });

    private static PowerPointOnlineUpdateResult BuildResult(
        bool success,
        PowerPointOnlineUpdateStatus status,
        PowerPointOnlineSessionResult session,
        PowerPointOnlineSessionResult? verificationSession,
        PowerPointOnlineSessionResult? templatePreparationSession,
        PowerPointOnlineSessionResult? templateCleanupSession,
        PowerPointOnlineSessionResult? sessionCleanupSession,
        PowerPointJobRecord jobRecord,
        PowerPointOnlineUpdatePhaseTimings phaseTimings,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<OperatorError> errors,
        IReadOnlyList<DesktopScreenshotResult> evidence) =>
        new()
        {
            Success = success,
            Status = status,
            SaveProofTier = ComputeSaveProofTier(session, verificationSession, jobRecord),
            Session = session,
            VerificationSession = verificationSession,
            TemplatePreparationSession = templatePreparationSession,
            TemplateCleanupSession = templateCleanupSession,
            SessionCleanupSession = sessionCleanupSession,
            JobRecord = jobRecord,
            PhaseTimings = phaseTimings,
            Evidence = evidence,
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };

    private static PowerPointOnlineUpdatePhaseTimings BuildPhaseTimings(
        PhaseTimingAccumulator accumulator,
        long totalStarted) =>
        new()
        {
            TotalMs = ElapsedMilliseconds(totalStarted),
            OpenSessionMs = accumulator.OpenSessionMs,
            AddInProbeMs = accumulator.AddInProbeMs,
            TemplatePreparationMs = accumulator.TemplatePreparationMs,
            JobMs = accumulator.JobMs,
            SaveMs = accumulator.SaveMs,
            EvidenceMs = accumulator.EvidenceMs,
            VerificationReopenMs = accumulator.VerificationReopenMs,
            TemplateCleanupMs = accumulator.TemplateCleanupMs,
            SessionCleanupMs = accumulator.SessionCleanupMs,
        };

    private static PowerPointOnlineSaveProofTier ComputeSaveProofTier(
        PowerPointOnlineSessionResult session,
        PowerPointOnlineSessionResult? verificationSession,
        PowerPointJobRecord jobRecord)
    {
        if (verificationSession?.Success == true &&
            verificationSession.Status == PowerPointOnlineSessionStatus.Ready &&
            verificationSession.Evidence.Count > 0)
        {
            return PowerPointOnlineSaveProofTier.Tier3ReopenVisual;
        }

        if (string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(session.SaveState, SavedState, StringComparison.OrdinalIgnoreCase))
        {
            return PowerPointOnlineSaveProofTier.Tier2SavedIndicator;
        }

        if (string.Equals(jobRecord.Status, Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            return PowerPointOnlineSaveProofTier.Tier1OfficeJsSync;
        }

        return PowerPointOnlineSaveProofTier.Tier0VisualOpen;
    }

    private static PowerPointJobRecord CreateNotQueuedJobRecord(PowerPointUpdateJob job) =>
        new()
        {
            JobId = job.JobId,
            Status = NotQueued,
            Job = job,
            EnqueuedAtUtc = job.CreatedAt,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async Task<T> MeasureAsync<T>(Action<long> record, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await action();
        record(ElapsedMilliseconds(started));
        return result;
    }

    private static long ElapsedMilliseconds(long startedTimestamp) =>
        (long)TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startedTimestamp) / (double)Stopwatch.Frequency).TotalMilliseconds;

    private static bool IsTerminal(string status) =>
        string.Equals(status, Succeeded, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    private static bool TemplateCleanupSucceeded(PowerPointOnlineSessionResult session) =>
        session.Success &&
        session.Status == PowerPointOnlineSessionStatus.Ready &&
        string.Equals(session.SaveState, SavedState, StringComparison.OrdinalIgnoreCase);

    private static bool EvidenceCollectionSucceeded(PowerPointOnlineSessionResult session) =>
        session.Success &&
        session.Status == PowerPointOnlineSessionStatus.Ready;

    private static bool SessionCleanupSucceeded(PowerPointOnlineSessionResult session) =>
        session.Success && session.Status == PowerPointOnlineSessionStatus.Closed;

    private static string BuildAddInProbeBlockedAction(PowerPointOnlineAddInProbeStatus status)
    {
        var name = ToCamelCase(status.ToString());
        return name.Length == 0 ? "addin_probe_blocked" : $"addin_probe_blocked:{name}";
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : $"{char.ToLowerInvariant(name[0])}{name[1..]}";

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> first, IReadOnlyList<T> second) =>
        first.Count == 0 ? second : second.Count == 0 ? first : first.Concat(second).ToArray();

    private static string? ResolveSessionDocumentUrl(PowerPointOnlineSessionResult session) =>
        new[] { session.CanonicalUrl, session.CurrentUrl, session.DeckUrl }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string BuildVerificationSessionId(string sessionId) => $"{sessionId}-verification";

    private static string NormalizeDocumentIdentity(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return value.Trim().TrimEnd('/').ToLowerInvariant();
        }

        var path = string.IsNullOrWhiteSpace(uri.AbsolutePath)
            ? "/"
            : uri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        var query = NormalizeQueryIdentity(uri.Query);
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority.ToLowerInvariant()}{path.ToLowerInvariant()}{query}";
    }

    private static string NormalizeQueryIdentity(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var pairs = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment =>
            {
                var parts = segment.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                return new KeyValuePair<string, string>(key, value);
            })
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.Key.ToLowerInvariant())}={Uri.EscapeDataString(pair.Value.ToLowerInvariant())}")
            .ToArray();

        return pairs.Length == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    private static void ValidateRequest(PowerPointOnlineUpdateRequest request)
    {
        if (request is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint Online update request is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DeckUrl) && string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Either deckUrl or sessionId is required."));
        }

        if (request.Job is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint update job is required."));
        }

        if (request.OpenWaitSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("openWaitSeconds must be zero or greater."));
        }

        if (request.JobTimeoutSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("jobTimeoutSeconds must be zero or greater."));
        }

        if (request.PollSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("pollSeconds must be zero or greater."));
        }

        if (request.SaveTimeoutSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("saveTimeoutSeconds must be zero or greater."));
        }

        if (request.SavePollSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("savePollSeconds must be zero or greater."));
        }

        if (request.ReopenWaitSeconds < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("reopenWaitSeconds must be zero or greater."));
        }

        if (request.EvidenceSlideNumber is <= 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("evidenceSlideNumber must be greater than zero when provided."));
        }

        if (RequiresDeckMutation(request) && !request.AllowDeckMutation)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed(
                "allowDeckMutation must be true for executable jobs or template prepare/cleanup because PowerPoint Online changes are saved to the deck."));
        }
    }

    private static bool RequiresDeckMutation(PowerPointOnlineUpdateRequest request) =>
        request.PrepareTemplate ||
        request.CleanupTemplate ||
        request.Job.BindNamedTargets ||
        (!request.Job.ValidateOnly && request.Job.Operations?.Any(IsMutatingOperation) is true);

    private static bool IsMutatingOperation(PowerPointUpdateOperation operation) =>
        operation.Kind is "replaceText" or "replaceImage" or "replaceTableCell" or "replaceTableRange" or "setShapeBounds";

    private sealed record VerificationAttemptResult(
        bool Success,
        PowerPointOnlineSessionResult? Session,
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<OperatorError> Errors,
        IReadOnlyList<DesktopScreenshotResult> Evidence);

    private sealed record JobOutcome(
        PowerPointOnlineSessionResult Session,
        PowerPointJobRecord JobRecord,
        bool TimedOut);

    private sealed class PhaseTimingAccumulator
    {
        public long? OpenSessionMs { get; private set; }

        public long? AddInProbeMs { get; private set; }

        public long? TemplatePreparationMs { get; private set; }

        public long? JobMs { get; private set; }

        public long? SaveMs { get; private set; }

        public long? EvidenceMs { get; private set; }

        public long? VerificationReopenMs { get; private set; }

        public long? TemplateCleanupMs { get; private set; }

        public long? SessionCleanupMs { get; private set; }

        public void AddOpenSession(long value) => OpenSessionMs = Sum(OpenSessionMs, value);

        public void AddAddInProbe(long value) => AddInProbeMs = Sum(AddInProbeMs, value);

        public void AddTemplatePreparation(long value) => TemplatePreparationMs = Sum(TemplatePreparationMs, value);

        public void AddJob(long value) => JobMs = Sum(JobMs, value);

        public void AddSave(long value) => SaveMs = Sum(SaveMs, value);

        public void AddEvidence(long value) => EvidenceMs = Sum(EvidenceMs, value);

        public void AddVerificationReopen(long value) => VerificationReopenMs = Sum(VerificationReopenMs, value);

        public void AddTemplateCleanup(long value) => TemplateCleanupMs = Sum(TemplateCleanupMs, value);

        public void AddSessionCleanup(long value) => SessionCleanupMs = Sum(SessionCleanupMs, value);

        private static long Sum(long? current, long value) => (current ?? 0) + value;
    }
}
