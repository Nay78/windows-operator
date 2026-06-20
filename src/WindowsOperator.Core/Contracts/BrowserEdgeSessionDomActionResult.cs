namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeSessionDomActionResult(
    bool Success,
    string SessionId,
    string Action,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset ObservedAtUtc,
    string? MatchedBy = null,
    string? MatchedText = null,
    string? TagName = null,
    string? Url = null,
    string? Title = null,
    string? BodyText = null,
    string? StatePath = null);
