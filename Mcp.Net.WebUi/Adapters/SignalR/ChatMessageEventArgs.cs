using Mcp.Net.LLM.Models;

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
    /// Typed transcript entry that was added to the session.
    /// </summary>
    public ChatTranscriptEntry Entry { get; }

    public ChatMessageEventArgs(string chatId, ChatTranscriptEntry entry)
    {
        ChatId = chatId;
        Entry = entry;
    }
}
