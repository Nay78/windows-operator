namespace WindowsOperator.Core.Contracts;

public sealed record BrowserEdgeDevEvalRequest
{
    public string Source { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 5;

    public bool AllowUnsafeRawJs { get; init; }

    public bool CaptureScreenshot { get; init; }

    public string? RunId { get; init; }

    public string? Label { get; init; }
}
