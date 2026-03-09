namespace Mcp.Net.WebUi.Adapters.SignalR;

/// <summary>
/// Event args for chat messages
/// </summary>
public class ChatMessageEventArgs : EventArgs
{
    /// <summary>
    /// Chat ID for the session
    /// </summary>
    public string ChatId { get; }

    /// <summary>
    /// Unique message identifier
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Message content
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Message type (user, assistant, system, etc.)
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Structured metadata associated with the message, when available.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; }

    public ChatMessageEventArgs(
        string chatId,
        string messageId,
        string content,
        string type,
        Dictionary<string, object>? metadata = null
    )
    {
        ChatId = chatId;
        MessageId = messageId;
        Content = content;
        Type = type;
        Metadata = metadata;
    }
}
