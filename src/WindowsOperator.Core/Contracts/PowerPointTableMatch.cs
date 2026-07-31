namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointTableMatch(
    int RowIndex,
    int ColumnIndex,
    string Text);
