namespace WindowsOperator.Core.Contracts;

public sealed record ArtifactListResult(
    string RunId,
    IReadOnlyList<ArtifactRef> Artifacts,
    DateTimeOffset CheckedAtUtc);

public sealed record ArtifactContent(
    byte[] Bytes,
    string MediaType,
    string FileName,
    string Sha256);
