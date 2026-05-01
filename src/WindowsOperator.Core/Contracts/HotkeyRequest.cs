namespace WindowsOperator.Core.Contracts;

public sealed record HotkeyRequest
{
    public required IReadOnlyList<string> Keys { get; init; }
}
