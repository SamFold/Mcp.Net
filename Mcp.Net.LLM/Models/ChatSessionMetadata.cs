namespace Mcp.Net.LLM.Models;

public class ChatSessionMetadata
{
    /// <summary>
    /// Unique identifier for the chat session
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly title for the chat
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// When the chat was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the chat was last updated
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Model used for this chat (e.g., "claude-sonnet-4-5-20250929")
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// LLM provider for this chat (Anthropic, OpenAI, etc.)
    /// </summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Anthropic;

    /// <summary>
    /// System prompt used for this chat
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Preview of the last message (for display in the sidebar)
    /// </summary>
    public string LastMessagePreview { get; set; } = string.Empty;

    /// <summary>
    /// ID of the agent used for this session, if any
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Name of the agent used for this session, if any
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// User ID associated with this session
    /// </summary>
    public string? UserId { get; set; }
}
