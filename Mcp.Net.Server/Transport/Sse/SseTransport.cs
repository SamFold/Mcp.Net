using System.Diagnostics;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Logging;

namespace Mcp.Net.Server.Transport.Sse;

/// <summary>
/// Implements the MCP server transport over Server-Sent Events (SSE), including connection metadata and per-session metrics.
/// </summary>
public class SseTransport : TransportBase, IServerTransport
{
    // Cache the SSE data format for better performance
    private const string SSE_DATA_FORMAT = "data: {0}\n\n";
    private const string TRANSPORT_TYPE = "SSE";

    protected readonly IResponseWriter ResponseWriter;
    private bool _isStarted;
    private readonly Stopwatch _uptime;
    private int _messagesSent;
    private int _messagesReceived;
    private int _bytesReceived;
    private int _bytesSent;


    /// <summary>
    /// Gets the unique identifier for this transport session.
    /// </summary>
    public string SessionId => ResponseWriter.Id;

    /// <summary>
    /// Gets a value indicating whether this transport has been started.
    /// </summary>
    public new bool IsStarted => _isStarted;

    /// <summary>
    /// Gets the per-connection metadata captured during authentication or handshake.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SseTransport"/> class
    /// </summary>
    /// <param name="writer">Response writer for SSE</param>
    /// <param name="logger">Logger</param>
    /// <param name="parser">Optional message parser</param>
    public SseTransport(
        IResponseWriter writer,
        ILogger<SseTransport> logger,
        IMessageParser? parser = null
    )
        : base(parser ?? new JsonRpcMessageParser(), logger, writer.Id)
    {
        ResponseWriter = writer ?? throw new ArgumentNullException(nameof(writer));
        _uptime = Stopwatch.StartNew();
        _messagesSent = 0;
        _messagesReceived = 0;
        _bytesReceived = 0;
        _bytesSent = 0;

        // Log connection info
        using (logger.BeginConnectionScope(writer.Id))
        {
            // Set up SSE headers
            writer.SetHeader("Content-Type", "text/event-stream");
            writer.SetHeader("Cache-Control", "no-cache");
            writer.SetHeader("Connection", "keep-alive");
            writer.SetHeader("Mcp-Session-Id", SessionId);

            // Get client info and log connection
            var clientInfo = new Dictionary<string, string?>();
            foreach (var header in writer.GetRequestHeaders())
            {
                if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    clientInfo["UserAgent"] = header.Value;
                }
                else if (
                    header.Key.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase)
                )
                {
                    clientInfo["IP"] = header.Value;
                }
                else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                {
                    clientInfo["Referer"] = header.Value;
                }
            }

            if (writer.RemoteIpAddress != null)
            {
                clientInfo["RemoteIP"] = writer.RemoteIpAddress;
            }

