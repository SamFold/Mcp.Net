using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Mcp.Net.LLM.Interfaces;

namespace Mcp.Net.LLM.Models;

public sealed class ChatCompletionStream : IChatCompletionStream
{
    private readonly Func<CancellationToken, Task<ChatClientTurnResult>> _resultFactory;
    private readonly Func<ChannelWriter<ChatClientAssistantTurn>, CancellationToken, Task<ChatClientTurnResult>>
        _streamingFactory;
    private readonly CancellationToken _requestCancellationToken;
    private readonly object _sync = new();
    private Task<ChatClientTurnResult>? _resultTask;
    private Channel<ChatClientAssistantTurn>? _updates;
    private CancellationTokenSource? _streamingLifetimeCts;
    private ExecutionMode? _mode;
    private int _enumeratorCreated;

    private ChatCompletionStream(
        Func<CancellationToken, Task<ChatClientTurnResult>> resultFactory,
        Func<ChannelWriter<ChatClientAssistantTurn>, CancellationToken, Task<ChatClientTurnResult>>
            streamingFactory,
        CancellationToken requestCancellationToken
    )
    {
        _resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
        _streamingFactory = streamingFactory ?? throw new ArgumentNullException(nameof(streamingFactory));
        _requestCancellationToken = requestCancellationToken;
    }

    public static ChatCompletionStream Create(
        Func<CancellationToken, Task<ChatClientTurnResult>> resultFactory,
        Func<ChannelWriter<ChatClientAssistantTurn>, CancellationToken, Task<ChatClientTurnResult>>
            streamingFactory,
        CancellationToken requestCancellationToken = default
    ) => new(resultFactory, streamingFactory, requestCancellationToken);

    public static ChatCompletionStream FromResult(
        ChatClientTurnResult result,
        CancellationToken requestCancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        return Create(
            _ => Task.FromResult(result),
            (_, _) => Task.FromResult(result),
            requestCancellationToken
        );
    }

    public static ChatCompletionStream FromStreaming(
        IReadOnlyList<ChatClientAssistantTurn> updates,
        ChatClientTurnResult result,
        CancellationToken requestCancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(result);

        return Create(
            _ => Task.FromResult(result),
            async (writer, cancellationToken) =>
            {
                foreach (var update in updates)
                {
                    await writer.WriteAsync(update, cancellationToken);
                }

                return result;
            },
            requestCancellationToken
        );
    }

    public ValueTask<ChatClientTurnResult> GetResultAsync(CancellationToken cancellationToken = default)
    {
        var resultTask = EnsureResultOnlyStarted(cancellationToken);

        return cancellationToken.CanBeCanceled
            ? new ValueTask<ChatClientTurnResult>(resultTask.WaitAsync(cancellationToken))
            : new ValueTask<ChatClientTurnResult>(resultTask);
    }

    public IAsyncEnumerator<ChatClientAssistantTurn> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        if (Interlocked.Exchange(ref _enumeratorCreated, 1) != 0)
        {
            throw new InvalidOperationException("ChatCompletionStream only supports a single async enumerator.");
        }

        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async IAsyncEnumerable<ChatClientAssistantTurn> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var updates = EnsureStreamingStarted(cancellationToken, out var resultTask);

        try
        {
            while (await updates.Reader.WaitToReadAsync(cancellationToken))
            {
                while (updates.Reader.TryRead(out var update))
                {
                    yield return update;
                }
            }

            await resultTask;
        }
        finally
        {
            CancelStreamingExecution();
        }
    }

    private Task<ChatClientTurnResult> EnsureResultOnlyStarted(CancellationToken startCancellationToken)
    {
        lock (_sync)
        {
            if (_mode == ExecutionMode.Streaming)
            {
                return _resultTask!;
            }

            if (_mode == ExecutionMode.ResultOnly)
            {
                return _resultTask!;
            }

            _mode = ExecutionMode.ResultOnly;
            _resultTask = RunResultOnlyAsync(startCancellationToken);
            return _resultTask;
        }
    }

    private Channel<ChatClientAssistantTurn> EnsureStreamingStarted(
        CancellationToken startCancellationToken,
        out Task<ChatClientTurnResult> resultTask
    )
    {
        lock (_sync)
        {
            if (_mode == ExecutionMode.ResultOnly)
            {
                throw new InvalidOperationException(
                    "Cannot enumerate a chat completion stream after result-only execution has started."
                );
            }

            if (_mode != ExecutionMode.Streaming)
            {
                _mode = ExecutionMode.Streaming;
                _updates = Channel.CreateBounded<ChatClientAssistantTurn>(
                    new BoundedChannelOptions(1)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        FullMode = BoundedChannelFullMode.Wait,
                    }
                );
                _streamingLifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _requestCancellationToken,
                    startCancellationToken
                );
                _resultTask = RunStreamingAsync(_updates.Writer, _streamingLifetimeCts.Token);
            }

            resultTask = _resultTask!;
            return _updates!;
        }
    }

    private async Task<ChatClientTurnResult> RunResultOnlyAsync(CancellationToken startCancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _requestCancellationToken,
            startCancellationToken
        );

        return await _resultFactory(linkedCts.Token);
    }

    private async Task<ChatClientTurnResult> RunStreamingAsync(
        ChannelWriter<ChatClientAssistantTurn> writer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var result = await _streamingFactory(writer, cancellationToken);
            writer.TryComplete();
            return result;
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            throw;
        }
        finally
        {
            lock (_sync)
            {
                _streamingLifetimeCts?.Dispose();
                _streamingLifetimeCts = null;
            }
        }
    }

    private void CancelStreamingExecution()
    {
        lock (_sync)
        {
            try
            {
                _streamingLifetimeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private enum ExecutionMode
    {
        ResultOnly,
        Streaming,
    }
}
