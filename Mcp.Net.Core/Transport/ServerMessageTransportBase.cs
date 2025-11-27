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
    /// Transports are outbound-only; inbound message dispatch is handled by host components.
    /// </summary>
    public abstract class ServerMessageTransportBase : MessageTransportBase, IServerTransport
    {
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
