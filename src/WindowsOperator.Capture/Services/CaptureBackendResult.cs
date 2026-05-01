using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Capture.Services;

public sealed record CaptureBackendResult(
    RawCaptureFrame? Frame,
    OperatorError? Error)
{
    public static CaptureBackendResult Success(RawCaptureFrame frame) => new(frame, null);

    public static CaptureBackendResult Fail(OperatorError error) => new(null, error);
}
