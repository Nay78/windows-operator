namespace WindowsOperator.Core.Contracts;

public sealed record WindowRef(
    long Hwnd,
    uint ProcessId,
    string Title,
    string ClassName,
    WindowBounds Bounds,
    double DpiScale,
    DateTimeOffset CapturedAtUtc,
    bool IsForeground,
    bool IsMinimized);
