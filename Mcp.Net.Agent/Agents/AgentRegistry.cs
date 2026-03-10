using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Agents;

/// <summary>
/// Implementation of IAgentRegistry that manages agent definitions
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    private readonly IAgentStore _store;
    private readonly ILogger<AgentRegistry> _logger;
    private Dictionary<string, AgentDefinition> _agentsCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private readonly object _initializationGate = new();
    private Task _initializationTask;

    public event EventHandler<AgentDefinition>? AgentRegistered;
    public event EventHandler<AgentDefinition>? AgentUpdated;
    public event EventHandler<string>? AgentUnregistered;
    public event EventHandler? AgentsReloaded;

    public AgentRegistry(IAgentStore store, ILogger<AgentRegistry> logger)
    {
        _store = store;
        _logger = logger;

        // Start loading the cache; public methods await this before accessing state
        _initializationTask = ReloadAgentsCoreAsync();
    }

    private Task CurrentInitializationTask()
    {
        lock (_initializationGate)
        {
            return _initializationTask;
        }
    }

    private async Task EnsureInitializedAsync()
    {
        await CurrentInitializationTask();
    }

    public async Task<IEnumerable<AgentDefinition>> GetAllAgentsAsync()
    {
        await EnsureInitializedAsync();
        await _cacheLock.WaitAsync();
        try
        {
            return _agentsCache.Values.ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<AgentDefinition?> GetAgentByIdAsync(string id)
    {
        await EnsureInitializedAsync();
        await _cacheLock.WaitAsync();
        try
        {
            return _agentsCache.TryGetValue(id, out var agent) ? agent : null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<IEnumerable<AgentDefinition>> GetAgentsByCategoryAsync(AgentCategory category)
    {
        await EnsureInitializedAsync();
        await _cacheLock.WaitAsync();
        try
        {
            return _agentsCache.Values.Where(a => a.Category == category).ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> RegisterAgentAsync(AgentDefinition agent, string createdByUserId)
    {
        if (string.IsNullOrEmpty(createdByUserId))
        {
            throw new ArgumentNullException(
                nameof(createdByUserId),
                "User ID is required when creating an agent"
            );
        }

        await EnsureInitializedAsync();

        if (string.IsNullOrEmpty(agent.Id))
        {
            agent.Id = Guid.NewGuid().ToString();
        }

        // Set the creation metadata
        agent.CreatedBy = createdByUserId;
        agent.ModifiedBy = createdByUserId;
        agent.CreatedAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;

        var success = await _store.SaveAgentAsync(agent);
        if (success)
        {
            await _cacheLock.WaitAsync();
            try
            {
                _agentsCache[agent.Id] = agent;
            }
            finally
            {
                _cacheLock.Release();
            }

            _logger.LogInformation(
                "Registered agent: {AgentId} - {AgentName} by user {UserId}",
                agent.Id,
                agent.Name,
                createdByUserId
            );
            AgentRegistered?.Invoke(this, agent);
        }

        return success;
    }

    public async Task<bool> UpdateAgentAsync(AgentDefinition agent, string modifiedByUserId)
    {
        if (string.IsNullOrEmpty(modifiedByUserId))
        {
            throw new ArgumentNullException(
                nameof(modifiedByUserId),
                "User ID is required when updating an agent"
            );
        }

        await EnsureInitializedAsync();

        // Check if the agent exists
        var existingAgent = await GetAgentByIdAsync(agent.Id);
        if (existingAgent == null)
        {
            _logger.LogWarning("Attempted to update non-existent agent: {AgentId}", agent.Id);
            return false;
        }

        // Preserve the original creation metadata
        agent.CreatedBy = existingAgent.CreatedBy;
        agent.CreatedAt = existingAgent.CreatedAt;

        // Update the modification metadata
        agent.ModifiedBy = modifiedByUserId;
        agent.UpdatedAt = DateTime.UtcNow;

        var success = await _store.SaveAgentAsync(agent);
        if (success)
        {
            await _cacheLock.WaitAsync();
            try
            {
                _agentsCache[agent.Id] = agent;
            }
            finally
            {
                _cacheLock.Release();
            }

            _logger.LogInformation(
                "Updated agent: {AgentId} - {AgentName} by user {UserId}",
                agent.Id,
                agent.Name,
                modifiedByUserId
            );
            AgentUpdated?.Invoke(this, agent);
        }

        return success;
    }

    public async Task<bool> UnregisterAgentAsync(string id)
    {
        await EnsureInitializedAsync();

        var success = await _store.DeleteAgentAsync(id);
        if (success)
        {
            await _cacheLock.WaitAsync();
            try
            {
                _agentsCache.Remove(id);
            }
            finally
            {
                _cacheLock.Release();
            }

            _logger.LogInformation("Unregistered agent: {AgentId}", id);
            AgentUnregistered?.Invoke(this, id);
        }

        return success;
    }

    public async Task ReloadAgentsAsync()
    {
        Task reloadTask;
        lock (_initializationGate)
        {
            reloadTask = ReloadAgentsCoreAsync();
            _initializationTask = reloadTask;
        }

        await reloadTask;
    }

    private async Task ReloadAgentsCoreAsync()
    {
        _logger.LogInformation("Reloading agents from store");

        var agents = await _store.ListAgentsAsync();
        var agentDict = agents.ToDictionary(a => a.Id);

        await _cacheLock.WaitAsync();
        try
        {
            _agentsCache = agentDict;
        }
        finally
        {
            _cacheLock.Release();
        }

        _logger.LogInformation("Loaded {Count} agents", agentDict.Count);
        AgentsReloaded?.Invoke(this, EventArgs.Empty);
    }
}
