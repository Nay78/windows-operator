using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IEdgeBrowserService
{
    Task<BrowserEdgeResetResult> ResetAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> StartSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> GetSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> NavigateSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionDomActionResult> ClickDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionDomActionResult> FillDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken);

    Task<BrowserEdgeSessionStateResult> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);
}
