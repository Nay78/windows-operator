using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Json;
using WindowsOperator.Host.Services;

namespace WindowsOperator.Host.Tests;

public sealed class DesktopAgentClientTests
{
    [Fact]
    public async Task GetHealthAsync_MapsEmptyAgentFailureToOperatorError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.LockedDesktop, failure.Error.Code);
        Assert.Contains("HTTP 500", failure.Error.Details!["detail"]);
    }

    [Fact]
    public async Task GetHealthAsync_PropagatesJsonAgentError()
    {
        var error = OperatorErrors.UnsupportedControl("uia property unsupported");
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(error, options: OperatorJson.SerializerOptions),
        });

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => client.GetHealthAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.UnsupportedControl, failure.Error.Code);
        Assert.Equal("uia property unsupported", failure.Error.Details!["detail"]);
    }

    private static DesktopAgentClient CreateClient(HttpResponseMessage response) =>
        new(
            new HttpClient(new StaticResponseHandler(response)),
            Options.Create(new DesktopAgentOptions { BaseUrl = "http://127.0.0.1:43119" }));

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticResponseHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}
