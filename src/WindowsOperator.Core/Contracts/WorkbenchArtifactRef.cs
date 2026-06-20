namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchArtifactRef(
    string Path,
    string RelativePath,
    string HostPath,
    string MediaType,
    long Bytes);
