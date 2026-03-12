using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Models;
using Mcp.Net.Client.Interfaces;

namespace Mcp.Net.WebUi.Sessions;

/// <summary>
/// Holds a live ChatSession together with the per-session resources it depends on.
/// Disposing tears down owned resources and unhooks events.
/// </summary>
public sealed class ManagedSession : IAsyncDisposable
{
    private readonly List<IDisposable> _eventSubscriptions = new();
    private int _disposed;

    public ManagedSession(
        string id,
        ChatSession chatSession,
        ChatSessionMetadata metadata,
        IMcpClient? mcpClient)
    {
        Id = id;
        ChatSession = chatSession;
        Metadata = metadata;
        McpClient = mcpClient;
        LastActiveAt = DateTime.UtcNow;
    }

    public string Id { get; }
    public ChatSession ChatSession { get; }
    public ChatSessionMetadata Metadata { get; }
    public IMcpClient? McpClient { get; }
    public DateTime LastActiveAt { get; private set; }

    public void Touch() => LastActiveAt = DateTime.UtcNow;

    /// <summary>
    /// Track a disposable event subscription for cleanup.
    /// </summary>
    public void TrackSubscription(IDisposable subscription) =>
        _eventSubscriptions.Add(subscription);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var sub in _eventSubscriptions)
            sub.Dispose();
        _eventSubscriptions.Clear();

        if (McpClient is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (McpClient is IDisposable disposable)
            disposable.Dispose();
    }
}
