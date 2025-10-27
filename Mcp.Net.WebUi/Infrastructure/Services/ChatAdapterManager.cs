using System.Collections.Concurrent;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Adapters.SignalR;
using Microsoft.Extensions.Logging;
using Mcp.Net.WebUi.Chat.Interfaces;

namespace Mcp.Net.WebUi.Infrastructure.Services;

/// <summary>
/// Manages chat adapters across sessions with memory management and cleanup
/// </summary>
public class ChatAdapterManager : IChatAdapterManager, IHostedService, IDisposable
{
    private readonly ILogger<ChatAdapterManager> _logger;
    private readonly IChatFactory _chatFactory;
    private readonly ConcurrentDictionary<
        string,
        (ISignalRChatAdapter Adapter, DateTime LastActive)
    > _adapters = new();
    private readonly TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(30);
    private Timer? _cleanupTimer;

    public ChatAdapterManager(ILogger<ChatAdapterManager> logger, IChatFactory chatFactory)
    {
        _logger = logger;
        _chatFactory = chatFactory;
    }

    /// <summary>
    /// Gets an existing adapter or creates a new one using the provided function
    /// </summary>
    public async Task<ISignalRChatAdapter> GetOrCreateAdapterAsync(
        string sessionId,
        Func<string, Task<ISignalRChatAdapter>> createFunc
    )
    {
        if (_adapters.TryGetValue(sessionId, out var adapterEntry))
        {
            _logger.LogDebug("Reusing adapter for session {SessionId}", sessionId);
            // Update last active time
            MarkAdapterAsActive(sessionId);
            return adapterEntry.Adapter;
        }

        _logger.LogDebug("Creating new adapter for session {SessionId}", sessionId);
        var adapter = await createFunc(sessionId);

        _adapters[sessionId] = (adapter, DateTime.UtcNow);
        _logger.LogInformation(
            "Created and stored new adapter for session {SessionId}. Total adapters: {Count}",
            sessionId,
            _adapters.Count
        );

        return adapter;
    }

    /// <summary>
    /// Updates the last active timestamp for an adapter
    /// </summary>
    public void MarkAdapterAsActive(string sessionId)
    {
        if (_adapters.TryGetValue(sessionId, out var adapterEntry))
        {
            _adapters[sessionId] = (adapterEntry.Adapter, DateTime.UtcNow);
            _logger.LogDebug("Updated last active time for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Removes an adapter and properly disposes it
    /// </summary>
    public async Task RemoveAdapterAsync(string sessionId)
    {
        if (_adapters.TryRemove(sessionId, out var adapterEntry))
        {
            _logger.LogInformation("Removed adapter for session {SessionId}", sessionId);

            try
            {
                if (adapterEntry.Adapter is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (adapterEntry.Adapter is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            finally
            {
                _chatFactory.ReleaseSessionResources(sessionId);
            }
        }
    }

    public bool TryGetAdapter(string sessionId, out ISignalRChatAdapter adapter)
    {
        if (_adapters.TryGetValue(sessionId, out var entry))
        {
            adapter = entry.Adapter;
            return true;
        }

        adapter = null!;
        return false;
    }

    /// <summary>
    /// Gets all active session IDs
    /// </summary>
    public IEnumerable<string> GetActiveSessions() => _adapters.Keys;

    /// <summary>
    /// Starts the cleanup timer when the application starts
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Chat adapter cleanup service is starting");
        _cleanupTimer = new Timer(
            CleanupInactiveAdapters,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the cleanup timer when the application stops
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Chat adapter cleanup service is stopping");
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Timer callback to clean up inactive adapters
    /// </summary>
    private void CleanupInactiveAdapters(object? state)
    {
        _logger.LogDebug("Running adapter cleanup task");

        var now = DateTime.UtcNow;
        var inactiveSessions = _adapters
            .Where(kvp => now - kvp.Value.LastActive > _inactivityThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in inactiveSessions)
        {
            _logger.LogInformation(
                "Cleaning up inactive adapter for session {SessionId}",
                sessionId
            );
            _ = RemoveAdapterAsync(sessionId);
        }

        _logger.LogInformation(
            "Cleanup complete. Removed {Count} inactive adapters. {RemainingCount} adapters still active.",
            inactiveSessions.Count,
            _adapters.Count
        );
    }

    /// <summary>
    /// Disposes the cleanup timer
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
