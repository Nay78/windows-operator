namespace WindowsOperator.Core.Contracts;

public sealed record MicrosoftAuthorizeProbeRequest
{
    public required string AuthorizeUrl { get; init; }

    public string? RunId { get; init; }

    public int PageLoadSeconds { get; init; } = 6;

    public int ObservationTimeoutSeconds { get; init; } = 90;

    public bool InPrivate { get; init; }

    public bool ReuseExistingProfile { get; init; }

    public bool DryRun { get; init; }
}
