using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Capture.Services;

public interface ICaptureBackend
{
    string Name { get; }

    Task<CaptureBackendResult> CaptureAsync(WindowRef window, CancellationToken cancellationToken);
}
