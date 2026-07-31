using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace WindowsOperator.Relay;

internal sealed record RelayIdentity(string CallerId, RelayCallerOptions Caller);

internal sealed record RelayRouteMatch(RelayRouteOptions Rule);

internal sealed class RelayAuthenticator(IOptions<RelayOptions> options)
{
    public bool TryAuthenticate(HttpRequest request, out RelayIdentity? identity)
    {
        identity = null;
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        foreach (var caller in options.Value.Callers)
        {
            byte[] expected;
            try
            {
                expected = Convert.FromHexString(caller.TokenSha256);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                identity = new RelayIdentity(caller.Id, caller);
                return true;
            }
        }

        return false;
    }

    public static RelayRouteMatch? Match(RelayIdentity identity, HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        foreach (var route in identity.Caller.Routes)
        {
            if (string.Equals(route.Method, request.Method, StringComparison.Ordinal) &&
                RouteMatches(route.RouteTemplate, path))
            {
                return new RelayRouteMatch(route);
            }
        }

        return null;
    }

    private static bool RouteMatches(string template, string path)
    {
        var expected = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var actual = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var segment = expected[index];
            if (segment.StartsWith('{') && segment.EndsWith('}'))
            {
                if (actual[index].Length == 0)
                {
                    return false;
                }
            }
            else if (!string.Equals(segment, actual[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class RelayRateLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public bool TryAcquire(string callerId, RelayRouteOptions route)
    {
        var key = $"{callerId}\n{route.Method}\n{route.RouteTemplate}";
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(timeProvider.GetUtcNow()));
        lock (bucket)
        {
            var now = timeProvider.GetUtcNow();
            if (now - bucket.WindowStart >= TimeSpan.FromMinutes(1))
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= route.RequestsPerMinute)
            {
                return false;
            }

            bucket.Count++;
            return true;
        }
    }

    private sealed class Bucket(DateTimeOffset windowStart)
    {
        public DateTimeOffset WindowStart { get; set; } = windowStart;

        public int Count { get; set; }
    }
}
