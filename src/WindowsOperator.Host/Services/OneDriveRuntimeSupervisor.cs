using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Host.Services;

internal sealed class OneDriveRuntimeSupervisor(
    OneDriveConfigurationRecoveryService recovery,
    OneDriveRuntimeStateStore stateStore,
    ILogger<OneDriveRuntimeSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OneDriveConfigurationRecoveryService.IsRecoveryEnabled(
                Environment.MachineName,
                Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS")))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!stateStore.ShouldAttempt(DateTimeOffset.UtcNow))
            {
                await DelayAsync(stoppingToken);
                continue;
            }

            try
            {
                await recovery.RecoverAsync(
                    new OneDriveConfigurationRecoveryRequest
                    {
                        ClearConfiguration = false,
                    },
                    stoppingToken);
            }
            catch (OperatorFailureException failure)
            {
                logger.LogWarning(
                    "OneDrive process supervision incomplete. Code={Code} Detail={Detail}",
                    failure.Error.Code,
                    failure.Error.Details?["detail"]);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "OneDrive process supervision failed.");
            }

            await DelayAsync(stoppingToken);
        }
    }

    private static async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(CheckInterval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
