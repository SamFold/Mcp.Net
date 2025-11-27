using System.IO;
using System.IO.Pipelines;
using System.Text;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Server.Transport.Stdio;

/// <summary>
/// Outbound-only stdio transport; ingress is handled by a host-level reader.
/// </summary>
public class StdioTransport : ServerMessageTransportBase
{
    private readonly Stream _inputStream;
    private readonly PipeWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class.
    /// </summary>
    public StdioTransport(string id, Stream? input = null, Stream? output = null)
        : base(new JsonRpcMessageParser(), NullLogger<StdioTransport>.Instance, id)
    {
        _inputStream = input ?? Console.OpenStandardInput();
        _writer = PipeWriter.Create(output ?? Console.OpenStandardOutput());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class with a logger.
    /// </summary>
    public StdioTransport(string id, Stream input, Stream output, ILogger<StdioTransport> logger)
        : base(new JsonRpcMessageParser(), logger, id)
    {
        _inputStream = input ?? throw new ArgumentNullException(nameof(input));
        _writer = PipeWriter.Create(output ?? throw new ArgumentNullException(nameof(output)));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioTransport"/> class with full dependency injection.
    /// </summary>
    public StdioTransport(
        string id,
        Stream input,
        Stream output,
        IMessageParser parser,
        ILogger<StdioTransport> logger
    )
        : base(parser, logger, id)
    {
        _inputStream = input ?? throw new ArgumentNullException(nameof(input));
        _writer = PipeWriter.Create(output ?? throw new ArgumentNullException(nameof(output)));
    }

    /// <summary>
    /// Gets the per-connection metadata captured during authentication or handshake.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new();

    /// <summary>
    /// Exposes the input stream so hosts can process ingress.
    /// </summary>
    internal Stream InputStream => _inputStream;

    /// <inheritdoc />
    public override Task StartAsync()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException(
                "StdioTransport already started! If using Server class, note that ConnectAsync calls StartAsync automatically."
            );
        }

        IsStarted = true;
        Logger.LogDebug("StdioTransport started (outbound only)");
        return Task.CompletedTask;
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
            await WriteRawAsync(Encoding.UTF8.GetBytes(json + "\n"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending response message");
            RaiseOnError(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task SendRequestAsync(JsonRpcRequestMessage message)
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

            string json = SerializeMessage(message);
            await WriteRawAsync(Encoding.UTF8.GetBytes(json + "\n"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending request message");
            RaiseOnError(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task SendNotificationAsync(JsonRpcNotificationMessage message)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        try
        {
            Logger.LogDebug("Sending notification: Method={Method}", message.Method);
            string json = SerializeMessage(message);
            await WriteRawAsync(Encoding.UTF8.GetBytes(json + "\n"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending notification message");
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
