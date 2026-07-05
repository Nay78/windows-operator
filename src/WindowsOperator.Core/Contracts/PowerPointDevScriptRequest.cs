namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointDevScriptRequest
{
    public string ScriptId { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string>? Args { get; init; }

    public int TimeoutSeconds { get; init; } = 5;

    public bool AllowDeckMutation { get; init; }

    public bool CaptureScreenshot { get; init; }

    public string? RunId { get; init; }

    public string? Label { get; init; }
}
