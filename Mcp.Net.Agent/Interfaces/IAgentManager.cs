using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Interfaces;

namespace Mcp.Net.Agent.Interfaces;

/// <summary>
/// Manager interface for working with agent definitions and creating chat clients from them
/// </summary>
public interface IAgentManager
{
    /// <summary>
    /// Creates a chat client from an agent identified by ID
    /// </summary>
    /// <param name="agentId">The unique ID of the agent</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>A configured chat client instance</returns>
    /// <exception cref="KeyNotFoundException">If the agent is not found</exception>
    Task<IChatClient> CreateChatClientAsync(string agentId, string? userId = null);

    /// <summary>
    /// Gets all available agents
    /// </summary>
    /// <param name="category">Optional category filter</param>
    /// <returns>A collection of agent definitions</returns>
    Task<IEnumerable<AgentDefinition>> GetAgentsAsync(AgentCategory? category = null);

    /// <summary>
    /// Gets an agent by its unique ID
    /// </summary>
    /// <param name="agentId">The unique ID of the agent</param>
    /// <returns>The agent definition, or null if not found</returns>
    Task<AgentDefinition?> GetAgentByIdAsync(string agentId);

    /// <summary>
    /// Creates a new agent
    /// </summary>
    /// <param name="agent">The agent definition</param>
    /// <param name="userId">The ID of the user creating the agent</param>
    /// <returns>The created agent with generated ID</returns>
    Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent, string userId);

    /// <summary>
    /// Updates an existing agent
    /// </summary>
    /// <param name="agent">The updated agent definition</param>
    /// <param name="userId">The ID of the user updating the agent</param>
    /// <returns>True if successful, false if the agent was not found</returns>
    Task<bool> UpdateAgentAsync(AgentDefinition agent, string userId);

    /// <summary>
    /// Deletes an agent by its ID
    /// </summary>
    /// <param name="agentId">The unique ID of the agent to delete</param>
    /// <returns>True if successful, false if the agent was not found</returns>
    Task<bool> DeleteAgentAsync(string agentId);

    /// <summary>
    /// Creates a new agent by cloning an existing one
    /// </summary>
    /// <param name="sourceAgentId">The ID of the agent to clone</param>
    /// <param name="userId">The ID of the user creating the clone</param>
    /// <param name="newName">Optional new name for the clone</param>
    /// <returns>The cloned agent definition with a new ID</returns>
    /// <exception cref="KeyNotFoundException">If the source agent is not found</exception>
    Task<AgentDefinition> CloneAgentAsync(
        string sourceAgentId,
        string userId,
        string? newName = null
    );

    /// <summary>
    /// Gets all available tool categories
    /// </summary>
    /// <returns>A collection of tool category names</returns>
    Task<IEnumerable<string>> GetToolCategoriesAsync();

    /// <summary>
    /// Gets all tools in a specific category
    /// </summary>
    /// <param name="category">The category name</param>
    /// <returns>A collection of tool IDs in the specified category</returns>
    Task<IEnumerable<string>> GetToolsByCategoryAsync(string category);
}
