using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Agent.Compaction;
using Mcp.Net.Agent.Core;
using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Mcp.Net.Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RuntimeToolInvocation = Mcp.Net.Agent.Tools.ToolInvocation;
using Xunit;

namespace Mcp.Net.Tests.Agent.Core;

public class ChatSessionTests
{
    [Fact]
    public async Task SendUserMessageAsync_ShouldAppendUserTranscriptEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.SystemPrompt == string.Empty
                    && request.Tools.Count == 0
                    && request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Hello from the model") }
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        var transcriptEntries = new List<ChatTranscriptEntry>();
        session.TranscriptChanged += (_, args) => transcriptEntries.Add(args.Entry);

        await session.SendUserMessageAsync("Hi there");

        transcriptEntries.Should().HaveCount(2);
        transcriptEntries[0].Should().BeOfType<UserChatEntry>().Which.Content.Should().Be("Hi there");
    }

    [Fact]
    public async Task SendUserMessageAsync_ProviderAssistantTurn_ShouldAppendSingleAssistantEntryWithOrderedBlocks()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "anthropic",
                    "claude-sonnet-4-5-20250929",
                    new AssistantContentBlock[]
                    {
                        new ReasoningAssistantBlock(
                            "reasoning-1",
                            "I should greet the user first.",
                            ReasoningVisibility.Visible
                        ),
                        new TextAssistantBlock("text-1", "Hello from Claude"),
                    },
                    StopReason: "end_turn",
                    Usage: new ChatUsage(
                        11,
                        7,
                        18,
                        new Dictionary<string, int> { ["cacheCreationInputTokens"] = 4 }
                    )
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        var transcriptEntries = new List<ChatTranscriptEntry>();
        session.TranscriptChanged += (_, args) => transcriptEntries.Add(args.Entry);

        await session.SendUserMessageAsync("Hi there");

        var assistantEntry = transcriptEntries.OfType<AssistantChatEntry>().Single();
        assistantEntry.Provider.Should().Be("anthropic");
        assistantEntry.Model.Should().Be("claude-sonnet-4-5-20250929");
        assistantEntry.StopReason.Should().Be("end_turn");
        assistantEntry.Usage.Should().NotBeNull();
        assistantEntry.Usage!.TotalTokens.Should().Be(18);
        assistantEntry.Usage.AdditionalCounts.Should().Contain("cacheCreationInputTokens", 4);
        assistantEntry.Blocks.Should().HaveCount(2);
        assistantEntry.Blocks[0].Should().BeOfType<ReasoningAssistantBlock>();
        assistantEntry.Blocks[1].Should().BeOfType<TextAssistantBlock>();
    }

    [Fact]
    public async Task SendUserMessageAsync_StreamingAssistantUpdate_ShouldUpdateSingleAssistantEntryInPlace()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (
                    ChatClientRequest request,
                    CancellationToken cancellationToken
                ) =>
                {
                    request.Transcript.OfType<UserChatEntry>().Single().Content.Should().Be("Hi there");
                    cancellationToken.CanBeCanceled.Should().BeTrue();
                    return CreateStreamingResultStream(
                        [
                            new ChatClientAssistantTurn(
                                "assistant-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new TextAssistantBlock("text-1", "Hel"),
                                }
                            ),
                        ],
                        new ChatClientAssistantTurn(
                            "assistant-1",
                            "openai",
                            "gpt-5",
                            new AssistantContentBlock[]
                            {
                                new TextAssistantBlock("text-1", "Hello"),
                            },
                            StopReason: "stop",
                            Usage: new ChatUsage(6, 2, 8)
                        )
                    );
                }
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        var transcriptChanges = new List<ChatTranscriptChangedEventArgs>();
        session.TranscriptChanged += (_, args) => transcriptChanges.Add(args);

        await session.SendUserMessageAsync("Hi there");

        session.Transcript.Should().HaveCount(2);
        session.Transcript.Should().ContainSingle(entry => entry is AssistantChatEntry);

        transcriptChanges.Should().HaveCount(3);
        transcriptChanges[1].ChangeKind.Should().Be(ChatTranscriptChangeKind.Added);
        transcriptChanges[1].Entry.Should().BeOfType<AssistantChatEntry>();
        transcriptChanges[2].ChangeKind.Should().Be(ChatTranscriptChangeKind.Updated);
        transcriptChanges[2].Entry.Should().BeOfType<AssistantChatEntry>();
        transcriptChanges[2].Entry.Id.Should().Be(transcriptChanges[1].Entry.Id);

        var assistantEntry = session.Transcript.OfType<AssistantChatEntry>().Single();
        assistantEntry.StopReason.Should().Be("stop");
        assistantEntry.Usage.Should().NotBeNull();
        assistantEntry.Usage!.TotalTokens.Should().Be(8);
        assistantEntry.Blocks.Should().ContainSingle();
        assistantEntry.Blocks[0]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("Hello");
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldReturnTurnSummaryWithAddedAndUpdatedEntries()
    {
        var llmClient = new Mock<IChatClient>();

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                ChatCompletionStream.FromStreaming(
                    [
                        new ChatClientAssistantTurn(
                            "assistant-1",
                            "openai",
                            "gpt-5",
                            new AssistantContentBlock[]
                            {
                                new TextAssistantBlock("text-1", "Hel"),
                            }
                        ),
                    ],
                    new ChatClientAssistantTurn(
                        "assistant-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "Hello"),
                        }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, Mock.Of<IToolRegistry>());

        var summary = await session.SendUserMessageAsync("Hi there");

        summary.Completion.Should().Be(ChatTurnCompletion.Completed);
        summary.TurnId.Should().NotBeNullOrWhiteSpace();
        summary.AddedEntries.Should().HaveCount(2);
        summary.AddedEntries[0].Should().BeOfType<UserChatEntry>();
        summary.AddedEntries[1].Should().BeOfType<AssistantChatEntry>();
        summary.UpdatedEntries.Should().ContainSingle();
        summary.UpdatedEntries[0].Should().BeOfType<AssistantChatEntry>();
        summary.UpdatedEntries[0].Id.Should().Be(summary.AddedEntries[1].Id);
        summary.AddedEntries.Select(entry => entry.TurnId).Should().OnlyContain(turnId => turnId == summary.TurnId);
        summary.UpdatedEntries.Select(entry => entry.TurnId).Should().OnlyContain(turnId => turnId == summary.TurnId);
    }

    [Fact]
    public async Task SendUserMessageAsync_ProviderFailure_ShouldAppendErrorEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                new ChatClientFailure(
                    ChatErrorSource.Provider,
                    "Provider failure",
                    Code: "provider_error",
                    Provider: "openai",
                    Model: "gpt-5"
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        var transcriptEntries = new List<ChatTranscriptEntry>();
        session.TranscriptChanged += (_, args) => transcriptEntries.Add(args.Entry);

        await session.SendUserMessageAsync("Hi there");

        transcriptEntries.Should().HaveCount(2);
        transcriptEntries[1]
            .Should()
            .BeOfType<ErrorChatEntry>()
            .Which.Message.Should().Be("Provider failure");
    }

    [Fact]
    public async Task SendUserMessageAsync_WhenProviderRequestIsCanceled_ShouldReturnCancelledSummaryWithoutAppendingErrorEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activities = new List<ChatSessionActivity>();
        using var cancellationTokenSource = new CancellationTokenSource();

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            cancellationToken.CanBeCanceled.Should().BeTrue();
                            providerStarted.TrySetResult();

                            using var registration = streamCancellationToken.Register(() =>
                                providerCanceled.TrySetResult()
                            );

                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        session.ActivityChanged += (_, args) => activities.Add(args.Activity);

        var sendTask = session.SendUserMessageAsync("hello", cancellationTokenSource.Token);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellationTokenSource.Cancel();

        await providerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var summary = await sendTask;

        session.Transcript.Should().ContainSingle(entry => entry is UserChatEntry);
        session.Transcript.Should().NotContain(entry => entry is ErrorChatEntry);
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
        summary.AddedEntries.Should().ContainSingle(entry => entry is UserChatEntry);
        summary.UpdatedEntries.Should().BeEmpty();
        activities.Should().ContainInOrder(ChatSessionActivity.WaitingForProvider, ChatSessionActivity.Idle);
    }

    [Fact]
    public async Task SendUserMessageAsync_WhenAnotherTurnIsActive_ShouldThrowInvalidOperationException()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationTokenSource = new CancellationTokenSource();

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            providerStarted.TrySetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        var firstTurn = session.SendUserMessageAsync("hello", cancellationTokenSource.Token);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var act = () => session.SendUserMessageAsync("second");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in progress*");

        cancellationTokenSource.Cancel();
        var firstSummary = await firstTurn;
        firstSummary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldReportIsProcessingWhileTurnIsActive()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationTokenSource = new CancellationTokenSource();

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            providerStarted.TrySetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        session.IsProcessing.Should().BeFalse();

        var sendTask = session.SendUserMessageAsync("hello", cancellationTokenSource.Token);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.IsProcessing.Should().BeTrue();

        cancellationTokenSource.Cancel();

        var summary = await sendTask;
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
        session.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task AbortCurrentTurn_ShouldCancelTheActiveTurn()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            cancellationToken.CanBeCanceled.Should().BeTrue();
                            streamCancellationToken.CanBeCanceled.Should().BeTrue();
                            providerStarted.TrySetResult();

                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        var sendTask = session.SendUserMessageAsync("hello");

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.AbortCurrentTurn();

        var summary = await sendTask;
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
        session.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public void AbortCurrentTurn_WhenIdle_ShouldBeANoOp()
    {
        var session = CreateSession(Mock.Of<IChatClient>(), Mock.Of<IToolRegistry>());

        var act = () => session.AbortCurrentTurn();

        act.Should().NotThrow();
        session.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task WaitForIdleAsync_ShouldReturnImmediatelyWhenIdle()
    {
        var session = CreateSession(Mock.Of<IChatClient>(), Mock.Of<IToolRegistry>());

        await session.WaitForIdleAsync();

        session.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task WaitForIdleAsync_ShouldCompleteWhenTheActiveTurnFinishesAndReturnCancelledSummary()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            providerStarted.TrySetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        var sendTask = session.SendUserMessageAsync("hello");

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var waitTask = session.WaitForIdleAsync();
        session.AbortCurrentTurn();

        await waitTask;
        var summary = await sendTask;
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
        session.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task Mutators_WhenTurnIsActive_ShouldThrowInvalidOperationException()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationTokenSource = new CancellationTokenSource();

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (ChatClientRequest _, CancellationToken cancellationToken) =>
                    ChatCompletionStream.Create(
                        _ => throw new InvalidOperationException("Result-only execution should not start."),
                        async (_, streamCancellationToken) =>
                        {
                            providerStarted.TrySetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, streamCancellationToken);

                            return new ChatClientAssistantTurn(
                                "turn-unreachable",
                                "openai",
                                "gpt-5",
                                Array.Empty<AssistantContentBlock>()
                            );
                        },
                        cancellationToken
                    )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        var sendTask = session.SendUserMessageAsync("hello", cancellationTokenSource.Token);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Func<Task>[] mutators =
        {
            () =>
            {
                session.SetSystemPrompt("Be concise.");
                return Task.CompletedTask;
            },
            () =>
            {
                session.SetRequestDefaults(new ChatRequestOptions { Temperature = 0.25f });
                return Task.CompletedTask;
            },
            () =>
            {
                session.RegisterTools(Array.Empty<Tool>());
                return Task.CompletedTask;
            },
            () =>
            {
                session.ApplyConfiguration(new ChatSessionConfiguration());
                return Task.CompletedTask;
            },
            () =>
            {
                session.ResetConversation();
                return Task.CompletedTask;
            },
            () => session.LoadTranscriptAsync(Array.Empty<ChatTranscriptEntry>()),
        };

        foreach (var mutator in mutators)
        {
            await mutator.Should().ThrowAsync<InvalidOperationException>().WithMessage("*turn is in progress*");
        }

        cancellationTokenSource.Cancel();
        var summary = await sendTask;
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
    }

    [Fact]
    public async Task SendUserMessageAsync_ToolExecution_ShouldEmitActivityAndAppendToolResultEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>();
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var toolInvocationBlock = new ToolCallAssistantBlock(
            "tool-block-1",
            "call-1",
            "calculator_add",
            new Dictionary<string, object?> { ["a"] = 2.0, ["b"] = 3.0 }
        );

        llmClient
            .SetupSequence(c => c.SendAsync(
                It.IsAny<ChatClientRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { toolInvocationBlock }
                )
            ))
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-2",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Result is 5") }
                )
            ));

        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        var tool = new Tool
        {
            Name = "calculator_add",
            Description = "Adds two numbers",
            InputSchema = schemaDocument.RootElement.Clone(),
        };

        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-1"
                    && invocation.ToolName == "calculator_add"
                    && invocation.Arguments.Count == 2
                    && Equals(invocation.Arguments["a"], 2.0)
                    && Equals(invocation.Arguments["b"], 3.0)
                ),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(CreateToolResult("call-1", "calculator_add", "5"));

        var session = CreateSession(llmClient.Object, toolRegistry.Object, toolExecutor: toolExecutor.Object);
        session.RegisterTools(new[] { tool });

        var transcriptEntries = new List<ChatTranscriptEntry>();
        var toolActivities = new List<ToolCallActivityChangedEventArgs>();
        session.TranscriptChanged += (_, args) => transcriptEntries.Add(args.Entry);
        session.ToolCallActivityChanged += (_, args) => toolActivities.Add(args);

        await session.SendUserMessageAsync("Please add numbers");

        transcriptEntries.Should().ContainSingle(entry => entry is ToolResultChatEntry);

        var toolResultEntry = transcriptEntries.OfType<ToolResultChatEntry>().Single();
        toolResultEntry.ToolCallId.Should().Be("call-1");
        toolResultEntry.ToolName.Should().Be("calculator_add");
        toolResultEntry.IsError.Should().BeFalse();
        toolResultEntry.Result.Text.Should().ContainSingle().Which.Should().Be("5");

        toolActivities.Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(ToolCallExecutionState.Queued, ToolCallExecutionState.Running, ToolCallExecutionState.Completed);
    }

    [Fact]
    public async Task SendUserMessageAsync_MultipleToolCalls_ShouldAppendTranscriptEntriesInToolCallOrder()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>();
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var runningToolCalls = new ConcurrentDictionary<string, byte>();
        var allToolsRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolActivities = new ConcurrentQueue<ToolCallActivityChangedEventArgs>();
        var capturedRequests = new ConcurrentQueue<ChatClientRequest>();

        using var searchSchema = System.Text.Json.JsonDocument.Parse("{}");
        var searchTool = new Tool
        {
            Name = "search",
            Description = "Searches documents",
            InputSchema = searchSchema.RootElement.Clone(),
        };

        using var calculatorSchema = System.Text.Json.JsonDocument.Parse("{}");
        var calculatorTool = new Tool
        {
            Name = "calculate",
            Description = "Performs calculations",
            InputSchema = calculatorSchema.RootElement.Clone(),
        };

        var searchCompletion = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var calculatorCompletion = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-1" && invocation.ToolName == "search"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => searchCompletion.Task);
        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-2" && invocation.ToolName == "calculate"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => calculatorCompletion.Task);

        var providerCallCount = 0;
        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                (
                    ChatClientRequest request,
                    CancellationToken _
                ) =>
                {
                    capturedRequests.Enqueue(request);
                    providerCallCount++;

                    return providerCallCount == 1
                        ? CreateResultStream(
                            new ChatClientAssistantTurn(
                                "turn-1",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new ToolCallAssistantBlock(
                                        "tool-block-1",
                                        "call-1",
                                        "search",
                                        new Dictionary<string, object?> { ["query"] = "weather" }
                                    ),
                                    new ToolCallAssistantBlock(
                                        "tool-block-2",
                                        "call-2",
                                        "calculate",
                                        new Dictionary<string, object?> { ["expression"] = "2+2" }
                                    ),
                                }
                            )
                        )
                        : CreateResultStream(
                            new ChatClientAssistantTurn(
                                "turn-2",
                                "openai",
                                "gpt-5",
                                new AssistantContentBlock[]
                                {
                                    new TextAssistantBlock("text-1", "done"),
                                }
                            )
                        );
                }
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object, toolExecutor: toolExecutor.Object);
        session.RegisterTools(new[] { searchTool, calculatorTool });

        session.ToolCallActivityChanged += (_, args) =>
        {
            toolActivities.Enqueue(args);

            if (args.ExecutionState == ToolCallExecutionState.Running)
            {
                runningToolCalls.TryAdd(args.ToolCallId, 0);
                if (runningToolCalls.Count == 2)
                {
                    allToolsRunning.TrySetResult();
                }
            }
        };

        var sendTask = session.SendUserMessageAsync("run both tools");

        await allToolsRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        toolActivities
            .Select(activity => activity.ExecutionState)
            .Should()
            .OnlyContain(state =>
                state == ToolCallExecutionState.Queued || state == ToolCallExecutionState.Running
            );

        calculatorCompletion.SetResult(CreateToolResult("call-2", "calculate", "4"));
        searchCompletion.SetResult(CreateToolResult("call-1", "search", "sunny"));

        await sendTask;

        var toolResults = session.Transcript.OfType<ToolResultChatEntry>().ToArray();
        toolResults.Should().HaveCount(2);
        toolResults.Select(entry => entry.ToolCallId).Should().Equal("call-1", "call-2");
        toolResults.SelectMany(entry => entry.Result.Text).Should().Contain(new[] { "sunny", "4" });

        capturedRequests.Should().HaveCount(2);
        var secondRequest = capturedRequests.Last();
        secondRequest.Transcript.OfType<ToolResultChatEntry>().Select(entry => entry.ToolCallId)
            .Should()
            .Equal("call-1", "call-2");
    }

    [Fact]
    public async Task SendUserMessageAsync_MultipleToolCalls_ShouldAppendAllResultsInOrderWhenOneToolFails()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>();
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var runningToolCalls = new ConcurrentDictionary<string, byte>();
        var allToolsRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolActivities = new ConcurrentQueue<ToolCallActivityChangedEventArgs>();

        using var searchSchema = System.Text.Json.JsonDocument.Parse("{}");
        var searchTool = new Tool
        {
            Name = "search",
            Description = "Searches documents",
            InputSchema = searchSchema.RootElement.Clone(),
        };

        using var calculatorSchema = System.Text.Json.JsonDocument.Parse("{}");
        var calculatorTool = new Tool
        {
            Name = "calculate",
            Description = "Performs calculations",
            InputSchema = calculatorSchema.RootElement.Clone(),
        };

        var searchCompletion = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var calculatorCompletion = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-1" && invocation.ToolName == "search"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => searchCompletion.Task);
        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-2" && invocation.ToolName == "calculate"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => calculatorCompletion.Task);

        llmClient
            .SetupSequence(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new ToolCallAssistantBlock(
                                "tool-block-1",
                                "call-1",
                                "search",
                                new Dictionary<string, object?> { ["query"] = "weather" }
                            ),
                            new ToolCallAssistantBlock(
                                "tool-block-2",
                                "call-2",
                                "calculate",
                                new Dictionary<string, object?> { ["expression"] = "2+2" }
                            ),
                        }
                    )
                )
            )
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-2",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "done"),
                        }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object, toolExecutor: toolExecutor.Object);
        session.RegisterTools(new[] { searchTool, calculatorTool });

        session.ToolCallActivityChanged += (_, args) =>
        {
            toolActivities.Enqueue(args);

            if (args.ExecutionState == ToolCallExecutionState.Running)
            {
                runningToolCalls.TryAdd(args.ToolCallId, 0);
                if (runningToolCalls.Count == 2)
                {
                    allToolsRunning.TrySetResult();
                }
            }
        };

        var sendTask = session.SendUserMessageAsync("run both tools");

        await allToolsRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        calculatorCompletion.SetResult(CreateErrorToolResult("call-2", "calculate", "calculation failed"));
        searchCompletion.SetResult(CreateToolResult("call-1", "search", "sunny"));

        await sendTask;

        var toolResults = session.Transcript.OfType<ToolResultChatEntry>().ToArray();
        toolResults.Should().HaveCount(2);
        toolResults.Select(entry => entry.ToolCallId).Should().Equal("call-1", "call-2");
        toolResults[0].IsError.Should().BeFalse();
        toolResults[0].Result.Text.Should().ContainSingle().Which.Should().Be("sunny");
        toolResults[1].IsError.Should().BeTrue();
        toolResults[1].Result.Text.Single().Should().Contain("calculation failed");

        toolActivities.Where(activity => activity.ToolCallId == "call-1").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Completed
            );
        toolActivities.Where(activity => activity.ToolCallId == "call-2").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Failed
            );
    }

    [Fact]
    public async Task SendUserMessageAsync_MixedLocalAndMcpToolCalls_ShouldAppendTranscriptEntriesInToolCallOrder()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var runningToolCalls = new ConcurrentDictionary<string, byte>();
        var allToolsRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolActivities = new ConcurrentQueue<ToolCallActivityChangedEventArgs>();

        var localCompletion = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var remoteCompletion = new TaskCompletionSource<ToolCallResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var localTool = new TestLocalTool(
            CreateToolDescriptor("search_local", "Searches local documents"),
            (_, _) => localCompletion.Task
        );
        var remoteTool = CreateToolDescriptor("calculate_remote", "Performs remote calculations");

        mcpClient
            .Setup(client => client.CallTool("calculate_remote", It.IsAny<object?>()))
            .Returns(() => remoteCompletion.Task);

        llmClient
            .SetupSequence(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new ToolCallAssistantBlock(
                                "tool-block-1",
                                "call-1",
                                "search_local",
                                new Dictionary<string, object?> { ["query"] = "weather" }
                            ),
                            new ToolCallAssistantBlock(
                                "tool-block-2",
                                "call-2",
                                "calculate_remote",
                                new Dictionary<string, object?> { ["expression"] = "2+2" }
                            ),
                        }
                    )
                )
            )
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-2",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "done"),
                        }
                    )
                )
            );

        var toolExecutor = new CompositeToolExecutor(
            new LocalToolExecutor(new[] { localTool }),
            new McpToolExecutor(mcpClient.Object, NullLogger<McpToolExecutor>.Instance)
        );
        var session = CreateSession(llmClient.Object, Mock.Of<IToolRegistry>(), toolExecutor: toolExecutor);
        session.RegisterTools(new[] { localTool.Descriptor, remoteTool });

        session.ToolCallActivityChanged += (_, args) =>
        {
            toolActivities.Enqueue(args);

            if (args.ExecutionState == ToolCallExecutionState.Running)
            {
                runningToolCalls.TryAdd(args.ToolCallId, 0);
                if (runningToolCalls.Count == 2)
                {
                    allToolsRunning.TrySetResult();
                }
            }
        };

        var sendTask = session.SendUserMessageAsync("run both tools");

        await allToolsRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        remoteCompletion.SetResult(CreateMcpToolCallResult("4"));
        localCompletion.SetResult(CreateToolResult("call-1", "search_local", "sunny"));

        await sendTask;

        var toolResults = session.Transcript.OfType<ToolResultChatEntry>().ToArray();
        toolResults.Should().HaveCount(2);
        toolResults.Select(entry => entry.ToolCallId).Should().Equal("call-1", "call-2");
        toolResults[0].ToolName.Should().Be("search_local");
        toolResults[0].Result.Text.Should().ContainSingle().Which.Should().Be("sunny");
        toolResults[1].ToolName.Should().Be("calculate_remote");
        toolResults[1].Result.Text.Should().ContainSingle().Which.Should().Be("4");

        toolActivities.Where(activity => activity.ToolCallId == "call-1").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Completed
            );
        toolActivities.Where(activity => activity.ToolCallId == "call-2").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Completed
            );
    }

    [Fact]
    public async Task SendUserMessageAsync_WhenToolExecutionIsCanceled_ShouldAppendCompletedResultsAndCancelRemainingTools()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>();
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var runningToolCalls = new ConcurrentDictionary<string, byte>();
        var allToolsRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolActivities = new ConcurrentQueue<ToolCallActivityChangedEventArgs>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var completedTool = CreateToolDescriptor("search", "Searches documents");
        var canceledTool = CreateToolDescriptor("calculate", "Performs calculations");
        var completedToolResult = new TaskCompletionSource<ToolInvocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var canceledToolObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-1" && invocation.ToolName == "search"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => completedToolResult.Task);
        toolExecutor
            .Setup(e => e.ExecuteAsync(
                It.Is<RuntimeToolInvocation>(invocation =>
                    invocation.ToolCallId == "call-2" && invocation.ToolName == "calculate"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                async (RuntimeToolInvocation _, CancellationToken cancellationToken) =>
                {
                    using var registration = cancellationToken.Register(() =>
                        canceledToolObserved.TrySetResult()
                    );
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return CreateToolResult("call-2", "calculate", "unreachable");
                }
            );

        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new ToolCallAssistantBlock(
                                "tool-block-1",
                                "call-1",
                                "search",
                                new Dictionary<string, object?> { ["query"] = "weather" }
                            ),
                            new ToolCallAssistantBlock(
                                "tool-block-2",
                                "call-2",
                                "calculate",
                                new Dictionary<string, object?> { ["expression"] = "2+2" }
                            ),
                        }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object, toolExecutor: toolExecutor.Object);
        session.RegisterTools(new[] { completedTool, canceledTool });
        session.ToolCallActivityChanged += (_, args) =>
        {
            toolActivities.Enqueue(args);

            if (args.ExecutionState == ToolCallExecutionState.Running)
            {
                runningToolCalls.TryAdd(args.ToolCallId, 0);
                if (runningToolCalls.Count == 2)
                {
                    allToolsRunning.TrySetResult();
                }
            }
        };

        var sendTask = session.SendUserMessageAsync("run both tools", cancellationTokenSource.Token);

        await allToolsRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        completedToolResult.SetResult(CreateToolResult("call-1", "search", "sunny"));
        cancellationTokenSource.Cancel();

        await canceledToolObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var summary = await sendTask;

        var toolResults = session.Transcript.OfType<ToolResultChatEntry>().ToArray();
        toolResults.Should().ContainSingle();
        toolResults[0].ToolCallId.Should().Be("call-1");
        toolResults[0].ToolName.Should().Be("search");
        toolResults[0].Result.Text.Should().ContainSingle().Which.Should().Be("sunny");

        toolActivities.Where(activity => activity.ToolCallId == "call-1").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Completed
            );
        toolActivities.Where(activity => activity.ToolCallId == "call-2").Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(
                ToolCallExecutionState.Queued,
                ToolCallExecutionState.Running,
                ToolCallExecutionState.Cancelled
            );
        summary.Completion.Should().Be(ChatTurnCompletion.Cancelled);
        summary.AddedEntries.Should().Contain(entry => entry is ToolResultChatEntry);

        llmClient.Verify(
            c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SendUserMessageAsync_MissingSessionTool_ShouldAppendErrorResultAndFailedActivity()
    {
        var llmClient = new Mock<IChatClient>();
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var toolActivities = new List<ToolCallActivityChangedEventArgs>();

        llmClient
            .SetupSequence(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new ToolCallAssistantBlock(
                                "tool-block-1",
                                "call-1",
                                "missing_tool",
                                new Dictionary<string, object?> { ["query"] = "weather" }
                            ),
                        }
                    )
                )
            )
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-2",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "done"),
                        }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object, toolExecutor: toolExecutor.Object);
        session.ToolCallActivityChanged += (_, args) => toolActivities.Add(args);

        await session.SendUserMessageAsync("run missing tool");

        var toolResult = session.Transcript.OfType<ToolResultChatEntry>().Single();
        toolResult.ToolCallId.Should().Be("call-1");
        toolResult.ToolName.Should().Be("missing_tool");
        toolResult.IsError.Should().BeTrue();
        toolResult.Result.Text.Single().Should().Contain("Tool not registered for this session");

        toolActivities.Select(activity => activity.ExecutionState)
            .Should()
            .ContainInOrder(ToolCallExecutionState.Queued, ToolCallExecutionState.Failed);
        toolActivities.Last().ErrorMessage.Should().Contain("Tool not registered for this session");

        toolExecutor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldTransitionActivityWaitingForProviderThenIdle()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.OfType<UserChatEntry>().Single().Content == "Hi there"
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Hello from the model") }
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        var activities = new List<ChatSessionActivity>();
        session.ActivityChanged += (_, args) => activities.Add(args.Activity);

        await session.SendUserMessageAsync("Hi there");

        activities.Should().ContainInOrder(ChatSessionActivity.WaitingForProvider, ChatSessionActivity.Idle);
    }

    [Fact]
    public async Task LoadTranscriptAsync_ShouldPopulateTranscriptWithoutCallingProvider()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "hello", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "hi") },
                "turn-1",
                "openai",
                "gpt-5"
            ),
        };

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        await session.LoadTranscriptAsync(transcript);

        session.Transcript.Should().Equal(transcript);
        llmClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContinueAsync_ShouldResumeFromToolResultTailWithoutAppendingUserEntry()
    {
        var llmClient = new Mock<IChatClient>();

        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "hello", "turn-1"),
            new ToolResultChatEntry(
                "tool-1",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "call-1",
                "calculator_add",
                CreateToolResult("call-1", "calculator_add", "5"),
                false,
                "turn-1"
            ),
        };

        llmClient
            .Setup(c => c.SendAsync(
                It.Is<ChatClientRequest>(request =>
                    request.Transcript.Count == 2
                    && request.Transcript[0].Id == "user-1"
                    && request.Transcript[1].Id == "tool-1"
                    && request.Transcript.OfType<UserChatEntry>().Count() == 1
                ),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "assistant-2",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, Mock.Of<IToolRegistry>());
        await session.LoadTranscriptAsync(transcript);

        var summary = await session.ContinueAsync();

        session.Transcript.Should().HaveCount(3);
        session.Transcript.OfType<UserChatEntry>().Should().ContainSingle();
        summary.Completion.Should().Be(ChatTurnCompletion.Completed);
        summary.AddedEntries.Should().ContainSingle(entry => entry is AssistantChatEntry);
        summary.UpdatedEntries.Should().BeEmpty();
        summary.AddedEntries[0].TurnId.Should().Be(summary.TurnId);
    }

    [Fact]
    public async Task ContinueAsync_WhenTranscriptEndsWithAssistantEntry_ShouldThrowInvalidOperationException()
    {
        var session = CreateSession(Mock.Of<IChatClient>(), Mock.Of<IToolRegistry>());
        await session.LoadTranscriptAsync(
            [
                new AssistantChatEntry(
                    "assistant-1",
                    DateTimeOffset.UtcNow,
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "done") },
                    "turn-1"
                ),
            ]
        );

        var act = () => session.ContinueAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot continue*");
    }

    [Fact]
    public void SetSystemPrompt_ShouldUpdateSessionState()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        session.SetSystemPrompt("Be concise.");

        session.GetSystemPrompt().Should().Be("Be concise.");
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldIncludeConfiguredRequestDefaultsInProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(
                It.IsAny<ChatClientRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<ChatClientRequest, CancellationToken>(
                (request, _) => capturedRequest = request
            )
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "ok") }
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        session.SetRequestDefaults(new ChatRequestOptions { Temperature = 0.25f, MaxOutputTokens = 1536 });

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.25f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(1536);
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldBuildProviderRequestFromRuntimeConfiguration()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        var registeredTool = new Tool
        {
            Name = "search",
            Description = "Searches documents",
            InputSchema = schemaDocument.RootElement.Clone(),
        };

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(
                It.IsAny<ChatClientRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<ChatClientRequest, CancellationToken>(
                (request, _) => capturedRequest = request
            )
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "ok") }
                    )
            ));

        var session = CreateSession(
            llmClient.Object,
            toolRegistry.Object,
            configuration: new ChatSessionConfiguration
            {
                SystemPrompt = "Be concise.",
                Tools = new[] { registeredTool },
                RequestDefaults = new ChatRequestOptions
                {
                    Temperature = 0.4f,
                    MaxOutputTokens = 2048,
                },
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.SystemPrompt.Should().Be("Be concise.");
        capturedRequest.Tools.Should().ContainSingle();
        capturedRequest.Tools[0].Name.Should().Be("search");
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.4f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(2048);
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldIncludeConfiguredToolChoiceInProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.SetupGet(r => r.EnabledTools).Returns(Array.Empty<Tool>());
        toolRegistry.SetupGet(r => r.AllTools).Returns(Array.Empty<Tool>());

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-1",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "ok") }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        session.SetRequestDefaults(new ChatRequestOptions { ToolChoice = ChatToolChoice.ForTool("search") });

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.ToolChoice.Should().BeEquivalentTo(ChatToolChoice.ForTool("search"));
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldBuildProviderRequestFromSessionState()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        var registeredTool = new Tool
        {
            Name = "search",
            Description = "Searches documents",
            InputSchema = schemaDocument.RootElement.Clone(),
        };

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(
                It.IsAny<ChatClientRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<ChatClientRequest, CancellationToken>(
                (request, _) => capturedRequest = request
            )
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "ok") }
                )
            ));

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        session.SetSystemPrompt("Be concise.");
        session.RegisterTools(new[] { registeredTool });
        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.SystemPrompt.Should().Be("Be concise.");
        capturedRequest.Tools.Should().ContainSingle();
        capturedRequest.Tools[0].Name.Should().Be("search");
        capturedRequest.Transcript.OfType<UserChatEntry>().Single().Content.Should().Be("hello");
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldCompactTranscriptBeforeBuildingProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var compactor = new EntryCountChatTranscriptCompactor(
            new ChatTranscriptCompactionOptions
            {
                MaxEntryCount = 4,
                PreservedRecentEntryCount = 2,
                SummaryEntryCount = 6,
            }
        );

        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-5), "first question", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                DateTimeOffset.UtcNow.AddMinutes(-4),
                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "first answer") },
                "turn-1"
            ),
            new UserChatEntry("user-2", DateTimeOffset.UtcNow.AddMinutes(-3), "second question", "turn-2"),
            new AssistantChatEntry(
                "assistant-2",
                DateTimeOffset.UtcNow.AddMinutes(-2),
                new AssistantContentBlock[] { new TextAssistantBlock("text-2", "second answer") },
                "turn-2"
            ),
        };

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(It.IsAny<ChatClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatClientRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(
                CreateResultStream(
                    new ChatClientAssistantTurn(
                        "turn-3",
                        "openai",
                        "gpt-5",
                        new AssistantContentBlock[] { new TextAssistantBlock("text-3", "current answer") }
                    )
                )
            );

        var session = CreateSession(llmClient.Object, toolRegistry.Object, transcriptCompactor: compactor);

        await session.LoadTranscriptAsync(transcript);
        await session.SendUserMessageAsync("current question");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Transcript.Should().HaveCount(4);
        capturedRequest.Transcript[0].Should().BeOfType<AssistantChatEntry>();
        capturedRequest.Transcript[1].Id.Should().Be("user-2");
        capturedRequest.Transcript[2].Id.Should().Be("assistant-2");
        capturedRequest.Transcript[3].Should().BeOfType<UserChatEntry>().Which.Content.Should().Be("current question");

        session.Transcript.Should().HaveCount(6);
        session.Transcript.Select(entry => entry.Id).Should().Contain("user-1");
        session.Transcript.Select(entry => entry.Id).Should().Contain("assistant-1");
    }

    [Fact]
    public async Task ResetConversation_ShouldClearTranscriptWithoutCallingProvider()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-1), "hello", "turn-1"),
        };

        var session = CreateSession(llmClient.Object, toolRegistry.Object);

        await session.LoadTranscriptAsync(transcript);

        session.ResetConversation();

        session.Transcript.Should().BeEmpty();
        llmClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RegisterTools_ShouldAffectNextProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        var session = CreateSession(llmClient.Object, toolRegistry.Object);
        var tools = new[]
        {
            new Tool
            {
                Name = "calculator_add",
                Description = "Adds numbers",
                InputSchema = schemaDocument.RootElement.Clone(),
            },
        };

        ChatClientRequest? capturedRequest = null;
        llmClient
            .Setup(c => c.SendAsync(
                It.IsAny<ChatClientRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<ChatClientRequest, CancellationToken>(
                (request, _) => capturedRequest = request
            )
            .Returns(
                CreateResultStream(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "ok") }
                )
            ));

        session.RegisterTools(tools);
        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Tools.Should().ContainSingle();
        capturedRequest.Tools[0].Name.Should().Be("calculator_add");
    }

    private static ChatCompletionStream CreateResultStream(ChatClientTurnResult result) =>
        ChatCompletionStream.FromResult(result);

    private static ChatCompletionStream CreateStreamingResultStream(
        IReadOnlyList<ChatClientAssistantTurn> updates,
        ChatClientTurnResult result
    ) => ChatCompletionStream.FromStreaming(updates, result);

    private static ChatSession CreateSession(
        IChatClient llmClient,
        IToolRegistry toolRegistry,
        IToolExecutor? toolExecutor = null,
        ChatSessionConfiguration? configuration = null,
        IChatTranscriptCompactor? transcriptCompactor = null
    ) =>
        configuration == null
            ? new ChatSession(
                llmClient,
                toolExecutor ?? Mock.Of<IToolExecutor>(),
                NullLogger<ChatSession>.Instance,
                transcriptCompactor
            )
            : new ChatSession(
                llmClient,
                toolExecutor ?? Mock.Of<IToolExecutor>(),
                NullLogger<ChatSession>.Instance,
                configuration,
                transcriptCompactor
            );

    private static ToolInvocationResult CreateToolResult(
        string toolCallId,
        string toolName,
        params string[] text
    ) =>
        new(
            toolCallId,
            toolName,
            false,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

    private static ToolInvocationResult CreateErrorToolResult(
        string toolCallId,
        string toolName,
        params string[] text
    ) =>
        new(
            toolCallId,
            toolName,
            true,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

    private static Tool CreateToolDescriptor(string name, string description)
    {
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = schemaDocument.RootElement.Clone(),
        };
    }

    private static ToolCallResult CreateMcpToolCallResult(params string[] text) =>
        new()
        {
            IsError = false,
            Content = text.Select(fragment => new TextContent { Text = fragment }).ToArray(),
        };

    private sealed class TestLocalTool(
        Tool descriptor,
        Func<RuntimeToolInvocation, CancellationToken, Task<ToolInvocationResult>> executeAsync
    ) : ILocalTool
    {
        public Tool Descriptor { get; } = descriptor;

        public Task<ToolInvocationResult> ExecuteAsync(
            RuntimeToolInvocation invocation,
            CancellationToken cancellationToken = default
        ) => executeAsync(invocation, cancellationToken);
    }
}
