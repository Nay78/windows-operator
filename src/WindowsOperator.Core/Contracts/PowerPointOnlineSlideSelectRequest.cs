namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineSlideSelectRequest
{
    public required int SlideNumber { get; init; }

    public bool Capture { get; init; } = true;

    public int WaitSeconds { get; init; } = 15;

    public string? Label { get; init; }
}
