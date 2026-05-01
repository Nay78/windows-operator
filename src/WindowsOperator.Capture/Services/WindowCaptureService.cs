using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Capture.Services;

public sealed class WindowCaptureService : IScreenshotService
{
    private readonly IEnumerable<ICaptureBackend> _backends;
    private readonly ImageEncodingService _imageEncodingService;

    public WindowCaptureService(IEnumerable<ICaptureBackend> backends, ImageEncodingService imageEncodingService)
    {
        _backends = backends;
        _imageEncodingService = imageEncodingService;
    }

    public async Task<ScreenshotResult> CaptureAsync(
        WindowRef window,
        ScreenshotFormat? format,
        CancellationToken cancellationToken)
    {
        if (window.IsMinimized)
        {
            throw new OperatorFailureException(
                OperatorErrors.MinimizedRdp($"hwnd={window.Hwnd}"));
        }

        OperatorError? lastError = null;

        foreach (var backend in _backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await backend.CaptureAsync(window, cancellationToken);
            if (result.Frame is null)
            {
                lastError = result.Error;
                continue;
            }

            if (LooksBlank(result.Frame.Image))
            {
                lastError = OperatorErrors.BlankCapture($"{backend.Name} returned blank frame.");
                continue;
            }

            return await _imageEncodingService.EncodeAsync(result.Frame, format, cancellationToken);
        }

        throw new OperatorFailureException(
            lastError ?? OperatorErrors.BlankCapture($"No capture backend succeeded for hwnd={window.Hwnd}."));
    }

    private static bool LooksBlank(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        var sampleCount = 0;
        var nonBlack = 0;

        image.ProcessPixelRows(
            accessor =>
            {
                for (var y = 0; y < accessor.Height; y += Math.Max(1, accessor.Height / 20))
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x += Math.Max(1, row.Length / 20))
                    {
                        sampleCount++;
                        var pixel = row[x];
                        if (pixel.A > 0 && (pixel.R > 0 || pixel.G > 0 || pixel.B > 0))
                        {
                            nonBlack++;
                        }
                    }
                }
            });

        return sampleCount > 0 && nonBlack == 0;
    }
}
