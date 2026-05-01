using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Automation.Services;

public interface IUiAutomationBackend
{
    string Name { get; }

    Task<IReadOnlyList<UiElementRef>> QueryAsync(UiQuery query, CancellationToken cancellationToken);

    Task<ActionResult> ClickAsync(UiaClickRequest request, CancellationToken cancellationToken);

    Task<ActionResult> TypeAsync(UiaTypeRequest request, CancellationToken cancellationToken);
}
