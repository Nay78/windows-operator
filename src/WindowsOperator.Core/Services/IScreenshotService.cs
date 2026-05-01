using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IScreenshotService
{
    Task<ScreenshotResult> CaptureAsync(
        WindowRef window,
        ScreenshotFormat? format,
        CancellationToken cancellationToken);
}
