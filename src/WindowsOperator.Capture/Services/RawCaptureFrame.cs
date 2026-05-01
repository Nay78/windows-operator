using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Capture.Services;

public sealed record RawCaptureFrame(
    Image<Rgba32> Image,
    WindowBounds Bounds,
    double DpiScale,
    string Backend,
    DateTimeOffset CapturedAtUtc);
