using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IMicrosoftAuthService
{
    Task<MicrosoftAuthCleanupResult> CleanupAuthWindowsAsync(
        MicrosoftAuthCleanupRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftAuthorizeProbeResult> StartAuthorizeProbeAsync(
        MicrosoftAuthorizeProbeRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftAuthorizeProbeResult> GetAuthorizeProbeStatusAsync(
        string runId,
        CancellationToken cancellationToken);

    Task<MicrosoftDeviceLoginResult> StartDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken);

    Task<MicrosoftDeviceLoginResult> GetDeviceLoginStatusAsync(
        string runId,
        CancellationToken cancellationToken);
}
