using System.Collections.Concurrent;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Agents.Stores;

/// <summary>
/// In-memory implementation of IAgentStore for testing and simple scenarios
/// </summary>
public class InMemoryAgentStore : IAgentStore
{
    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new();
    private readonly ILogger<InMemoryAgentStore> _logger;

    public InMemoryAgentStore(ILogger<InMemoryAgentStore> logger)
    {
        _logger = logger;
    }

    public Task<AgentDefinition?> GetAgentByIdAsync(string id)
    {
        _logger.LogDebug("Getting agent with ID: {AgentId}", id);
        _agents.TryGetValue(id, out var agent);
        return Task.FromResult(agent);
    }

    public Task<IEnumerable<AgentDefinition>> ListAgentsAsync()
    {
        _logger.LogDebug("Listing all agents, found {Count}", _agents.Count);
        return Task.FromResult<IEnumerable<AgentDefinition>>(_agents.Values.ToList());
    }

    public Task<bool> SaveAgentAsync(AgentDefinition agent)
    {
        if (string.IsNullOrEmpty(agent.Id))
        {
            agent.Id = Guid.NewGuid().ToString();
        }

        // Update timestamps
        if (_agents.TryGetValue(agent.Id, out _))
        {
            // Existing agent - update timestamp
            agent.UpdatedAt = DateTime.UtcNow;
            _logger.LogDebug("Updating existing agent: {AgentId}", agent.Id);
        }
        else
        {
            // New agent - set both timestamps
            var now = DateTime.UtcNow;
            agent.CreatedAt = now;
            agent.UpdatedAt = now;
            _logger.LogDebug("Creating new agent with ID: {AgentId}", agent.Id);
        }

        _agents[agent.Id] = agent;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAgentAsync(string id)
    {
        _logger.LogDebug("Deleting agent with ID: {AgentId}", id);
        var result = _agents.TryRemove(id, out _);
        return Task.FromResult(result);
    }

    public Task<bool> AgentExistsAsync(string id)
    {
        var exists = _agents.ContainsKey(id);
        _logger.LogDebug(
            "Checking if agent exists with ID: {AgentId}, Result: {Exists}",
            id,
            exists
        );
        return Task.FromResult(exists);
    }
}
