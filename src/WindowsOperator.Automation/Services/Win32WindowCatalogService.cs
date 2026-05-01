using System.Text;
using WindowsOperator.Automation.Interop;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Automation.Services;

public sealed class Win32WindowCatalogService : IWindowCatalogService
{
    public Task<IReadOnlyList<WindowRef>> ListAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<WindowRef>>(Array.Empty<WindowRef>());
        }

        var windows = new List<WindowRef>();
        var foreground = User32.GetForegroundWindow();
        var capturedAt = DateTimeOffset.UtcNow;

        User32.EnumWindows(
            (hwnd, lParam) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (!User32.IsWindowVisible(hwnd) || !User32.GetWindowRect(hwnd, out var rect))
                {
                    return true;
                }

                var bounds = rect.ToRectangle();
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    return true;
                }

                var title = GetWindowText(hwnd);
                var className = GetClassName(hwnd);
                _ = lParam;
                User32.GetWindowThreadProcessId(hwnd, out var processId);
                var dpi = User32.GetDpiForWindow(hwnd);

                windows.Add(
                    new WindowRef(
                        hwnd.ToInt64(),
                        processId,
                        title,
                        className,
                        new WindowBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                        dpi <= 0 ? 1.0d : dpi / 96.0d,
                        capturedAt,
                        hwnd == foreground,
                        User32.IsIconic(hwnd)));

                return true;
            },
            IntPtr.Zero);

        var ordered = windows
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => !string.IsNullOrWhiteSpace(window.Title))
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<WindowRef>>(ordered);
    }

    public async Task<WindowRef?> GetAsync(long hwnd, CancellationToken cancellationToken)
    {
        var windows = await ListAsync(cancellationToken);
        return windows.FirstOrDefault(window => window.Hwnd == hwnd);
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        var builder = new StringBuilder(1024);
        _ = User32.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        _ = User32.GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }
}
