namespace WindowsOperator.Core.Contracts;

public sealed record ScreenshotResult(
    string MediaType,
    string ImageBase64,
    int PixelWidth,
    int PixelHeight,
    WindowBounds NativeBounds,
    double DpiScale,
    DateTimeOffset CapturedAtUtc,
    string Backend,
    int LongestEdge,
    int? JpegQuality,
    bool Lossless);
