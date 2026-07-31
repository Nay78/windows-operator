using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class EdgeMicrosoftAuthServiceDeviceLoginTests
{
    [Fact]
    public async Task BrowserStatus_UnknownSession_ReturnsTypedNotFound()
    {
        using var service = new EdgeMicrosoftAuthService();

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.GetSessionStateAsync($"missing-{Guid.NewGuid():N}", CancellationToken.None));

        Assert.Equal(ErrorCodes.BrowserSessionNotFound, failure.Error.Code);
        Assert.Equal(OperatorErrorCategory.NotFound, failure.Error.Category);
        Assert.False(failure.Error.Retryable);
    }

    [Fact]
    public async Task StatusReads_UnknownRun_ReturnTypedNotFound()
    {
        using var service = new EdgeMicrosoftAuthService();
        var runId = $"missing-{Guid.NewGuid():N}";

        var deviceFailure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.GetDeviceLoginStatusAsync(runId, CancellationToken.None));
        Assert.Equal(ErrorCodes.AuthRunNotFound, deviceFailure.Error.Code);
        Assert.Equal(OperatorErrorCategory.NotFound, deviceFailure.Error.Category);
        Assert.False(deviceFailure.Error.Retryable);

        var probeFailure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.GetAuthorizeProbeStatusAsync(runId, CancellationToken.None));
        Assert.Equal(ErrorCodes.AuthRunNotFound, probeFailure.Error.Code);
        Assert.Equal(OperatorErrorCategory.NotFound, probeFailure.Error.Category);
        Assert.False(probeFailure.Error.Retryable);
    }

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

    [Fact]
    public void ClassifyBrowserState_ReportsEntrustAuthenticationFailure()
    {
        var actual = EdgeMicrosoftAuthService.ClassifyBrowserState(
            "Identity as a Service - Antofagasta Minerals",
            "Se produjo un error durante la autenticación.");

        Assert.Equal(MicrosoftDeviceLoginStatus.Failed, actual.Status);
        Assert.Equal("browser_authentication_failed", actual.State);
    }

    [Theory]
    [InlineData("Need admin approval This app requires approval.", "browser_needs_admin_approval")]
    [InlineData("Approve sign in request Open your Microsoft Authenticator app.", "browser_needs_mfa")]
    [InlineData("Confirme la solicitud de autenticación que aparece en su aplicación móvil.", "browser_needs_mfa")]
    [InlineData("Stay signed in? Yes No", "browser_needs_stay_signed_in")]
    [InlineData("Pick an account user@example.com Connected to Windows", "browser_needs_account_selection")]
    [InlineData("Permissions requested Accept", "browser_needs_consent")]
    public void ClassifyBrowserState_ReportsExactUserActionState(string text, string expectedState)
    {
        var actual = EdgeMicrosoftAuthService.ClassifyBrowserState("Sign in to your account", text);

        Assert.Equal(MicrosoftDeviceLoginStatus.NeedsUserAction, actual.Status);
        Assert.Equal(expectedState, actual.State);
    }
}
