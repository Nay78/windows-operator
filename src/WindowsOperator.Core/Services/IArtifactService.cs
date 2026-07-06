using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IArtifactService
{
    Task<ArtifactContent> GetArtifactAsync(string artifactId, CancellationToken cancellationToken);

    Task<ArtifactListResult> ListRunArtifactsAsync(string runId, CancellationToken cancellationToken);
}
