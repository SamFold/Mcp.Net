using FluentAssertions;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Tests.LLM.Models;

public class ChatCompletionStreamTests
{
    [Fact]
    public async Task GetResultAsync_ShouldExecuteResultFactoryOnceAndReturnResult()
    {
        var expected = CreateAssistantTurn(text: "final");
        var resultFactoryCalls = 0;
        var streamingFactoryCalls = 0;
        var stream = ChatCompletionStream.Create(
            _ =>
            {
                Interlocked.Increment(ref resultFactoryCalls);
                return Task.FromResult<ChatClientTurnResult>(expected);
            },
            (_, _) =>
            {
                Interlocked.Increment(ref streamingFactoryCalls);
                return Task.FromResult<ChatClientTurnResult>(expected);
            }
        );

        var firstResult = await stream.GetResultAsync();
        var secondResult = await stream.GetResultAsync();

        firstResult.Should().BeSameAs(expected);
        secondResult.Should().BeSameAs(expected);
        resultFactoryCalls.Should().Be(1);
        streamingFactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetResultAsync_AfterStreaming_ShouldReturnStreamingResultWithoutStartingResultFactory()
    {
        var firstUpdate = CreateAssistantTurn(text: "Hel");
        var secondUpdate = CreateAssistantTurn(text: "Hello");
        var expected = CreateAssistantTurn(text: "Hello");
        var resultFactoryCalls = 0;
        var streamingFactoryCalls = 0;
        var stream = ChatCompletionStream.Create(
            _ =>
            {
                Interlocked.Increment(ref resultFactoryCalls);
                return Task.FromResult<ChatClientTurnResult>(CreateAssistantTurn(text: "result-only"));
            },
            async (writer, cancellationToken) =>
            {
                Interlocked.Increment(ref streamingFactoryCalls);
                await writer.WriteAsync(firstUpdate, cancellationToken);
                await writer.WriteAsync(secondUpdate, cancellationToken);
                return expected;
            }
        );

        var updates = new List<ChatClientAssistantTurn>();
        await foreach (var update in stream)
        {
            updates.Add(update);
        }

        var result = await stream.GetResultAsync();

        updates.Should().ContainInOrder(firstUpdate, secondUpdate);
        result.Should().BeSameAs(expected);
        resultFactoryCalls.Should().Be(0);
        streamingFactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAsyncEnumerator_AfterResultOnlyExecutionHasStarted_ShouldThrowInvalidOperationException()
    {
        var expected = CreateAssistantTurn(text: "final");
        var stream = ChatCompletionStream.Create(
            _ => Task.FromResult<ChatClientTurnResult>(expected),
            (_, _) => throw new InvalidOperationException("Streaming execution should not start.")
        );

        await stream.GetResultAsync();

        Func<Task> act = async () =>
        {
            await foreach (var _ in stream)
            {
            }
        };

        await act
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot enumerate a chat completion stream after result-only execution has started.");
    }

    [Fact]
    public async Task GetAsyncEnumerator_WhenCalledMoreThanOnce_ShouldThrowInvalidOperationException()
    {
        var update = CreateAssistantTurn(text: "partial");
        var stream = ChatCompletionStream.FromStreaming([update], update);

        await using var enumerator = stream.GetAsyncEnumerator();

        Func<Task> act = async () =>
        {
            await using var secondEnumerator = stream.GetAsyncEnumerator();
            await secondEnumerator.MoveNextAsync();
        };

        await act
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("ChatCompletionStream only supports a single async enumerator.");
    }

    [Fact]
    public async Task GetResultAsync_WhenRequestCancellationTokenIsCanceled_ShouldCancelResultFactory()
    {
        using var requestCancellationTokenSource = new CancellationTokenSource();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = ChatCompletionStream.Create(
            async cancellationToken =>
            {
                using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateAssistantTurn(text: "unreachable");
            },
            (_, _) => throw new InvalidOperationException("Streaming execution should not start."),
            requestCancellationTokenSource.Token
        );

        var resultTask = stream.GetResultAsync().AsTask();

        requestCancellationTokenSource.Cancel();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resultTask);
    }

    [Fact]
    public async Task GetAsyncEnumerator_WhenConsumerStopsEarly_ShouldCancelStreamingFactory()
    {
        var update = CreateAssistantTurn(text: "partial");
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = ChatCompletionStream.Create(
            _ => throw new InvalidOperationException("Result-only execution should not start."),
            async (writer, cancellationToken) =>
            {
                using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult());
                await writer.WriteAsync(update, cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateAssistantTurn(text: "unreachable");
            }
        );

        var updates = new List<ChatClientAssistantTurn>();
        await foreach (var assistantTurn in stream)
        {
            updates.Add(assistantTurn);
            break;
        }

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        updates.Should().ContainSingle().Which.Should().Be(update);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.GetResultAsync().AsTask());
    }

    [Fact]
    public async Task GetAsyncEnumerator_WhenStreamingFactoryThrows_ShouldSurfaceTheFailureToEnumerationAndFinalResult()
    {
        var update = CreateAssistantTurn(text: "partial");
        var expectedException = new InvalidOperationException("boom");
        var stream = ChatCompletionStream.Create(
            _ => throw new InvalidOperationException("Result-only execution should not start."),
            async (writer, cancellationToken) =>
            {
                await writer.WriteAsync(update, cancellationToken);
                throw expectedException;
            }
        );

        var updates = new List<ChatClientAssistantTurn>();
        var thrownFromEnumeration = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var assistantTurn in stream)
            {
                updates.Add(assistantTurn);
            }
        });

        updates.Should().ContainSingle().Which.Should().Be(update);
        thrownFromEnumeration.Message.Should().Be("boom");

        var thrownFromResult = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stream.GetResultAsync().AsTask()
        );
        thrownFromResult.Message.Should().Be("boom");
    }

    private static ChatClientAssistantTurn CreateAssistantTurn(
        string id = "turn-1",
        string provider = "openai",
        string model = "gpt-5",
        string text = "hello"
    ) => new(id, provider, model, [new TextAssistantBlock("text-1", text)]);
}
