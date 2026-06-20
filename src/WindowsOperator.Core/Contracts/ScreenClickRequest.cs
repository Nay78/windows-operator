namespace WindowsOperator.Core.Contracts;

public sealed record ScreenClickRequest
{
    public required int X { get; init; }

    public required int Y { get; init; }

    public bool DoubleClick { get; init; }
}
