using Microsoft.Extensions.Options;

namespace WindowsOperator.Relay;

public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    public string UpstreamBaseUrl { get; set; } = "http://127.0.0.1:43117";

    public string PublicBaseUrl { get; set; } = string.Empty;

    public List<RelayCallerOptions> Callers { get; set; } = new();
}

public sealed class RelayCallerOptions
{
    public string Id { get; set; } = string.Empty;

    public string TokenSha256 { get; set; } = string.Empty;

    public List<RelayRouteOptions> Routes { get; set; } = new();
}

public sealed class RelayRouteOptions
{
    public string Method { get; set; } = string.Empty;

    public string RouteTemplate { get; set; } = string.Empty;

    public int RequestsPerMinute { get; set; }
}

internal sealed class RelayOptionsValidator : IValidateOptions<RelayOptions>
{
    public ValidateOptionsResult Validate(string? name, RelayOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.UpstreamBaseUrl, UriKind.Absolute, out var upstream) ||
            !upstream.IsLoopback ||
            upstream.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(upstream.Query) ||
            !string.IsNullOrEmpty(upstream.Fragment) ||
            (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("Relay:UpstreamBaseUrl must be an absolute HTTP(S) loopback URL.");
        }

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBase) ||
            (publicBase.Scheme != Uri.UriSchemeHttps &&
             !(publicBase.Scheme == Uri.UriSchemeHttp && publicBase.IsLoopback)))
        {
            failures.Add("Relay:PublicBaseUrl must use HTTPS, except HTTP loopback development URLs.");
        }

        if (options.Callers.Count == 0)
        {
            failures.Add("Relay:Callers must contain at least one caller.");
        }

        var callerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var caller in options.Callers)
        {
            if (string.IsNullOrWhiteSpace(caller.Id) ||
                caller.Id.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character is not '.' and not '_' and not '-'))
            {
                failures.Add("Relay caller IDs may contain only ASCII letters, digits, '.', '_', and '-'.");
            }
            else if (!callerIds.Add(caller.Id))
            {
                failures.Add($"Relay caller ID '{caller.Id}' is duplicated.");
            }

            if (!IsSha256(caller.TokenSha256))
            {
                failures.Add($"Relay caller '{caller.Id}' must provide a 64-character hexadecimal TokenSha256.");
            }

            if (caller.Routes.Count == 0)
            {
                failures.Add($"Relay caller '{caller.Id}' must allow at least one route.");
            }

            var routes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in caller.Routes)
            {
                if (!IsMethod(route.Method))
                {
                    failures.Add($"Relay caller '{caller.Id}' has invalid HTTP method.");
                }
                if (!IsRouteTemplate(route.RouteTemplate))
                {
                    failures.Add($"Relay caller '{caller.Id}' has invalid route template.");
                }
                if (route.RequestsPerMinute <= 0)
                {
                    failures.Add($"Relay caller '{caller.Id}' route limits must be positive.");
                }
                if (!routes.Add($"{route.Method}\n{route.RouteTemplate}"))
                {
                    failures.Add(
                        $"Relay caller '{caller.Id}' has duplicate method/route entries.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsMethod(string value) =>
        value.Length > 0 &&
        value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsRouteTemplate(string value)
    {
        if (!value.StartsWith("/v1/", StringComparison.Ordinal) ||
            value.Contains('?', StringComparison.Ordinal) ||
            value.EndsWith('/'))
        {
            return false;
        }

        return value
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment =>
                !string.IsNullOrWhiteSpace(segment) &&
                (segment.IndexOfAny(['{', '}']) < 0 ||
                 (segment.StartsWith('{') &&
                  segment.EndsWith('}') &&
                  segment.Length > 2 &&
                  segment.Count(character => character == '{') == 1 &&
                  segment.Count(character => character == '}') == 1)));
    }
}
