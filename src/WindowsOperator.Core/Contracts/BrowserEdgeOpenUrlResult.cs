namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeOpenUrlResult(
    bool Success,
    BrowserEdgeSessionStateResult State,
    DesktopScreenshotResult? Screenshot,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Warnings);
