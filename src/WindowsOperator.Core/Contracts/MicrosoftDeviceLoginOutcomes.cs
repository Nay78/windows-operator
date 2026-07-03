namespace WindowsOperator.Core.Contracts;

public static class MicrosoftDeviceLoginOutcomes
{
    public static bool IsSuccess(MicrosoftDeviceLoginStatus status) =>
        status is MicrosoftDeviceLoginStatus.BrowserAccepted or MicrosoftDeviceLoginStatus.DryRun;
}
