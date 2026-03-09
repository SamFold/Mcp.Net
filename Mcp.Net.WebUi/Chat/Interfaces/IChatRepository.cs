using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.WebUi.Chat.Interfaces;

/// <summary>
/// Repository interface for storing and retrieving chat sessions and messages
/// Provides a clean separation from chat session management and SignalR communication
/// </summary>
public interface IChatRepository
{
    /// <summary>
    /// Get all chat sessions for a user
    /// </summary>
    Task<List<SessionMetadataDto>> GetAllChatsAsync(string userId);

    /// <summary>
    /// Get session metadata
    /// </summary>
    Task<ChatSessionMetadata?> GetChatMetadataAsync(string chatId);

    /// <summary>
    /// Create a new chat session
    /// </summary>
    Task<string> CreateChatAsync(ChatSessionMetadata metadata);

    /// <summary>
    /// Update chat session metadata
    /// </summary>
    Task UpdateChatMetadataAsync(ChatSessionMetadata metadata);

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    Task DeleteChatAsync(string chatId);

    /// <summary>
    /// Get all messages for a chat session
    /// </summary>
    Task<List<ChatTranscriptEntryDto>> GetChatMessagesAsync(string chatId);

    /// <summary>
    /// Get the typed transcript entries for a chat session
    /// </summary>
    Task<IReadOnlyList<ChatTranscriptEntry>> GetTranscriptEntriesAsync(string chatId);

    /// <summary>
    /// Append a transcript entry to a chat session
    /// </summary>
    Task AppendTranscriptEntryAsync(string chatId, ChatTranscriptEntry entry);

    /// <summary>
    /// Clear all messages from a chat session
    /// </summary>
    Task ClearChatMessagesAsync(string chatId);

    /// <summary>
    /// Get the system prompt for a chat session
    /// </summary>
    Task<string> GetSystemPromptAsync(string chatId);

    /// <summary>
    /// Set the system prompt for a chat session
    /// </summary>
    Task SetSystemPromptAsync(string chatId, string systemPrompt);

    /// <summary>
    /// Update chat session with new properties
    /// </summary>
    Task UpdateChatAsync(SessionUpdateDto updateDto);
}
