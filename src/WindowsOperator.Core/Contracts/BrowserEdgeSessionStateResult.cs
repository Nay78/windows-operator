namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionStateResult(
    bool Success,
    string SessionId,
    BrowserEdgeProfileMode ProfileMode,
    bool InPrivate,
    bool IsAlive,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset ObservedAtUtc,
    int? ProcessId = null,
    long? Hwnd = null,
    string? Title = null,
    string? Url = null,
    string? BodyText = null,
    IReadOnlyList<BrowserEdgeSessionElementRef>? Elements = null,
    int? DevToolsPort = null,
    string? BrowserState = null,
    string? StatePath = null);
