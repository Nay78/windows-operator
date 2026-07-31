using Microsoft.Extensions.Options;

namespace WindowsOperator.Relay;

public interface IRelayUpstream
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

internal sealed class HttpRelayUpstream(
    IHttpClientFactory clientFactory,
    IOptions<RelayOptions> options) : IRelayUpstream
{
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.RequestUri = new Uri(new Uri(options.Value.UpstreamBaseUrl), request.RequestUri!);
        return clientFactory.CreateClient("WindowsOperator.Relay.Upstream")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
