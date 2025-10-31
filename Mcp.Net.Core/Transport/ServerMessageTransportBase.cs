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
    /// Base class for server message-based transports.
    /// </summary>
    public abstract class ServerMessageTransportBase : MessageTransportBase, IServerTransport
    {
        /// <inheritdoc />
        public event Action<JsonRpcRequestMessage>? OnRequest;

    /// <inheritdoc />
    public event Action<JsonRpcNotificationMessage>? OnNotification;

    /// <inheritdoc />
    public event Action<JsonRpcResponseMessage>? OnResponse;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerMessageTransportBase"/> class.
        /// </summary>
        /// <param name="messageParser">Parser for JSON-RPC messages</param>
        /// <param name="logger">Logger for transport operations</param>
        /// <param name="id"></param>
        protected ServerMessageTransportBase(IMessageParser messageParser, ILogger logger, string id)
            : base(messageParser, logger, id)
        {
        }

        /// <summary>
        /// Raises the OnRequest event with the specified request message.
        /// </summary>
        /// <param name="request">The JSON-RPC request message.</param>
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
        /// Raises the OnNotification event with the specified notification message.
        /// </summary>
        /// <param name="notification">The JSON-RPC notification message.</param>
        protected void RaiseOnNotification(JsonRpcNotificationMessage notification)
        {
            Logger.LogDebug("Processing notification: Method={Method}", notification.Method);
            OnNotification?.Invoke(notification);
        }

        /// <summary>
        /// Raises the OnResponse event with the specified response message.
        /// </summary>
        /// <param name="response">The JSON-RPC response message.</param>
        protected void RaiseOnResponse(JsonRpcResponseMessage response)
        {
            Logger.LogDebug(
                "Processing response: Id={Id}, HasError={HasError}",
                response.Id,
                response.Error != null
            );
            OnResponse?.Invoke(response);
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
                else if (MessageParser.IsJsonRpcResponse(message))
                {
                    var responseMessage = MessageParser.DeserializeResponse(message);

                    Logger.LogDebug(
                        "Deserialized JSON-RPC response: Id={Id}, HasError={HasError}",
                        responseMessage.Id,
                        responseMessage.Error != null
                    );

                    RaiseOnResponse(responseMessage);
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

        /// <inheritdoc />
        public virtual async Task SendAsync(JsonRpcResponseMessage message)
        {
            if (IsClosed)
            {
                throw new InvalidOperationException("Transport is closed");
            }

            try
            {
                Logger.LogDebug(
                    "Sending response: ID={Id}, HasResult={HasResult}, HasError={HasError}",
                    message.Id,
                    message.Result != null,
                    message.Error != null
                );

                await WriteMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sending message");
                RaiseOnError(ex);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual async Task SendRequestAsync(JsonRpcRequestMessage message)
        {
            if (IsClosed)
            {
                throw new InvalidOperationException("Transport is closed");
            }

            try
            {
                Logger.LogDebug(
                    "Sending request: Method={Method}, Id={Id}",
                    message.Method,
                    message.Id
                );

                await WriteMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sending request message");
                RaiseOnError(ex);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual async Task SendNotificationAsync(JsonRpcNotificationMessage message)
        {
            if (IsClosed)
            {
                throw new InvalidOperationException("Transport is closed");
            }

            try
            {
                Logger.LogDebug("Sending notification: Method={Method}", message.Method);
                await WriteMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sending notification message");
                RaiseOnError(ex);
                throw;
            }
        }

    }
}
