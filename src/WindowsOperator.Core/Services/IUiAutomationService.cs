using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IUiAutomationService
{
    Task<IReadOnlyList<UiElementRef>> QueryAsync(UiQuery query, CancellationToken cancellationToken);

    Task<ActionResult> ClickAsync(UiaClickRequest request, CancellationToken cancellationToken);

    Task<ActionResult> TypeAsync(UiaTypeRequest request, CancellationToken cancellationToken);
}
