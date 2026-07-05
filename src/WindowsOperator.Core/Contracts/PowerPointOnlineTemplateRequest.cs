namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointOnlineTemplateRequest
{
    public bool Capture { get; init; }

    public int WaitSeconds { get; init; } = 2;

    public bool AllowDeckMutation { get; init; }

    public bool NamedOnly { get; init; }

    public string? Label { get; init; }
}
