using System.Net;
using System.Net.Sockets;
using System.Text;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class BrowserCallbackRelayServiceTests
{
    [Fact]
    public async Task RelaysOneLoopbackConnectionAndCleansItUp()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var forwardPort = ((IPEndPoint)target.LocalEndpoint).Port;
        var listenPort = forwardPort - 1;
        var targetTask = Task.Run(async () =>
        {
            using var client = await target.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var request = new byte[4];
            var read = 0;
            while (read < request.Length)
            {
                read += await stream.ReadAsync(request.AsMemory(read));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("ok"));
        });

        using var relay = new BrowserCallbackRelayService();
        var started = await relay.StartAsync(
            new BrowserCallbackRelayRequest
            {
                RelayId = "test-relay",
                ListenPort = listenPort,
                ForwardPort = forwardPort,
                TtlSeconds = 30,
            },
            CancellationToken.None);

        using var incoming = new TcpClient();
        await incoming.ConnectAsync(IPAddress.Loopback, listenPort);
        await incoming.GetStream().WriteAsync(Encoding.ASCII.GetBytes("ping"));
        var response = new byte[2];
        var responseRead = 0;
        while (responseRead < response.Length)
        {
            responseRead += await incoming.GetStream().ReadAsync(response.AsMemory(responseRead));
        }

        Assert.Equal("ok", Encoding.ASCII.GetString(response));
        await targetTask;

        var cleaned = await relay.CleanupAsync(started.RelayId, CancellationToken.None);
        Assert.Equal("cleaned", cleaned.State);
    }

    [Fact]
    public async Task RejectsUnsafeRelayShape()
    {
        using var relay = new BrowserCallbackRelayService();

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => relay.StartAsync(
            new BrowserCallbackRelayRequest
            {
                RelayId = "bad/id",
                ListenPort = 8020,
                ForwardPort = 8021,
                TtlSeconds = 30,
            },
            CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidRequest, failure.Error.Code);
    }

}
