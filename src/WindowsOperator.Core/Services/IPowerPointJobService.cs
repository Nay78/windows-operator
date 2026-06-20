using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IPowerPointJobService
{
    Task<PowerPointJobRecord> EnqueueAsync(PowerPointUpdateJob job, CancellationToken cancellationToken);

    Task<PowerPointUpdateJob?> ClaimNextAsync(PowerPointClaimJobRequest request, CancellationToken cancellationToken);

    Task<PowerPointJobRecord> CompleteAsync(
        string jobId,
        PowerPointUpdateResult result,
        CancellationToken cancellationToken);

    Task<PowerPointJobRecord> FailAsync(
        string jobId,
        PowerPointUpdateError error,
        CancellationToken cancellationToken);

    Task<PowerPointJobRecord> GetAsync(string jobId, CancellationToken cancellationToken);

    Task<PowerPointArtifactContent> GetArtifactAsync(
        string jobId,
        string artifactId,
        CancellationToken cancellationToken);
}
