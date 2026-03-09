using Mcp.Net.LLM.Events;

namespace Mcp.Net.LLM.Interfaces;

/// <summary>
/// Interface for typed chat session events that UI components can subscribe to.
/// </summary>
public interface IChatSessionEvents
{
    event EventHandler? SessionStarted;

    event EventHandler<ChatTranscriptChangedEventArgs>? TranscriptChanged;

    event EventHandler<ChatSessionActivityChangedEventArgs>? ActivityChanged;

    event EventHandler<ToolCallActivityChangedEventArgs>? ToolCallActivityChanged;
}
