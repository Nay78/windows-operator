using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Capture.Services;

public sealed class ImageEncodingService
{
    private readonly IOptions<OperatorOptions> _options;

    public ImageEncodingService(IOptions<OperatorOptions> options)
    {
        _options = options;
    }

    public async Task<ScreenshotResult> EncodeAsync(
        RawCaptureFrame frame,
        ScreenshotFormat? format,
        CancellationToken cancellationToken)
    {
        var options = _options.Value.Screenshot;
        var resolvedFormat = format ?? options.DefaultFormat;

        using Image<Rgba32> image = frame.Image.Clone();
        if (Math.Max(image.Width, image.Height) > options.LongestEdge)
        {
            image.Mutate(
                ctx => ctx.Resize(
                    new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(options.LongestEdge, options.LongestEdge),
                    }));
        }

        await using var stream = new MemoryStream();
        if (resolvedFormat == ScreenshotFormat.Png)
        {
            await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
        }
        else
        {
            await image.SaveAsync(stream, new JpegEncoder { Quality = options.JpegQuality }, cancellationToken);
        }

        return new ScreenshotResult(
            resolvedFormat == ScreenshotFormat.Png ? "image/png" : "image/jpeg",
            Convert.ToBase64String(stream.ToArray()),
            image.Width,
            image.Height,
            frame.Bounds,
            frame.DpiScale,
            frame.CapturedAtUtc,
            frame.Backend,
            options.LongestEdge,
            resolvedFormat == ScreenshotFormat.Jpeg ? options.JpegQuality : null,
            resolvedFormat == ScreenshotFormat.Png);
    }
}
