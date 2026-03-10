using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Interfaces;

/// <summary>
/// Interface for chat history management
/// </summary>
public interface IChatHistoryManager
{
    /// <summary>
    /// Get all chat sessions for a user
    /// </summary>
    Task<List<ChatSessionMetadata>> GetAllSessionsAsync(string userId);

    /// <summary>
    /// Get metadata for a specific chat session
    /// </summary>
    Task<ChatSessionMetadata?> GetSessionMetadataAsync(string sessionId);

    /// <summary>
    /// Create a new chat session
    /// </summary>
    Task<ChatSessionMetadata> CreateSessionAsync(string userId, ChatSessionMetadata metadata);

    /// <summary>
    /// Update chat session metadata
    /// </summary>
    Task UpdateSessionMetadataAsync(ChatSessionMetadata metadata);

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Get the persisted transcript for a chat session
    /// </summary>
    Task<IReadOnlyList<ChatTranscriptEntry>> GetSessionTranscriptAsync(string sessionId);

    /// <summary>
    /// Append a transcript entry to a chat session
    /// </summary>
    Task AddTranscriptEntryAsync(string sessionId, ChatTranscriptEntry entry);

    /// <summary>
    /// Replace an existing transcript entry by identifier or append it when no entry exists yet.
    /// </summary>
    Task UpsertTranscriptEntryAsync(string sessionId, ChatTranscriptEntry entry);

    /// <summary>
    /// Append multiple transcript entries to a chat session
    /// </summary>
    Task AddTranscriptEntriesAsync(string sessionId, IReadOnlyList<ChatTranscriptEntry> entries);

    /// <summary>
    /// Clear the persisted transcript for a chat session
    /// </summary>
    Task ClearSessionTranscriptAsync(string sessionId);
}
