namespace WindowsOperator.Core.Configuration;

public sealed class MailOptions
{
    public int SyncFreshnessSeconds { get; set; } = 300;

    public int SyncWaitSeconds { get; set; } = 45;

    public bool ForceSyncWhenFolderMissing { get; set; } = true;

    public bool AllowAttachToVisibleOutlook { get; set; } = true;

    public bool CloseOwnedOutlookOnly { get; set; } = true;

    public bool AllowAutomaticSoftRecovery { get; set; } = true;

    public bool AllowAutomaticRestart { get; set; } = true;

    public bool AllowAutomaticForceKill { get; set; } = true;
}
