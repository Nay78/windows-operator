using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Configuration;

public sealed class ScreenshotOptions
{
    public int JpegQuality { get; set; } = 85;

    public int LongestEdge { get; set; } = 1600;

    public ScreenshotFormat DefaultFormat { get; set; } = ScreenshotFormat.Jpeg;
}
