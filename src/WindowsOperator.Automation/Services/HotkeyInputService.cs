using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Automation.Services;

public sealed class HotkeyInputService : IInputService
{
    public Task<ActionResult> SendHotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl("Hotkeys require Windows desktop session."));
        }

        var keys = request.Keys.Select(Parse).ToArray();
        Keyboard.TypeSimultaneously(keys);
        return Task.FromResult(new ActionResult(true, "Hotkey dispatched."));
    }

    private static VirtualKeyShort Parse(string key) =>
        key.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => VirtualKeyShort.CONTROL,
            "shift" => VirtualKeyShort.SHIFT,
            "alt" => VirtualKeyShort.LMENU,
            "win" or "windows" => VirtualKeyShort.LWIN,
            "enter" => VirtualKeyShort.RETURN,
            "tab" => VirtualKeyShort.TAB,
            "esc" or "escape" => VirtualKeyShort.ESCAPE,
            var single when single.Length == 1 && char.IsLetter(single[0]) =>
                Enum.Parse<VirtualKeyShort>($"KEY_{char.ToUpperInvariant(single[0])}"),
            var single when single.Length == 1 && char.IsDigit(single[0]) =>
                Enum.Parse<VirtualKeyShort>($"KEY_{single[0]}"),
            _ => throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl($"Unsupported hotkey token '{key}'.")),
        };
}
