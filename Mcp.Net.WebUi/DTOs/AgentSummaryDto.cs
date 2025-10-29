using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO with minimal information about an agent for listing purposes
/// </summary>
public class AgentSummaryDto
{
    /// <summary>
    /// Unique identifier for the agent
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the agent
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short description of the agent's purpose and capabilities
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The category this agent belongs to
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The LLM provider (OpenAI, Anthropic, etc.)
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The specific model name to use (e.g., "gpt-5", "claude-sonnet-4-5-20250929")
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Creation date of this agent definition
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last modified date of this agent definition
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Create a summary DTO from an agent definition
    /// </summary>
    public static AgentSummaryDto FromAgentDefinition(AgentDefinition agent)
    {
        return new AgentSummaryDto
        {
            Id = agent.Id,
            Name = agent.Name,
            Description = agent.Description,
            Category = agent.Category.ToString(),
            Provider = agent.Provider.ToString(),
            ModelName = agent.ModelName,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt
        };
    }
}
