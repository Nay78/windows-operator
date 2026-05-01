using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IWindowCatalogService
{
    Task<IReadOnlyList<WindowRef>> ListAsync(CancellationToken cancellationToken);

    Task<WindowRef?> GetAsync(long hwnd, CancellationToken cancellationToken);
}
