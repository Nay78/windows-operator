namespace WindowsOperator.Core.Contracts;

public enum PowerPointOnlineUpdateStatus
{
    Succeeded,
    Failed,
    BlockedSession,
    BlockedAddIn,
    SaveUnverified,
    VerificationFailed,
    CleanupFailed,
    SessionCleanupFailed,
}
