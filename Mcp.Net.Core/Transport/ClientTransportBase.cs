using System.Collections.Concurrent;
using System.Text.Json;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Core.Transport;

/// <summary>
/// Base class for client transports that implements common functionality
/// </summary>
public abstract class ClientTransportBase : TransportBase, IClientTransport
{
    /// <summary>
    /// Dictionary of pending requests, keyed by request ID
    /// </summary>
    protected readonly ConcurrentDictionary<string, TaskCompletionSource<object>> PendingRequests =
        new();

    /// <inheritdoc />
    public event Action<JsonRpcResponseMessage>? OnResponse;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientTransportBase"/> class
    /// </summary>
    /// <param name="messageParser">Parser for JSON-RPC messages</param>
    /// <param name="logger">Logger for transport operations</param>
    protected ClientTransportBase(IMessageParser messageParser, ILogger logger)
        : base(messageParser, logger) { }

    /// <summary>
    /// Processes a JSON-RPC response message
    /// </summary>
    /// <param name="response">The response message to process</param>
    protected virtual void ProcessResponse(JsonRpcResponseMessage response)
    {
        Logger.LogDebug(
            "Received response: id={Id}, has error: {HasError}",
            response.Id,
            response.Error != null
        );

        // Raise the OnResponse event
        OnResponse?.Invoke(response);

        // Complete the pending request if this is a response with a matching ID
        if (PendingRequests.TryRemove(response.Id, out var tcs))
        {
            if (response.Error != null)
            {
                Logger.LogError(
                    "Request {Id} failed: {ErrorMessage}",
                    response.Id,
                    response.Error.Message
                );
                tcs.SetException(new Exception($"RPC Error: {response.Error.Message}"));
            }
            else if (response.Result != null)
            {
                Logger.LogDebug("Request {Id} succeeded", response.Id);
                tcs.SetResult(response.Result);
            }
            else
            {
                Logger.LogDebug("Request {Id} completed with no result", response.Id);
                tcs.SetResult(new { });
            }
        }
        else
        {
            Logger.LogWarning("Received response for unknown request: {Id}", response.Id);
        }
    }

    /// <summary>
    /// Processes a JSON-RPC message and dispatches it to the appropriate handler
    /// </summary>
    /// <param name="message">The JSON-RPC message to process</param>
    protected void ProcessJsonRpcMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            // For client transports, we mostly expect responses
            if (MessageParser.IsJsonRpcResponse(message))
            {
                var responseMessage = MessageParser.DeserializeResponse(message);
                ProcessResponse(responseMessage);
            }
            else
            {
                Logger.LogWarning(
                    "Received unexpected message format: {Message}",
                    message.Length > 100 ? message.Substring(0, 97) + "..." : message
                );
            }
        }
        catch (JsonException ex)
        {
            string truncatedMessage =
                message.Length > 100 ? message.Substring(0, 97) + "..." : message;
            Logger.LogError(ex, "Invalid JSON message: {TruncatedMessage}", truncatedMessage);
            RaiseOnError(new Exception($"Invalid JSON message: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing message");
            RaiseOnError(ex);
        }
    }

    /// <inheritdoc />
    public abstract Task<object> SendRequestAsync(string method, object? parameters = null);

    /// <inheritdoc />
    public abstract Task SendNotificationAsync(string method, object? parameters = null);

    /// <inheritdoc />
    protected override Task OnClosingAsync()
    {
        // Complete all pending requests with an error when closing
        foreach (var kvp in PendingRequests)
        {
            kvp.Value.TrySetException(new Exception("Transport closed"));
        }

        PendingRequests.Clear();
        return Task.CompletedTask;
    }
}
