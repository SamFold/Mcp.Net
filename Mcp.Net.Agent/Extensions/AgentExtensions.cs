using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Agents;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Extensions;

/// <summary>
/// Extension methods for working with agents and agent-related services
/// </summary>
public static class AgentExtensions
{
    /// <summary>
    /// Creates a chat session from an agent ID
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="agentId">The agent ID to use</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>A configured chat session</returns>
    /// <exception cref="InvalidOperationException">If required services are not registered</exception>
    /// <exception cref="KeyNotFoundException">If the agent is not found</exception>
    public static async Task<ChatSession> CreateChatSessionFromAgentAsync(
        this IServiceProvider serviceProvider,
        string agentId,
        string? userId = null
    )
    {
        // Get required services
        var agentManager = serviceProvider.GetRequiredService<IAgentManager>();
        var toolExecutor = serviceProvider.GetRequiredService<IToolExecutor>();
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ChatSession>();
        var agent = await agentManager.GetAgentByIdAsync(agentId);

        if (agent == null)
        {
            throw new KeyNotFoundException($"Agent with ID {agentId} not found");
        }

        var chatClient = await agentManager.CreateChatClientAsync(agentId, userId);

        return new ChatSession(
            chatClient,
            toolExecutor,
            toolRegistry,
            logger,
            agent.ToChatSessionConfiguration(toolRegistry)
        );
    }

    /// <summary>
    /// Creates a chat session using a specific agent definition
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="agent">The agent definition to use</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>A configured chat session</returns>
    /// <exception cref="InvalidOperationException">If required services are not registered</exception>
    public static async Task<ChatSession> CreateChatSessionFromAgentDefinitionAsync(
        this IServiceProvider serviceProvider,
        AgentDefinition agent,
        string? userId = null
    )
    {
        // Get required services
        var agentFactory = serviceProvider.GetRequiredService<IAgentFactory>();
        var toolExecutor = serviceProvider.GetRequiredService<IToolExecutor>();
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ChatSession>();
        var chatClient = string.IsNullOrEmpty(userId)
            ? await agentFactory.CreateClientFromAgentDefinitionAsync(agent)
            : await agentFactory.CreateClientFromAgentDefinitionAsync(agent, userId);

        return new ChatSession(
            chatClient,
            toolExecutor,
            toolRegistry,
            logger,
            agent.ToChatSessionConfiguration(toolRegistry)
        );
    }
}
