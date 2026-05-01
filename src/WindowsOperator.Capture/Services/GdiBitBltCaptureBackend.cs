using System.Drawing;
using System.Drawing.Imaging;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using Image = SixLabors.ImageSharp.Image;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace WindowsOperator.Capture.Services;

public sealed class GdiBitBltCaptureBackend : ICaptureBackend
{
    public string Name => "GdiBitBlt";

    public Task<CaptureBackendResult> CaptureAsync(WindowRef window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                CaptureBackendResult.Fail(
                    OperatorErrors.BlankCapture("GDI capture unavailable outside Windows.")));
        }

        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            return Task.FromResult(
                CaptureBackendResult.Fail(
                    OperatorErrors.WindowNotFound($"Invalid bounds for hwnd={window.Hwnd}.")));
        }

        using var bitmap = new Bitmap(window.Bounds.Width, window.Bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(window.Bounds.X, window.Bounds.Y, 0, 0, new Size(window.Bounds.Width, window.Bounds.Height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        return Task.FromResult(
            CaptureBackendResult.Success(
                new RawCaptureFrame(
                    Image.Load<Rgba32>(stream),
                    window.Bounds,
                    window.DpiScale,
                    Name,
                    DateTimeOffset.UtcNow)));
    }
}
