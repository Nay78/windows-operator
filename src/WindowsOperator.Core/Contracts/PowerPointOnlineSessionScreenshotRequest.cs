namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineSessionScreenshotRequest
{
    public string? Label { get; init; }

    public ScreenshotFormat Format { get; init; } = ScreenshotFormat.Png;
}
