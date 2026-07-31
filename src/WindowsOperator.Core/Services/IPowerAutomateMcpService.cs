using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IPowerAutomateMcpService
{
    Task<PowerAutomateMcpStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<PowerAutomateMcpStartResult> StartBridgeAsync(
        PowerAutomateMcpStartRequest request,
        CancellationToken cancellationToken);

    Task<PowerAutomateMcpEdgeResult> OpenEdgeAsync(
        PowerAutomateMcpEdgeRequest request,
        CancellationToken cancellationToken);

    Task<PowerAutomateMcpEdgeCleanupResult> CleanupEdgeAsync(CancellationToken cancellationToken);

    Task<PowerAutomateMcpFlowReadResult> ReadFlowAsync(
        PowerAutomateMcpFlowReadRequest request,
        CancellationToken cancellationToken);

    Task<PowerAutomateMcpFlowUpdateResult> UpdateFlowAsync(
        PowerAutomateMcpFlowUpdateRequest request,
        CancellationToken cancellationToken);
}
