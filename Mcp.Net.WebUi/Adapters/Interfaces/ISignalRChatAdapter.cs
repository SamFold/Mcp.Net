using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
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

    /// <summary>
    /// Provides the cached prompt descriptors for the session.
    /// </summary>
    Task<IReadOnlyList<Prompt>> GetPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full prompt payload for a specific prompt.
    /// </summary>
    Task<object[]> GetPromptMessagesAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides the cached resource descriptors for the session.
    /// </summary>
    Task<IReadOnlyList<Resource>> GetResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the contents of a resource from the server.
    /// </summary>
    Task<ResourceContent[]> ReadResourceAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests completion suggestions for a prompt argument.
    /// </summary>
    Task<CompletionValues> CompletePromptAsync(
        string promptName,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Requests completion suggestions for a resource argument.
    /// </summary>
    Task<CompletionValues> CompleteResourceAsync(
        string resourceUri,
        string argumentName,
        string currentValue,
        IReadOnlyDictionary<string, string>? contextArguments = null,
        CancellationToken cancellationToken = default
    );
}
