using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IInputService
{
    Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken);
}
