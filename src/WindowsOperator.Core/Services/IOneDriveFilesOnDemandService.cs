using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IOneDriveFilesOnDemandService
{
    Task<IReadOnlyList<OneDriveFileEntry>> ListFilesAsync(
        OneDriveListRequest request,
        CancellationToken cancellationToken);

    Task<OneDriveLeaseResult> AcquireLeaseAsync(
        OneDriveLeaseRequest request,
        CancellationToken cancellationToken);

    Task<OneDriveLeaseStatusResult> GetLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken);

    Task<OneDriveLeaseResult> RenewLeaseAsync(
        string leaseId,
        OneDriveLeaseRenewRequest request,
        CancellationToken cancellationToken);

    Task<OneDriveLeaseResult> ReleaseLeaseAsync(
        string leaseId,
        CancellationToken cancellationToken);

    Task<OneDriveFilesOnDemandStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<OneDriveConfigResult> GetConfigAsync(CancellationToken cancellationToken);

    Task<OneDriveConfigResult> UpdateConfigAsync(
        OneDriveConfigUpdateRequest request,
        CancellationToken cancellationToken);

    Task<OneDriveReclaimResult> StartReclaimAsync(
        OneDriveReclaimRequest request,
        CancellationToken cancellationToken);

    Task<OneDriveReclaimResult> GetReclaimAsync(
        string runId,
        CancellationToken cancellationToken);
}
