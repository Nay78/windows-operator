namespace WindowsOperator.Core.Contracts;

public enum MicrosoftDeviceLoginStatus
{
    DryRun,
    Submitted,
    BrowserAccepted,
    NeedsUserAction,
    InvalidCode,
    Failed,
    TimedOut,
}

public sealed record MicrosoftDeviceLoginResult(
    bool Success,
    string LoginUrl,
    bool InPrivate,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc,
    string? RunId = null,
    MicrosoftDeviceLoginStatus Status = MicrosoftDeviceLoginStatus.Submitted,
    string? BrowserState = null,
    string? BrowserTitle = null,
    DateTimeOffset? ObservedAtUtc = null,
    string? StatusPath = null);
