using System.Text.Json;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Agent.Agents.Stores;

/// <summary>
/// File system implementation of IAgentStore that saves agent definitions as JSON files
/// </summary>
public class FileSystemAgentStore : IAgentStore
{
    private readonly string _storageDirectory;
    private readonly ILogger<FileSystemAgentStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public FileSystemAgentStore(string storageDirectory, ILogger<FileSystemAgentStore> logger)
    {
        _storageDirectory = storageDirectory;
        _logger = logger;

        // Ensure the storage directory exists
        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task<AgentDefinition?> GetAgentByIdAsync(string id)
    {
        var filePath = GetAgentFilePath(id);

        _logger.LogDebug("Getting agent with ID: {AgentId} from: {FilePath}", id, filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Agent file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            using var fileStream = File.OpenRead(filePath);
            var agent = await JsonSerializer.DeserializeAsync<AgentDefinition>(fileStream);
            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading agent file: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<IEnumerable<AgentDefinition>> ListAgentsAsync()
    {
        var result = new List<AgentDefinition>();
        var files = Directory.GetFiles(_storageDirectory, "*.json");

        _logger.LogDebug("Found {Count} agent files", files.Length);

        foreach (var file in files)
        {
            try
            {
                using var fileStream = File.OpenRead(file);
                var agent = await JsonSerializer.DeserializeAsync<AgentDefinition>(fileStream);
                if (agent != null)
                {
                    result.Add(agent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading agent file: {FilePath}", file);
            }
        }

        return result;
    }

    public async Task<bool> SaveAgentAsync(AgentDefinition agent)
    {
        if (string.IsNullOrEmpty(agent.Id))
        {
            agent.Id = Guid.NewGuid().ToString();
        }

        var filePath = GetAgentFilePath(agent.Id);

        // Update timestamps
        if (File.Exists(filePath))
        {
            // Existing agent - update timestamp
            agent.UpdatedAt = DateTime.UtcNow;
            _logger.LogDebug(
                "Updating existing agent: {AgentId} at {FilePath}",
                agent.Id,
                filePath
            );
        }
        else
        {
            // New agent - set both timestamps
            var now = DateTime.UtcNow;
            agent.CreatedAt = now;
            agent.UpdatedAt = now;
            _logger.LogDebug(
                "Creating new agent with ID: {AgentId} at {FilePath}",
                agent.Id,
                filePath
            );
        }

        try
        {
            using var fileStream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(fileStream, agent, _jsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving agent file: {FilePath}", filePath);
            return false;
        }
    }

    public Task<bool> DeleteAgentAsync(string id)
    {
        var filePath = GetAgentFilePath(id);

        _logger.LogDebug("Deleting agent with ID: {AgentId} at {FilePath}", id, filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Agent file not found for deletion: {FilePath}", filePath);
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent file: {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    public Task<bool> AgentExistsAsync(string id)
    {
        var filePath = GetAgentFilePath(id);
        var exists = File.Exists(filePath);

        _logger.LogDebug(
            "Checking if agent exists with ID: {AgentId}, Result: {Exists}",
            id,
            exists
        );

        return Task.FromResult(exists);
    }

    private string GetAgentFilePath(string id)
    {
        // Sanitize ID for use in filenames
        var safeId = string.Join("_", id.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, $"{safeId}.json");
    }
}
