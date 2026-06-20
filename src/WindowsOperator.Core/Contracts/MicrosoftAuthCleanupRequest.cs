namespace WindowsOperator.Core.Contracts;

public sealed record MicrosoftAuthCleanupRequest
{
    public int PreserveRecentSeconds { get; init; }

    public bool DryRun { get; init; }
}
