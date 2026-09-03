using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Host.Services;

/// <summary>
/// Relays one loopback TCP connection from Edge to the alternate SSH-forwarded
/// port. The relay accepts no remote host or remote port, and never logs bytes.
/// </summary>
public sealed class BrowserCallbackRelayService : IDisposable
{
    private const int MinimumPort = 1024;
    private const int MaximumPort = 65535;
    private const int MinimumTtlSeconds = 30;
    private const int MaximumTtlSeconds = 900;
    private readonly ConcurrentDictionary<string, RelayHandle> _relays = new(StringComparer.Ordinal);

    public Task<BrowserCallbackRelayResult> StartAsync(
        BrowserCallbackRelayRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);

        var relayId = string.IsNullOrWhiteSpace(request.RelayId)
            ? Guid.NewGuid().ToString("N")
            : request.RelayId.Trim();
        var handle = new RelayHandle(
            relayId,
            request.ListenPort,
            request.ForwardPort,
            TimeSpan.FromSeconds(request.TtlSeconds));

        try
        {
            handle.Start();
            if (!_relays.TryAdd(relayId, handle))
            {
                handle.Stop();
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("Callback relay id is already active."));
            }

            _ = RunAsync(handle);
            return Task.FromResult(new BrowserCallbackRelayResult(
                true,
                relayId,
                request.ListenPort,
                request.ForwardPort,
                "listening",
                handle.ExpiresAtUtc,
                ["listen-loopback", "await-one-connection"],
                []));
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (SocketException exception)
        {
            handle.Stop();
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Callback relay could not bind the requested loopback ports: {exception.SocketErrorCode}."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            handle.Stop();
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Callback relay could not start on the requested loopback ports."));
        }
    }

    public Task<BrowserCallbackRelayResult> CleanupAsync(
        string relayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(relayId) || !_relays.TryRemove(relayId, out var handle))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Callback relay is not active."));
        }

        handle.Stop();
        return Task.FromResult(new BrowserCallbackRelayResult(
            true,
            relayId,
            handle.ListenPort,
            handle.ForwardPort,
            "cleaned",
            handle.ExpiresAtUtc,
            ["stop-listener"],
            []));
    }

    public void Dispose()
    {
        foreach (var pair in _relays.ToArray())
        {
            if (_relays.TryRemove(pair.Key, out var handle))
            {
                handle.Stop();
            }
        }
    }

    private async Task RunAsync(RelayHandle handle)
    {
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(handle.StopToken);
            lifetime.CancelAfter(handle.Ttl);

            var ipv4Accept = handle.Ipv4Listener!.AcceptTcpClientAsync(lifetime.Token).AsTask();
            var ipv6Accept = handle.Ipv6Listener!.AcceptTcpClientAsync(lifetime.Token).AsTask();
            var completed = await Task.WhenAny(ipv4Accept, ipv6Accept).ConfigureAwait(false);
            using var incoming = await completed.ConfigureAwait(false);
            handle.StopListeners();

            using var target = new TcpClient();
            await target.ConnectAsync(IPAddress.Loopback, handle.ForwardPort, lifetime.Token).ConfigureAwait(false);
            await Task.WhenAll(
                incoming.GetStream().CopyToAsync(target.GetStream(), lifetime.Token),
                target.GetStream().CopyToAsync(incoming.GetStream(), lifetime.Token)).ConfigureAwait(false);

            await IgnoreAsync(ipv4Accept).ConfigureAwait(false);
            await IgnoreAsync(ipv6Accept).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            handle.Stop();
            _relays.TryRemove(new KeyValuePair<string, RelayHandle>(handle.RelayId, handle));
        }
    }

    private static async Task IgnoreAsync(Task<TcpClient> acceptTask)
    {
        try
        {
            using var client = await acceptTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private static void Validate(BrowserCallbackRelayRequest request)
    {
        if (request.ListenPort is < MinimumPort or > MaximumPort ||
            request.ForwardPort is < MinimumPort or > MaximumPort ||
            request.ListenPort == request.ForwardPort ||
            request.ForwardPort != request.ListenPort + 1)
        {
            throw new OperatorFailureException(
                OperatorErrors.InvalidRequest("Callback relay requires listen and forward ports to be consecutive local ports between 1024 and 65535."));
        }

        if (request.TtlSeconds is < MinimumTtlSeconds or > MaximumTtlSeconds)
        {
            throw new OperatorFailureException(
                OperatorErrors.InvalidRequest("Callback relay TTL must be between 30 and 900 seconds."));
        }

        var relayId = request.RelayId?.Trim();
        if (relayId is not null &&
            (relayId.Length > 80 ||
             !relayId.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new OperatorFailureException(
                OperatorErrors.InvalidRequest("Callback relay id contains unsupported characters."));
        }
    }

    private sealed class RelayHandle
    {
        public RelayHandle(string relayId, int listenPort, int forwardPort, TimeSpan ttl)
        {
            RelayId = relayId;
            ListenPort = listenPort;
            ForwardPort = forwardPort;
            Ttl = ttl;
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl);
            StopSource = new CancellationTokenSource();
        }

        public string RelayId { get; }
        public int ListenPort { get; }
        public int ForwardPort { get; }
        public TimeSpan Ttl { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public CancellationTokenSource StopSource { get; }
        public CancellationToken StopToken => StopSource.Token;
        public TcpListener? Ipv4Listener { get; private set; }
        public TcpListener? Ipv6Listener { get; private set; }
        private int _stopped;

        public void Start()
        {
            Ipv4Listener = new TcpListener(IPAddress.Loopback, ListenPort);
            Ipv6Listener = new TcpListener(IPAddress.IPv6Loopback, ListenPort);
            Ipv4Listener.Start();
            try
            {
                Ipv6Listener.Start();
            }
            catch
            {
                Ipv4Listener.Stop();
                throw;
            }
        }

        public void StopListeners()
        {
            Ipv4Listener?.Stop();
            Ipv6Listener?.Stop();
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            StopSource.Cancel();
            StopListeners();
            StopSource.Dispose();
        }
    }
}
