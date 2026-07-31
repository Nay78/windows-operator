namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTableSnapshot(
    int RowCount,
    int ColumnCount,
    IReadOnlyList<IReadOnlyList<string>> Values,
    PowerPointTableGeometry? Geometry = null);
