using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Agents.Stores;
using Mcp.Net.LLM.ApiKeys;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Extensions;

/// <summary>
/// Extension methods for registering Agent services with dependency injection
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Adds core agent services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        // Register the registry as a singleton
        services.AddSingleton<IAgentRegistry, AgentRegistry>();

        // Register chat client factory
        services.AddSingleton<IChatClientFactory, Factories.ChatClientFactory>();

        // Register the factory as a singleton
        services.AddSingleton<IAgentFactory, AgentFactory>();

        // Register the agent manager
        services.AddSingleton<IAgentManager, AgentManager>();

        // Register API key providers
        services.AddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();

        // Register tool registry
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolRegistry>(sp =>
            (ToolRegistry)sp.GetRequiredService<IToolRegistry>()
        );

        return services;
    }

    /// <summary>
    /// Adds in-memory agent store implementation
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddInMemoryAgentStore(this IServiceCollection services)
    {
        services.AddSingleton<IAgentStore, InMemoryAgentStore>();
        return services;
    }

    /// <summary>
    /// Adds file system agent store implementation
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="storageDirectory">Directory where agent definitions will be stored</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFileSystemAgentStore(
        this IServiceCollection services,
        string storageDirectory
    )
    {
        services.AddSingleton<IAgentStore>(sp => new FileSystemAgentStore(
            storageDirectory,
            sp.GetRequiredService<ILogger<FileSystemAgentStore>>()
        ));

        return services;
    }

    /// <summary>
    /// Adds in-memory user API key provider
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddInMemoryUserApiKeyProvider(this IServiceCollection services)
    {
        services.AddSingleton<IUserApiKeyProvider, InMemoryUserApiKeyProvider>();
        return services;
    }
}
