namespace WindowsOperator.Core.Contracts;

public enum PowerPointOnlineSessionStatus
{
    Opening,
    Ready,
    BlockedAuth,
    BlockedPermission,
    BlockedReadonly,
    BlockedOfficeError,
    Failed,
    Closed,
}
