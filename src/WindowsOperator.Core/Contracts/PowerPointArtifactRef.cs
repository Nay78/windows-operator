namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointArtifactRef
{
    public required string ArtifactId { get; init; }

    public string? Url { get; init; }

    public required string MediaType { get; init; }

    public string? Sha256 { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
