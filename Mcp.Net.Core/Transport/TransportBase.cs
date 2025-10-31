using System.Text.Json;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Core.Transport
{
    /// <summary>
    /// Base abstract class for MCP transport implementations.
    /// Provides common functionality for all transport implementations.
    /// </summary>
    public abstract class TransportBase : ITransport, IDisposable
    {
        protected readonly IMessageParser MessageParser;
        protected readonly ILogger Logger;
        protected readonly CancellationTokenSource CancellationTokenSource = new();
        protected readonly JsonSerializerOptions SerializerOptions;
        protected bool IsStarted = false;
        protected bool IsClosed = false;
        private readonly string _id;


        /// <inheritdoc />
        public event Action<Exception>? OnError;

        /// <inheritdoc />
        public event Action? OnClose;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransportBase"/> class.
        /// </summary>
        /// <param name="messageParser">Parser for JSON-RPC messages</param>
        /// <param name="logger">Logger for transport operations</param>
        /// <param name="id"></param>
        protected TransportBase(IMessageParser messageParser, ILogger logger, string id)
        {
            MessageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _id = id;
            // Standard serializer options for JSON-RPC messages
            SerializerOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System
                    .Text
                    .Json
                    .Serialization
                    .JsonIgnoreCondition
                    .WhenWritingNull,
            };
        }

        /// <inheritdoc />
        public abstract Task StartAsync();

        /// <inheritdoc />
        public virtual async Task CloseAsync()
        {
            if (IsClosed)
            {
                return;
            }

            Logger.LogInformation("Closing transport");
            IsClosed = true;
            CancellationTokenSource.Cancel();

            await OnClosingAsync();

            OnClose?.Invoke();
        }

        /// <summary>
        /// Performs transport-specific cleanup when closing.
        /// Override this method in derived classes to implement transport-specific closing operations.
        /// </summary>
        protected virtual Task OnClosingAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Raises the OnError event with the specified exception.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        protected void RaiseOnError(Exception exception)
        {
            Logger.LogError(exception, "Transport error");
            OnError?.Invoke(exception);
        }

        /// <summary>
        /// Serializes a JSON-RPC message to a string.
        /// </summary>
        /// <param name="message">The message to serialize.</param>
        /// <returns>The serialized message.</returns>
        protected string SerializeMessage(object message)
        {
            return JsonSerializer.Serialize(message, SerializerOptions);
        }

        /// <summary>
        /// Disposes the transport resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (!IsClosed)
            {
                try
                {
                    CloseAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error closing transport during disposal");
                }
            }

            CancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }

        public string Id()
        {
            return _id;
        }
    }
}