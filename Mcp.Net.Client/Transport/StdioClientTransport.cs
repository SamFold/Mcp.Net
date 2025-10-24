using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Client.Transport;

/// <summary>
/// Client transport implementation that uses standard input/output streams.
/// </summary>
public class StdioClientTransport : ClientMessageTransportBase
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingRequests =
        new();
    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly Process? _serverProcess;
    private Task? _readTask;
    private StreamReader? _reader;
    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioClientTransport"/> class using default stdin/stdout.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public StdioClientTransport(ILogger? logger = null)
        : base(new JsonRpcMessageParser(), logger ?? NullLogger.Instance)
    {
        _inputStream = Console.OpenStandardInput();
        _outputStream = Console.OpenStandardOutput();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioClientTransport"/> class.
    /// </summary>
    /// <param name="input">Input stream.</param>
    /// <param name="output">Output stream.</param>
    /// <param name="logger">Optional logger.</param>
    public StdioClientTransport(Stream input, Stream output, ILogger? logger = null)
        : base(new JsonRpcMessageParser(), logger ?? NullLogger.Instance)
    {
        _inputStream = input ?? throw new ArgumentNullException(nameof(input));
        _outputStream = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioClientTransport"/> class with a server command.
    /// </summary>
    /// <param name="serverCommand">The command to launch the server.</param>
    /// <param name="logger">Optional logger.</param>
    public StdioClientTransport(string serverCommand, ILogger? logger = null)
        : base(new JsonRpcMessageParser(), logger ?? NullLogger.Instance)
    {
        Logger.LogInformation("Launching server: {ServerCommand}", serverCommand);

        _serverProcess = new Process();
        _serverProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"-c \"{serverCommand}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.LogInformation("[SERVER] {ErrorData}", e.Data);
            }
        };

        _serverProcess.Start();
        _serverProcess.BeginErrorReadLine();

        _inputStream = _serverProcess.StandardOutput.BaseStream;
        _outputStream = _serverProcess.StandardInput.BaseStream;
    }

    /// <inheritdoc />
    public override Task StartAsync()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException("Transport already started");
        }

        IsStarted = true;
        _readTask = ProcessMessagesAsync();
        Logger.LogDebug("StdioClientTransport started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task<object> SendRequestAsync(string method, object? parameters = null)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        // Create a unique ID for this request
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        // Create the request message
        var request = new JsonRpcRequestMessage("2.0", id, method, parameters);

        try
        {
            // Send the request
            await WriteMessageAsync(request);

            if (_requestTimeout == Timeout.InfiniteTimeSpan)
            {
                return await tcs.Task;
            }

            var timeoutTask = Task.Delay(_requestTimeout, CancellationTokenSource.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _pendingRequests.TryRemove(id, out _);
                if (timeoutTask.IsCanceled)
                {
                    throw new OperationCanceledException("Request was cancelled.", CancellationTokenSource.Token);
                }

                throw new TimeoutException($"Request timed out: {method}");
            }

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(id, out _);
            Logger.LogError(ex, "Error sending request: {Method}", method);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task SendNotificationAsync(string method, object? parameters = null)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("Transport is closed");
        }

        // Create the notification message
        var notification = new JsonRpcNotificationMessage(
            "2.0",
            method,
            parameters != null ? JsonSerializer.SerializeToElement(parameters) : null
        );

        // Send the notification
        await WriteMessageAsync(notification);
    }

    private async Task ProcessMessagesAsync()
    {
        _reader ??= new StreamReader(
            _inputStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true
        );

        try
        {
            while (!CancellationTokenSource.Token.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(CancellationTokenSource.Token);
                if (line == null)
                {
                    Logger.LogInformation("End of input stream detected");
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                ProcessJsonRpcMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Message processing cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reading from input stream");
            RaiseOnError(ex);
        }
        finally
        {
            Logger.LogInformation("Message processing loop terminated");
        }
    }

    /// <inheritdoc />
    protected override void ProcessResponse(JsonRpcResponseMessage response)
    {
        if (_pendingRequests.TryRemove(response.Id, out var tcs))
        {
            if (response.Error != null)
            {
                Logger.LogError(
                    "Request {Id} failed: {ErrorMessage}",
                    response.Id,
                    response.Error.Message
                );
                tcs.TrySetException(
                    new Exception($"RPC Error ({response.Error.Code}): {response.Error.Message}")
                );
            }
            else
            {
                Logger.LogDebug("Request {Id} succeeded", response.Id);
                tcs.TrySetResult(response.Result ?? new { });
            }
        }
        else
        {
            Logger.LogWarning("Received response for unknown request: {Id}", response.Id);
        }

        base.ProcessResponse(response);
    }

    /// <inheritdoc />
    protected override async Task WriteRawAsync(byte[] data)
    {
        await _outputStream.WriteAsync(data, 0, data.Length, CancellationTokenSource.Token);
        await _outputStream.FlushAsync(CancellationTokenSource.Token);
    }

    /// <inheritdoc />
    protected override async Task OnClosingAsync()
    {
        foreach (var kvp in _pendingRequests)
        {
            if (kvp.Value.TrySetException(new OperationCanceledException("Transport is closing.", CancellationTokenSource.Token)))
            {
                _pendingRequests.TryRemove(kvp.Key, out _);
            }
        }

        if (_readTask != null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Read loop terminated with exception during shutdown.");
            }
        }

        // Terminate the server process if we started it
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            Logger.LogInformation("Terminating server process");
            try
            {
                _serverProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error terminating server process");
            }
        }

        await base.OnClosingAsync();
    }

    /// <summary>
    /// Gets or sets the request timeout applied to stdio RPC requests. Use <see cref="Timeout.InfiniteTimeSpan"/> to disable the timeout.
    /// </summary>
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        set
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Request timeout must be non-negative or Timeout.InfiniteTimeSpan."
                );
            }

            _requestTimeout = value;
        }
    }
}
