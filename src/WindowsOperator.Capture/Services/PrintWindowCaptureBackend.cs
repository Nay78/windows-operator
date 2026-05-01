using System.Drawing;
using System.Drawing.Imaging;
using WindowsOperator.Automation.Interop;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using Image = SixLabors.ImageSharp.Image;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace WindowsOperator.Capture.Services;

public sealed class PrintWindowCaptureBackend : ICaptureBackend
{
    public string Name => "PrintWindow";

    public Task<CaptureBackendResult> CaptureAsync(WindowRef window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(
                CaptureBackendResult.Fail(
                    OperatorErrors.BlankCapture("PrintWindow unavailable outside Windows.")));
        }

        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            return Task.FromResult(
                CaptureBackendResult.Fail(
                    OperatorErrors.WindowNotFound($"Invalid bounds for hwnd={window.Hwnd}.")));
        }

        using var bitmap = new Bitmap(window.Bounds.Width, window.Bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            var success = User32.PrintWindow(new IntPtr(window.Hwnd), hdc, 0);
            if (!success)
            {
                return Task.FromResult(
                    CaptureBackendResult.Fail(
                        OperatorErrors.BlankCapture($"PrintWindow failed for hwnd={window.Hwnd}.")));
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return Task.FromResult(CaptureBackendResult.Success(CreateFrame(bitmap, window, Name)));
    }

    private static RawCaptureFrame CreateFrame(Bitmap bitmap, WindowRef window, string backend)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return new RawCaptureFrame(
            Image.Load<Rgba32>(stream),
            window.Bounds,
            window.DpiScale,
            backend,
            DateTimeOffset.UtcNow);
    }
}
