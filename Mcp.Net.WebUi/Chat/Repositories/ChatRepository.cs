using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Infrastructure.Notifications;

namespace Mcp.Net.WebUi.Chat.Repositories;

/// <summary>
/// Repository for chat sessions and messages
/// </summary>
public class ChatRepository : IChatRepository
{
    private readonly ILogger<ChatRepository> _logger;
    private readonly IChatHistoryManager _historyManager;
    private readonly SessionNotifier _sessionNotifier;
    private readonly SemaphoreSlim _sessionLock = new(1);

    // Default user ID for single-user mode
    private const string DefaultUserId = "default";

    public ChatRepository(
        ILogger<ChatRepository> logger,
        IChatHistoryManager historyManager,
        SessionNotifier sessionNotifier
    )
    {
        _logger = logger;
        _historyManager = historyManager;
        _sessionNotifier = sessionNotifier;
    }

    /// <summary>
    /// Get all chat sessions
    /// </summary>
    public async Task<List<SessionMetadataDto>> GetAllChatsAsync(string userId = DefaultUserId)
    {
        _logger.LogInformation("[REPOSITORY] GetAllChatsAsync called for user: {UserId}", userId);
        var sessions = await _historyManager.GetAllSessionsAsync(userId);

        _logger.LogInformation(
            "[REPOSITORY] Retrieved {Count} sessions from history manager",
            sessions.Count
        );

        // Convert to DTOs and maintain ordering by LastUpdatedAt descending
        var dtos = sessions
            .Select(s => new SessionMetadataDto
            {
                Id = s.Id,
                Title = s.Title,
                CreatedAt = s.CreatedAt,
                LastUpdatedAt = s.LastUpdatedAt,
                Model = s.Model,
                Provider = s.Provider.ToString(),
                SystemPrompt = s.SystemPrompt,
                LastMessagePreview = s.LastMessagePreview,
            })
            .OrderByDescending(s => s.LastUpdatedAt)
            .ToList();

        _logger.LogInformation("[REPOSITORY] Returning {Count} session DTOs", dtos.Count);
        return dtos;
    }

    /// <summary>
    /// Get session metadata
    /// </summary>
    public async Task<ChatSessionMetadata?> GetChatMetadataAsync(string chatId)
    {
        return await _historyManager.GetSessionMetadataAsync(chatId);
    }

