using System;
using Mcp.Net.Core.Transport;

/// <summary>
/// Represents the active transport context for a server-initiated operation
/// and provides a disposable scope that restores the previous context when disposed.
/// </summary>
internal sealed class TransportScope : IDisposable
{
    private readonly Action<TransportScope?> _restore;
    private bool _disposed;

    internal TransportScope(IServerTransport transport, TransportScope? previous, Action<TransportScope?> restore)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        SessionId = transport.Id();
        Previous = previous;
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
    }

    internal IServerTransport Transport { get; }

    internal string SessionId { get; }

    internal TransportScope? Previous { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _restore(Previous);
        _disposed = true;
    }
}
