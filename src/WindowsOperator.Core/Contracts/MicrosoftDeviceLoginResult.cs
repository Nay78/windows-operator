namespace WindowsOperator.Core.Contracts;

public sealed record MicrosoftDeviceLoginResult(
    bool Success,
    string LoginUrl,
    bool InPrivate,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
