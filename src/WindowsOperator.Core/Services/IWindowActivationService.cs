using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IWindowActivationService
{
    Task<ActionResult> ActivateAsync(WindowRef window, CancellationToken cancellationToken);
}
