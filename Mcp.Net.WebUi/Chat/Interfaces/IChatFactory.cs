using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Adapters.Interfaces;

namespace Mcp.Net.WebUi.Chat.Interfaces;

/// <summary>
/// Factory interface for creating chat session components
/// </summary>
public interface IChatFactory
{
    /// <summary>
    /// Create a new SignalR chat adapter
    /// </summary>
    /// <param name="sessionId">Unique identifier for the chat session</param>
    /// <param name="model">LLM model to use</param>
    /// <param name="provider">LLM provider to use</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <returns>A configured SignalR chat adapter</returns>
    Task<ISignalRChatAdapter> CreateSignalRAdapterAsync(
        string sessionId,
        string? model = null,
        string? provider = null,
        string? systemPrompt = null
    );

    /// <summary>
    /// Create a new SignalR chat adapter from an agent definition
    /// </summary>
    /// <param name="sessionId">Unique identifier for the chat session</param>
    /// <param name="agent">Agent definition to use for configuration</param>
    /// <returns>A configured SignalR chat adapter</returns>
    Task<ISignalRChatAdapter> CreateSignalRAdapterFromAgentAsync(
        string sessionId,
        AgentDefinition agent
    );

    /// <summary>
    /// Create session metadata for a new chat
    /// </summary>
    /// <param name="sessionId">Unique identifier for the chat session</param>
    /// <param name="model">LLM model to use</param>
    /// <param name="provider">LLM provider to use</param>
    /// <param name="systemPrompt">Optional system prompt</param>
    /// <returns>Configured ChatSessionMetadata</returns>
    ChatSessionMetadata CreateSessionMetadata(
        string sessionId,
        string? model = null,
        string? provider = null,
        string? systemPrompt = null
    );

    /// <summary>
    /// Create session metadata from an agent definition
    /// </summary>
    /// <param name="sessionId">Unique identifier for the chat session</param>
    /// <param name="agent">Agent definition to use for configuration</param>
    /// <returns>Configured ChatSessionMetadata</returns>
    ChatSessionMetadata CreateSessionMetadataFromAgent(string sessionId, AgentDefinition agent);

    /// <summary>
    /// Releases session-specific resources that were provisioned by the factory.
    /// </summary>
    void ReleaseSessionResources(string sessionId);
}
