namespace WindowsOperator.Core.Contracts;

public sealed record DesktopScreenshotResult(
    bool Success,
    WorkbenchArtifactRef Artifact,
    WindowRef Window,
    int PixelWidth,
    int PixelHeight,
    string Backend,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings);
