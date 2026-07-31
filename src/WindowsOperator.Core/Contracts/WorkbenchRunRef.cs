namespace WindowsOperator.Core.Contracts;

public sealed record WorkbenchRunRef(
    string RunId,
    [property: OperatorInternal] string Path,
    [property: OperatorInternal] string RelativePath,
    [property: OperatorInternal] string HostPath);
