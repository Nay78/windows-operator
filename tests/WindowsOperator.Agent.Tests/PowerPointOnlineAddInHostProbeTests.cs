using System.Net;
using System.Net.Http;
using WindowsOperator.Agent.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class PowerPointOnlineAddInHostProbeTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsManifestDiagnostics_WhenTaskPaneAndManifestHealthy()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/taskpane.html", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>Windows Operator PowerPoint</body></html>"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1">
                      <Id>6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7</Id>
                      <Version>1.0.0.0</Version>
                      <DisplayName DefaultValue="Windows Operator PowerPoint"/>
                      <DefaultSettings>
                        <SourceLocation DefaultValue="https://localhost:3003/taskpane.html"/>
                      </DefaultSettings>
                    </OfficeApp>
                    """),
            };
        }));
        var probe = new HttpPowerPointOnlineAddInHostProbe(httpClient);

        var result = await probe.ProbeAsync(
            new Uri("https://localhost:3003/"),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Detail);
        Assert.Equal("https://localhost:3003/taskpane.html", result.TaskPaneUrl);
        Assert.True(result.TaskPaneReachable);
        Assert.Equal("https://localhost:3003/manifest.xml", result.ManifestUrl);
        Assert.True(result.ManifestReachable);
        Assert.Equal("6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7", result.ManifestId);
        Assert.Equal("1.0.0.0", result.ManifestVersion);
        Assert.Equal("Windows Operator PowerPoint", result.ManifestDisplayName);
        Assert.Equal("https://localhost:3003/taskpane.html", result.ManifestSourceLocation);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsCombinedFailureDetail_WhenManifestUnavailable()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/taskpane.html", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>Windows Operator PowerPoint</body></html>"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var probe = new HttpPowerPointOnlineAddInHostProbe(httpClient);

        var result = await probe.ProbeAsync(
            new Uri("https://localhost:3003/"),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.TaskPaneReachable);
        Assert.False(result.ManifestReachable);
        Assert.Contains("manifest:", result.Detail);
        Assert.Null(result.ManifestId);
        Assert.Null(result.ManifestVersion);
        Assert.Null(result.ManifestDisplayName);
        Assert.Null(result.ManifestSourceLocation);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }
}
