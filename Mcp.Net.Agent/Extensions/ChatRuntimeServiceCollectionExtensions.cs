using Mcp.Net.Agent.Factories;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Net.Agent.Extensions;

/// <summary>
/// Extension methods for registering the shared chat runtime surface.
/// </summary>
public static class ChatRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddChatRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddChatSessionFactory();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton(sp => (ToolRegistry)sp.GetRequiredService<IToolRegistry>());

        return services;
    }

    public static IServiceCollection AddChatSessionFactory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IChatSessionFactory, ChatSessionFactory>();
        return services;
    }
}
