using WindowsOperator.Core.Configuration;

namespace WindowsOperator.Mcp.Tests;

public sealed class MailOptionsTests
{
    [Fact]
    public void Defaults_UseBoundedVisibleOutlookPolicy()
    {
        var options = new MailOptions();

        Assert.Equal(300, options.SyncFreshnessSeconds);
        Assert.Equal(45, options.SyncWaitSeconds);
        Assert.True(options.ForceSyncWhenFolderMissing);
        Assert.True(options.AllowAttachToVisibleOutlook);
        Assert.True(options.CloseOwnedOutlookOnly);
        Assert.True(options.AllowAutomaticSoftRecovery);
        Assert.False(options.AllowAutomaticRestart);
        Assert.False(options.AllowAutomaticForceKill);
    }
}
