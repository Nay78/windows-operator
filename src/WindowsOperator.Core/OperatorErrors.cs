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