    /// <summary>
    /// Create a new chat session with the provided metadata
    /// </summary>
    public async Task<string> CreateChatAsync(ChatSessionMetadata metadata)
    {
        await _sessionLock.WaitAsync();
        try
        {
            // Set timestamps if not already set
            if (metadata.CreatedAt == default)
            {
                metadata.CreatedAt = DateTime.UtcNow;
            }

            if (metadata.LastUpdatedAt == default)
            {
                metadata.LastUpdatedAt = DateTime.UtcNow;
            }

            // Store session metadata
            await _historyManager.CreateSessionAsync(DefaultUserId, metadata);

            _logger.LogInformation(
                "[REPOSITORY] Created chat session {SessionId} with model {Model}",
                metadata.Id,
                metadata.Model
            );

            return metadata.Id;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Update chat session metadata
    /// </summary>
    public async Task UpdateChatMetadataAsync(ChatSessionMetadata metadata)
    {
        // Update the metadata in the history manager
        await _historyManager.UpdateSessionMetadataAsync(metadata);

        // Create DTO for notification
        var dto = new SessionMetadataDto
        {
            Id = metadata.Id,
            Title = metadata.Title,
            CreatedAt = metadata.CreatedAt,
            LastUpdatedAt = metadata.LastUpdatedAt,
            Model = metadata.Model,
            Provider = metadata.Provider.ToString(),
            SystemPrompt = metadata.SystemPrompt,
            LastMessagePreview = metadata.LastMessagePreview,
        };

        // Notify clients
        await _sessionNotifier.NotifySessionUpdatedAsync(dto);
    }

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    public async Task DeleteChatAsync(string chatId)
    {
        _logger.LogInformation("[REPOSITORY] DeleteChatAsync called for session {ChatId}", chatId);

        await _sessionLock.WaitAsync();
        try
        {
            // Delete the session history
            await _historyManager.DeleteSessionAsync(chatId);
            _logger.LogInformation("[REPOSITORY] Deleted session {ChatId} from history", chatId);

            // Notify clients
            await _sessionNotifier.NotifySessionDeletedAsync(chatId);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Get all messages for a chat session
    /// </summary>
    public async Task<List<ChatTranscriptEntryDto>> GetChatMessagesAsync(string chatId)
    {
        var transcript = await _historyManager.GetSessionTranscriptAsync(chatId);
        return transcript
            .Select(entry => ChatTranscriptEntryMapper.ToDto(chatId, entry))
            .ToList();
    }

    /// <summary>
    /// Get the typed transcript for a chat session
    /// </summary>
    public Task<IReadOnlyList<ChatTranscriptEntry>> GetTranscriptEntriesAsync(string chatId)
    {
        return _historyManager.GetSessionTranscriptAsync(chatId);
    }

    /// <summary>
    /// Append a transcript entry to a chat session
    /// </summary>
    public async Task AppendTranscriptEntryAsync(string chatId, ChatTranscriptEntry entry)
    {
        _logger.LogInformation(
            "[REPOSITORY] AppendTranscriptEntryAsync called for session {ChatId}",
            chatId
        );

        try
        {
            await _historyManager.AddTranscriptEntryAsync(chatId, entry);
            _logger.LogInformation(
                "[REPOSITORY] Stored transcript entry in history for session {ChatId}",
                chatId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to store transcript entry in history for session {ChatId}",
                chatId
            );
            throw;
        }
    }

    /// <summary>
    /// Replace an existing transcript entry by identifier or append it when no entry exists yet.
    /// </summary>
    public Task UpsertTranscriptEntryAsync(string chatId, ChatTranscriptEntry entry)
    {
        _logger.LogInformation(
            "[REPOSITORY] UpsertTranscriptEntryAsync called for session {ChatId}",
            chatId
        );

        return _historyManager.UpsertTranscriptEntryAsync(chatId, entry);
    }

    /// <summary>
    /// Clear all messages from a chat session
    /// </summary>
    public async Task ClearChatMessagesAsync(string chatId)
    {
        await _historyManager.ClearSessionTranscriptAsync(chatId);
        _logger.LogInformation("[REPOSITORY] Cleared messages for session {ChatId}", chatId);
    }

    /// <summary>
    /// Get the system prompt for a chat session
    /// </summary>
    public async Task<string> GetSystemPromptAsync(string chatId)
    {
        // Try to get from history
        var metadata = await _historyManager.GetSessionMetadataAsync(chatId);
        if (metadata != null && !string.IsNullOrEmpty(metadata.SystemPrompt))
        {
            return metadata.SystemPrompt;
        }

        // Default prompt if nothing else is available
        return "You are a helpful AI assistant.";
    }

    /// <summary>
    /// Set the system prompt for a chat session
    /// </summary>
    public async Task SetSystemPromptAsync(string chatId, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            _logger.LogWarning("Attempted to set empty system prompt for session {ChatId}", chatId);
            throw new ArgumentException("System prompt cannot be empty", nameof(systemPrompt));
        }

        // Check if session exists in history
        var metadata = await _historyManager.GetSessionMetadataAsync(chatId);
        if (metadata == null)
        {
            _logger.LogWarning(
                "Session {ChatId} not found in history when setting system prompt",
                chatId
            );
            throw new KeyNotFoundException($"Session {chatId} not found");
        }

        // Update system prompt in history
        metadata.SystemPrompt = systemPrompt;
        metadata.LastUpdatedAt = DateTime.UtcNow;
        await _historyManager.UpdateSessionMetadataAsync(metadata);
    }

    /// <summary>
    /// Update chat session with new properties
    /// </summary>
    public async Task UpdateChatAsync(SessionUpdateDto updateDto)
    {
        // Get current metadata
        var metadata = await _historyManager.GetSessionMetadataAsync(updateDto.Id);
        if (metadata == null)
        {
            _logger.LogWarning("Session {ChatId} not found when updating metadata", updateDto.Id);
            throw new KeyNotFoundException($"Session {updateDto.Id} not found");
        }

        // Update fields
        if (!string.IsNullOrEmpty(updateDto.Title))
        {
            metadata.Title = updateDto.Title;
        }

        if (!string.IsNullOrEmpty(updateDto.Model))
        {
            metadata.Model = updateDto.Model;
        }

        if (!string.IsNullOrEmpty(updateDto.Provider))
        {
            if (Enum.TryParse<LlmProvider>(updateDto.Provider, true, out var provider))
            {
                metadata.Provider = provider;
            }
        }

        if (!string.IsNullOrEmpty(updateDto.SystemPrompt))
        {
            metadata.SystemPrompt = updateDto.SystemPrompt;
        }

        metadata.LastUpdatedAt = DateTime.UtcNow;

        // Save changes
        await _historyManager.UpdateSessionMetadataAsync(metadata);
        _logger.LogInformation("[REPOSITORY] Updated metadata for session {ChatId}", updateDto.Id);
    }
}
