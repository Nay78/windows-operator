namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointInspectResult(
    bool Success,
    PowerPointPresentationRef? Presentation,
    IReadOnlyList<PowerPointSlideRef> Slides,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
