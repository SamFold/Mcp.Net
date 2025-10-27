using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.WebUi.Adapters.Interfaces;

/// <summary>
/// Adapter interface that connects ChatSession to SignalR for web UI
/// </summary>
public interface ISignalRChatAdapter : IDisposable
{
    /// <summary>
    /// Session ID for this adapter instance
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Start the chat session
    /// </summary>
    void Start();

    /// <summary>
    /// Process a user message
    /// </summary>
    void ProcessUserInput(string message);

    /// <summary>
    /// Reset the conversation history in the LLM client
    /// </summary>
    void ResetConversation();

    /// <summary>
    /// Load conversation history from stored messages
    /// </summary>
    Task LoadHistoryAsync(List<StoredChatMessage> messages);

    /// <summary>
    /// Get the LLM client used by this session
    /// </summary>
    IChatClient? GetLlmClient();

    /// <summary>
    /// Notify clients that session metadata has been updated
    /// </summary>
    Task NotifyMetadataUpdated(ChatSessionMetadata metadata);

    /// <summary>
    /// Attempts to resolve a pending elicitation request with the supplied client response.
    /// </summary>
    Task<bool> TryResolveElicitationAsync(ElicitationResponseDto response);

    /// <summary>
    /// Event raised when a message is received from the assistant
    /// </summary>
    event EventHandler<ChatMessageEventArgs>? MessageReceived;
}
