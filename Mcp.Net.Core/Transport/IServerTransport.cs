using System;
using System.Threading.Tasks;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;

namespace Mcp.Net.Core.Transport;

/// <summary>
/// Interface for server-specific transport operations.
/// Transports are outbound-only; inbound message dispatch is handled by host components
/// (e.g., StdioIngressHost, SseJsonRpcProcessor) that call McpServer entry points directly.
/// </summary>
public interface IServerTransport : ITransport
{
    /// <summary>
    /// Sends a JSON-RPC response to the client
    /// </summary>
    /// <param name="message">The response to send</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    Task SendAsync(JsonRpcResponseMessage message);

    /// <summary>
    /// Sends a JSON-RPC request to the client.
    /// </summary>
    /// <param name="message">The request to send.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    Task SendRequestAsync(JsonRpcRequestMessage message);

    /// <summary>
    /// Sends a JSON-RPC notification to the client.
    /// </summary>
    /// <param name="message">The notification to send.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    Task SendNotificationAsync(JsonRpcNotificationMessage message);
}
