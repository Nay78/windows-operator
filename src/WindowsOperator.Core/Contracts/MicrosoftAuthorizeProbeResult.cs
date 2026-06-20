namespace WindowsOperator.Core.Contracts;

public enum MicrosoftAuthorizeProbeStatus
{
    DryRun,
    Opened,
    NeedsUserAction,
    RedirectObserved,
    Failed,
    TimedOut,
}

public sealed record MicrosoftAuthorizeProbeResult(
    bool Success,
    string AuthorizeUrl,
    bool InPrivate,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc,
    string? RunId = null,
    MicrosoftAuthorizeProbeStatus Status = MicrosoftAuthorizeProbeStatus.Opened,
    string? BrowserState = null,
    string? BrowserTitle = null,
    string? ObservedUrl = null,
    string? ObservedOrigin = null,
    string? ObservedError = null,
    bool ObservedCodePresent = false,
    DateTimeOffset? ObservedAtUtc = null,
    string? StatusPath = null);
