using Mcp.Net.LLM.Events;
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
    /// Typed transcript entry that changed in the session.
    /// </summary>
    public ChatTranscriptEntry Entry { get; }

    /// <summary>
    /// The transcript change kind associated with the entry.
    /// </summary>
    public ChatTranscriptChangeKind ChangeKind { get; }

    public ChatMessageEventArgs(
        string chatId,
        ChatTranscriptEntry entry,
        ChatTranscriptChangeKind changeKind = ChatTranscriptChangeKind.Added
    )
    {
        ChatId = chatId;
        Entry = entry;
        ChangeKind = changeKind;
    }
}
