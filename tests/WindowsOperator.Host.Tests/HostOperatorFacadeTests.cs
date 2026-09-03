using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class HostOperatorFacadeTests
{
    [Fact]
    public async Task GetHealthAsync_BoundsHungDesktopAgentProbe()
    {
        using var handler = new HangingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new DesktopAgentClient(
            httpClient,
            Options.Create(new DesktopAgentOptions { BaseUrl = "http://127.0.0.1:43119" }));
        var facade = new HostOperatorFacade(
            client,
            new RuntimeBuildIdentity("test", "test", "test"),
            Options.Create(new OperatorOptions()),
            Options.Create(new DesktopAgentOptions()),
            Options.Create(new PowerPointAddInOptions()),
            new OneDriveRuntimeStateStore(Path.Combine(Path.GetTempPath(), $"onedrive-state-{Guid.NewGuid():N}.json")));

        var stopwatch = Stopwatch.StartNew();
        var result = await facade.GetHealthAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal("degraded", result.Status);
        Assert.True(handler.CancellationObserved);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Health probe took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ListOneDriveFilesAsync_FailsClosedFromDurableWaitingForSessionState()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new DesktopAgentClient(
            httpClient,
            Options.Create(new DesktopAgentOptions { BaseUrl = "http://127.0.0.1:43119" }));
        var statePath = Path.Combine(Path.GetTempPath(), $"onedrive-state-{Guid.NewGuid():N}.json");
        var store = new OneDriveRuntimeStateStore(statePath);
        store.BeginAttempt("WIN-UUKQS009K4J", recoveryAllowed: true, targetSessionId: 2);
        store.RecordFailure(OperatorErrors.OneDriveUnavailable(
            "target_rdp_session_not_ready",
            new OneDriveRuntimeEvidence
            {
                ComputerName = "WIN-UUKQS009K4J",
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 2,
                ConfiguredSessionId = 2,
                InteractiveUser = "Administrator",
                InteractiveSessionState = "disconnected",
                InteractiveSessionProtocol = 0,
                RecoveryActions = new[] { "operator_open_administrator_rdp_session_2" },
            }));
        var facade = new HostOperatorFacade(
            client,
            new RuntimeBuildIdentity("test", "test", "test"),
            Options.Create(new OperatorOptions()),
            Options.Create(new DesktopAgentOptions()),
            Options.Create(new PowerPointAddInOptions()),
            store);

        try
        {
            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => facade.ListOneDriveFilesAsync(
                new OneDriveListRequest { RootId = "geosupport" },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveUnavailable, failure.Error.Code);
            Assert.Equal("2", failure.Error.Details!["configuredSessionId"]);
            Assert.Equal("disconnected", failure.Error.Details["interactiveSessionState"]);
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
