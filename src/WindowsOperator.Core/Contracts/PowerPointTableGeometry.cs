namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTableGeometry(
    PowerPointShapeBounds Bounds,
    IReadOnlyList<PowerPointTableGeometryColumn> Columns,
    IReadOnlyList<PowerPointTableGeometryRow> Rows);
