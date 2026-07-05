using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IDevAutomationService
{
    Task<DevScriptResult> RunPowerPointOnlineScriptAsync(
        string sessionId,
        PowerPointDevScriptRequest request,
        CancellationToken cancellationToken);

    Task<DevScriptResult> EvaluateEdgeBrowserSessionAsync(
        string sessionId,
        BrowserEdgeDevEvalRequest request,
        CancellationToken cancellationToken);
}
