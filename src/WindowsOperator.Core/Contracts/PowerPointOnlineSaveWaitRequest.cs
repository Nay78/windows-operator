namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineSaveWaitRequest
{
    public int TimeoutSeconds { get; init; } = 30;

    public int PollSeconds { get; init; } = 1;

    public bool Capture { get; init; } = false;

    public string? Label { get; init; }
}
