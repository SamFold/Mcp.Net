using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Core.Transport
{
    /// <summary>
    /// Base class for client message-based transports.
    /// </summary>
    public abstract class ClientMessageTransportBase : MessageTransportBase, IClientTransport
    {
        /// <inheritdoc />
        public event Action<JsonRpcRequestMessage>? OnRequest;

        /// <inheritdoc />
        public event Action<JsonRpcResponseMessage>? OnResponse;

        /// <summary>
        /// Event triggered when a notification is received.
        /// </summary>
        public event Action<JsonRpcNotificationMessage>? OnNotification;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientMessageTransportBase"/> class.
        /// </summary>
        /// <param name="messageParser">Parser for JSON-RPC messages</param>
        /// <param name="logger">Logger for transport operations</param>
        /// <param name="id"></param>
        protected ClientMessageTransportBase(IMessageParser messageParser, ILogger logger, string id)
            : base(messageParser, logger, id) { }

        /// <summary>
        /// Processes a JSON-RPC request message.
        /// </summary>
        /// <param name="request">The request message to process.</param>
        protected virtual void ProcessRequest(JsonRpcRequestMessage request)
        {
            Logger.LogDebug(
                "Received request from server: method={Method}, id={Id}",
                request.Method,
                request.Id
            );

            OnRequest?.Invoke(request);
        }

        /// <summary>
        /// Process a JSON-RPC response message.
        /// </summary>
        /// <param name="response">The response message to process.</param>
        protected virtual void ProcessResponse(JsonRpcResponseMessage response)
        {
            Logger.LogDebug(
                "Received response: id={Id}, has error: {HasError}",
                response.Id,
                response.Error != null
            );

            OnResponse?.Invoke(response);
        }

        /// <summary>
        /// Process a JSON-RPC notification message.
        /// </summary>
        /// <param name="notification">The notification message to process.</param>
        protected virtual void ProcessNotification(JsonRpcNotificationMessage notification)
        {
            Logger.LogDebug("Received notification: method={Method}", notification.Method);
            OnNotification?.Invoke(notification);
        }

        /// <summary>
        /// Processes a JSON-RPC message and dispatches it to the appropriate handler.
        /// </summary>
        /// <param name="message">The JSON-RPC message to process.</param>
        protected void ProcessJsonRpcMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using var _ = JsonDocument.Parse(message);
            }
            catch (JsonException ex)
            {
                string truncated =
                    message.Length > 100 ? message.Substring(0, 97) + "..." : message;
                Logger.LogError(ex, "Invalid JSON message: {TruncatedMessage}", truncated);
                RaiseOnError(new Exception($"Invalid JSON message: {ex.Message}", ex));
                return;
            }

            try
            {
                // For client transports, we mostly expect responses
                if (MessageParser.IsJsonRpcRequest(message))
                {
                    var requestMessage = MessageParser.DeserializeRequest(message);
                    ProcessRequest(requestMessage);
                }
                else if (MessageParser.IsJsonRpcResponse(message))
                {
                    var responseMessage = MessageParser.DeserializeResponse(message);
                    ProcessResponse(responseMessage);
                }
                else if (MessageParser.IsJsonRpcNotification(message))
                {
                    var notificationMessage = MessageParser.DeserializeNotification(message);
                    ProcessNotification(notificationMessage);
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
        public virtual async Task SendResponseAsync(JsonRpcResponseMessage message)
        {
            if (IsClosed)
            {
                throw new InvalidOperationException("Transport is closed");
            }

            try
            {
                Logger.LogDebug(
                    "Sending response: ID={Id}, HasError={HasError}",
                    message.Id,
                    message.Error != null
                );
                await WriteMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sending response to server");
                RaiseOnError(ex);
                throw;
            }
        }
    }
}
