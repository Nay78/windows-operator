namespace WindowsOperator.Core.Contracts;

public sealed record UiaTypeRequest
{
    public required UiQuery Query { get; init; }

    public required string Text { get; init; }

    public bool Append { get; init; }

    public bool Submit { get; init; }
}
