namespace Mcp.Net.LLM.Models;

/// <summary>
/// Represents a message in the conversation, whether from user, system, or containing tool information
/// </summary>
public class LlmMessage
{
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of the message (System, User, Assistant, Tool, Error)
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Text content of the message (primarily for User and Assistant messages)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// ID of the tool call this message is associated with (for Tool messages)
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// Name of the tool this message is associated with (for Tool messages)
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Results from tool execution (for Tool messages)
    /// </summary>
    public ToolInvocationResult? ToolResult { get; set; }

    /// <summary>
    /// Creates a new user message
    /// </summary>
    public static LlmMessage FromUser(string content)
    {
        return new LlmMessage { Type = MessageType.User, Content = content };
    }

    /// <summary>
    /// Creates a new system message
    /// </summary>
    public static LlmMessage FromSystem(string content)
    {
        return new LlmMessage { Type = MessageType.System, Content = content };
    }

    /// <summary>
    /// Creates a new assistant message
    /// </summary>
    public static LlmMessage FromAssistant(string content)
    {
        return new LlmMessage { Type = MessageType.Assistant, Content = content };
    }

    /// <summary>
    /// Creates a new tool result message
    /// </summary>
    public static LlmMessage FromToolResult(ToolInvocationResult result)
    {
        return new LlmMessage
        {
            Type = MessageType.Tool,
            ToolCallId = result.ToolCallId,
            ToolName = result.ToolName,
            ToolResult = result,
        };
    }
}
