using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat;

namespace Mcp.Net.WebUi.Infrastructure.Persistence;

/// <summary>
/// In-memory implementation of chat history manager
/// </summary>
public class InMemoryChatHistoryManager : IChatHistoryManager
{
    private readonly ILogger<InMemoryChatHistoryManager> _logger;
    private readonly Dictionary<string, List<ChatSessionMetadata>> _userSessions = new();
    private readonly Dictionary<string, List<ChatTranscriptEntry>> _sessionTranscript = new();
    private readonly SemaphoreSlim _lock = new(1);

    public InMemoryChatHistoryManager(ILogger<InMemoryChatHistoryManager> logger)
    {
        _logger = logger;
    }

    public async Task<List<ChatSessionMetadata>> GetAllSessionsAsync(string userId)
    {
        _logger.LogInformation("[HISTORY] GetAllSessionsAsync for user {UserId}", userId);
        await _lock.WaitAsync();
        try
        {
            if (!_userSessions.TryGetValue(userId, out var sessions))
            {
                _logger.LogInformation("[HISTORY] No sessions found for user {UserId}", userId);
                return new List<ChatSessionMetadata>();
            }

            _logger.LogInformation(
                "[HISTORY] Found {Count} sessions for user {UserId}: {SessionIds}",
                sessions.Count,
                userId,
                string.Join(", ", sessions.Select(s => s.Id))
            );

            // Return a copy to prevent external modification
            return sessions.OrderByDescending(s => s.LastUpdatedAt).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ChatSessionMetadata?> GetSessionMetadataAsync(string sessionId)
    {
        _logger.LogInformation(
            "[HISTORY] GetSessionMetadataAsync for session {SessionId}",
            sessionId
        );
        await _lock.WaitAsync();
        try
        {
            // Log the current state of user sessions for debugging
            _logger.LogInformation(
                "[HISTORY] Current user sessions: {UserSessionCounts}",
                string.Join(
                    ", ",
                    _userSessions.Select(kv => $"{kv.Key}: {kv.Value.Count} sessions")
                )
            );

            foreach (var userEntry in _userSessions)
            {
                var userId = userEntry.Key;
                var sessions = userEntry.Value;

                _logger.LogInformation(
                    "[HISTORY] Checking user {UserId} with {Count} sessions",
                    userId,
                    sessions.Count
                );

                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    _logger.LogInformation(
                        "[HISTORY] Found session {SessionId} for user {UserId}",
                        sessionId,
                        userId
                    );

                    // Return a copy to prevent external modification
                    return new ChatSessionMetadata
                    {
                        Id = session.Id,
                        Title = session.Title,
                        CreatedAt = session.CreatedAt,
                        LastUpdatedAt = session.LastUpdatedAt,
                        Model = session.Model,
                        Provider = session.Provider,
                        SystemPrompt = session.SystemPrompt,
                        LastMessagePreview = session.LastMessagePreview,
                        UserId = session.UserId,
                    };
                }
            }

            _logger.LogWarning(
                "[HISTORY] Session {SessionId} not found in any user's sessions",
                sessionId
            );
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ChatSessionMetadata> CreateSessionAsync(
        string userId,
        ChatSessionMetadata metadata
    )
    {
        _logger.LogInformation(
            "[HISTORY] CreateSessionAsync for user {UserId} with metadata: ID={SessionId}, Title={Title}, Model={Model}",
            userId,
            metadata.Id,
            metadata.Title,
            metadata.Model
        );

        await _lock.WaitAsync();
        try
        {
            // Ensure the user has a session list
            if (!_userSessions.TryGetValue(userId, out var sessions))
            {
                _logger.LogInformation(
                    "[HISTORY] Creating new session list for user {UserId}",
                    userId
                );
                sessions = new List<ChatSessionMetadata>();
                _userSessions[userId] = sessions;
            }
            else
            {
                _logger.LogInformation(
                    "[HISTORY] User {UserId} already has {Count} sessions",
                    userId,
                    sessions.Count
                );
            }

            // Generate a new ID if not provided
            if (string.IsNullOrEmpty(metadata.Id))
            {
                metadata.Id = Guid.NewGuid().ToString();
                _logger.LogInformation(
                    "[HISTORY] Generated new session ID: {SessionId}",
                    metadata.Id
                );
            }

            // Set default title if not provided
            if (string.IsNullOrEmpty(metadata.Title))
            {
                metadata.Title = $"Chat {sessions.Count + 1}";
                _logger.LogInformation("[HISTORY] Set default title: {Title}", metadata.Title);
            }

            // Set timestamps
            metadata.CreatedAt = DateTime.UtcNow;
            metadata.LastUpdatedAt = DateTime.UtcNow;

            // Add to user's sessions
            sessions.Add(metadata);
            _logger.LogInformation(
                "[HISTORY] Added session to user {UserId}, now has {Count} sessions",
                userId,
                sessions.Count
            );

            // Initialize empty message list
            _sessionTranscript[metadata.Id] = new List<ChatTranscriptEntry>();
            _logger.LogInformation(
                "[HISTORY] Initialized empty transcript for session {SessionId}",
                metadata.Id
            );

            // Log state after creation
            _logger.LogInformation(
                "[HISTORY] Created new chat session {SessionId} for user {UserId}. All users now have: {UserSessions}",
                metadata.Id,
                userId,
                string.Join(
                    ", ",
                    _userSessions.Select(kv => $"{kv.Key}: {kv.Value.Count} sessions")
                )
            );

            return metadata;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateSessionMetadataAsync(ChatSessionMetadata metadata)
    {
        _logger.LogInformation(
            "[HISTORY] UpdateSessionMetadataAsync for session {SessionId} with title: {Title}",
            metadata.Id,
            metadata.Title
        );

        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation(
                "[HISTORY] Current user sessions: {UserSessions}",
                string.Join(
                    ", ",
                    _userSessions.Select(kv => $"{kv.Key}: {kv.Value.Count} sessions")
                )
            );

            bool found = false;

            foreach (var userEntry in _userSessions)
            {
                var userId = userEntry.Key;
                var sessions = userEntry.Value;

                _logger.LogInformation(
                    "[HISTORY] Checking user {UserId} for session {SessionId}",
                    userId,
                    metadata.Id
                );

                var existingSession = sessions.FirstOrDefault(s => s.Id == metadata.Id);
                if (existingSession != null)
                {
                    found = true;
                    _logger.LogInformation(
                        "[HISTORY] Found session {SessionId} in user {UserId}'s sessions, updating...",
                        metadata.Id,
                        userId
                    );

                    // Log before update
                    _logger.LogInformation(
                        "[HISTORY] Before update - Title: {OldTitle}, Model: {OldModel}, Provider: {OldProvider}",
                        existingSession.Title,
                        existingSession.Model,
                        existingSession.Provider
                    );

                    // Update properties
                    existingSession.Title = metadata.Title;
                    existingSession.LastUpdatedAt = DateTime.UtcNow;
                    existingSession.Model = metadata.Model;
                    existingSession.Provider = metadata.Provider;
                    existingSession.SystemPrompt = metadata.SystemPrompt;
                    existingSession.LastMessagePreview = metadata.LastMessagePreview;

                    // Log after update
                    _logger.LogInformation(
                        "[HISTORY] After update - Title: {NewTitle}, Model: {NewModel}, Provider: {NewProvider}",
                        existingSession.Title,
                        existingSession.Model,
                        existingSession.Provider
                    );

                    _logger.LogInformation(
                        "[HISTORY] Updated chat session {SessionId} for user {UserId}",
                        metadata.Id,
                        userId
                    );
                    return;
                }
            }

            if (!found)
            {
                _logger.LogWarning(
                    "[HISTORY] Session {SessionId} not found in any user's sessions",
                    metadata.Id
                );
                throw new KeyNotFoundException($"Session {metadata.Id} not found");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        _logger.LogInformation("[HISTORY] DeleteSessionAsync for session {SessionId}", sessionId);

        // Log state before deletion
        _logger.LogInformation(
            "[HISTORY] Before deletion - User sessions: {UserSessions}",
            string.Join(
                ", ",
                _userSessions.Select(kv =>
                    $"{kv.Key}: {kv.Value.Count} sessions ({string.Join(",", kv.Value.Select(s => s.Id))})"
                )
            )
        );

        await _lock.WaitAsync();
        try
        {
            bool found = false;

            // Remove from user sessions
            foreach (var userId in _userSessions.Keys.ToList())
            {
                var sessions = _userSessions[userId];
                _logger.LogInformation(
                    "[HISTORY] Checking user {UserId} with {Count} sessions: {SessionIds}",
                    userId,
                    sessions.Count,
                    string.Join(", ", sessions.Select(s => s.Id))
                );

                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    _logger.LogInformation(
                        "[HISTORY] Found session {SessionId} in user {UserId}'s sessions, removing...",
                        sessionId,
                        userId
                    );

                    sessions.Remove(session);
                    found = true;
                    _logger.LogInformation(
                        "[HISTORY] Removed chat session {SessionId} from user {UserId}, user now has {Count} sessions: {SessionIds}",
                        sessionId,
                        userId,
                        sessions.Count,
                        string.Join(", ", sessions.Select(s => s.Id))
                    );
                }
                else
                {
                    _logger.LogInformation(
                        "[HISTORY] Session {SessionId} not found in user {UserId}'s sessions",
                        sessionId,
                        userId
                    );
                }
            }

            // Remove messages
            if (_sessionTranscript.ContainsKey(sessionId))
            {
                _logger.LogInformation(
                    "[HISTORY] Found transcript for session {SessionId}, removing...",
                    sessionId
                );
                _sessionTranscript.Remove(sessionId);
                found = true;
                _logger.LogInformation(
                    "[HISTORY] Removed transcript for chat session {SessionId}",
                    sessionId
                );
            }
            else
            {
                _logger.LogInformation(
                    "[HISTORY] No transcript found for session {SessionId}",
                    sessionId
                );
            }

            if (!found)
            {
                _logger.LogWarning(
                    "[HISTORY] Attempted to delete non-existent session {SessionId} - it was not found in any user's sessions or message store",
                    sessionId
                );
            }

            // Log final state after deletion
            _logger.LogInformation(
                "[HISTORY] After deletion - User sessions: {UserSessions}",
                string.Join(
                    ", ",
                    _userSessions.Select(kv =>
                        $"{kv.Key}: {kv.Value.Count} sessions ({string.Join(",", kv.Value.Select(s => s.Id))})"
                    )
                )
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ChatTranscriptEntry>> GetSessionTranscriptAsync(string sessionId)
    {
        _logger.LogInformation(
            "[HISTORY] GetSessionTranscriptAsync for session {SessionId}",
            sessionId
        );
        await _lock.WaitAsync();
        try
        {
            if (!_sessionTranscript.TryGetValue(sessionId, out var transcript))
            {
                _logger.LogInformation(
                    "[HISTORY] No transcript found for session {SessionId}",
                    sessionId
                );
                return Array.Empty<ChatTranscriptEntry>();
            }

            _logger.LogInformation(
                "[HISTORY] Found {Count} transcript entries for session {SessionId}",
                transcript.Count,
                sessionId
            );

            return transcript.OrderBy(entry => entry.Timestamp).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddTranscriptEntryAsync(string sessionId, ChatTranscriptEntry entry)
    {
        _logger.LogInformation(
            "[HISTORY] AddTranscriptEntryAsync for session {SessionId}, entry kind: {Kind}",
            sessionId,
            entry.Kind
        );

        await _lock.WaitAsync();
        try
        {
            if (!_sessionTranscript.TryGetValue(sessionId, out var transcript))
            {
                _logger.LogInformation(
                    "[HISTORY] Creating new transcript list for session {SessionId}",
                    sessionId
                );
                transcript = new List<ChatTranscriptEntry>();
                _sessionTranscript[sessionId] = transcript;
            }
            else
            {
                _logger.LogInformation(
                    "[HISTORY] Session {SessionId} already has {Count} transcript entries",
                    sessionId,
                    transcript.Count
                );
            }

            transcript.Add(entry);
            _logger.LogInformation(
                "[HISTORY] Added transcript entry {EntryId} to session {SessionId}, now has {Count} entries",
                entry.Id,
                sessionId,
                transcript.Count
            );

            _logger.LogInformation(
                "[HISTORY] Transcript preview: '{ContentPreview}'",
                ChatTranscriptEntryMapper.ToPreview(entry, 30)
            );
            UpdateSessionPreview(sessionId, entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertTranscriptEntryAsync(string sessionId, ChatTranscriptEntry entry)
    {
        _logger.LogInformation(
            "[HISTORY] UpsertTranscriptEntryAsync for session {SessionId}, entry kind: {Kind}",
            sessionId,
            entry.Kind
        );

        await _lock.WaitAsync();
        try
        {
            if (!_sessionTranscript.TryGetValue(sessionId, out var transcript))
            {
                _logger.LogInformation(
                    "[HISTORY] Creating new transcript list for session {SessionId}",
                    sessionId
                );
                transcript = new List<ChatTranscriptEntry>();
                _sessionTranscript[sessionId] = transcript;
            }

            var existingIndex = transcript.FindIndex(existing => existing.Id == entry.Id);
            if (existingIndex >= 0)
            {
                transcript[existingIndex] = entry;
                _logger.LogInformation(
                    "[HISTORY] Replaced transcript entry {EntryId} in session {SessionId}",
                    entry.Id,
                    sessionId
                );
            }
            else
            {
                transcript.Add(entry);
                _logger.LogInformation(
                    "[HISTORY] Appended transcript entry {EntryId} to session {SessionId}",
                    entry.Id,
                    sessionId
                );
            }

            UpdateSessionPreview(sessionId, entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddTranscriptEntriesAsync(
        string sessionId,
        IReadOnlyList<ChatTranscriptEntry> entries
    )
    {
        if (entries.Count == 0)
        {
            return;
        }

        await _lock.WaitAsync();
        try
        {
            if (!_sessionTranscript.TryGetValue(sessionId, out var transcript))
            {
                transcript = new List<ChatTranscriptEntry>();
                _sessionTranscript[sessionId] = transcript;
            }

            foreach (var entry in entries)
            {
                transcript.Add(entry);
            }

            var lastEntry = entries.OrderBy(entry => entry.Timestamp).Last();
            foreach (var sessions in _userSessions.Values)
            {
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    session.LastUpdatedAt = DateTime.UtcNow;
                    session.LastMessagePreview = ChatTranscriptEntryMapper.ToPreview(lastEntry);
                }
            }

            _logger.LogDebug(
                "Added {Count} transcript entries to session {SessionId}",
                entries.Count,
                sessionId
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearSessionTranscriptAsync(string sessionId)
    {
        _logger.LogInformation(
            "[HISTORY] ClearSessionTranscriptAsync for session {SessionId}",
            sessionId
        );
        await _lock.WaitAsync();
        try
        {
            if (_sessionTranscript.TryGetValue(sessionId, out var transcript))
            {
                _logger.LogInformation(
                    "[HISTORY] Found {Count} transcript entries for session {SessionId}, clearing...",
                    transcript.Count,
                    sessionId
                );
                transcript.Clear();
                _logger.LogInformation(
                    "[HISTORY] Cleared transcript for session {SessionId}",
                    sessionId
                );
            }
            else
            {
                _logger.LogWarning(
                    "[HISTORY] Attempted to clear transcript for non-existent session {SessionId}",
                    sessionId
                );
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void UpdateSessionPreview(string sessionId, ChatTranscriptEntry entry)
    {
        bool sessionFound = false;
        foreach (var userEntry in _userSessions)
        {
            var userId = userEntry.Key;
            var sessions = userEntry.Value;

            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                sessionFound = true;
                _logger.LogInformation(
                    "[HISTORY] Updating session metadata for transcript entry in user {UserId}",
                    userId
                );

                session.LastUpdatedAt = DateTime.UtcNow;
                session.LastMessagePreview = ChatTranscriptEntryMapper.ToPreview(entry);

                _logger.LogInformation(
                    "[HISTORY] Updated last message preview for session {SessionId}: '{NewPreview}'",
                    sessionId,
                    session.LastMessagePreview
                );
            }
        }

        if (!sessionFound)
        {
            _logger.LogWarning(
                "[HISTORY] Transcript entry stored for session {SessionId}, but session metadata not found in any user's sessions",
                sessionId
            );
        }
    }
}