            logger.LogConnectionEvent(SessionId, clientInfo, TRANSPORT_TYPE, true);
        }
    }

    /// <inheritdoc />
    /// <remarks>Marks the transport as started and logs connection startup.</remarks>
    public override async Task StartAsync()
    {
        if (_isStarted)
        {
            throw new InvalidOperationException("SSE transport already started");
        }

        using (Logger.BeginConnectionScope(SessionId))
        using (Logger.BeginTimingScope("SseTransportStartup"))
        {
            _isStarted = true;
            Logger.LogInformation("SSE transport started for session {SessionId}", SessionId);
            // Flush headers immediately so clients finish the SSE handshake before any events are sent.
            await FlushHeadersAsync();
            await SendHandshakeCommentAsync();
        }
    }

    private async Task FlushHeadersAsync()
    {
        try
        {
            await ResponseWriter.FlushAsync(CancellationTokenSource.Token);
            Logger.LogDebug("SSE headers flushed for session {SessionId}", SessionId);
        }
        catch (Exception ex)
        {
            Logger.LogTransportError(ex, SessionId, "FlushHeadersAsync", TRANSPORT_TYPE);
            throw;
        }
    }

    private async Task SendHandshakeCommentAsync()
    {
        try
        {
            await ResponseWriter.WriteAsync(":\n\n", CancellationTokenSource.Token);
            await ResponseWriter.FlushAsync(CancellationTokenSource.Token);
            Logger.LogDebug("SSE handshake comment sent for session {SessionId}", SessionId);
        }
        catch (Exception ex)
        {
            Logger.LogTransportError(ex, SessionId, "SendHandshakeCommentAsync", TRANSPORT_TYPE);
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>Serialises the response into SSE frames and tracks throughput in the transport metrics.</remarks>
    public async Task SendAsync(JsonRpcResponseMessage responseMessage)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        using (Logger.BeginConnectionScope(SessionId))
        using (Logger.BeginRequestScope(responseMessage.Id, "response"))
        using (Logger.BeginTimingScope($"SendResponse_{responseMessage.Id}"))
        {
            try
            {
                string serialized = SerializeMessage(responseMessage);
                int payloadSize = serialized.Length;

                // Log basic message info at debug level
                Logger.LogMessageSent(
                    SessionId,
                    "Response",
                    responseMessage.Id,
                    payloadSize,
                    TRANSPORT_TYPE
                );

                // Format as SSE data and send
                await SendDataAsync(serialized);

                // Update statistics
                _messagesSent++;
                _bytesSent += payloadSize;

                // Log detailed response info
                Logger.LogJsonRpcResponse(responseMessage, SessionId);
            }
            catch (Exception ex)
            {
                // Log detailed error with transport context
                Logger.LogTransportError(
                    ex,
                    SessionId,
                    "SendAsync",
                    TRANSPORT_TYPE,
                    responseMessage.Id
                );
                RaiseOnError(ex);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task SendRequestAsync(JsonRpcRequestMessage requestMessage)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        using (Logger.BeginConnectionScope(SessionId))
        using (Logger.BeginRequestScope(requestMessage.Id, requestMessage.Method))
        using (Logger.BeginTimingScope($"SendRequest_{requestMessage.Id}"))
        {
            try
            {
                string serialized = SerializeMessage(requestMessage);
                int payloadSize = serialized.Length;

                Logger.LogMessageSent(
                    SessionId,
                    "Request",
                    requestMessage.Id,
                    payloadSize,
                    TRANSPORT_TYPE
                );

                await SendDataAsync(serialized);

                _messagesSent++;
                _bytesSent += payloadSize;

                Logger.LogDebug(
                    "Sent JSON-RPC request: Method={Method}, Id={Id}",
                    requestMessage.Method,
                    requestMessage.Id
                );
            }
            catch (Exception ex)
            {
                Logger.LogTransportError(
                    ex,
                    SessionId,
                    "SendRequestAsync",
                    TRANSPORT_TYPE,
                    requestMessage.Id
                );
                RaiseOnError(ex);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task SendNotificationAsync(JsonRpcNotificationMessage notificationMessage)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        using (Logger.BeginConnectionScope(SessionId))
        using (Logger.BeginTimingScope($"SendNotification_{notificationMessage.Method}"))
        {
            try
            {
                string serialized = SerializeMessage(notificationMessage);
                int payloadSize = serialized.Length;

                Logger.LogMessageSent(
                    SessionId,
                    "Notification",
                    notificationMessage.Method,
                    payloadSize,
                    TRANSPORT_TYPE
                );

                await SendDataAsync(serialized);

                _messagesSent++;
                _bytesSent += payloadSize;

                Logger.LogDebug(
                    "Sent JSON-RPC notification: Method={Method}",
                    notificationMessage.Method
                );
            }
            catch (Exception ex)
            {
                Logger.LogTransportError(
                    ex,
                    SessionId,
                    "SendNotificationAsync",
                    TRANSPORT_TYPE
                );
                RaiseOnError(ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Sends data as an SSE data-only event.
    /// </summary>
    /// <param name="data">The data to send</param>
    private async Task SendDataAsync(string data)
    {
        string sseData = string.Format(SSE_DATA_FORMAT, data);
        await ResponseWriter.WriteAsync(sseData, CancellationTokenSource.Token);
        await ResponseWriter.FlushAsync(CancellationTokenSource.Token);
    }

    /// <summary>
    /// Emits the latest connection metrics through the logging extensions.
    /// </summary>
    private void LogMetrics()
    {
        var metrics = new Dictionary<string, object>
        {
            ["UptimeMs"] = _uptime.ElapsedMilliseconds,
            ["MessagesSent"] = _messagesSent,
            ["MessagesReceived"] = _messagesReceived,
            ["BytesSent"] = _bytesSent,
            ["BytesReceived"] = _bytesReceived,
            ["IsActive"] = !IsClosed && _isStarted,
        };

        Logger.LogTransportMetrics(SessionId, metrics, TRANSPORT_TYPE);
    }

    /// <inheritdoc/>
    protected override async Task OnClosingAsync()
    {
        using (Logger.BeginConnectionScope(SessionId))
        {
            try
            {
                LogMetrics();

                var clientInfo = new Dictionary<string, string?>();
                clientInfo["UptimeMs"] = _uptime.ElapsedMilliseconds.ToString();
                clientInfo["MessagesSent"] = _messagesSent.ToString();
                clientInfo["MessagesReceived"] = _messagesReceived.ToString();

                Logger.LogConnectionEvent(SessionId, clientInfo, TRANSPORT_TYPE, false);

                await ResponseWriter.CompleteAsync();
                await base.OnClosingAsync();
            }
            catch (Exception ex)
            {
                Logger.LogTransportError(ex, SessionId, "OnClosingAsync", TRANSPORT_TYPE);
                throw;
            }
        }
    }
}
