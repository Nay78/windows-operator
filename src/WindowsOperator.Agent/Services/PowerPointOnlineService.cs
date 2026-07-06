using System.Text.Json;
using System.Text.RegularExpressions;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class PowerPointOnlineService : IPowerPointOnlineService
{
    private const string AddInCommandChannel = "windows-operator.powerpoint-addin";
    private const string RunPendingJobCommandName = "runPendingJob";
    private static readonly JsonSerializerOptions AddInCommandJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly TimeSpan AddInActivationPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan AddInButtonObservationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AddInCommandSignalTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlideKeyboardStepDelay = TimeSpan.FromMilliseconds(75);
    private const int MaxKeyboardSlideSteps = 120;
    private const double ThumbnailRailCenterXAt1296 = 140d;
    private const double ThumbnailFirstCenterYAt776 = 317d;
    private const double ThumbnailStepYAt776 = 122d;
    private const double ReferenceWidth = 1296d;
    private const double ReferenceHeight = 776d;
    private static readonly Regex SlideCountRegex = new(@"^Slide\s+(?<current>\d+)\s+of\s+(?<count>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IEdgeBrowserService _edgeBrowserService;
    private readonly IInputService _inputService;
    private readonly IPowerPointOnlineAddInHostProbe _addInHostProbe;
    private readonly IUiAutomationService _uiAutomationService;
    private readonly WorkbenchRunStore _runs;
    private readonly IWorkbenchService _workbenchService;
    private readonly IEdgeDevToolsService? _devToolsService;

    public PowerPointOnlineService(
        IEdgeBrowserService edgeBrowserService,
        IInputService inputService,
        IPowerPointOnlineAddInHostProbe addInHostProbe,
        IUiAutomationService uiAutomationService,
        WorkbenchRunStore runs,
        IWorkbenchService workbenchService)
        : this(edgeBrowserService, inputService, addInHostProbe, uiAutomationService, runs, workbenchService, null)
    {
    }

    internal PowerPointOnlineService(
        IEdgeBrowserService edgeBrowserService,
        IInputService inputService,
        IPowerPointOnlineAddInHostProbe addInHostProbe,
        IUiAutomationService uiAutomationService,
        WorkbenchRunStore runs,
        IWorkbenchService workbenchService,
        IEdgeDevToolsService? devToolsService)
    {
        _edgeBrowserService = edgeBrowserService;
        _inputService = inputService;
        _addInHostProbe = addInHostProbe;
        _uiAutomationService = uiAutomationService;
        _runs = runs;
        _workbenchService = workbenchService;
        _devToolsService = devToolsService;
    }

    public async Task<PowerPointOnlineSessionResult> StartOnlineSessionAsync(
        PowerPointOnlineSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var deckUrl = NormalizeDeckUrl(request.DeckUrl);
        var sessionId = WorkbenchRunStore.SanitizePathSegment(
            request.SessionId,
            $"powerpoint-online-{Guid.NewGuid():N}");
        var run = _runs.ResolveRun(request.RunId ?? sessionId, "powerpoint-online");
        var metadata = TryReadSessionMetadata(sessionId);
        BrowserEdgeSessionStateResult state;
        var actions = new List<string>();
        var warnings = new List<string>();

        if (metadata is not null)
        {
            try
            {
                state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
                if (ShouldRecreateSession(state))
                {
                    state = await StartBrowserSessionAsync(sessionId, deckUrl, run.RunId, request, cancellationToken);
                    actions.Add("session_recreated_stale_closed");
                }
                else
                {
                    actions.Add("session_reused");
                    if (!UrlsMatch(deckUrl, state.Url))
                    {
                        state = await _edgeBrowserService.NavigateSessionAsync(
                            metadata.BrowserSessionId,
                            new BrowserEdgeSessionNavigateRequest
                            {
                                Url = deckUrl,
                                WaitSeconds = Math.Clamp(request.WaitSeconds, 0, 30),
                            },
                            cancellationToken);
                        actions.Add("deck_navigated");
                    }
                }
            }
            catch (OperatorFailureException)
            {
                state = await StartBrowserSessionAsync(sessionId, deckUrl, run.RunId, request, cancellationToken);
                actions.Add("session_recreated");
            }
        }
        else
        {
            state = await StartBrowserSessionAsync(sessionId, deckUrl, run.RunId, request, cancellationToken);
            actions.Add("session_started");
        }

        var result = await BuildSessionResultAsync(
            sessionId,
            run,
            deckUrl,
            state,
            request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
            actions,
            warnings,
            cancellationToken);
        WriteSessionMetadata(result);
        return result;
    }

    public async Task<PowerPointOnlineSessionResult> GetOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var metadata = RequireSessionMetadata(sessionId);
        try
        {
            var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
            var result = await BuildSessionResultAsync(
                metadata.SessionId,
                metadata.ArtifactRoot,
                metadata.DeckUrl,
                state,
                null,
                new[] { "session_state_observed" },
                Array.Empty<string>(),
                cancellationToken);
            WriteSessionMetadata(result);
            return result;
        }
        catch (OperatorFailureException failure) when (metadata.Status == PowerPointOnlineSessionStatus.Closed)
        {
            var result = StoredResult(
                metadata,
                new[] { "session_state_from_cache" },
                new[] { failure.Error.Message },
                Array.Empty<OperatorError>());
            WriteSessionMetadata(result);
            return result;
        }
    }

    public async Task<PowerPointOnlineSessionResult> SelectOnlineSlideAsync(
        string sessionId,
        PowerPointOnlineSlideSelectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SlideNumber <= 0)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"slideNumber must be >= 1. Received {request.SlideNumber}."));
        }

        var metadata = RequireSessionMetadata(sessionId);
        var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var status = ClassifyState(state);
        if (status != PowerPointOnlineSessionStatus.Ready)
        {
            var blocked = await BuildSessionResultAsync(
                metadata.SessionId,
                metadata.ArtifactRoot,
                metadata.DeckUrl,
                state,
                request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
                new[] { $"slide_select_skipped:{request.SlideNumber}" },
                new[] { $"session_not_ready:{status}" },
                cancellationToken);
            WriteSessionMetadata(blocked);
            return blocked;
        }

        var actions = new List<string> { $"slide_select_requested:{request.SlideNumber}" };
        var warnings = new List<string>();
        BrowserEdgeSessionDomActionResult? domResult = null;
        ActionResult? thumbnailClick = null;
        var clickDispatched = false;
        if (ShouldAttemptDomSlideSelection(state.Actions))
        {
            foreach (var clickRequest in SlideClickRequests(request.SlideNumber))
            {
                domResult = await _edgeBrowserService.ClickDomAsync(metadata.BrowserSessionId, clickRequest, cancellationToken);
                actions.AddRange(domResult.Actions);
                if (domResult.Success)
                {
                    actions.Add($"slide_click_dispatched:{request.SlideNumber}");
                    clickDispatched = true;
                    break;
                }

                warnings.AddRange(domResult.Errors);
            }
        }
        else
        {
            actions.Add($"slide_select_dom_skipped:{request.SlideNumber}");
            actions.AddRange(state.Actions.Where(action => action.StartsWith("devtools_status:", StringComparison.Ordinal)));
        }

        if (domResult is not { Success: true })
        {
            actions.Add($"slide_select_dom_unavailable:{request.SlideNumber}");
            var geometryCapture = await _workbenchService.CaptureEdgeSessionScreenshotAsync(
                metadata.BrowserSessionId,
                new DesktopScreenshotRequest
                {
                    RunId = metadata.ArtifactRoot.RunId,
                    Label = $"slide-select-{request.SlideNumber}-geometry",
                    Format = ScreenshotFormat.Png,
                },
                cancellationToken);
            var point = ComputeThumbnailClickPoint(geometryCapture.Window.Bounds, request.SlideNumber);
            thumbnailClick = await _inputService.ClickScreenAsync(
                new ScreenClickRequest
                {
                    X = point.X,
                    Y = point.Y,
                },
                cancellationToken);
            actions.Add($"slide_select_thumbnail_click:{request.SlideNumber}:{point.X}:{point.Y}");
            if (thumbnailClick.Success)
            {
                actions.Add($"slide_click_dispatched:{request.SlideNumber}");
                clickDispatched = true;
            }
            else
            {
                warnings.Add(thumbnailClick.Message);
            }
        }

        if (request.WaitSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(request.WaitSeconds, 0, 30)), cancellationToken);
        }

        var observedAfterClick = await ObserveSlideSelectionAsync(metadata, actions, warnings, cancellationToken);
        if (TryCreateKeyboardCorrection(observedAfterClick, request.SlideNumber, clickDispatched, out var correction))
        {
            await ApplyKeyboardSlideCorrectionAsync(correction, actions, warnings, cancellationToken);
        }

        state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var result = await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
            actions,
            warnings,
            cancellationToken);
        var verification = BuildSlideSelectionVerification(metadata.SessionId, request.SlideNumber, clickDispatched, result);
        if (verification.Errors.Count > 0 || verification.Warnings.Count > 0 || verification.Actions.Count > 0)
        {
            result = result with
            {
                Success = result.Success && verification.Errors.Count == 0,
                Actions = MergeDistinct(result.Actions, verification.Actions),
                Warnings = MergeDistinct(result.Warnings, verification.Warnings),
                Errors = Merge(result.Errors, verification.Errors),
            };
        }

        WriteSessionMetadata(result);
        return result;
    }

    public async Task<PowerPointOnlineSessionResult> CaptureOnlineSessionScreenshotAsync(
        string sessionId,
        PowerPointOnlineSessionScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new PowerPointOnlineSessionScreenshotRequest();
        var metadata = RequireSessionMetadata(sessionId);
        var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var result = await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            request,
            new[] { "screenshot_requested" },
            Array.Empty<string>(),
            cancellationToken);
        WriteSessionMetadata(result);
        return result;
    }

    public async Task<PowerPointOnlineSessionResult> WaitForOnlineSaveAsync(
        string sessionId,
        PowerPointOnlineSaveWaitRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new PowerPointOnlineSaveWaitRequest();

        var metadata = RequireSessionMetadata(sessionId);
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 0, 120);
        var pollSeconds = Math.Clamp(request.PollSeconds, 1, 10);
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
            var status = ClassifyState(state);
            if (status != PowerPointOnlineSessionStatus.Ready)
            {
                var skipped = await BuildSessionResultAsync(
                    metadata.SessionId,
                    metadata.ArtifactRoot,
                    metadata.DeckUrl,
                    state,
                    request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
                    new[] { $"save_wait_skipped:{status}" },
                    new[] { $"session_not_ready:{status}" },
                    cancellationToken);
                WriteSessionMetadata(skipped);
                return skipped;
            }

            var observed = await BuildSessionResultAsync(
                metadata.SessionId,
                metadata.ArtifactRoot,
                metadata.DeckUrl,
                state,
                request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
                new[] { "save_wait_observed" },
                Array.Empty<string>(),
                cancellationToken);

            if (string.Equals(observed.SaveState, "saved", StringComparison.OrdinalIgnoreCase))
            {
                var saved = observed with
                {
                    Success = true,
                    Actions = MergeDistinct(observed.Actions, new[] { "save_wait_observed:saved" }),
                };
                WriteSessionMetadata(saved);
                return saved;
            }

            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                var saveState = string.IsNullOrWhiteSpace(observed.SaveState) ? "unknown" : observed.SaveState;
                var timedOut = observed with
                {
                    Success = false,
                    Actions = MergeDistinct(observed.Actions, new[] { "save_wait_timeout" }),
                    Warnings = MergeDistinct(observed.Warnings, new[] { $"save_state_not_saved:{saveState}" }),
                    Errors = new[]
                    {
                        OperatorErrors.PowerPointUnavailable(
                            $"PowerPoint Online save state did not reach saved for session '{metadata.SessionId}' before timeout."),
                    },
                };
                WriteSessionMetadata(timedOut);
                return timedOut;
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), cancellationToken);
        }
    }

    public async Task<PowerPointOnlineAddInProbeResult> ProbeOnlineAddInAsync(
        string sessionId,
        PowerPointOnlineAddInProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = NormalizeAddInBaseUrl(request.AddInBaseUrl);
        var taskPaneUrl = new Uri(new Uri($"{baseUrl}/", UriKind.Absolute), "taskpane.html").ToString();
        var manifestUrl = new Uri(new Uri($"{baseUrl}/", UriKind.Absolute), "manifest.xml").ToString();
        var metadata = RequireSessionMetadata(sessionId);
        var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var session = await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            request.Capture ? new PowerPointOnlineSessionScreenshotRequest { Label = request.Label } : null,
            request.Capture ? new[] { "addin_probe_screenshot_requested" } : Array.Empty<string>(),
            Array.Empty<string>(),
            cancellationToken);
        WriteSessionMetadata(session);

        var actions = new List<string>();
        if (request.Capture)
        {
            actions.Add("addin_probe_screenshot_requested");
        }

        if (session.Status != PowerPointOnlineSessionStatus.Ready)
        {
            return new PowerPointOnlineAddInProbeResult
            {
                Success = false,
                Status = PowerPointOnlineAddInProbeStatus.BlockedSession,
                Session = session,
                AddInBaseUrl = baseUrl,
                HostReachable = false,
                TaskPaneUrl = taskPaneUrl,
                TaskPaneReachable = false,
                ManifestUrl = manifestUrl,
                ManifestReachable = false,
                ManifestId = null,
                ManifestVersion = null,
                ManifestDisplayName = null,
                ManifestSourceLocation = null,
                TaskPaneVisible = false,
                CommandVisible = false,
                MatchedElements = Array.Empty<UiElementRef>(),
                Evidence = session.Evidence,
                Actions = actions,
                Warnings = session.Warnings,
                Errors = session.Errors,
                ObservedAtUtc = session.ObservedAtUtc,
            };
        }

        PowerPointOnlineAddInHostProbeResult hostProbe;
        try
        {
            hostProbe = await _addInHostProbe.ProbeAsync(
                new Uri($"{baseUrl}/", UriKind.Absolute),
                TimeSpan.FromSeconds(Math.Clamp(request.HostTimeoutSeconds, 1, 60)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hostProbe = new PowerPointOnlineAddInHostProbeResult(
                false,
                FormatObservationFailureMessage(ex),
                taskPaneUrl,
                false,
                manifestUrl,
                false,
                null,
                null,
                null,
                null);
        }

        actions.Add(hostProbe.TaskPaneReachable ? "addin_taskpane_probe_ok" : "addin_taskpane_probe_failed");
        actions.Add(hostProbe.ManifestReachable ? "addin_manifest_probe_ok" : "addin_manifest_probe_failed");

        if (!hostProbe.Success)
        {
            actions.Add("addin_host_probe_failed");
            actions.Add("addin_taskpane_not_visible");
            actions.Add("addin_command_not_visible");
            return new PowerPointOnlineAddInProbeResult
            {
                Success = false,
                Status = PowerPointOnlineAddInProbeStatus.HostUnavailable,
                Session = session,
                AddInBaseUrl = baseUrl,
                HostReachable = false,
                TaskPaneUrl = hostProbe.TaskPaneUrl,
                TaskPaneReachable = hostProbe.TaskPaneReachable,
                ManifestUrl = hostProbe.ManifestUrl,
                ManifestReachable = hostProbe.ManifestReachable,
                ManifestId = hostProbe.ManifestId,
                ManifestVersion = hostProbe.ManifestVersion,
                ManifestDisplayName = hostProbe.ManifestDisplayName,
                ManifestSourceLocation = hostProbe.ManifestSourceLocation,
                TaskPaneVisible = false,
                CommandVisible = false,
                MatchedElements = Array.Empty<UiElementRef>(),
                Evidence = session.Evidence,
                Actions = actions,
                Warnings = session.Warnings,
                Errors = new[]
                {
                    OperatorErrors.PowerPointUnavailable(
                        $"PowerPoint add-in host unavailable at {baseUrl}: {hostProbe.Detail ?? "probe failed"}"),
                },
                ObservedAtUtc = session.ObservedAtUtc,
            };
        }

        actions.Add("addin_host_probe_ok");

        IReadOnlyList<UiElementRef> uiaElements = Array.Empty<UiElementRef>();
        var warnings = new List<string>(session.Warnings);
        try
        {
            uiaElements = await QueryWindowElementsAsync(session.Hwnd, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"addin_uia_unavailable:{FormatObservationFailureMessage(ex)}");
        }

        var matchedElements = FindAddInElements(uiaElements);
        var diagnosticMatchedElements = matchedElements.ToList();
        var taskPaneVisible = matchedElements.Any(IsTaskPaneMatch);
        var commandVisible = matchedElements.Any(IsCommandMatch);

        actions.Add(taskPaneVisible ? "addin_taskpane_visible" : "addin_taskpane_not_visible");
        actions.Add(commandVisible ? "addin_command_visible" : "addin_command_not_visible");

        if (taskPaneVisible)
        {
            return new PowerPointOnlineAddInProbeResult
            {
                Success = true,
                Status = PowerPointOnlineAddInProbeStatus.Ready,
                Session = session,
                AddInBaseUrl = baseUrl,
                HostReachable = true,
                TaskPaneUrl = hostProbe.TaskPaneUrl,
                TaskPaneReachable = hostProbe.TaskPaneReachable,
                ManifestUrl = hostProbe.ManifestUrl,
                ManifestReachable = hostProbe.ManifestReachable,
                ManifestId = hostProbe.ManifestId,
                ManifestVersion = hostProbe.ManifestVersion,
                ManifestDisplayName = hostProbe.ManifestDisplayName,
                ManifestSourceLocation = hostProbe.ManifestSourceLocation,
                TaskPaneVisible = true,
                CommandVisible = commandVisible,
                MatchedElements = matchedElements,
                Evidence = session.Evidence,
                Actions = actions,
                Warnings = warnings,
                Errors = Array.Empty<OperatorError>(),
                ObservedAtUtc = session.ObservedAtUtc,
            };
        }

        if (request.ActivateIfNeeded)
        {
            actions.Add("addin_activation_requested");
            var activationCandidate = FindBestActivationCommand(matchedElements);
            if (!taskPaneVisible && !HasVisibleRealLaunchCommand(matchedElements))
            {
                var originalActivationCandidate = activationCandidate;
                var revealObservation = await TryRevealAddInCommandAsync(session.Hwnd, actions, warnings, cancellationToken);
                matchedElements = revealObservation.MatchedElements;
                diagnosticMatchedElements.AddRange(revealObservation.MatchedElements);
                taskPaneVisible = revealObservation.TaskPaneVisible;
                commandVisible = revealObservation.CommandVisible;
                warnings.AddRange(revealObservation.Warnings);
                activationCandidate = FindBestActivationCommand(matchedElements) ?? originalActivationCandidate;
                if (activationCandidate is not null &&
                    !IsRealLaunchCommandMatch(activationCandidate) &&
                    Math.Clamp(request.ActivationTimeoutSeconds, 1, 60) > 1)
                {
                    activationCandidate = null;
                }
            }

            if (activationCandidate is null)
            {
                actions.Add("addin_activation_command_not_clickable");
            }
            else
            {
                if (activationCandidate.IsOffscreen)
                {
                    actions.Add("addin_activation_click_offscreen_candidate");
                }

                actions.Add(DescribeActivationClickTarget("addin_activation_click_target", activationCandidate));
                var clickResult = await _inputService.ClickScreenAsync(
                    CreateActivationClickRequest(activationCandidate),
                    cancellationToken);
                if (clickResult.Success)
                {
                    actions.Add("addin_activation_click_dispatched");
                    var activationObservation = await WaitForAddInTaskPaneAsync(
                        session.Hwnd,
                        Math.Clamp(request.ActivationTimeoutSeconds, 1, 60),
                        cancellationToken);
                    matchedElements = activationObservation.MatchedElements;
                    diagnosticMatchedElements.AddRange(activationObservation.MatchedElements);
                    taskPaneVisible = activationObservation.TaskPaneVisible;
                    commandVisible = activationObservation.CommandVisible;
                    warnings.AddRange(activationObservation.Warnings);
                    actions.Add(taskPaneVisible
                        ? "addin_activation_observed_ready"
                        : "addin_activation_timeout");
                    if (!taskPaneVisible && Math.Clamp(request.ActivationTimeoutSeconds, 1, 60) > 1)
                    {
                        var retryObservation = await RetryActivateAddInAsync(
                            session.Hwnd,
                            actions,
                            warnings,
                            cancellationToken);
                        matchedElements = retryObservation.MatchedElements;
                        diagnosticMatchedElements.AddRange(retryObservation.MatchedElements);
                        taskPaneVisible = retryObservation.TaskPaneVisible;
                        commandVisible = retryObservation.CommandVisible;
                        warnings.AddRange(retryObservation.Warnings);
                    }
                }
                else
                {
                    actions.Add("addin_activation_command_not_clickable");
                    warnings.Add(clickResult.Message);
                }
            }
        }

        matchedElements = FindAddInElements(MergeUiElements(diagnosticMatchedElements, matchedElements));
        return new PowerPointOnlineAddInProbeResult
        {
            Success = taskPaneVisible,
            Status = taskPaneVisible
                ? PowerPointOnlineAddInProbeStatus.Ready
                : PowerPointOnlineAddInProbeStatus.BlockedActivation,
            Session = session,
            AddInBaseUrl = baseUrl,
            HostReachable = true,
            TaskPaneUrl = hostProbe.TaskPaneUrl,
            TaskPaneReachable = hostProbe.TaskPaneReachable,
            ManifestUrl = hostProbe.ManifestUrl,
            ManifestReachable = hostProbe.ManifestReachable,
            ManifestId = hostProbe.ManifestId,
            ManifestVersion = hostProbe.ManifestVersion,
            ManifestDisplayName = hostProbe.ManifestDisplayName,
            ManifestSourceLocation = hostProbe.ManifestSourceLocation,
            TaskPaneVisible = taskPaneVisible,
            CommandVisible = commandVisible,
            MatchedElements = matchedElements,
            Evidence = session.Evidence,
            Actions = actions,
            Warnings = warnings,
            Errors = taskPaneVisible
                ? Array.Empty<OperatorError>()
                : new[]
                {
                    OperatorErrors.PowerPointUnavailable(
                        $"PowerPoint add-in task pane not visible for session '{session.SessionId}'."),
                },
            ObservedAtUtc = session.ObservedAtUtc,
        };
    }

    public Task<PowerPointOnlineSessionResult> PrepareOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        TriggerTemplateButtonAsync(
            sessionId,
            request,
            request.NamedOnly ? "Prepare Named Targets" : "Prepare Template",
            request.NamedOnly ? "named_template_prepare" : "template_prepare",
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> CleanupOnlineTemplateAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest request,
        CancellationToken cancellationToken) =>
        TriggerTemplateButtonAsync(
            sessionId,
            request,
            request.NamedOnly ? "Cleanup Named Targets" : "Cleanup Template",
            request.NamedOnly ? "named_template_cleanup" : "template_cleanup",
            cancellationToken);

    public Task<PowerPointOnlineSessionResult> RunOnlinePendingJobAsync(
        string sessionId,
        PowerPointOnlineAddInCommandRequest request,
        CancellationToken cancellationToken)
    {
        ValidateAddInCommandRequest(request);
        return TriggerAddInButtonAsync(
            sessionId,
            request.Capture,
            request.WaitSeconds,
            request.Label,
            "Run Pending Job",
            "addin_run_pending_job",
            cancellationToken);
    }

    public async Task<PowerPointOnlineSessionResult> CleanupOnlineSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var metadata = RequireSessionMetadata(sessionId);
        try
        {
            var state = await _workbenchService.CleanupEdgeSessionAsync(metadata.BrowserSessionId, cancellationToken);
            var status = state.IsAlive ? PowerPointOnlineSessionStatus.Failed : PowerPointOnlineSessionStatus.Closed;
            var cleanupAction = state.IsAlive
                ? "powerpoint_online_cleanup_still_alive"
                : "powerpoint_online_cleanup_verified_closed";
            var warnings = state.IsAlive
                ? state.Errors.Concat(new[] { "cleanup_still_alive" }).ToArray()
                : state.Errors.ToArray();
            var errors = state.IsAlive
                ? new[]
                {
                    OperatorErrors.PowerPointUnavailable(
                        $"PowerPoint Online session '{metadata.SessionId}' cleanup left the browser session alive."),
                }
                : Array.Empty<OperatorError>();
            var result = new PowerPointOnlineSessionResult
            {
                Success = status == PowerPointOnlineSessionStatus.Closed,
                SessionId = metadata.SessionId,
                Status = status,
                DeckUrl = metadata.DeckUrl,
                CanonicalUrl = state.Url ?? metadata.CanonicalUrl,
                CurrentUrl = state.Url ?? metadata.CurrentUrl,
                CurrentTitle = state.Title ?? metadata.CurrentTitle,
                CurrentSlide = metadata.CurrentSlide,
                SlideCount = metadata.SlideCount,
                EditMode = metadata.EditMode,
                SaveState = metadata.SaveState,
                BrowserSessionId = metadata.BrowserSessionId,
                Hwnd = state.Hwnd ?? metadata.Hwnd,
                ArtifactRoot = metadata.ArtifactRoot,
                Evidence = Array.Empty<DesktopScreenshotResult>(),
                Actions = state.Actions.Concat(new[] { "powerpoint_online_cleanup", cleanupAction }).ToArray(),
                Warnings = warnings,
                Errors = errors,
                ObservedAtUtc = state.ObservedAtUtc,
            };
            WriteSessionMetadata(result);
            return result;
        }
        catch (OperatorFailureException failure)
        {
            var result = StoredResult(
                metadata with { Status = PowerPointOnlineSessionStatus.Closed },
                new[] { "powerpoint_online_cleanup_assumed_closed" },
                new[] { failure.Error.Message },
                Array.Empty<OperatorError>());
            WriteSessionMetadata(result);
            return result;
        }
    }

    private async Task<PowerPointOnlineSessionResult> TriggerTemplateButtonAsync(
        string sessionId,
        PowerPointOnlineTemplateRequest? request,
        string buttonName,
        string actionPrefix,
        CancellationToken cancellationToken)
    {
        request ??= new PowerPointOnlineTemplateRequest();
        ValidateTemplateRequest(request);

        return await TriggerAddInButtonAsync(
            sessionId,
            request.Capture,
            request.WaitSeconds,
            request.Label,
            buttonName,
            actionPrefix,
            cancellationToken);
    }

    private async Task<PowerPointOnlineSessionResult> TriggerAddInButtonAsync(
        string sessionId,
        bool capture,
        int waitSeconds,
        string? label,
        string buttonName,
        string actionPrefix,
        CancellationToken cancellationToken)
    {
        var metadata = RequireSessionMetadata(sessionId);
        var actions = new List<string> { $"{actionPrefix}_requested" };
        var warnings = new List<string>();
        var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var session = await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            null,
            actions,
            warnings,
            cancellationToken);

        if (session.Status != PowerPointOnlineSessionStatus.Ready)
        {
            var blocked = session with
            {
                Success = false,
                Actions = MergeDistinct(session.Actions, new[] { $"{actionPrefix}_skipped:{session.Status}" }),
            };
            WriteSessionMetadata(blocked);
            return blocked;
        }

        if (session.Hwnd is null)
        {
            var noWindow = TemplateActionFailed(
                session,
                $"{actionPrefix}_button_not_found:no_hwnd",
                warnings,
                $"PowerPoint Online session '{metadata.SessionId}' does not expose a window handle.");
            WriteSessionMetadata(noWindow);
            return noWindow;
        }

        if (string.Equals(actionPrefix, "addin_run_pending_job", StringComparison.Ordinal))
        {
            var commandSignal = TryDispatchRunPendingJobCommand(state, session.DeckUrl);
            if (commandSignal.Dispatched)
            {
                actions.Add($"{actionPrefix}_command_signal_dispatched");
                if (waitSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0, 30)), cancellationToken);
                }

                state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
                var commandResult = await BuildSessionResultAsync(
                    metadata.SessionId,
                    metadata.ArtifactRoot,
                    metadata.DeckUrl,
                    state,
                    capture ? new PowerPointOnlineSessionScreenshotRequest { Label = label } : null,
                    actions,
                    warnings,
                    cancellationToken);
                WriteSessionMetadata(commandResult);
                return commandResult;
            }

            actions.Add($"{actionPrefix}_command_signal_unavailable");
            if (!string.IsNullOrWhiteSpace(commandSignal.Detail))
            {
                warnings.Add($"{actionPrefix}_command_signal_detail:{commandSignal.Detail}");
            }
        }

        UiElementRef? button = null;
        ScreenClickRequest? fallbackClick = null;
        try
        {
            (button, fallbackClick) = await ObserveAddInButtonAsync(
                session.Hwnd,
                buttonName,
                actionPrefix,
                actions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{actionPrefix}_uia_unavailable:{FormatObservationFailureMessage(ex)}");
        }

        if (button is null && fallbackClick is null)
        {
            var notFound = TemplateActionFailed(
                session with
                {
                    Actions = MergeDistinct(session.Actions, actions),
                    Warnings = MergeDistinct(session.Warnings, warnings),
                },
                $"{actionPrefix}_button_not_found",
                warnings,
                $"PowerPoint add-in button '{buttonName}' is not visible for session '{metadata.SessionId}'.");
            WriteSessionMetadata(notFound);
            return notFound;
        }

        var clickRequest = fallbackClick ?? CreateScreenClickRequest(button!.Bounds);
        var click = await _inputService.ClickScreenAsync(clickRequest, cancellationToken);
        if (!click.Success)
        {
            warnings.Add(click.Message);
            var failed = TemplateActionFailed(
                session,
                $"{actionPrefix}_click_failed",
                warnings,
                $"PowerPoint add-in button '{buttonName}' could not be clicked for session '{metadata.SessionId}'.");
            WriteSessionMetadata(failed);
            return failed;
        }

        actions.Add($"{actionPrefix}_click_dispatched");
        if (waitSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0, 30)), cancellationToken);
        }

        state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        var result = await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            capture ? new PowerPointOnlineSessionScreenshotRequest { Label = label } : null,
            actions,
            warnings,
            cancellationToken);
        WriteSessionMetadata(result);
        return result;
    }

    private AddInCommandSignalDispatchResult TryDispatchRunPendingJobCommand(
        BrowserEdgeSessionStateResult state,
        string deckUrl)
    {
        if (_devToolsService is null)
        {
            return new AddInCommandSignalDispatchResult(false, "devtools_service_unavailable");
        }

        if (state.DevToolsPort is null)
        {
            return new AddInCommandSignalDispatchResult(false, "devtools_port_missing");
        }

        var target = _devToolsService.ReadTarget(state.DevToolsPort.Value, state.Url ?? deckUrl);
        if (target is null)
        {
            return new AddInCommandSignalDispatchResult(false, "devtools_target_unavailable");
        }

        var evaluation = _devToolsService.Evaluate(
            target.Value.WebSocketDebuggerUrl,
            BuildRunPendingJobCommandSignalExpression(),
            AddInCommandSignalTimeout);
        if (!evaluation.Success)
        {
            return new AddInCommandSignalDispatchResult(
                false,
                evaluation.TimedOut ? "command_signal_timeout" : evaluation.ErrorText ?? "command_signal_failed");
        }

        var payload = ParseAddInCommandSignalResponse(evaluation);
        return payload?.Accepted == true
            ? new AddInCommandSignalDispatchResult(true, payload.Detail)
            : new AddInCommandSignalDispatchResult(false, payload?.Detail ?? "command_signal_unacknowledged");
    }

    private static string BuildRunPendingJobCommandSignalExpression()
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            channel = AddInCommandChannel,
            kind = "command",
            command = RunPendingJobCommandName,
            requestId = Guid.NewGuid().ToString("N"),
        });
        var timeoutMilliseconds = (int)AddInCommandSignalTimeout.TotalMilliseconds;
        return $$"""
(() => new Promise(resolve => {
  const request = {{requestJson}};
  const timeoutMs = {{timeoutMilliseconds}};
  const frames = Array.from(document.querySelectorAll("iframe"))
    .map(frame => frame && frame.contentWindow ? frame.contentWindow : null)
    .filter(frame => frame);
  let settled = false;
  const finish = payload => {
    if (settled) {
      return;
    }
    settled = true;
    window.removeEventListener("message", onMessage);
    resolve(JSON.stringify(payload));
  };
  const onMessage = event => {
    const data = event && event.data ? event.data : null;
    if (!data || data.channel !== request.channel || data.kind !== "ack" || data.command !== request.command || data.requestId !== request.requestId) {
      return;
    }
    finish({
      accepted: !!data.accepted,
      detail: data.error || null
    });
  };
  window.addEventListener("message", onMessage);
  let dispatched = 0;
  for (const frame of frames) {
    try {
      frame.postMessage(request, "*");
      dispatched++;
    } catch {
      // Ignore frame postMessage failures. UIA fallback remains available.
    }
  }
  if (dispatched === 0) {
    finish({
      accepted: false,
      detail: "no_taskpane_frames"
    });
    return;
  }
  window.setTimeout(() => finish({
    accepted: false,
    detail: "command_signal_timeout"
  }), timeoutMs);
}))()
""";
    }

    private static AddInCommandSignalResponse? ParseAddInCommandSignalResponse(EdgeDevToolsEvaluation evaluation)
    {
        var raw = evaluation.ValueText ?? evaluation.ValueJson;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AddInCommandSignalResponse>(raw, AddInCommandJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(UiElementRef? Button, ScreenClickRequest? FallbackClick)> ObserveAddInButtonAsync(
        long? hwnd,
        string buttonName,
        string actionPrefix,
        List<string> actions,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var deadlineUtc = DateTimeOffset.UtcNow + AddInButtonObservationTimeout;

        while (true)
        {
            var elements = await QueryWindowElementsAsync(hwnd, cancellationToken);
            var button = FindTemplateButton(elements, buttonName);
            var fallbackClick = button is null ? FindRunPendingJobFallbackClick(elements, buttonName, actions) : null;
            if (button is null)
            {
                button = await QuerySpecificButtonAsync(hwnd, buttonName, cancellationToken);
            }

            if (button is not null || fallbackClick is not null)
            {
                if (attempt > 0)
                {
                    actions.Add($"{actionPrefix}_button_observed_after_retry:{attempt}");
                }

                return (button, fallbackClick);
            }

            if (!ShouldRetryAddInButtonObservation(buttonName))
            {
                return (null, null);
            }

            attempt++;
            actions.Add($"{actionPrefix}_button_observation_retry:{attempt}");
            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                actions.Add($"{actionPrefix}_button_observation_timeout");
                return (null, null);
            }

            await Task.Delay(AddInActivationPollInterval, cancellationToken);
        }
    }

    private async Task<BrowserEdgeSessionStateResult> StartBrowserSessionAsync(
        string sessionId,
        string deckUrl,
        string runId,
        PowerPointOnlineSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        var open = await _workbenchService.OpenEdgeUrlAsync(
            new BrowserEdgeOpenUrlRequest
            {
                Url = deckUrl,
                SessionId = sessionId,
                ProfileMode = request.ProfileMode,
                WaitSeconds = Math.Clamp(request.WaitSeconds, 1, 30),
                Capture = false,
                RunId = runId,
            },
            cancellationToken);
        return open.State;
    }

    private async Task<PowerPointOnlineSessionResult> BuildSessionResultAsync(
        string sessionId,
        WorkbenchRunRef artifactRoot,
        string deckUrl,
        BrowserEdgeSessionStateResult state,
        PowerPointOnlineSessionScreenshotRequest? screenshotRequest,
        IEnumerable<string> actions,
        IEnumerable<string> warnings,
        CancellationToken cancellationToken,
        IReadOnlyList<OperatorError>? errors = null)
    {
        var evidence = Array.Empty<DesktopScreenshotResult>();
        var existingMetadata = TryReadSessionMetadata(sessionId);
        var actionList = state.Actions.Concat(actions).Distinct(StringComparer.Ordinal).ToList();
        var warningList = state.Errors.Concat(warnings).Distinct(StringComparer.Ordinal).ToList();
        var observation = SessionObservation.Empty;

        if (screenshotRequest is not null && state.Hwnd is not null)
        {
            var screenshot = await _workbenchService.CaptureEdgeSessionScreenshotAsync(
                state.SessionId,
                new DesktopScreenshotRequest
                {
                    RunId = artifactRoot.RunId,
                    Label = screenshotRequest.Label ?? DefaultScreenshotLabel(sessionId),
                    Format = screenshotRequest.Format,
                },
                cancellationToken);
            evidence = new[] { screenshot };
        }

        if (state.Hwnd is not null)
        {
            try
            {
                var elements = await _uiAutomationService.QueryAsync(
                    new UiQuery
                    {
                        WindowHwnd = state.Hwnd,
                        IncludeOffscreen = false,
                        MaxResults = 1000,
                    },
                    cancellationToken);
                observation = ObserveSession(elements);
                actionList.Add("powerpoint_online_uia_observed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warningList.Add($"powerpoint_online_uia_unavailable:{FormatObservationFailureMessage(ex)}");
            }
        }

        observation = observation with
        {
            CurrentSlide = observation.CurrentSlide ?? existingMetadata?.CurrentSlide,
            SlideCount = observation.SlideCount ?? existingMetadata?.SlideCount,
            EditMode = observation.EditMode ?? existingMetadata?.EditMode,
            SaveState = observation.SaveState ?? existingMetadata?.SaveState,
        };

        var status = ClassifyState(state);
        var errorList = errors ?? ErrorsForStatus(status, state);
        return new PowerPointOnlineSessionResult
        {
            Success = errorList.Count == 0 && status is PowerPointOnlineSessionStatus.Ready or PowerPointOnlineSessionStatus.Closed,
            SessionId = sessionId,
            Status = status,
            DeckUrl = deckUrl,
            CanonicalUrl = state.Url,
            CurrentUrl = state.Url,
            CurrentTitle = state.Title,
            CurrentSlide = observation.CurrentSlide,
            SlideCount = observation.SlideCount,
            EditMode = observation.EditMode,
            SaveState = observation.SaveState,
            BrowserSessionId = state.SessionId,
            Hwnd = state.Hwnd,
            ArtifactRoot = artifactRoot,
            Evidence = evidence,
            Actions = actionList.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warningList.Distinct(StringComparer.Ordinal).ToArray(),
            Errors = errorList,
            ObservedAtUtc = state.ObservedAtUtc,
        };
    }

    private PowerPointOnlineSessionResult StoredResult(
        PowerPointOnlineSessionMetadata metadata,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<OperatorError> errors) =>
        new()
        {
            Success = errors.Count == 0 && metadata.Status is PowerPointOnlineSessionStatus.Ready or PowerPointOnlineSessionStatus.Closed,
            SessionId = metadata.SessionId,
            Status = metadata.Status,
            DeckUrl = metadata.DeckUrl,
            CanonicalUrl = metadata.CanonicalUrl,
            CurrentUrl = metadata.CurrentUrl,
            CurrentTitle = metadata.CurrentTitle,
            CurrentSlide = metadata.CurrentSlide,
            SlideCount = metadata.SlideCount,
            EditMode = metadata.EditMode,
            SaveState = metadata.SaveState,
            BrowserSessionId = metadata.BrowserSessionId,
            Hwnd = metadata.Hwnd,
            ArtifactRoot = metadata.ArtifactRoot,
            Evidence = Array.Empty<DesktopScreenshotResult>(),
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };

    private void WriteSessionMetadata(PowerPointOnlineSessionResult result)
    {
        var metadata = new PowerPointOnlineSessionMetadata(
            result.SessionId,
            result.BrowserSessionId ?? result.SessionId,
            result.DeckUrl,
            result.Status,
            result.ArtifactRoot ?? _runs.ResolveRun(result.SessionId, "powerpoint-online"),
            result.CanonicalUrl,
            result.CurrentUrl,
            result.CurrentTitle,
            result.CurrentSlide,
            result.SlideCount,
            result.EditMode,
            result.SaveState,
            result.Hwnd,
            result.ObservedAtUtc);
        Directory.CreateDirectory(SessionRoot());
        File.WriteAllText(
            SessionPath(result.SessionId),
            JsonSerializer.Serialize(metadata, OperatorJson.SerializerOptions));
        _runs.WriteJson(metadata.ArtifactRoot, "powerpoint-online-session.json", result);
        _runs.AppendEvent(
            metadata.ArtifactRoot,
            "powerpoint_online_session",
            new
            {
                result.SessionId,
                Status = result.Status.ToString(),
                result.BrowserSessionId,
                result.CurrentUrl,
            });
    }

    private PowerPointOnlineSessionMetadata RequireSessionMetadata(string sessionId) =>
        TryReadSessionMetadata(sessionId)
        ?? throw new OperatorFailureException(
            OperatorErrors.PowerPointUnavailable($"PowerPoint Online session was not found: {sessionId}"));

    private PowerPointOnlineSessionMetadata? TryReadSessionMetadata(string sessionId)
    {
        var path = SessionPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<PowerPointOnlineSessionMetadata>(
            File.ReadAllText(path),
            OperatorJson.SerializerOptions);
    }

    private string SessionRoot() =>
        Path.Combine(_runs.ExchangeRoot, "powerpoint-online-sessions");

    private string SessionPath(string sessionId) =>
        Path.Combine(
            SessionRoot(),
            WorkbenchRunStore.SanitizePathSegment(sessionId, "powerpoint-online") + ".json");

    private static string NormalizeDeckUrl(string? deckUrl)
    {
        if (!Uri.TryCreate(deckUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint Online deck URL: {deckUrl}"));
        }

        return uri.ToString();
    }

    private static string NormalizeAddInBaseUrl(string? addInBaseUrl)
    {
        if (!Uri.TryCreate(addInBaseUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint add-in base URL: {addInBaseUrl}"));
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static void ValidateTemplateRequest(PowerPointOnlineTemplateRequest request)
    {
        if (!request.AllowDeckMutation)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed(
                    "allowDeckMutation must be true for template prepare/cleanup because PowerPoint Online changes are saved to the deck."));
        }

        if (request.WaitSeconds < 0)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed("waitSeconds must be zero or greater."));
        }
    }

    private static void ValidateAddInCommandRequest(PowerPointOnlineAddInCommandRequest request)
    {
        if (request.WaitSeconds < 0)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed("waitSeconds must be zero or greater."));
        }
    }

    private async Task<PowerPointOnlineSessionResult> ObserveSlideSelectionAsync(
        PowerPointOnlineSessionMetadata metadata,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var state = await _edgeBrowserService.GetSessionStateAsync(metadata.BrowserSessionId, cancellationToken);
        return await BuildSessionResultAsync(
            metadata.SessionId,
            metadata.ArtifactRoot,
            metadata.DeckUrl,
            state,
            null,
            actions,
            warnings,
            cancellationToken);
    }

    private static bool TryCreateKeyboardCorrection(
        PowerPointOnlineSessionResult observed,
        int targetSlide,
        bool clickDispatched,
        out SlideKeyboardCorrection correction)
    {
        correction = default;
        if (!clickDispatched ||
            observed.Status != PowerPointOnlineSessionStatus.Ready ||
            observed.CurrentSlide is not int currentSlide ||
            currentSlide == targetSlide)
        {
            return false;
        }

        if (observed.SlideCount is int slideCount && targetSlide > slideCount)
        {
            return false;
        }

        var delta = targetSlide - currentSlide;
        var steps = Math.Abs(delta);
        if (steps == 0 || steps > MaxKeyboardSlideSteps)
        {
            return false;
        }

        correction = new SlideKeyboardCorrection(
            currentSlide,
            targetSlide,
            delta > 0 ? "pagedown" : "pageup",
            steps);
        return true;
    }

    private async Task ApplyKeyboardSlideCorrectionAsync(
        SlideKeyboardCorrection correction,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        actions.Add(
            $"slide_select_keyboard_correction:{correction.FromSlide}:{correction.TargetSlide}:{correction.Key}:{correction.Steps}");
        for (var index = 0; index < correction.Steps; index++)
        {
            var hotkey = await _inputService.SendHotkeyAsync(
                new HotkeyRequest { Keys = new[] { correction.Key } },
                cancellationToken);
            if (!hotkey.Success)
            {
                actions.Add("slide_select_keyboard_correction_failed");
                warnings.Add(hotkey.Message);
                return;
            }

            await Task.Delay(SlideKeyboardStepDelay, cancellationToken);
        }

        actions.Add("slide_select_keyboard_correction_dispatched");
    }

    private static SlideSelectionVerification BuildSlideSelectionVerification(
        string sessionId,
        int requestedSlide,
        bool clickDispatched,
        PowerPointOnlineSessionResult result)
    {
        var actions = new List<string>();
        var warnings = new List<string>();
        var errors = new List<OperatorError>();

        if (!clickDispatched)
        {
            errors.Add(OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online slide {requestedSlide} could not be selected for session '{sessionId}'."));
            return new SlideSelectionVerification(actions, warnings, errors);
        }

        if (result.SlideCount is int slideCount && requestedSlide > slideCount)
        {
            actions.Add($"slide_select_verification_failed:out_of_range:{requestedSlide}:{slideCount}");
            errors.Add(OperatorErrors.PowerPointValidationFailed(
                $"PowerPoint Online slide {requestedSlide} is outside the observed slide count {slideCount}."));
            return new SlideSelectionVerification(actions, warnings, errors);
        }

        if (result.CurrentSlide is null)
        {
            actions.Add($"slide_select_verification_unavailable:{requestedSlide}");
            warnings.Add("slide_selection_not_observed");
            return new SlideSelectionVerification(actions, warnings, errors);
        }

        if (result.CurrentSlide != requestedSlide)
        {
            actions.Add($"slide_select_verification_failed:{result.CurrentSlide}:{requestedSlide}");
            errors.Add(OperatorErrors.PowerPointUnavailable(
                $"PowerPoint Online selected slide {result.CurrentSlide}, expected slide {requestedSlide}, for session '{sessionId}'."));
            return new SlideSelectionVerification(actions, warnings, errors);
        }

        actions.Add($"slide_select_verified:{requestedSlide}");
        return new SlideSelectionVerification(actions, warnings, errors);
    }

    private static IReadOnlyList<BrowserEdgeSessionDomClickRequest> SlideClickRequests(int slideNumber) =>
        new[]
        {
            new BrowserEdgeSessionDomClickRequest { Selector = $"[aria-label='Slide {slideNumber}']", TimeoutSeconds = 2 },
            new BrowserEdgeSessionDomClickRequest { Selector = $"[aria-label*='Slide {slideNumber}']", TimeoutSeconds = 2 },
            new BrowserEdgeSessionDomClickRequest { Selector = $"[data-slide-number='{slideNumber}']", TimeoutSeconds = 2 },
            new BrowserEdgeSessionDomClickRequest { VisibleText = slideNumber.ToString(), TimeoutSeconds = 2 },
        };

    private static bool ShouldAttemptDomSlideSelection(IReadOnlyList<string>? actions)
    {
        var status = ReadLatestDevToolsStatus(actions);
        return !string.Equals(status, "target_unavailable", StringComparison.Ordinal) &&
            !string.Equals(status, "port_closed", StringComparison.Ordinal);
    }

    private static string? ReadLatestDevToolsStatus(IReadOnlyList<string>? actions)
    {
        if (actions is null)
        {
            return null;
        }

        for (var index = actions.Count - 1; index >= 0; index--)
        {
            const string prefix = "devtools_status:";
            var action = actions[index];
            if (action.StartsWith(prefix, StringComparison.Ordinal))
            {
                return action[prefix.Length..];
            }
        }

        return null;
    }

    private static bool UrlsMatch(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return string.Equals(
            NormalizeUrlIdentity(expected),
            NormalizeUrlIdentity(actual),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrlIdentity(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };
        var path = builder.Path.TrimEnd('/');
        builder.Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
        return builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.SafeUnescaped);
    }

    private static ScreenClickRequest ComputeThumbnailClickPoint(WindowBounds bounds, int slideNumber)
    {
        var widthScale = bounds.Width / ReferenceWidth;
        var heightScale = bounds.Height / ReferenceHeight;
        var x = bounds.X + (int)Math.Round(ThumbnailRailCenterXAt1296 * widthScale);
        var y = bounds.Y + (int)Math.Round((ThumbnailFirstCenterYAt776 + ((slideNumber - 1) * ThumbnailStepYAt776)) * heightScale);
        var clampedX = Math.Clamp(x, bounds.X + 16, bounds.X + Math.Max(16, bounds.Width - 16));
        var clampedY = Math.Clamp(y, bounds.Y + 16, bounds.Y + Math.Max(16, bounds.Height - 16));
        return new ScreenClickRequest
        {
            X = clampedX,
            Y = clampedY,
        };
    }

    private static string DefaultScreenshotLabel(string sessionId) =>
        $"powerpoint-online-{WorkbenchRunStore.SanitizePathSegment(sessionId, "session")}";

    private static PowerPointOnlineSessionStatus ClassifyState(BrowserEdgeSessionStateResult state)
    {
        if (!state.IsAlive)
        {
            return PowerPointOnlineSessionStatus.Closed;
        }

        var haystack = string.Join(
            "\n",
            new[]
            {
                state.Title,
                state.Url,
                state.BodyText,
                state.BrowserState,
            }.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

        if (ContainsAny(haystack, "sign in", "login.microsoftonline.com", "enter your password", "stay signed in"))
        {
            return PowerPointOnlineSessionStatus.BlockedAuth;
        }

        if (ContainsAny(haystack, "access denied", "need permission", "request access", "you do not have access"))
        {
            return PowerPointOnlineSessionStatus.BlockedPermission;
        }

        if (ContainsAny(haystack, "read only", "view only", "readonly", "can't edit"))
        {
            return PowerPointOnlineSessionStatus.BlockedReadonly;
        }

        if (ContainsAny(haystack, "something went wrong", "office has encountered", "sorry, powerpoint"))
        {
            return PowerPointOnlineSessionStatus.BlockedOfficeError;
        }

        if (ContainsAny(haystack, "powerpoint", ".pptx", "sharepoint.com", "officeapps.live.com"))
        {
            return PowerPointOnlineSessionStatus.Ready;
        }

        return PowerPointOnlineSessionStatus.Failed;
    }

    private static bool ShouldRecreateSession(BrowserEdgeSessionStateResult state) =>
        ClassifyState(state) == PowerPointOnlineSessionStatus.Closed;

    private static IReadOnlyList<OperatorError> ErrorsForStatus(
        PowerPointOnlineSessionStatus status,
        BrowserEdgeSessionStateResult state)
    {
        var detail = state.Url ?? state.Title ?? state.BrowserState ?? "PowerPoint Online session state unavailable.";
        return status switch
        {
            PowerPointOnlineSessionStatus.BlockedAuth => new[] { OperatorErrors.AuthUnavailable(detail) },
            PowerPointOnlineSessionStatus.BlockedPermission => new[] { OperatorErrors.PowerPointUnavailable(detail) },
            PowerPointOnlineSessionStatus.BlockedReadonly => new[] { OperatorErrors.PowerPointUnavailable(detail) },
            PowerPointOnlineSessionStatus.BlockedOfficeError => new[] { OperatorErrors.PowerPointUnavailable(detail) },
            PowerPointOnlineSessionStatus.Failed => new[] { OperatorErrors.PowerPointUnavailable(detail) },
            _ => Array.Empty<OperatorError>(),
        };
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static IReadOnlyList<string> MergeDistinct(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count == 0
            ? second.Distinct(StringComparer.Ordinal).ToArray()
            : second.Count == 0
                ? first.Distinct(StringComparer.Ordinal).ToArray()
                : first.Concat(second).Distinct(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> first, IReadOnlyList<T> second) =>
        first.Count == 0 ? second : second.Count == 0 ? first : first.Concat(second).ToArray();

    private async Task<IReadOnlyList<UiElementRef>> QueryWindowElementsAsync(long? hwnd, CancellationToken cancellationToken)
    {
        if (hwnd is null)
        {
            return Array.Empty<UiElementRef>();
        }

        return await _uiAutomationService.QueryAsync(
            new UiQuery
            {
                WindowHwnd = hwnd,
                IncludeOffscreen = true,
                MaxResults = 1000,
            },
            cancellationToken);
    }

    private async Task<UiElementRef?> QuerySpecificButtonAsync(
        long? hwnd,
        string buttonName,
        CancellationToken cancellationToken)
    {
        if (hwnd is null)
        {
            return null;
        }

        var elements = await _uiAutomationService.QueryAsync(
            new UiQuery
            {
                WindowHwnd = hwnd,
                Name = buttonName,
                ControlType = "Button",
                IncludeOffscreen = false,
                MaxResults = 25,
            },
            cancellationToken);
        return FindTemplateButton(elements, buttonName);
    }

    private static PowerPointOnlineSessionResult TemplateActionFailed(
        PowerPointOnlineSessionResult session,
        string action,
        IReadOnlyList<string> warnings,
        string message) =>
        session with
        {
            Success = false,
            Actions = MergeDistinct(session.Actions, new[] { action }),
            Warnings = MergeDistinct(session.Warnings, warnings),
            Errors = new[] { OperatorErrors.PowerPointUnavailable(message) },
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };

    private sealed record AddInCommandSignalDispatchResult(bool Dispatched, string? Detail);

    private sealed record AddInCommandSignalResponse(bool Accepted, string? Detail);

    private static UiElementRef? FindTemplateButton(IReadOnlyList<UiElementRef> elements, string buttonName) =>
        elements
            .Where(element =>
                ContainsFragment(element.Name, buttonName) &&
                ContainsFragment(element.ControlType, "Button") &&
                element.IsEnabled &&
                !element.IsOffscreen &&
                element.Bounds.Width > 0 &&
                element.Bounds.Height > 0)
            .OrderByDescending(element => element.Bounds.Width * element.Bounds.Height)
            .FirstOrDefault();

    private static ScreenClickRequest? FindRunPendingJobFallbackClick(
        IReadOnlyList<UiElementRef> elements,
        string buttonName,
        List<string> actions)
    {
        if (!ContainsFragment(buttonName, "Run Pending Job"))
        {
            return null;
        }

        var cleanup = FindTemplateButton(elements, "Cleanup Template");
        if (cleanup is not null)
        {
            actions.Add("addin_run_pending_job_click_fallback:cleanup_template_sibling");
            return new ScreenClickRequest
            {
                X = cleanup.Bounds.X + cleanup.Bounds.Width + 10 + (cleanup.Bounds.Width / 2),
                Y = cleanup.Bounds.Y + (cleanup.Bounds.Height / 2),
            };
        }

        var prepare = FindTemplateButton(elements, "Prepare Template");
        if (prepare is not null)
        {
            actions.Add("addin_run_pending_job_click_fallback:prepare_template_sibling");
            return new ScreenClickRequest
            {
                X = prepare.Bounds.X + (2 * (prepare.Bounds.Width + 10)) + (prepare.Bounds.Width / 2),
                Y = prepare.Bounds.Y + (prepare.Bounds.Height / 2),
            };
        }

        return null;
    }

    private static bool ShouldRetryAddInButtonObservation(string buttonName) =>
        ContainsFragment(buttonName, "Run Pending Job");

    private static IReadOnlyList<UiElementRef> FindAddInElements(IReadOnlyList<UiElementRef> elements) =>
        elements
            .Where(element => IsTaskPaneMatch(element) || IsCommandMatch(element))
            .ToArray();

    private static IReadOnlyList<UiElementRef> MergeUiElements(
        IEnumerable<UiElementRef> first,
        IEnumerable<UiElementRef> second) =>
        first
            .Concat(second)
            .Distinct()
            .ToArray();

    private static UiElementRef? FindBestActivationCommand(IReadOnlyList<UiElementRef> elements) =>
        elements
            .Where(IsActivationCommandMatch)
            .Where(IsClickableActivationCandidate)
            .OrderBy(element => element.IsOffscreen ? 1 : 0)
            .ThenByDescending(ScoreActivationCommand)
            .ThenByDescending(element => ContainsFragment(element.ControlType, "MenuItem", "Button") ? 1 : 0)
            .ThenByDescending(element => element.Bounds.Width * element.Bounds.Height)
            .FirstOrDefault();

    private static bool HasVisibleRealLaunchCommand(IReadOnlyList<UiElementRef> elements) =>
        elements.Any(element =>
            IsRealLaunchCommandMatch(element) &&
            IsClickableActivationCandidate(element) &&
            !element.IsOffscreen);

    private static bool IsClickableActivationCandidate(UiElementRef element) =>
        element.IsEnabled &&
        element.Bounds.Width > 0 &&
        element.Bounds.Height > 0;

    private static int ScoreActivationCommand(UiElementRef element)
    {
        if (ContainsFragment(element.Name, "Run Update"))
        {
            return 6;
        }

        if (ContainsFragment(element.Name, "Windows Operator PowerPoint"))
        {
            return 5;
        }

        if (ContainsFragment(element.Name, "My Add-ins"))
        {
            return 3;
        }

        if (ContainsFragment(element.Name, "Office Add-ins"))
        {
            return 2;
        }

        if (IsGenericAddInCommandMatch(element))
        {
            return 1;
        }

        return 0;
    }

    private static ScreenClickRequest CreateScreenClickRequest(WindowBounds bounds) =>
        new()
        {
            X = bounds.X + (bounds.Width / 2),
            Y = bounds.Y + (bounds.Height / 2),
        };

    private static ScreenClickRequest CreateActivationClickRequest(UiElementRef element)
    {
        if (ContainsFragment(element.ControlType, "MenuItem"))
        {
            var yInset = Math.Clamp(element.Bounds.Height / 5, 1, Math.Max(1, element.Bounds.Height - 1));
            return new ScreenClickRequest
            {
                X = element.Bounds.X + (element.Bounds.Width / 2),
                Y = element.Bounds.Y + yInset,
            };
        }

        return CreateScreenClickRequest(element.Bounds);
    }

    private static string DescribeActivationClickTarget(string action, UiElementRef element) =>
        $"{action}:{NormalizeActionFragment(element.Name)}:{NormalizeActionFragment(element.ControlType)}:offscreen={element.IsOffscreen}:bounds={element.Bounds.X},{element.Bounds.Y},{element.Bounds.Width},{element.Bounds.Height}";

    private static string NormalizeActionFragment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "_"
            : value.Trim().Replace(':', '_').Replace(',', '_');

    private async Task<AddInActivationObservation> WaitForAddInTaskPaneAsync(
        long? hwnd,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        var warnings = new List<string>();
        IReadOnlyList<UiElementRef> matchedElements = Array.Empty<UiElementRef>();

        while (true)
        {
            try
            {
                matchedElements = FindAddInElements(await QueryWindowElementsAsync(hwnd, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"addin_uia_unavailable:{FormatObservationFailureMessage(ex)}");
                if (DateTimeOffset.UtcNow >= deadlineUtc)
                {
                    break;
                }

                await Task.Delay(AddInActivationPollInterval, cancellationToken);
                continue;
            }

            if (matchedElements.Any(IsTaskPaneMatch))
            {
                return new AddInActivationObservation(
                    true,
                    matchedElements.Any(IsCommandMatch),
                    matchedElements,
                    warnings);
            }

            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                break;
            }

            await Task.Delay(AddInActivationPollInterval, cancellationToken);
        }

        return new AddInActivationObservation(
            false,
            matchedElements.Any(IsCommandMatch),
            matchedElements,
            warnings);
    }

    private async Task<AddInActivationObservation> TryRevealAddInCommandAsync(
        long? hwnd,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var elements = await QueryWindowElementsForActivationAsync(hwnd, warnings, cancellationToken);
        var homeTab = FindVisibleControl(elements, IsHomeTabMatch);
        if (homeTab is not null)
        {
            var clickResult = await _inputService.ClickScreenAsync(CreateScreenClickRequest(homeTab.Bounds), cancellationToken);
            if (clickResult.Success)
            {
                actions.Add("addin_activation_home_tab_click_dispatched");
                await Task.Delay(AddInActivationPollInterval, cancellationToken);
                elements = await QueryWindowElementsForActivationAsync(hwnd, warnings, cancellationToken);
            }
            else
            {
                warnings.Add(clickResult.Message);
            }
        }

        var matchedElements = FindAddInElements(elements);
        if (HasVisibleRealLaunchCommand(matchedElements) ||
            matchedElements.Any(IsTaskPaneMatch))
        {
            return new AddInActivationObservation(
                matchedElements.Any(IsTaskPaneMatch),
                matchedElements.Any(IsCommandMatch),
                matchedElements,
                Array.Empty<string>());
        }

        var overflow = FindVisibleControl(elements, IsRibbonOverflowMatch);
        if (overflow is not null)
        {
            var clickResult = await _inputService.ClickScreenAsync(CreateScreenClickRequest(overflow.Bounds), cancellationToken);
            if (clickResult.Success)
            {
                actions.Add("addin_activation_overflow_click_dispatched");
                await Task.Delay(AddInActivationPollInterval, cancellationToken);
                elements = await QueryWindowElementsForActivationAsync(hwnd, warnings, cancellationToken);
                matchedElements = FindAddInElements(elements);
            }
            else
            {
                warnings.Add(clickResult.Message);
            }
        }

        matchedElements = FindAddInElements(elements);
        if (HasVisibleRealLaunchCommand(matchedElements) ||
            matchedElements.Any(IsTaskPaneMatch))
        {
            return new AddInActivationObservation(
                matchedElements.Any(IsTaskPaneMatch),
                matchedElements.Any(IsCommandMatch),
                matchedElements,
                Array.Empty<string>());
        }

        var insertTab = FindVisibleControl(elements, IsInsertTabMatch);
        if (insertTab is not null)
        {
            var clickResult = await _inputService.ClickScreenAsync(CreateScreenClickRequest(insertTab.Bounds), cancellationToken);
            if (clickResult.Success)
            {
                actions.Add("addin_activation_insert_tab_click_dispatched");
                await Task.Delay(AddInActivationPollInterval, cancellationToken);
                elements = await QueryWindowElementsForActivationAsync(hwnd, warnings, cancellationToken);
                matchedElements = FindAddInElements(elements);
            }
            else
            {
                warnings.Add(clickResult.Message);
            }
        }

        matchedElements = FindAddInElements(elements);
        if (HasVisibleRealLaunchCommand(matchedElements) ||
            matchedElements.Any(IsTaskPaneMatch))
        {
            return new AddInActivationObservation(
                matchedElements.Any(IsTaskPaneMatch),
                matchedElements.Any(IsCommandMatch),
                matchedElements,
                Array.Empty<string>());
        }

        overflow = FindVisibleControl(elements, IsRibbonOverflowMatch);
        if (overflow is not null)
        {
            var clickResult = await _inputService.ClickScreenAsync(CreateScreenClickRequest(overflow.Bounds), cancellationToken);
            if (clickResult.Success)
            {
                actions.Add("addin_activation_overflow_click_dispatched");
                await Task.Delay(AddInActivationPollInterval, cancellationToken);
                elements = await QueryWindowElementsForActivationAsync(hwnd, warnings, cancellationToken);
                matchedElements = FindAddInElements(elements);
            }
            else
            {
                warnings.Add(clickResult.Message);
            }
        }

        return new AddInActivationObservation(
            matchedElements.Any(IsTaskPaneMatch),
            matchedElements.Any(IsCommandMatch),
            matchedElements,
            Array.Empty<string>());
    }

    private async Task<AddInActivationObservation> RetryActivateAddInAsync(
        long? hwnd,
        List<string> actions,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        actions.Add("addin_activation_retry_requested");
        var revealObservation = await TryRevealAddInCommandAsync(hwnd, actions, warnings, cancellationToken);
        var matchedElements = revealObservation.MatchedElements;
        warnings.AddRange(revealObservation.Warnings);
        if (revealObservation.TaskPaneVisible)
        {
            actions.Add("addin_activation_retry_observed_ready");
            return revealObservation;
        }

        var retryCandidate = FindBestActivationCommand(matchedElements);
        if (retryCandidate is null)
        {
            actions.Add("addin_activation_retry_command_not_clickable");
            return revealObservation;
        }

        if (!IsRealLaunchCommandMatch(retryCandidate))
        {
            actions.Add("addin_activation_retry_command_not_clickable");
            return revealObservation;
        }

        if (retryCandidate.IsOffscreen)
        {
            actions.Add("addin_activation_retry_click_offscreen_candidate");
        }

        actions.Add(DescribeActivationClickTarget("addin_activation_retry_click_target", retryCandidate));
        var clickResult = await _inputService.ClickScreenAsync(
            CreateActivationClickRequest(retryCandidate),
            cancellationToken);
        if (!clickResult.Success)
        {
            actions.Add("addin_activation_retry_command_not_clickable");
            warnings.Add(clickResult.Message);
            return revealObservation;
        }

        actions.Add("addin_activation_retry_click_dispatched");
        var activationObservation = await WaitForAddInTaskPaneAsync(
            hwnd,
            10,
            cancellationToken);
        actions.Add(activationObservation.TaskPaneVisible
            ? "addin_activation_retry_observed_ready"
            : "addin_activation_retry_timeout");
        return activationObservation;
    }

    private async Task<IReadOnlyList<UiElementRef>> QueryWindowElementsForActivationAsync(
        long? hwnd,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(2);
        Exception? lastException = null;
        while (true)
        {
            try
            {
                return await QueryWindowElementsAsync(hwnd, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                if (DateTimeOffset.UtcNow >= deadlineUtc)
                {
                    warnings.Add($"addin_uia_unavailable:{FormatObservationFailureMessage(lastException)}");
                    return Array.Empty<UiElementRef>();
                }

                await Task.Delay(AddInActivationPollInterval, cancellationToken);
            }
        }
    }

    private static UiElementRef? FindVisibleControl(
        IReadOnlyList<UiElementRef> elements,
        Func<UiElementRef, bool> predicate) =>
        elements
            .Where(predicate)
            .Where(element => element.IsEnabled && !element.IsOffscreen && element.Bounds.Width > 0 && element.Bounds.Height > 0)
            .OrderByDescending(element => ContainsFragment(element.ControlType, "Button", "TabItem") ? 1 : 0)
            .ThenByDescending(element => element.Bounds.Width * element.Bounds.Height)
            .FirstOrDefault();

    private static bool IsHomeTabMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "Home") &&
        (ContainsFragment(element.AutomationId, "Home") || ContainsFragment(element.ControlType, "TabItem"));

    private static bool IsInsertTabMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "Insert") &&
        (ContainsFragment(element.AutomationId, "Insert") || ContainsFragment(element.ControlType, "TabItem"));

    private static bool IsRibbonOverflowMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "More Options") ||
        ContainsFragment(element.AutomationId, "RibbonOverflowMenu-overflow");

    private static bool IsTaskPaneMatch(UiElementRef element) =>
        ContainsFragment(
            element.Name,
            "Windows Operator PowerPoint",
            "Run Pending Job",
            "Prepare Template",
            "Cleanup Template",
            "Prepare Named Targets",
            "Cleanup Named Targets");

    private static bool IsCommandMatch(UiElementRef element) =>
        IsActivationCommandMatch(element) ||
        IsUpdaterGroupMatch(element) ||
        ContainsFragment(element.ControlType, "Add-ins");

    private static bool IsActivationCommandMatch(UiElementRef element) =>
        IsRealLaunchCommandMatch(element) || IsGenericAddInCommandMatch(element);

    private static bool IsRealLaunchCommandMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "Run Update") ||
        (ContainsFragment(element.Name, "Windows Operator PowerPoint") &&
         !ContainsFragment(element.ControlType, "Pane"));

    private static bool IsGenericAddInCommandMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "Office Add-ins", "My Add-ins", "Add-ins");

    private static bool IsUpdaterGroupMatch(UiElementRef element) =>
        ContainsFragment(element.Name, "Updater") &&
        ContainsFragment(element.ControlType, "Group");

    private static bool ContainsFragment(string? text, params string[] values) =>
        !string.IsNullOrWhiteSpace(text) &&
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static SessionObservation ObserveSession(IReadOnlyList<UiElementRef> elements)
    {
        int? currentSlide = null;
        int? slideCount = null;
        string? editMode = null;
        string? saveState = null;

        foreach (var element in elements)
        {
            if ((currentSlide is null || slideCount is null) &&
                TryParseSlideObservation(element.Name, out var parsedCurrentSlide, out var parsedSlideCount))
            {
                currentSlide = parsedCurrentSlide;
                slideCount = parsedSlideCount;
            }

            if (saveState is null && TryNormalizeSaveState(element, out var parsedSaveState))
            {
                saveState = parsedSaveState;
            }

            if (editMode is null && TryNormalizeEditMode(element, out var parsedEditMode))
            {
                editMode = parsedEditMode;
            }

            if (currentSlide is not null && slideCount is not null && editMode is not null && saveState is not null)
            {
                break;
            }
        }

        return new SessionObservation(currentSlide, slideCount, editMode, saveState);
    }

    private static bool TryParseSlideObservation(string? name, out int currentSlide, out int slideCount)
    {
        currentSlide = 0;
        slideCount = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var match = SlideCountRegex.Match(name.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups["current"].Value, out currentSlide) ||
            !int.TryParse(match.Groups["count"].Value, out slideCount))
        {
            return false;
        }

        return true;
    }

    private static bool TryNormalizeSaveState(UiElementRef element, out string? saveState)
    {
        saveState = null;
        var hasSaveStatusId = string.Equals(element.AutomationId, "SaveStatusButton", StringComparison.Ordinal);
        if (!hasSaveStatusId && string.IsNullOrWhiteSpace(element.Name))
        {
            return false;
        }

        var name = element.Name?.Trim() ?? string.Empty;
        if (name.StartsWith("Saved", StringComparison.OrdinalIgnoreCase))
        {
            saveState = "saved";
            return true;
        }

        if (name.StartsWith("Saving", StringComparison.OrdinalIgnoreCase))
        {
            saveState = "saving";
            return true;
        }

        return false;
    }

    private static bool TryNormalizeEditMode(UiElementRef element, out string? editMode)
    {
        editMode = null;
        var hasModeSwitcherId = string.Equals(element.AutomationId, "ModeSwitcher", StringComparison.Ordinal);
        var name = element.Name?.Trim();
        if (!hasModeSwitcherId && string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name is null)
        {
            return false;
        }

        if (name.Contains("Editing", StringComparison.OrdinalIgnoreCase))
        {
            editMode = "editing";
            return true;
        }

        if (name.Contains("Viewing", StringComparison.OrdinalIgnoreCase))
        {
            editMode = "viewing";
            return true;
        }

        if (name.Contains("Reviewing", StringComparison.OrdinalIgnoreCase))
        {
            editMode = "reviewing";
            return true;
        }

        return false;
    }

    private static string FormatObservationFailureMessage(Exception ex)
    {
        if (ex is not OperatorFailureException failure)
        {
            return ex.Message;
        }

        return failure.Error.Details is not null &&
            failure.Error.Details.TryGetValue("detail", out var detail) &&
            !string.IsNullOrWhiteSpace(detail)
            ? $"{failure.Error.Message} {detail}"
            : failure.Error.Message;
    }

    private readonly record struct SlideKeyboardCorrection(
        int FromSlide,
        int TargetSlide,
        string Key,
        int Steps);

    private sealed record SlideSelectionVerification(
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<OperatorError> Errors);

    private sealed record PowerPointOnlineSessionMetadata(
        string SessionId,
        string BrowserSessionId,
        string DeckUrl,
        PowerPointOnlineSessionStatus Status,
        WorkbenchRunRef ArtifactRoot,
        string? CanonicalUrl,
        string? CurrentUrl,
        string? CurrentTitle,
        int? CurrentSlide,
        int? SlideCount,
        string? EditMode,
        string? SaveState,
        long? Hwnd,
        DateTimeOffset ObservedAtUtc);

    private sealed record SessionObservation(
        int? CurrentSlide,
        int? SlideCount,
        string? EditMode,
        string? SaveState)
    {
        public static SessionObservation Empty { get; } = new(null, null, null, null);
    }

    private sealed record AddInActivationObservation(
        bool TaskPaneVisible,
        bool CommandVisible,
        IReadOnlyList<UiElementRef> MatchedElements,
        IReadOnlyList<string> Warnings);
}
