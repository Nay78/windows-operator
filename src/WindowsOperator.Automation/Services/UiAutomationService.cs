using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Automation.Services;

public sealed class UiAutomationService : IUiAutomationService
{
    private readonly IUiAutomationBackend _backend;

    public UiAutomationService(IUiAutomationBackend backend)
    {
        _backend = backend;
    }

    public Task<IReadOnlyList<UiElementRef>> QueryAsync(UiQuery query, CancellationToken cancellationToken) =>
        _backend.QueryAsync(query, cancellationToken);

    public Task<ActionResult> ClickAsync(UiaClickRequest request, CancellationToken cancellationToken) =>
        _backend.ClickAsync(request, cancellationToken);

    public Task<ActionResult> TypeAsync(UiaTypeRequest request, CancellationToken cancellationToken) =>
        _backend.TypeAsync(request, cancellationToken);
}
