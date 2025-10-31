using System;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;

namespace Mcp.Net.Core.Interfaces;

/// <summary>
/// Base interface for JSON-RPC transport with common events and lifecycle methods
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Event triggered when an error occurs
    /// </summary>
    event Action<Exception>? OnError;

    /// <summary>
    /// Event triggered when the transport is closed
    /// </summary>
    event Action? OnClose;

    /// <summary>
    /// Starts the transport
    /// </summary>
    /// <returns>A task representing the asynchronous start operation</returns>
    Task StartAsync();

    /// <summary>
    /// Closes the transport
    /// </summary>
    /// <returns>A task representing the asynchronous close operation</returns>
    Task CloseAsync();

    string Id();
}
