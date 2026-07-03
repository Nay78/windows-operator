using WindowsOperator.Agent.Services;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class EdgeMicrosoftAuthServiceDeviceLoginTests
{
    [Theory]
    [InlineData(
        "Sign in to your account",
        "Stay signed in? Do this to reduce the number of times you are asked to sign in. Yes No",
        MicrosoftDeviceLoginStatus.NeedsUserAction)]
    [InlineData(
        "Sign in to your account",
        "Are you trying to sign in to Microsoft Azure Cross-platform Command Line Interface? Continue Cancel",
        MicrosoftDeviceLoginStatus.NeedsUserAction)]
    [InlineData(
        "Sign in to your account",
        "Pick an account Nayguel Alejandro Martinez Cordova nmartinez.drs@mineracentinela.cl Connected to Windows",
        MicrosoftDeviceLoginStatus.NeedsUserAction)]
    [InlineData(
        "Sign in to your account",
        "Enter password Forgot my password Sign in",
        MicrosoftDeviceLoginStatus.NeedsUserAction)]
    [InlineData(
        "Sign in to your account",
        "Success",
        MicrosoftDeviceLoginStatus.NeedsUserAction)]
    [InlineData(
        "Sign in to your account",
        "You have signed in to the Microsoft Azure Cross-platform Command Line Interface application on your device. You may now close this window.",
        MicrosoftDeviceLoginStatus.BrowserAccepted)]
    public void ClassifyBrowserState_DoesNotAcceptIntermediateMicrosoftPages(
        string title,
        string text,
        MicrosoftDeviceLoginStatus expected)
    {
        var actual = EdgeMicrosoftAuthService.ClassifyBrowserState(title, text);

        Assert.Equal(expected, actual.Status);
    }

    [Fact]
    public void ClassifyBrowserState_ReportsPasswordRequired()
    {
        var actual = EdgeMicrosoftAuthService.ClassifyBrowserState(
            "Sign in to your account",
            "Enter password Please enter your password. Forgot my password Sign in");

        Assert.Equal(MicrosoftDeviceLoginStatus.NeedsUserAction, actual.Status);
        Assert.Equal("browser_needs_password", actual.State);
    }
}
