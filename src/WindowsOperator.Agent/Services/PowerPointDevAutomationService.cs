using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

internal sealed class PowerPointDevAutomationService : IDevAutomationService
{
    private static readonly Regex SecretPattern = new(
        "(?i)(token|cookie|password|secret|authorization)([\\\"'\\s:=]+)([^\\\"'\\s,;}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IEdgeBrowserService _edgeBrowser;
    private readonly IEdgeDevToolsService _devTools;
    private readonly IPowerPointDevScriptCatalog _catalog;
    private readonly IWorkbenchService _workbench;
    private readonly WorkbenchRunStore _runs;
    private readonly IOptions<DevAutomationOptions> _options;

    public PowerPointDevAutomationService(
        IEdgeBrowserService edgeBrowser,
        IEdgeDevToolsService devTools,
        IPowerPointDevScriptCatalog catalog,
        IWorkbenchService workbench,
        WorkbenchRunStore runs,
        IOptions<DevAutomationOptions> options)
    {
        _edgeBrowser = edgeBrowser;
        _devTools = devTools;
        _catalog = catalog;
        _workbench = workbench;
        _runs = runs;
        _options = options;
    }

    public async Task<DevScriptResult> RunPowerPointOnlineScriptAsync(
        string sessionId,
        PowerPointDevScriptRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new PowerPointDevScriptRequest();
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(request.ScriptId))
        {
            throw new OperatorFailureException(
                OperatorErrors.DevAutomationValidationFailed("scriptId is required."));
        }

        var script = _catalog.Find(request.ScriptId);
        if (script is null)
        {
            return await WriteResultAsync(
                sessionId,
                request.RunId,
                request.ScriptId,
                null,
                null,
                DevScriptStatus.ScriptNotFound,
                false,
                new[] { "dev_script_requested" },
                Array.Empty<string>(),
                new[] { $"Unknown PowerPoint dev script: {request.ScriptId}" },
                null,
                null,
                null,
                request.CaptureScreenshot,
                request.Label,
                cancellationToken);
        }

        if (script.MutatesDeck && !request.AllowDeckMutation)
        {
            throw new OperatorFailureException(
                OperatorErrors.DevAutomationValidationFailed(
                    $"Script '{request.ScriptId}' mutates the deck and requires allowDeckMutation=true."));
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(
            request.TimeoutSeconds <= 0 ? 5 : request.TimeoutSeconds,
            1,
            Math.Max(1, script.TimeoutCapSeconds)));

        return await EvaluateAsync(
            sessionId,
            request.RunId,
            script.ScriptId,
            script.Target,
            script.Expression,
            timeout,
            request.CaptureScreenshot,
            request.Label,
            null,
            cancellationToken);
    }

    public async Task<DevScriptResult> EvaluateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeDevEvalRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new BrowserEdgeDevEvalRequest();
        EnsureEnabled();
        if (!_options.Value.AllowRawJs || !request.AllowUnsafeRawJs)
        {
            throw new OperatorFailureException(
                OperatorErrors.DevRawJsDisabled(
                    "Raw JS requires DevAutomation:AllowRawJs=true and allowUnsafeRawJs=true."));
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new OperatorFailureException(
                OperatorErrors.DevAutomationValidationFailed("source is required."));
        }

