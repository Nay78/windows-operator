namespace WindowsOperator.Core.Contracts;

public enum DevScriptStatus
{
    Succeeded,
    Disabled,
    BlockedSession,
    TargetNotFound,
    ScriptNotFound,
    MutationNotAllowed,
    RawJsDisabled,
    Timeout,
    ScriptFailed,
    ResultTooLarge,
}
