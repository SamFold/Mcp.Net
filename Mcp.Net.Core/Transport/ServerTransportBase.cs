using System.Text.Json;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Core.Transport;

/// <summary>
/// Base class for server transports that implements common functionality
/// </summary>
public abstract class ServerTransportBase : TransportBase, IServerTransport
{
    /// <inheritdoc />
    public event Action<JsonRpcRequestMessage>? OnRequest;

    /// <inheritdoc />
    public event Action<JsonRpcNotificationMessage>? OnNotification;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTransportBase"/> class
    /// </summary>
    /// <param name="messageParser">Parser for JSON-RPC messages</param>
    /// <param name="logger">Logger for transport operations</param>
    protected ServerTransportBase(IMessageParser messageParser, ILogger logger)
        : base(messageParser, logger)
    {
    }

    /// <inheritdoc />
    public abstract Task SendAsync(JsonRpcResponseMessage message);

    /// <summary>
    /// Raises the OnRequest event with the specified request message
    /// </summary>
    /// <param name="request">The JSON-RPC request message</param>
    protected void RaiseOnRequest(JsonRpcRequestMessage request)
    {
        Logger.LogDebug(
            "Processing request: Method={Method}, Id={Id}",
            request.Method,
            request.Id
        );
        OnRequest?.Invoke(request);
    }

    /// <summary>
    /// Raises the OnNotification event with the specified notification message
    /// </summary>
    /// <param name="notification">The JSON-RPC notification message</param>
    protected void RaiseOnNotification(JsonRpcNotificationMessage notification)
    {
        Logger.LogDebug("Processing notification: Method={Method}", notification.Method);
        OnNotification?.Invoke(notification);
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
            if (MessageParser.IsJsonRpcRequest(message))
            {
                var requestMessage = MessageParser.DeserializeRequest(message);

                Logger.LogDebug(
                    "Deserialized JSON-RPC request: Method={Method}, Id={Id}",
                    requestMessage.Method,
                    requestMessage.Id
                );

                RaiseOnRequest(requestMessage);
            }
            else if (MessageParser.IsJsonRpcNotification(message))
            {
                var notificationMessage = MessageParser.DeserializeNotification(message);

                Logger.LogDebug(
                    "Deserialized JSON-RPC notification: Method={Method}",
                    notificationMessage.Method
                );

                RaiseOnNotification(notificationMessage);
            }
            else
            {
                Logger.LogWarning(
                    "Received message that is neither a request nor notification: {Message}",
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
}
