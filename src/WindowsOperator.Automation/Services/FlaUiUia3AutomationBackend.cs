using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Automation.Services;

public sealed class FlaUiUia3AutomationBackend : IUiAutomationBackend
{
    public string Name => "FlaUI.UIA3";

    public Task<IReadOnlyList<UiElementRef>> QueryAsync(UiQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<UiElementRef>>(Array.Empty<UiElementRef>());
        }

        using var automation = new UIA3Automation();
        var root = ResolveRoot(automation, query.WindowHwnd);
        var matches = root
            .FindAllDescendants()
            .Where(element => Matches(element, query))
            .Take(Math.Max(1, query.MaxResults))
            .Select(Map)
            .ToArray();

        return Task.FromResult<IReadOnlyList<UiElementRef>>(matches);
    }

    public Task<ActionResult> ClickAsync(UiaClickRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl("UIA click requires Windows desktop session."));
        }

        using var automation = new UIA3Automation();
        var element = ResolveSingle(automation, request.Query);
        Mouse.MoveTo(element.GetClickablePoint());
        if (request.DoubleClick)
        {
            Mouse.DoubleClick();
        }
        else
        {
            Mouse.Click();
        }

        return Task.FromResult(new ActionResult(true, "Click dispatched."));
    }

    public Task<ActionResult> TypeAsync(UiaTypeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl("UIA type requires Windows desktop session."));
        }

        using var automation = new UIA3Automation();
        var element = ResolveSingle(automation, request.Query);
        element.Focus();

        if (!request.Append)
        {
            Keyboard.Press(VirtualKeyShort.CONTROL);
            Keyboard.Type(VirtualKeyShort.KEY_A);
            Keyboard.Release(VirtualKeyShort.CONTROL);
        }

        if (element.Patterns.Value.IsSupported && !request.Append)
        {
            element.Patterns.Value.Pattern.SetValue(request.Text);
        }
        else
        {
            Keyboard.Type(request.Text);
        }

        if (request.Submit)
        {
            Keyboard.Type(VirtualKeyShort.RETURN);
        }

        return Task.FromResult(new ActionResult(true, "Text dispatched."));
    }

    private static AutomationElement ResolveRoot(UIA3Automation automation, long? hwnd)
    {
        if (hwnd is null)
        {
            return automation.GetDesktop();
        }

        var element = automation.FromHandle(new IntPtr(hwnd.Value));
        if (element is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.WindowNotFound($"hwnd={hwnd.Value}"));
        }

        return element;
    }

    private static AutomationElement ResolveSingle(UIA3Automation automation, UiQuery query)
    {
        var root = ResolveRoot(automation, query.WindowHwnd);
        var element = root.FindAllDescendants().FirstOrDefault(candidate => Matches(candidate, query));

        if (element is null)
        {
            throw new OperatorFailureException(
                OperatorErrors.UnsupportedControl("No matching automation element found."));
        }

        return element;
    }

    private static bool Matches(AutomationElement element, UiQuery query)
    {
        if (!query.IncludeOffscreen && ReadProperty(element, item => item.IsOffscreen, false))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Name) &&
            !string.Equals(ReadProperty(element, item => item.Name, string.Empty), query.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId) &&
            !string.Equals(ReadProperty(element, item => item.AutomationId, string.Empty), query.AutomationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ControlType) &&
            !string.Equals(ReadProperty(element, item => item.ControlType.ToString(), string.Empty), query.ControlType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static UiElementRef Map(AutomationElement element)
    {
        var rect = ReadProperty(element, item => item.BoundingRectangle, default);
        return new UiElementRef(
            element.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ReadProperty(element, item => item.Name, string.Empty) ?? string.Empty,
            ReadProperty(element, item => item.AutomationId, string.Empty) ?? string.Empty,
            ReadProperty(element, item => item.ControlType.ToString(), string.Empty),
            ReadProperty(element, item => item.IsEnabled, false),
            ReadProperty(element, item => item.IsOffscreen, false),
            new WindowBounds((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
    }

    private static T ReadProperty<T>(AutomationElement element, Func<AutomationElement, T> read, T fallback)
    {
        try
        {
            return read(element);
        }
        catch (PropertyNotSupportedException)
        {
            return fallback;
        }
    }
}
