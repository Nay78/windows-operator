using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core;

public static class OperatorErrors
{
    public static OperatorError LockedDesktop(string detail) =>
        Create(
            ErrorCodes.LockedDesktop,
            "Desktop session locked or unavailable.",
            "Unlock desktop session or reconnect to active console session before retrying.",
            detail);

    public static OperatorError UipiBlocked(string detail) =>
        Create(
            ErrorCodes.UipiBlocked,
            "Windows blocked input across integrity boundary.",
            "Retry against a non-elevated target or add UIAccess hardening in a later phase.",
            detail);

    public static OperatorError ElevatedTarget(string detail) =>
        Create(
            ErrorCodes.ElevatedTarget,
            "Target window runs elevated and v1 will not cross UAC boundary.",
            "Launch target unelevated or postpone until UIAccess support exists.",
            detail);

    public static OperatorError WindowNotFound(string detail) =>
        Create(
            ErrorCodes.WindowNotFound,
            "Requested window handle no longer exists.",
            "Refresh window list and retry with a current hwnd.",
            detail);

    public static OperatorError BlankCapture(string detail) =>
        Create(
            ErrorCodes.BlankCapture,
            "Capture pipeline produced a blank image.",
            "Bring target window to foreground, avoid minimized RDP, then retry.",
            detail);

    public static OperatorError MinimizedRdp(string detail) =>
        Create(
            ErrorCodes.MinimizedRdp,
            "Target session appears minimized or not presentable.",
            "Restore desktop session or keep RDP window active before capture.",
            detail);

    public static OperatorError UnsupportedControl(string detail) =>
        Create(
            ErrorCodes.UnsupportedControl,
            "Requested control does not expose a supported automation path.",
            "Retry with a narrower selector or fallback to keyboard navigation.",
            detail);

    public static OperatorError AuthUnavailable(string detail) =>
        Create(
            ErrorCodes.AuthUnavailable,
            "Microsoft authentication browser handoff is unavailable.",
            "Confirm the Windows desktop session is logged in and Microsoft Edge is installed, then retry.",
            detail);

    public static OperatorError MailUnavailable(string detail) =>
        Create(
            ErrorCodes.MailUnavailable,
            "Outlook mailbox automation is unavailable.",
            "Confirm Classic Outlook is configured in the logged-in desktop session, then retry.",
            detail);

    public static OperatorError MailFolderNotFound(string detail) =>
        Create(
            ErrorCodes.MailFolderNotFound,
            "Requested Outlook folder was not found.",
            "List mail folders and retry with an exact folder path.",
            detail);

    public static OperatorError MailRunNotFound(string detail) =>
        Create(
            ErrorCodes.MailRunNotFound,
            "Requested mail run was not found.",
            "Check the run id or rerun the download.",
            detail);

    private static OperatorError Create(
        string code,
        string message,
        string remediation,
        string detail) =>
        new(
            code,
            message,
            remediation,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["detail"] = detail,
            });
}
