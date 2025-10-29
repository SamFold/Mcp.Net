using System.Text.Json.Serialization;

namespace Mcp.Net.LLM.Models;

public class AgentDefinition
{
    /// <summary>
    /// Unique identifier for the agent
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name for the agent
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the agent's purpose and capabilities
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The LLM provider (OpenAI, Anthropic, etc.)
    /// </summary>
    public LlmProvider Provider { get; set; }

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
    /// The category this agent belongs to
    /// </summary>
    public AgentCategory Category { get; set; } = AgentCategory.General;

    /// <summary>
    /// Creation date of this agent definition
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modified date of this agent definition
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID of the creator (required)
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// User ID of the last person who modified this agent
    /// </summary>
    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this is a system default agent that should not be deleted
    /// </summary>
    public bool IsSystemDefault { get; set; } = false;

    /// <summary>
    /// For system default agents, indicates which model this is the default for (if any)
    /// </summary>
    public string? DefaultForModel { get; set; }

    /// <summary>
    /// For system default agents, indicates which provider this is the default for (if any)
    /// </summary>
    public LlmProvider? DefaultForProvider { get; set; }

    /// <summary>
    /// Indicates whether this is the global system-wide default agent
    /// </summary>
    public bool IsGlobalDefault { get; set; } = false;
}
