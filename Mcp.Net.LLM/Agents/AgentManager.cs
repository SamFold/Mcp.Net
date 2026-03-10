using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Agents;

/// <summary>
/// Implementation of IAgentManager that provides a simple API for working with agents
/// </summary>
public class AgentManager : IAgentManager
{
    private readonly IAgentRegistry _registry;
    private readonly IAgentFactory _factory;
    private readonly ILogger<AgentManager> _logger;

    /// <summary>
    /// Initializes a new instance of the AgentManager class
    /// </summary>
    /// <param name="registry">The agent registry</param>
    /// <param name="factory">The agent factory</param>
    /// <param name="logger">The logger</param>
    public AgentManager(
        IAgentRegistry registry,
        IAgentFactory factory,
        ILogger<AgentManager> logger
    )
    {
        _registry = registry;
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IChatClient> CreateChatClientAsync(string agentId, string? userId = null)
    {
        _logger.LogDebug("Creating chat client for agent ID: {AgentId}", agentId);

        var agent = await _registry.GetAgentByIdAsync(agentId);

        if (agent == null)
        {
            _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
            throw new KeyNotFoundException($"Agent with ID {agentId} not found");
        }

        return string.IsNullOrEmpty(userId)
            ? await _factory.CreateClientFromAgentDefinitionAsync(agent)
            : await _factory.CreateClientFromAgentDefinitionAsync(agent, userId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AgentDefinition>> GetAgentsAsync(AgentCategory? category = null)
    {
        if (category.HasValue)
        {
            _logger.LogDebug("Getting agents for category: {Category}", category.Value);
            return await _registry.GetAgentsByCategoryAsync(category.Value);
        }

        _logger.LogDebug("Getting all agents");
        return await _registry.GetAllAgentsAsync();
    }

    /// <inheritdoc/>
    public async Task<AgentDefinition?> GetAgentByIdAsync(string agentId)
    {
        _logger.LogDebug("Getting agent by ID: {AgentId}", agentId);
        return await _registry.GetAgentByIdAsync(agentId);
    }

    /// <inheritdoc/>
    public async Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent, string userId)
    {
        // Validate required fields
        ValidateAgent(agent);

        // Set creation timestamp
        agent.CreatedAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Creating agent: {AgentName} ({Provider} {Model}) by user {UserId}",
            agent.Name,
            agent.Provider,
            agent.ModelName,
            userId
        );

        // Register with the registry
        var success = await _registry.RegisterAgentAsync(agent, userId);

        if (!success)
        {
            _logger.LogWarning("Failed to register agent: {AgentId}", agent.Id);
            throw new InvalidOperationException($"Failed to register agent: {agent.Id}");
        }

        return agent;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateAgentAsync(AgentDefinition agent, string userId)
    {
        // Validate required fields
        ValidateAgent(agent);

        _logger.LogInformation(
            "Updating agent: {AgentId} ({AgentName}) by user {UserId}",
            agent.Id,
            agent.Name,
            userId
        );

        return await _registry.UpdateAgentAsync(agent, userId);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAgentAsync(string agentId)
    {
        _logger.LogInformation("Deleting agent: {AgentId}", agentId);
        return await _registry.UnregisterAgentAsync(agentId);
    }

    /// <inheritdoc/>
    public async Task<AgentDefinition> CloneAgentAsync(
        string sourceAgentId,
        string userId,
        string? newName = null
    )
    {
        _logger.LogDebug("Cloning agent {SourceAgentId}", sourceAgentId);

        // Get the source agent
        var sourceAgent = await _registry.GetAgentByIdAsync(sourceAgentId);
        if (sourceAgent == null)
        {
            _logger.LogWarning("Source agent {SourceAgentId} not found for cloning", sourceAgentId);
            throw new KeyNotFoundException($"Agent with ID {sourceAgentId} not found");
        }

        // Create a deep clone using the extension method
        var clone = sourceAgent.Clone();

        if (!string.IsNullOrEmpty(newName))
        {
            clone.Name = newName;
        }
        else
        {
            clone.Name = $"Copy of {sourceAgent.Name}";
        }

        clone.Description =
            $"Cloned from {sourceAgent.Name} ({sourceAgent.Id}). {sourceAgent.Description}";

        var success = await _registry.RegisterAgentAsync(clone, userId);
        if (!success)
        {
            _logger.LogWarning("Failed to persist cloned agent: {AgentId}", clone.Id);
            throw new InvalidOperationException($"Failed to persist cloned agent: {clone.Id}");
        }

        _logger.LogInformation(
            "Cloned agent {SourceAgentId} to new agent {CloneAgentId} by user {UserId}",
            sourceAgentId,
            clone.Id,
            userId
        );

        return clone;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> GetToolCategoriesAsync()
    {
        return await _factory.GetToolCategoriesAsync();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> GetToolsByCategoryAsync(string category)
    {
        return await _factory.GetToolsByCategoryAsync(category);
    }

    /// <summary>
    /// Validates that an agent definition has the required fields
    /// </summary>
    /// <param name="agent">The agent definition to validate</param>
    /// <exception cref="ArgumentException">If any required fields are missing</exception>
    private void ValidateAgent(AgentDefinition agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            throw new ArgumentException("Agent name is required", nameof(agent));
        }

        if (string.IsNullOrWhiteSpace(agent.ModelName))
        {
            throw new ArgumentException("Model name is required", nameof(agent));
        }
    }
}
