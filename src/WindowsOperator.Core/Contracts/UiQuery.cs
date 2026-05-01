namespace WindowsOperator.Core.Contracts;

public sealed record UiQuery
{
    public long? WindowHwnd { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public string? ControlType { get; init; }

    public bool IncludeOffscreen { get; init; }

    public int MaxResults { get; init; } = 25;
}
