namespace WindowsOperator.Core.Contracts;

public sealed record UiElementRef(
    string RuntimeId,
    string Name,
    string AutomationId,
    string ControlType,
    bool IsEnabled,
    bool IsOffscreen,
    WindowBounds Bounds);
