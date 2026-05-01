using WindowsOperator.Automation.Interop;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Automation.Services;

public sealed class Win32WindowActivationService : IWindowActivationService
{
    public Task<ActionResult> ActivateAsync(WindowRef window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl("Window activation requires Windows desktop session."));
        }

        var hwnd = new IntPtr(window.Hwnd);
        if (!User32.IsWindow(hwnd))
        {
            throw new OperatorFailureException(OperatorErrors.WindowNotFound($"hwnd={window.Hwnd}"));
        }

        if (window.IsMinimized)
        {
            _ = User32.ShowWindowAsync(hwnd, User32.SwRestore);
        }

        var activated = User32.SetForegroundWindow(hwnd);
        if (!activated)
        {
            throw new OperatorFailureException(
                OperatorErrors.UipiBlocked($"SetForegroundWindow failed for hwnd={window.Hwnd}."));
        }

        return Task.FromResult(new ActionResult(true, "Window activated."));
    }
}
