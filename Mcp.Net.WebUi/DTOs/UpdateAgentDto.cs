using System.ComponentModel.DataAnnotations;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO for updating an existing agent
/// </summary>
public class UpdateAgentDto
{
    /// <summary>
    /// Human-readable name for the agent
    /// </summary>
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    /// <summary>
    /// Detailed description of the agent's purpose and capabilities
    /// </summary>
    [StringLength(1000, MinimumLength = 10)]
    public string? Description { get; set; }

    /// <summary>
    /// The category this agent belongs to
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The system prompt that defines the agent's behavior
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// IDs of tools this agent should have access to
    /// </summary>
    public List<string>? ToolIds { get; set; }

    /// <summary>
    /// Additional configuration parameters (temperature, max tokens, etc.)
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// Apply updates to an existing agent definition
    /// </summary>
    public void UpdateAgentDefinition(AgentDefinition agent, string userId)
    {
        if (Name != null)
            agent.Name = Name;

        if (Description != null)
            agent.Description = Description;

        if (Category != null && Enum.TryParse<AgentCategory>(Category, out var category))
            agent.Category = category;

        if (SystemPrompt != null)
            agent.SystemPrompt = SystemPrompt;

        if (ToolIds != null)
            agent.ToolIds = ToolIds;

        if (Parameters != null)
            agent.Parameters = Parameters;

        agent.UpdatedAt = DateTime.UtcNow;
        agent.ModifiedBy = userId;
    }
}
