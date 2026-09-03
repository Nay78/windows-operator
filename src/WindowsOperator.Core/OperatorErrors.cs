using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core;

public static class OperatorErrors
{
    public static OperatorError InvalidRequest(string detail) =>
        Create(
            ErrorCodes.InvalidRequest,
            "Request payload or parameters are invalid.",
            "Fix the request using the published OpenAPI contract, then retry.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError RouteNotFound(string detail) =>
        Create(
            ErrorCodes.RouteNotFound,
            "Requested API route does not exist.",
            "Use a method and path published by the live OpenAPI document.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError MethodNotAllowed(string detail) =>
        Create(
            ErrorCodes.MethodNotAllowed,
            "HTTP method is not allowed for this API route.",
            "Use the method published for this path by the live OpenAPI document.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError InternalError() =>
        Create(
            ErrorCodes.InternalError,
            "Windows Operator encountered an unexpected failure.",
            "Retry once. If the failure persists, inspect server logs using the correlation id.",
            "Unhandled endpoint exception.",
            OperatorErrorCategory.Internal,
            retryable: true);

    public static OperatorError LockedDesktop(string detail) =>
        Create(
            ErrorCodes.LockedDesktop,
            "Desktop session locked or unavailable.",
            "Unlock desktop session or reconnect to active console session before retrying.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError UipiBlocked(string detail) =>
        Create(
            ErrorCodes.UipiBlocked,
            "Windows blocked input across integrity boundary.",
            "Retry against a non-elevated target or add UIAccess hardening in a later phase.",
            detail,
            OperatorErrorCategory.Permission,
            retryable: false);

    public static OperatorError ElevatedTarget(string detail) =>
        Create(
            ErrorCodes.ElevatedTarget,
            "Target window runs elevated and v1 will not cross UAC boundary.",
            "Launch target unelevated or postpone until UIAccess support exists.",
            detail,
            OperatorErrorCategory.Permission,
            retryable: false);

    public static OperatorError WindowNotFound(string detail) =>
        Create(
            ErrorCodes.WindowNotFound,
            "Requested window handle no longer exists.",
            "Refresh window list and retry with a current hwnd.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: true);

    public static OperatorError BlankCapture(string detail) =>
        Create(
            ErrorCodes.BlankCapture,
            "Capture pipeline produced a blank image.",
            "Bring target window to foreground, avoid minimized RDP, then retry.",
            detail,
            OperatorErrorCategory.Conflict,
            retryable: true);

    public static OperatorError MinimizedRdp(string detail) =>
        Create(
            ErrorCodes.MinimizedRdp,
            "Target session appears minimized or not presentable.",
            "Restore desktop session or keep RDP window active before capture.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError UnsupportedControl(string detail) =>
        Create(
            ErrorCodes.UnsupportedControl,
            "Requested control does not expose a supported automation path.",
            "Retry with a narrower selector or fallback to keyboard navigation.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError AuthUnavailable(string detail) =>
        Create(
            ErrorCodes.AuthUnavailable,
            "Microsoft authentication browser handoff is unavailable.",
            "Confirm the Windows desktop session is logged in and Microsoft Edge is installed, then retry.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError AuthRunNotFound(string detail) =>
        Create(
            ErrorCodes.AuthRunNotFound,
            "Requested Microsoft authentication run was not found.",
            "Check the run id or start a new Microsoft authentication handoff.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError BrowserSessionNotFound(string detail) =>
        Create(
            ErrorCodes.BrowserSessionNotFound,
            "Requested Edge browser session was not found.",
            "Check the session id or start a new owned Edge browser session.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError WorkbenchSessionNotFound(string detail) =>
        Create(
            ErrorCodes.WorkbenchSessionNotFound,
            "Requested workbench session was not found.",
            "Check the session id or open a new workbench session.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError PowerPointUnavailable(string detail) =>
        Create(
            ErrorCodes.PowerPointUnavailable,
            "PowerPoint automation is unavailable.",
            "Confirm PowerPoint is installed in the logged-in Windows desktop session, then retry.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError PowerPointValidationFailed(string detail) =>
        Create(
            ErrorCodes.PowerPointValidationFailed,
            "PowerPoint edit request is invalid.",
            "Inspect the presentation, fix selectors or paths, then retry.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError PowerPointSessionNotFound(string detail) =>
        Create(
            ErrorCodes.PowerPointSessionNotFound,
            "Requested PowerPoint Online session was not found.",
            "Check the session id or start a new PowerPoint Online session.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError PowerPointJobNotFound(string detail) =>
        Create(
            ErrorCodes.PowerPointJobNotFound,
            "Requested PowerPoint job was not found.",
            "Check the job id or rerun the edit request.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError ArtifactNotFound(string detail) =>
        Create(
            ErrorCodes.ArtifactNotFound,
            "Requested artifact was not found.",
            "List run artifacts or retry with a current artifact id.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError DevAutomationDisabled(string detail) =>
        Create(
            ErrorCodes.DevAutomationDisabled,
            "Developer automation is disabled.",
            "Set DevAutomation:Enabled=true or WINDOWS_OPERATOR_DEV_AUTOMATION=1, then retry.",
            detail,
            OperatorErrorCategory.Permission,
            retryable: false);

    public static OperatorError DevRawJsDisabled(string detail) =>
        Create(
            ErrorCodes.DevRawJsDisabled,
            "Raw JavaScript evaluation is disabled.",
            "Set DevAutomation:AllowRawJs=true or WINDOWS_OPERATOR_DEV_RAW_JS=1 and send allowUnsafeRawJs=true.",
            detail,
            OperatorErrorCategory.Permission,
            retryable: false);

    public static OperatorError DevAutomationValidationFailed(string detail) =>
        Create(
            ErrorCodes.DevAutomationValidationFailed,
            "Developer automation request is invalid.",
            "Fix the script id, source, or mutation approval fields, then retry.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError MailUnavailable(string detail) =>
        Create(
            ErrorCodes.MailUnavailable,
            "Outlook mailbox automation is unavailable.",
            "Confirm Classic Outlook is configured in the logged-in desktop session, then retry.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError MailFolderNotFound(string detail) =>
        Create(
            ErrorCodes.MailFolderNotFound,
            "Requested Outlook folder was not found.",
            "List mail folders and retry with an exact folder path.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError MailRunNotFound(string detail) =>
        Create(
            ErrorCodes.MailRunNotFound,
            "Requested mail run was not found.",
            "Check the run id or rerun the download.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError PowerAutomateMcpUnavailable(string detail) =>
        Create(
            ErrorCodes.PowerAutomateMcpUnavailable,
            "Power Automate MCP bridge is unavailable.",
            "Confirm Windows desktop session, Microsoft Edge, Node.js, npm, and loopback bridge state, then retry.",
            detail,
            OperatorErrorCategory.Unavailable,
            retryable: true);

    public static OperatorError PowerAutomateMcpValidationFailed(string detail) =>
        Create(
            ErrorCodes.PowerAutomateMcpValidationFailed,
            "Power Automate MCP request is invalid.",
            "Fix the bridge host, port, package, URL, or extension path, then retry.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError OpenApiNamespaceNotFound(string detail) =>
        Create(
            ErrorCodes.OpenApiNamespaceNotFound,
            "Requested OpenAPI namespace was not found.",
            "List OpenAPI namespaces and retry with a known namespace.",
            detail,
            OperatorErrorCategory.NotFound,
            retryable: false);

    public static OperatorError OpenApiSurfaceInvalid(string detail) =>
        Create(
            ErrorCodes.OpenApiSurfaceInvalid,
            "OpenAPI surface filter is invalid.",
            "Use stable, diagnostic, development, all, or a comma-separated list.",
            detail,
            OperatorErrorCategory.Validation,
            retryable: false);

    public static OperatorError OneDriveUnavailable(string detail) =>
        Create(ErrorCodes.OneDriveUnavailable, "OneDrive Files-On-Demand is unavailable.", "Confirm OneDrive is running and signed in, then retry.", detail, OperatorErrorCategory.Unavailable, true);

    public static OperatorError OneDriveUnavailable(string detail, OneDriveRuntimeEvidence runtime)
    {
        var remediation = runtime.AuthenticationRequired
            ? "Sign in to OneDrive in the active Administrator desktop session, then retry. Windows Operator will not automate sign-in."
            : runtime.ConfiguredSessionId is not null &&
              (!string.Equals(runtime.InteractiveUser, "Administrator", StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(runtime.InteractiveSessionState, "active", StringComparison.OrdinalIgnoreCase) ||
               runtime.InteractiveSessionProtocol is not 0 and not 2)
                ? $"Open and unlock Administrator RDP session {runtime.ConfiguredSessionId} on the allowlisted VM, then retry."
                : runtime.ActiveInteractiveSessionId is null
                    ? "Open the Administrator desktop session on the allowlisted VM, then retry."
                : !runtime.RecoveryAllowed
                    ? "Enable OneDrive recovery only on WIN-UUKQS009K4J, then retry."
                    : "Retry after OneDrive process and Files-On-Demand provider readiness recover.";
        return new OperatorError(
            ErrorCodes.OneDriveUnavailable,
            "OneDrive Files-On-Demand is unavailable.",
            remediation,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["detail"] = detail,
                ["reason"] = runtime.ProviderReason ?? "provider_not_ready",
                ["computerName"] = runtime.ComputerName ?? "unknown",
                ["processPresent"] = runtime.ProcessPresent.ToString().ToLowerInvariant(),
                ["processSessionId"] = runtime.ProcessSessionId?.ToString() ?? "none",
                ["configuredSessionId"] = runtime.ConfiguredSessionId?.ToString() ?? "none",
                ["activeInteractiveSessionId"] = runtime.ActiveInteractiveSessionId?.ToString() ?? "none",
                ["interactiveUser"] = runtime.InteractiveUser ?? "unknown",
                ["interactiveSessionState"] = runtime.InteractiveSessionState ?? "unknown",
                ["interactiveSessionProtocol"] = runtime.InteractiveSessionProtocol?.ToString() ?? "unknown",
                ["recoveryAllowed"] = runtime.RecoveryAllowed.ToString().ToLowerInvariant(),
                ["authenticationRequired"] = runtime.AuthenticationRequired.ToString().ToLowerInvariant(),
                ["actions"] = string.Join(',', runtime.RecoveryActions),
            },
            Retryable: true,
            Category: OperatorErrorCategory.Unavailable);
    }

    public static OperatorError OneDriveRootNotFound(string detail) =>
        Create(ErrorCodes.OneDriveRootNotFound, "Configured OneDrive root was not found.", "Use an enabled configured root id.", detail, OperatorErrorCategory.NotFound, false);

    public static OperatorError OneDriveFileNotFound(string detail) =>
        Create(ErrorCodes.OneDriveFileNotFound, "Requested OneDrive file was not found.", "Check the configured root and relative path.", detail, OperatorErrorCategory.NotFound, false);

    public static OperatorError OneDriveLeaseNotFound(string detail) =>
        Create(ErrorCodes.OneDriveLeaseNotFound, "Requested OneDrive lease was not found.", "Check the lease id or acquire a new lease.", detail, OperatorErrorCategory.NotFound, false);

    public static OperatorError OneDriveReclaimNotFound(string detail) =>
        Create(ErrorCodes.OneDriveReclaimNotFound, "Requested OneDrive reclaim run was not found.", "Check the reclaim run id or start a new reclaim run.", detail, OperatorErrorCategory.NotFound, false);

    public static OperatorError OneDrivePathBlocked(string detail) =>
        Create(ErrorCodes.OneDrivePathBlocked, "OneDrive path is blocked by containment policy.", "Use a relative path contained by an approved root.", detail, OperatorErrorCategory.Validation, false);

    public static OperatorError OneDrivePolicyDenied(string detail) =>
        Create(ErrorCodes.OneDrivePolicyDenied, "OneDrive operation is denied by policy.", "Review the configured root and Files-On-Demand policy.", detail, OperatorErrorCategory.Permission, false);

    public static OperatorError OneDriveIdempotencyConflict(string detail) =>
        Create(ErrorCodes.OneDriveIdempotencyConflict, "OneDrive request id was reused with different content.", "Use a new request id or repeat the original request exactly.", detail, OperatorErrorCategory.Conflict, false);

    public static OperatorError OneDriveConfigConflict(string detail) =>
        Create(ErrorCodes.OneDriveConfigConflict, "OneDrive configuration version conflicts with the current policy.", "Read current configuration and retry with its ETag.", detail, OperatorErrorCategory.Conflict, false);

    public static OperatorError OneDriveLeaseConflict(string detail) =>
        Create(ErrorCodes.OneDriveLeaseConflict, "OneDrive lease cannot transition in its current state.", "Read lease status and retry only when its lifecycle permits the operation.", detail, OperatorErrorCategory.Conflict, false);

    public static OperatorError OneDriveContentChanged(string detail) =>
        Create(ErrorCodes.OneDriveContentChanged, "OneDrive file content or identity changed.", "Acquire a new lease after inspecting the current file.", detail, OperatorErrorCategory.Conflict, false);

    public static OperatorError OneDriveHydrationTimeout(string detail) =>
        Create(ErrorCodes.OneDriveHydrationTimeout, "OneDrive hydration did not complete before timeout.", "Confirm OneDrive connectivity, then retry.", detail, OperatorErrorCategory.Timeout, true);

    public static OperatorError OneDriveDehydrationTimeout(string detail) =>
        Create(ErrorCodes.OneDriveDehydrationTimeout, "OneDrive dehydration did not complete before timeout.", "Retry release after OneDrive becomes available; local bytes may remain resident.", detail, OperatorErrorCategory.Timeout, true);

    public static OperatorError OneDriveHydrationFailed(string detail) =>
        Create(ErrorCodes.OneDriveHydrationFailed, "OneDrive hydration failed.", "Confirm OneDrive connectivity and file availability, then retry.", detail, OperatorErrorCategory.Unavailable, true);

    public static OperatorError OneDriveDehydrationFailed(string detail) =>
        Create(ErrorCodes.OneDriveDehydrationFailed, "OneDrive dehydration failed.", "Inspect file state; local bytes were left resident for safety.", detail, OperatorErrorCategory.Conflict, false);

    public static OperatorError OneDriveVerificationFailed(string detail) =>
        Create(ErrorCodes.OneDriveVerificationFailed, "OneDrive operation could not be verified.", "Inspect file state before retrying; local bytes may remain resident.", detail, OperatorErrorCategory.Conflict, false);

    private static OperatorError Create(
        string code,
        string message,
        string remediation,
        string detail,
        OperatorErrorCategory category,
        bool retryable) =>
        new(
            code,
            message,
            remediation,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["detail"] = detail,
            },
            Retryable: retryable,
            Category: category);
}
