namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineAddInCommandRequest
{
    public bool Capture { get; init; }

    public int WaitSeconds { get; init; } = 2;

    public string? Label { get; init; }
}
