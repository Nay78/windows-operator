using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Mcp.Protocol;

namespace WindowsOperator.Mcp.DependencyInjection;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddOperatorMcp(this IServiceCollection services, bool hostStdioServer = true)
    {
        services.AddSingleton<McpToolCatalog>();
        services.AddSingleton<McpProtocolHandler>();
        if (hostStdioServer)
        {
            services.AddHostedService<McpStdioServer>();
        }

        return services;
    }
}
