namespace WindowsOperator.Agent.Services;

internal sealed class OneDriveRuntimeSupervisor(
    OneDriveFilesOnDemandService service,
    ILogger<OneDriveRuntimeSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runtime = await service.SuperviseRuntimeAsync(stoppingToken);
                if (runtime is not null && !WindowsOneDriveRuntimeRecovery.IsOperational(runtime))
                {
                    logger.LogWarning(
                        "OneDrive supervision incomplete. Reason={Reason} AuthenticationRequired={AuthenticationRequired} Actions={Actions}",
                        runtime.ProviderReason,
                        runtime.AuthenticationRequired,
                        string.Join(',', runtime.RecoveryActions));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "OneDrive runtime supervision failed.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
