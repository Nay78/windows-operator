namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointUpdateOperation
{
    public required string Kind { get; init; }

    public required string TargetId { get; init; }

    public string? Text { get; init; }

    public string? Mode { get; init; }

    public bool? AllowEmpty { get; init; }

    public PowerPointArtifactRef? Artifact { get; init; }

    public string? AltText { get; init; }

    public string? Fit { get; init; }
}
