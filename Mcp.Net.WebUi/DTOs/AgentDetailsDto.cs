using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO with complete details about an agent
/// </summary>
public class AgentDetailsDto
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
    /// Detailed description of the agent's purpose and capabilities
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
    /// The system prompt that defines the agent's behavior
    /// </summary>
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
    /// Creation date of this agent definition
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last modified date of this agent definition
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// User ID of the creator
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// User ID of the last person who modified this agent
    /// </summary>
    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Create a details DTO from an agent definition
    /// </summary>
    public static AgentDetailsDto FromAgentDefinition(AgentDefinition agent)
    {
        return new AgentDetailsDto
        {
            Id = agent.Id,
            Name = agent.Name,
            Description = agent.Description,
            Category = agent.Category.ToString(),
            Provider = agent.Provider.ToString(),
            ModelName = agent.ModelName,
            SystemPrompt = agent.SystemPrompt,
            ToolIds = agent.ToolIds,
            Parameters = agent.Parameters,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt,
            CreatedBy = agent.CreatedBy,
            ModifiedBy = agent.ModifiedBy
        };
    }

    /// <summary>
    /// Convert this DTO to an agent definition
    /// </summary>
    public AgentDefinition ToAgentDefinition()
    {
        return new AgentDefinition
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Category = Enum.Parse<AgentCategory>(Category),
            Provider = Enum.Parse<LlmProvider>(Provider),
            ModelName = ModelName,
            SystemPrompt = SystemPrompt,
            ToolIds = ToolIds,
            Parameters = Parameters,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatedBy = CreatedBy,
            ModifiedBy = ModifiedBy
        };
    }
}
