namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchArtifactRef(
    [property: OperatorInternal] string Path,
    [property: OperatorInternal] string RelativePath,
    [property: OperatorInternal] string HostPath,
    string MediaType,
    long Bytes,
    ArtifactRef? Artifact = null);
