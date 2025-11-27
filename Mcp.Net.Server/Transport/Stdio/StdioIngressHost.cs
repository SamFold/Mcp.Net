using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Transport.Stdio;

/// <summary>
/// Reads newline-delimited JSON-RPC messages from stdio and routes them to the server entry points.
/// </summary>
public sealed class StdioIngressHost
{
    private readonly McpServer _server;
    private readonly StdioTransport _transport;
    private readonly ILogger _logger;
    private readonly JsonRpcMessageParser _parser = new();

    public StdioIngressHost(McpServer server, StdioTransport transport, ILogger logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RunAsync(CancellationToken cancellationToken) => ProcessMessagesAsync(cancellationToken);

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(_transport.InputStream);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.End;

                try
                {
                    while (TryReadLine(ref buffer, out var lineSequence))
                    {
                        consumed = buffer.Start;
                        if (lineSequence.Length == 0)
                        {
                            continue;
                        }

                        string message = Encoding.UTF8.GetString(lineSequence.ToArray());
                        await ProcessLineAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    reader.AdvanceTo(consumed, examined);
                }

                if (result.IsCompleted)
                {
                    if (buffer.Length > 0)
                    {
                        _logger.LogWarning(
                            "Terminating stdio ingress with {ByteCount} unprocessed bytes (missing newline)",
                            buffer.Length
                        );
                    }

                    _logger.LogInformation("End of input stream detected, closing stdio transport");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Stdio ingress read cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from stdio ingress");
        }
        finally
        {
            await reader.CompleteAsync();
            _logger.LogInformation("Stdio ingress loop terminated");

            try
            {
                await _transport.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallow close errors to avoid masking the read error/EOF.
            }
        }
    }

    private async Task ProcessLineAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // Trim a trailing carriage-return so both LF and CRLF are accepted
        if (message[^1] == '\r')
        {
            message = message[..^1];
        }

        try
        {
            if (_parser.IsJsonRpcRequest(message))
            {
                var requestMessage = _parser.DeserializeRequest(message);
                var context = new ServerRequestContext(
                    _transport.Id(),
                    _transport.Id(),
                    requestMessage,
                    cancellationToken,
                    _transport.Metadata
                );

                _ = HandleRequestAsync(context);
                return;
            }

            if (_parser.IsJsonRpcNotification(message))
            {
                var notificationMessage = _parser.DeserializeNotification(message);
                var context = new ServerRequestContext(
                    _transport.Id(),
                    _transport.Id(),
                    new JsonRpcRequestMessage(
                        notificationMessage.JsonRpc,
                        string.Empty,
                        notificationMessage.Method,
                        notificationMessage.Params
                    ),
                    cancellationToken,
                    _transport.Metadata
                );

                _ = HandleNotificationAsync(context);
                return;
            }

            if (_parser.IsJsonRpcResponse(message))
            {
                var responseMessage = _parser.DeserializeResponse(message);
                await _server.HandleClientResponseAsync(_transport.Id(), responseMessage).ConfigureAwait(false);
                return;
            }

            _logger.LogWarning(
                "Received message that is neither a request nor notification: {Message}",
                message.Length > 100 ? message.Substring(0, 97) + "..." : message
            );
        }
        catch (JsonException ex)
        {
            string truncatedMessage =
                message.Length > 100 ? message.Substring(0, 97) + "..." : message;
            _logger.LogError(ex, "Invalid JSON message: {TruncatedMessage}", truncatedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stdio message");
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = buffer.PositionOf((byte)'\n');
        if (position == null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        var next = buffer.GetPosition(1, position.Value);
        buffer = buffer.Slice(next);
        return true;
    }

    private async Task HandleRequestAsync(ServerRequestContext context)
    {
        try
        {
            var response = await _server.HandleRequestAsync(context).ConfigureAwait(false);
            await _transport.SendAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling request {Method} on stdio ingress for transport {TransportId}",
                context.Request.Method,
                _transport.Id()
            );
        }
    }

    private async Task HandleNotificationAsync(ServerRequestContext context)
    {
        try
        {
            await _server.HandleNotificationAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling notification {Method} on stdio ingress for transport {TransportId}",
                context.Request.Method,
                _transport.Id()
            );
        }
    }
}
