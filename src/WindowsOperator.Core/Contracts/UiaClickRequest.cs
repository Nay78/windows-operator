namespace WindowsOperator.Core.Contracts;

public sealed record UiaClickRequest
{
    public required UiQuery Query { get; init; }

    public bool DoubleClick { get; init; }
}
