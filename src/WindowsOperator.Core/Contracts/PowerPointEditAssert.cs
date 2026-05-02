namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditAssert
{
    public bool ExactlyOneTarget { get; init; }

    public bool AllowMultiple { get; init; }
}
