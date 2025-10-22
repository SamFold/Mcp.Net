using System.Buffers;
using System.Text;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Transport.Stdio;

/// <summary>
/// Transport implementation for standard input/output streams
/// using high-performance System.IO.Pipelines
/// </summary>
public class StdioTransport : ServerMessageTransportBase
{
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private Task? _readTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class
    /// </summary>
    public StdioTransport(Stream? input = null, Stream? output = null)
        : base(new JsonRpcMessageParser(), NullLogger<StdioTransport>.Instance)
    {
        var inputStream = input ?? Console.OpenStandardInput();
        var outputStream = output ?? Console.OpenStandardOutput();

        _reader = PipeReader.Create(inputStream);
        _writer = PipeWriter.Create(outputStream);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class with a logger
    /// </summary>
    public StdioTransport(Stream input, Stream output, ILogger<StdioTransport> logger)
        : base(new JsonRpcMessageParser(), logger)
    {
        _reader = PipeReader.Create(input);
        _writer = PipeWriter.Create(output);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class with full dependency injection
    /// </summary>
    public StdioTransport(
        Stream input,
        Stream output,
        IMessageParser parser,
        ILogger<StdioTransport> logger
    )
        : base(parser, logger)
    {
        _reader = PipeReader.Create(input);
        _writer = PipeWriter.Create(output);
    }

    /// <inheritdoc />
    /// <remarks>Stdio initialisation simply kicks off the background read loop; the transport is fully duplex once this returns.</remarks>
    public override Task StartAsync()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException(
                "StdioTransport already started! If using Server class, note that connect() calls start() automatically."
            );
        }

        IsStarted = true;
        _readTask = ProcessMessagesAsync();
        Logger.LogDebug("StdioTransport started");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Continuously reads newline-delimited JSON-RPC messages from the input pipe.
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        try
        {
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                ReadResult result = await _reader.ReadAsync(CancellationTokenSource.Token);
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
                        if (string.IsNullOrWhiteSpace(message))
                        {
                            continue;
                        }

                        // Trim a trailing carriage-return so both LF and CRLF are accepted
                        if (message[^1] == '\r')
                        {
                            message = message[..^1];
                        }

                        ProcessJsonRpcMessage(message);
                    }
                }
                finally
                {
                    _reader.AdvanceTo(consumed, examined);
                }

                if (result.IsCompleted)
                {
                    if (buffer.Length > 0)
                    {
                        Logger.LogWarning(
                            "Terminating stdio transport with {ByteCount} unprocessed bytes (missing newline)",
                            buffer.Length
                        );
                    }

                    Logger.LogInformation("End of input stream detected, closing stdio transport");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("StdioTransport read operation cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reading from stdio");
            RaiseOnError(ex);
        }
        finally
        {
            await _reader.CompleteAsync();
            Logger.LogInformation("Stdio read loop terminated");
        }
    }

    /// <summary>
    /// Attempts to slice the buffer up to the next newline character.
    /// </summary>
    private static bool TryReadLine(
        ref ReadOnlySequence<byte> buffer,
        out ReadOnlySequence<byte> line
    )
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

    /// <inheritdoc />
    public override async Task SendAsync(JsonRpcResponseMessage message)
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

            string json = SerializeMessage(message);
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await WriteRawAsync(data);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending message");
            RaiseOnError(ex);
            throw;
        }
    }

    /// <summary>
    /// Writes raw data to the output pipe.
    /// </summary>
    protected override async Task WriteRawAsync(byte[] data)
    {
        await _writer.WriteAsync(data, CancellationTokenSource.Token);
        await _writer.FlushAsync(CancellationTokenSource.Token);
    }

    /// <inheritdoc />
    protected override async Task OnClosingAsync()
    {
        await _writer.CompleteAsync();
        await base.OnClosingAsync();
    }
}
