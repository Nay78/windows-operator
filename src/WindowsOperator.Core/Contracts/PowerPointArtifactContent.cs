namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointArtifactContent(
    byte[] Bytes,
    string MediaType,
    string FileName);
