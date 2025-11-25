using System;
using System.Threading;

namespace Mcp.Net.Server.Services;

/// <summary>
/// Provides access to the ambient tool invocation context, including the active session id.
/// </summary>
public interface IToolInvocationContextAccessor
{
    /// <summary>
    /// Gets the session id associated with the active tool invocation, if any.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// Pushes a session id into the ambient context for the lifetime of the returned scope.
    /// </summary>
    /// <param name="sessionId">The session id to associate with the current tool invocation.</param>
    IDisposable Push(string sessionId);
}

/// <summary>
/// Default implementation that stores the context in an <see cref="AsyncLocal{T}"/>.
/// </summary>
public sealed class ToolInvocationContextAccessor : IToolInvocationContextAccessor
{
    private readonly AsyncLocal<string?> _current = new();

    public string? SessionId => _current.Value;

    public IDisposable Push(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        var previous = _current.Value;
        _current.Value = sessionId;
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ToolInvocationContextAccessor _accessor;
        private readonly string? _previous;
        private bool _disposed;

        public Scope(ToolInvocationContextAccessor accessor, string? previous)
        {
            _accessor = accessor;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _accessor._current.Value = _previous;
            _disposed = true;
        }
    }
}
