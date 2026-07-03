namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchRunRef(
    string RunId,
    string Path,
    string RelativePath,
    string HostPath);
