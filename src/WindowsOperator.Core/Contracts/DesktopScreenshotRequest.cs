namespace WindowsOperator.Core.Contracts;

public sealed record DesktopScreenshotRequest
{
    public string Target { get; init; } = "foreground";

    public long? Hwnd { get; init; }

    public string? TitleContains { get; init; }

    public string? RunId { get; init; }

    public string? Label { get; init; }

    public ScreenshotFormat Format { get; init; } = ScreenshotFormat.Png;
}
