namespace Mcp.Net.WebUi.DTOs;

/// <summary>
/// DTO for chat messages sent between client and server
/// </summary>
public class ChatMessageDto
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Session ID this message belongs to
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Type of message (user, assistant, system)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Content of the message
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Structured metadata associated with this message.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
