using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace WindowsOperator.Relay;

public static class RelayHostingExtensions
{
    public static IServiceCollection AddWindowsOperatorRelay(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RelayOptions>()
            .Bind(configuration)
            .ValidateOnStart();
        return AddCore(services);
    }

    public static IServiceCollection AddWindowsOperatorRelay(
        this IServiceCollection services,
        Action<RelayOptions> configure)
    {
        services.AddOptions<RelayOptions>()
            .Configure(configure)
            .ValidateOnStart();
        return AddCore(services);
    }

    public static IEndpointConventionBuilder MapWindowsOperatorRelay(
        this IEndpointRouteBuilder endpoints) =>
        endpoints.Map("/v1/{**relayPath}", async context =>
        {
            await context.RequestServices
                .GetRequiredService<RelayProxy>()
                .HandleAsync(context);
        });

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<RelayOptions>, RelayOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RelayAuthenticator>();
        services.AddSingleton<RelayRateLimiter>();
        services.AddSingleton<RelayProxy>();
        services.AddHttpClient("WindowsOperator.Relay.Upstream", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(12);
        });
        services.AddSingleton<IRelayUpstream, HttpRelayUpstream>();
        return services;
    }
}
