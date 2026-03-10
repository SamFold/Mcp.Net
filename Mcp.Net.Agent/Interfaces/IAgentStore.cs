using Mcp.Net.Agent.Models;

namespace Mcp.Net.Agent.Interfaces;

/// <summary>
/// Interface for agent definition persistence
/// </summary>
public interface IAgentStore
{
    /// <summary>
    /// Gets an agent by its ID
    /// </summary>
    /// <param name="id">The unique ID of the agent</param>
    /// <returns>The agent definition if found, null otherwise</returns>
    Task<AgentDefinition?> GetAgentByIdAsync(string id);

    /// <summary>
    /// Lists all available agents
    /// </summary>
    /// <returns>Collection of agent definitions</returns>
    Task<IEnumerable<AgentDefinition>> ListAgentsAsync();

    /// <summary>
    /// Saves an agent definition (creates or updates)
    /// </summary>
    /// <param name="agent">The agent definition to save</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SaveAgentAsync(AgentDefinition agent);

    /// <summary>
    /// Deletes an agent by its ID
    /// </summary>
    /// <param name="id">The unique ID of the agent to delete</param>
    /// <returns>True if successful, false if agent not found or deletion failed</returns>
    Task<bool> DeleteAgentAsync(string id);

    /// <summary>
    /// Checks if an agent with the given ID exists
    /// </summary>
    /// <param name="id">The unique ID of the agent</param>
    /// <returns>True if agent exists, false otherwise</returns>
    Task<bool> AgentExistsAsync(string id);
}
