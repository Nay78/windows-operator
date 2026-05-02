using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IPowerPointService
{
    Task<PowerPointInspectResult> InspectAsync(PowerPointInspectRequest request, CancellationToken cancellationToken);

    Task<PowerPointEditResult> EditAsync(PowerPointEditRequest request, CancellationToken cancellationToken);

    Task<PowerPointEditResult> GetJobAsync(string jobId, CancellationToken cancellationToken);
}
