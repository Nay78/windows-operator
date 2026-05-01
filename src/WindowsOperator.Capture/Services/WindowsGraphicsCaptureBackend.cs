using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Capture.Services;

public sealed class WindowsGraphicsCaptureBackend : ICaptureBackend
{
    public string Name => "WindowsGraphicsCapture";

    public Task<CaptureBackendResult> CaptureAsync(WindowRef window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            CaptureBackendResult.Fail(
                OperatorErrors.BlankCapture(
                    $"WGC interop scaffold present but not yet activated for hwnd={window.Hwnd}.")));
    }
}