        var sourceHash = Sha256(request.Source);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds <= 0 ? 5 : request.TimeoutSeconds, 1, 30));
        return await EvaluateAsync(
            sessionId,
            request.RunId,
            "raw.browser.eval",
            "browser-page",
            request.Source,
            timeout,
            request.CaptureScreenshot,
            request.Label,
            sourceHash,
            cancellationToken);
    }

    private async Task<DevScriptResult> EvaluateAsync(
        string sessionId,
        string? runId,
        string scriptId,
        string targetName,
        string expression,
        TimeSpan timeout,
        bool captureScreenshot,
        string? label,
        string? sourceSha256,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var actions = new List<string> { "dev_script_requested" };
        var warnings = new List<string>();
        var errors = new List<string>();
        BrowserEdgeSessionStateResult? state = null;
        EdgePageTarget? target = null;

        try
        {
            state = await _edgeBrowser.GetSessionStateAsync(sessionId, cancellationToken);
        }
        catch (OperatorFailureException ex)
        {
            errors.Add(ex.Error.Details?.TryGetValue("detail", out var detail) == true ? detail : ex.Error.Message);
            return await WriteResultAsync(
                sessionId,
                runId,
                scriptId,
                targetName,
                null,
                DevScriptStatus.BlockedSession,
                false,
                actions,
                warnings,
                errors,
                null,
                null,
                sourceSha256,
                captureScreenshot,
                label,
                cancellationToken,
                startedAtUtc);
        }

        if (!state.IsAlive || state.DevToolsPort is null)
        {
            errors.Add(state.DevToolsPort is null
                ? "Edge session has no DevTools port."
                : "Edge session is not alive.");
            return await WriteResultAsync(
                sessionId,
                runId,
                scriptId,
                targetName,
                null,
                DevScriptStatus.BlockedSession,
                false,
                actions,
                warnings,
                errors,
                null,
                null,
                sourceSha256,
                captureScreenshot,
                label,
                cancellationToken,
                startedAtUtc);
        }

        target = _devTools.ReadTarget(state.DevToolsPort.Value, state.Url);
        if (target is null)
        {
            errors.Add("DevTools target unavailable.");
            return await WriteResultAsync(
                sessionId,
                runId,
                scriptId,
                targetName,
                null,
                DevScriptStatus.TargetNotFound,
                false,
                actions,
                warnings,
                errors,
                null,
                null,
                sourceSha256,
                captureScreenshot,
                label,
                cancellationToken,
                startedAtUtc);
        }

        actions.Add("devtools_target_selected");
        var evaluation = _devTools.Evaluate(target.Value.WebSocketDebuggerUrl, expression, timeout);
        if (evaluation.TimedOut)
        {
            errors.Add(evaluation.ErrorText ?? "JavaScript execution timed out.");
            return await WriteResultAsync(
                sessionId,
                runId,
                scriptId,
                targetName,
                target,
                DevScriptStatus.Timeout,
                false,
                actions,
                warnings,
                errors,
                null,
                null,
                sourceSha256,
                captureScreenshot,
                label,
                cancellationToken,
                startedAtUtc);
        }

        if (!evaluation.Success)
        {
            errors.Add(evaluation.ErrorText ?? "JavaScript execution failed.");
            return await WriteResultAsync(
                sessionId,
                runId,
                scriptId,
                targetName,
                target,
                DevScriptStatus.ScriptFailed,
                false,
                actions,
                warnings,
                errors,
                null,
                null,
                sourceSha256,
                captureScreenshot,
                label,
                cancellationToken,
                startedAtUtc);
        }

        actions.Add("dev_script_evaluated");
        var output = BuildOutput(evaluation, _options.Value.MaxResultBytes, warnings);
        var status = output.Truncated ? DevScriptStatus.ResultTooLarge : DevScriptStatus.Succeeded;
        if (output.Truncated)
        {
            errors.Add("Result exceeded DevAutomation:MaxResultBytes and was capped.");
        }

        return await WriteResultAsync(
            sessionId,
            runId,
            scriptId,
            targetName,
            target,
            status,
            !output.Truncated,
            actions,
            warnings,
            errors,
            output.ResultJson,
            output.ResultText,
            sourceSha256,
            captureScreenshot,
            label,
            cancellationToken,
            startedAtUtc);
    }

    private async Task<DevScriptResult> WriteResultAsync(
        string sessionId,
        string? runId,
        string scriptId,
        string? targetName,
        EdgePageTarget? target,
        DevScriptStatus status,
        bool success,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        string? resultJson,
        string? resultText,
        string? sourceSha256,
        bool captureScreenshot,
        string? label,
        CancellationToken cancellationToken,
        DateTimeOffset? startedAtUtc = null)
    {
        var run = _runs.ResolveRun(runId ?? $"dev-js-{sessionId}", "dev-js");
        var nextActions = actions.ToList();
        var nextWarnings = warnings.ToList();
        string? screenshotPath = null;
        if (captureScreenshot)
        {
            try
            {
                var screenshot = await _workbench.CaptureEdgeSessionScreenshotAsync(
                    sessionId,
                    new DesktopScreenshotRequest
                    {
                        RunId = run.RunId,
                        Label = label ?? scriptId,
                        Target = "hwnd",
                    },
                    cancellationToken);
                screenshotPath = screenshot.Artifact.HostPath;
                nextActions.Add($"screenshot_captured:{screenshot.Artifact.RelativePath}");
            }
            catch (Exception ex)
            {
                nextWarnings.Add($"screenshot_capture_failed:{ex.Message}");
            }
        }

        var observedAtUtc = DateTimeOffset.UtcNow;
        var result = new DevScriptResult
        {
            Success = success,
            Status = status,
            SessionId = sessionId,
            ScriptId = scriptId,
            Target = targetName,
            TargetUrl = target?.Url,
            TargetTitle = target?.Title,
            ResultJson = resultJson,
            ResultText = resultText,
            SourceSha256 = sourceSha256,
            Actions = nextActions,
            Warnings = nextWarnings,
            Errors = errors,
            ObservedAtUtc = observedAtUtc,
        };
        var auditName = WorkbenchRunStore.SanitizePathSegment(
            $"{scriptId}-{observedAtUtc:yyyyMMddTHHmmssfffZ}.json",
            "dev-script-result.json");
        var resultByteCount = Encoding.UTF8.GetByteCount(resultJson ?? string.Empty)
            + Encoding.UTF8.GetByteCount(resultText ?? string.Empty);
        _runs.WriteJson(
            run,
            auditName,
            new
            {
                scriptId,
                target = targetName,
                targetUrl = target?.Url,
                targetTitle = target?.Title,
                status,
                success,
                startedAtUtc = startedAtUtc ?? observedAtUtc,
                observedAtUtc,
                completedAtUtc = observedAtUtc,
                resultByteCount,
                screenshotPath,
                sourceSha256,
                result,
                targetDetails = target,
            });
        var evidencePath = run.HostPath.TrimEnd('/', '\\') + "/" + auditName;
        _runs.AppendEvent(
            run,
            "dev_script_run",
            new
            {
                scriptId,
                status,
                success,
                targetUrl = target?.Url,
                sourceSha256,
                audit = auditName,
            });
        return result with
        {
            EvidencePath = evidencePath,
            Actions = result.Actions.Concat(new[] { $"audit_written:{auditName}" }).ToArray(),
        };
    }

    private void EnsureEnabled()
    {
        if (_options.Value.Enabled)
        {
            return;
        }

        throw new OperatorFailureException(
            OperatorErrors.DevAutomationDisabled(
                "Developer automation endpoints are disabled by default."));
    }

    private static DevOutput BuildOutput(
        EdgeDevToolsEvaluation evaluation,
        int maxResultBytes,
        List<string> warnings)
    {
        maxResultBytes = Math.Clamp(maxResultBytes <= 0 ? 65536 : maxResultBytes, 1024, 1024 * 1024);
        var raw = evaluation.ValueText ?? evaluation.ValueJson ?? string.Empty;
        raw = Redact(raw);
        var truncated = raw.Length > maxResultBytes;
        if (truncated)
        {
            raw = raw[..maxResultBytes];
            warnings.Add("result_capped");
        }

        if (LooksLikeJson(raw))
        {
            return new DevOutput(raw, null, truncated);
        }

        return new DevOutput(null, raw, truncated);
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string Redact(string value) =>
        SecretPattern.Replace(value, "$1$2[redacted]");

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record DevOutput(string? ResultJson, string? ResultText, bool Truncated);
}
