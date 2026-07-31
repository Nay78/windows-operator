namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTableGeometryColumn(
    int ColumnIndex,
    double Left,
    double Width,
    double Right);
