using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IPowerPointOnlineUpdateService
{
    Task<PowerPointOnlineUpdateResult> UpdateAsync(
        PowerPointOnlineUpdateRequest request,
        CancellationToken cancellationToken);
}
