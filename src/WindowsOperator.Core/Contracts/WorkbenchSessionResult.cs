namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchSessionResult(
    bool Success,
    string SessionId,
    string Kind,
    bool IsAlive,
    WorkbenchRunRef ArtifactRoot,
    IReadOnlyList<int> OwnedProcessIds,
    IReadOnlyList<long> Hwnds,
    string? Title,
    string? Url,
    string StatePath,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ObservedAtUtc);
