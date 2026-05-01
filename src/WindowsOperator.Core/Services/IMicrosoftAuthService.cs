using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public interface IMicrosoftAuthService
{
    Task<MicrosoftDeviceLoginResult> StartDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken);
}
