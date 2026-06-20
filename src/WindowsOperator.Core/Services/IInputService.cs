using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IInputService
{
    Task<ActionResult> ClickScreenAsync(ScreenClickRequest request, CancellationToken cancellationToken);

    Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken);
}
