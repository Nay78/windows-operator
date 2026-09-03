namespace WindowsOperator.Core.Contracts;

public sealed record BrowserCallbackRelayResult(
    bool Success,
    string RelayId,
    int ListenPort,
    int ForwardPort,
    string State,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors);
