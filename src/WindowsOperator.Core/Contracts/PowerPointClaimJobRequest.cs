namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointClaimJobRequest
{
    public string? WorkerId { get; init; }

    public string? DocumentUrl { get; init; }
}
