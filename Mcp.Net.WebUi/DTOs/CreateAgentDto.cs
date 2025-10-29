using System.ComponentModel.DataAnnotations;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO for creating a new agent
/// </summary>
public class CreateAgentDto
{
    /// <summary>
    /// Human-readable name for the agent
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the agent's purpose and capabilities
    /// </summary>
    [Required]
    [StringLength(1000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The category this agent belongs to
    /// </summary>
    [Required]
    public string Category { get; set; } = "General";

    /// <summary>
    /// The LLM provider (OpenAI, Anthropic, etc.)
    /// </summary>
    [Required]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The specific model name to use (e.g., "gpt-5", "claude-sonnet-4-5-20250929")
    /// </summary>
    [Required]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// The system prompt that defines the agent's behavior
    /// </summary>
    [Required]
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// IDs of tools this agent should have access to
    /// </summary>
    public List<string> ToolIds { get; set; } = new();

    /// <summary>
    /// Additional configuration parameters (temperature, max tokens, etc.)
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Convert this DTO to an agent definition
    /// </summary>
    public AgentDefinition ToAgentDefinition(string userId)
    {
        return new AgentDefinition
        {
            Name = Name,
            Description = Description,
            Category = Enum.Parse<AgentCategory>(Category),
            Provider = Enum.Parse<LlmProvider>(Provider),
            ModelName = ModelName,
            SystemPrompt = SystemPrompt,
            ToolIds = ToolIds,
            Parameters = Parameters,
            CreatedBy = userId,
            ModifiedBy = userId,
        };
    }
}
