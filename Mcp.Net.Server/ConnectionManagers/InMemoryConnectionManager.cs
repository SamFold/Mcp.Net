using System.Collections.Concurrent;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Server.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.ConnectionManagers;

/// <summary>
/// In-memory implementation of IConnectionManager
/// Suitable for single-instance deployments
/// </summary>
public class InMemoryConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly ILogger<InMemoryConnectionManager> _logger;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _connectionTimeout;

    /// <summary>
    /// Tracks connection information including last activity time
    /// </summary>
    private class ConnectionInfo
    {
        public ITransport Transport { get; }
        public DateTime LastActivity { get; set; }

        public ConnectionInfo(ITransport transport)
        {
            Transport = transport;
            LastActivity = DateTime.UtcNow;
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryConnectionManager"/> class
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    /// <param name="connectionTimeout">Optional timeout for stale connections</param>
    public InMemoryConnectionManager(
        ILoggerFactory loggerFactory,
        TimeSpan? connectionTimeout = null
    )
    {
        _logger =
            loggerFactory?.CreateLogger<InMemoryConnectionManager>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromMinutes(30);

        // Create a timer to periodically check for stale connections
        _cleanupTimer = new Timer(
            CleanupStaleConnections,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );
    }

    /// <inheritdoc />
    public Task<ITransport?> GetTransportAsync(string sessionId)
    {
        if (_connections.TryGetValue(sessionId, out var connectionInfo))
        {
            // Update the last activity time
            connectionInfo.UpdateActivity();
            return Task.FromResult<ITransport?>(connectionInfo.Transport);
        }

        _logger.LogWarning("Transport not found for session ID: {SessionId}", sessionId);
        return Task.FromResult<ITransport?>(null);
    }

    /// <inheritdoc />
    public Task RegisterTransportAsync(string sessionId, ITransport transport)
    {
        var connectionInfo = new ConnectionInfo(transport);
        if (_connections.TryGetValue(sessionId, out var existingConnection))
        {
            _logger.LogInformation(
                "Replacing existing transport for session ID: {SessionId}. Old transport: {OldTransportId}, new transport: {NewTransportId}",
                sessionId,
                existingConnection.Transport.Id(),
                transport.Id()
            );
        }

        _connections[sessionId] = connectionInfo;

        _logger.LogInformation("Registered transport with session ID: {SessionId}", sessionId);

        // Remove the transport when it closes
        transport.OnClose += () =>
        {
            _ = RemoveTransportAsync(sessionId, transport);
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveTransportAsync(string sessionId)
    {
        bool result = _connections.TryRemove(sessionId, out _);

        if (result)
        {
            _logger.LogDebug("Removed transport with session ID: {SessionId}", sessionId);
        }

        return Task.FromResult(result);
    }

    internal Task<bool> RemoveTransportAsync(string sessionId, ITransport transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(transport);

        if (
            !_connections.TryGetValue(sessionId, out var currentConnection)
            || !ReferenceEquals(currentConnection.Transport, transport)
        )
        {
            _logger.LogDebug(
                "Skipping transport removal for session ID {SessionId} because a different transport is registered.",
                sessionId
            );
            return Task.FromResult(false);
        }

        bool result = _connections.TryRemove(
            new KeyValuePair<string, ConnectionInfo>(sessionId, currentConnection)
        );

        if (result)
        {
            _logger.LogDebug(
                "Removed transport with session ID: {SessionId} and transport ID: {TransportId}",
                sessionId,
                transport.Id()
            );
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task CloseAllConnectionsAsync()
    {
        _logger.LogInformation("Closing all connections...");
        _cleanupTimer.Dispose();

        // Create a copy of the connections to avoid enumeration issues
        var connectionsCopy = _connections.ToArray();

        // Close each transport
        var closeTasks = connectionsCopy
            .Select(async kvp =>
            {
                try
                {
                    await kvp.Value.Transport.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing transport: {SessionId}", kvp.Key);
                }
            })
            .ToArray();

        // Wait for all connections to close with a timeout
        await Task.WhenAll(closeTasks).WaitAsync(TimeSpan.FromSeconds(10));

        // Clear the connections dictionary
        _connections.Clear();

        _logger.LogInformation("All connections closed");
    }

    /// <summary>
    /// Periodically checks for and removes stale connections
    /// </summary>
    private void CleanupStaleConnections(object? state)
    {
        try
        {
            _logger.LogDebug("Checking for stale connections...");

            var now = DateTime.UtcNow;
            var staleThreshold = now.Subtract(_connectionTimeout);

            // Find stale connections
            var staleConnections = _connections
                .Where(kvp => kvp.Value.LastActivity < staleThreshold)
                .ToList();

            foreach (var conn in staleConnections)
            {
                _logger.LogInformation("Removing stale connection: {SessionId}", conn.Key);
                if (
                    RemoveTransportAsync(conn.Key, conn.Value.Transport)
                        .GetAwaiter()
                        .GetResult()
                )
                {
                    try
                    {
                        // Close the transport
                        conn.Value.Transport.CloseAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error closing stale connection");
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Skipped stale cleanup for session {SessionId} because the transport was replaced.",
                        conn.Key
                    );
                }
            }

            _logger.LogDebug(
                "Active connections: {Count}, Removed stale: {RemovedCount}",
                _connections.Count,
                staleConnections.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up stale connections");
        }
    }
}
