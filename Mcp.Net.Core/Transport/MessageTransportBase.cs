using System;
using System.Text;
using System.Threading.Tasks;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Core.Transport
{
    /// <summary>
    /// Base class for transports that involve reading and writing messages,
    /// such as Stdio or WebSocket transports.
    /// </summary>
    public abstract class MessageTransportBase : TransportBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessageTransportBase"/> class.
        /// </summary>
        /// <param name="messageParser">Parser for JSON-RPC messages</param>
        /// <param name="logger">Logger for transport operations</param>
        /// <param name="id"></param>
        protected MessageTransportBase(IMessageParser messageParser, ILogger logger, string id)
            : base(messageParser, logger, id) { }

        /// <summary>
        /// Writes raw data to the transport.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        protected abstract Task WriteRawAsync(byte[] data);

        /// <summary>
        /// Writes a JSON-RPC message to the transport.
        /// </summary>
        /// <param name="message">The message to write.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        protected async Task WriteMessageAsync(object message)
        {
            if (IsClosed)
            {
                throw new InvalidOperationException("Transport is closed");
            }

            string json = SerializeMessage(message);
            Logger.LogDebug("Sending message: {Json}", json);

            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await WriteRawAsync(data);
        }

        /// <summary>
        /// Processes a buffer containing one or more JSON-RPC messages.
        /// </summary>
        /// <param name="buffer">The buffer to process.</param>
        /// <param name="bytesConsumed">The number of bytes consumed from the buffer.</param>
        protected void ProcessBuffer(ReadOnlySpan<char> buffer, out int bytesConsumed)
        {
            bytesConsumed = 0;
            int position = 0;

            while (position < buffer.Length)
            {
                ReadOnlySpan<char> remaining = buffer.Slice(position);

                // Try to parse a message from the buffer
                if (MessageParser.TryParseMessage(remaining, out string message, out int consumed))
                {
                    try
                    {
                        // Each subclass will provide its own implementation of processing messages
                        // Client transports will process responses
                        // Server transports will process requests and notifications

                        // Move position forward by the characters consumed
                        position += consumed;
                        bytesConsumed = position;
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue processing
                        Logger.LogError(ex, "Error processing message");

                        // Move position forward by the characters consumed even on error
                        position += consumed;
                        bytesConsumed = position;
                    }
                }
                else
                {
                    // We couldn't parse a complete message, so stop and wait for more data
                    break;
                }
            }
        }
    }
}