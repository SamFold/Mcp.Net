using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;

namespace Mcp.Net.Tests.LLM.OpenAI;

public class OpenAiChatClientTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldPopulateUsageAndStopReasonOnAssistantTurn()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

#pragma warning disable OPENAI001
        var completionInvoker = new ReturningChatCompletionInvoker(
            OpenAIChatModelFactory.ChatCompletion(
                finishReason: ChatFinishReason.Stop,
                content: new ChatMessageContent([ChatMessageContentPart.CreateTextPart("Hello")]),
                role: ChatMessageRole.Assistant,
                createdAt: DateTimeOffset.UtcNow,
                model: "gpt-5",
                usage: OpenAIChatModelFactory.ChatTokenUsage(
                    outputTokenCount: 12,
                    inputTokenCount: 8,
                    totalTokenCount: 20,
                    outputTokenDetails: OpenAIChatModelFactory.ChatOutputTokenUsageDetails(
                        reasoningTokenCount: 4
                    ),
                    inputTokenDetails: OpenAIChatModelFactory.ChatInputTokenUsageDetails(
                        cachedTokenCount: 3
                    )
                )
            )
        );
#pragma warning restore OPENAI001
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        var result = await client.SendMessageAsync("hello");

        var assistantTurn = result.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.StopReason.Should().Be("stop");
        var usage = assistantTurn.Usage;
        usage.Should().NotBeNull();
        usage!.InputTokens.Should().Be(8);
        usage.OutputTokens.Should().Be(12);
        usage.TotalTokens.Should().Be(20);
        usage.AdditionalCounts.Should().Contain("cachedInputTokens", 3);
        usage.AdditionalCounts.Should().Contain("reasoningOutputTokens", 4);
    }

    [Fact]
    public async Task SendMessageAsync_WithConfiguredSystemPrompt_ShouldIncludePromptInFirstOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages.Should().HaveCount(2);
        ExtractSystemPrompt(completionInvoker.CapturedMessages!).Should().Be(options.SystemPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutConfiguredSystemPrompt_ShouldNotInjectPromptInFirstOutboundRequest()
    {
        // Arrange
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        // Act
        await client.SendMessageAsync("hello");

        // Assert
        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages.Should().HaveCount(1);
        completionInvoker.CapturedMessages![0].Should().BeOfType<UserChatMessage>();
        client.GetSystemPrompt().Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WithConfiguredMaxOutputTokens_ShouldSetCompletionOption()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
            MaxOutputTokens = 321,
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        await client.SendMessageAsync("hello");

        completionInvoker.CapturedOptions.Should().NotBeNull();
        completionInvoker.CapturedOptions!.MaxOutputTokenCount.Should().Be(321);
    }

    [Fact]
    public void ParseToolArguments_WithNestedObjectAndArray_ShouldPreserveStructuredValues()
    {
        // Arrange
        const string argumentsJson =
            """
            {
              "query": "bugs",
              "filter": {
                "status": "open",
                "labels": ["server", "urgent"]
              },
              "tags": ["triage", "backend"]
            }
            """;

        // Act
        var arguments = ParseToolArguments(argumentsJson);

        // Assert
        arguments.Should().ContainKey("query").WhoseValue.Should().Be("bugs");
        JsonSerializer.Serialize(arguments["filter"])
            .Should()
            .Be("""{"status":"open","labels":["server","urgent"]}""");
        JsonSerializer.Serialize(arguments["tags"])
            .Should()
            .Be("""["triage","backend"]""");
    }

    [Fact]
    public async Task LoadReplayTranscript_ShouldIncludePriorHistoryInFirstOutboundRequest()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        client.LoadReplayTranscript(
            new Mcp.Net.LLM.Replay.ProviderReplayTranscript(
                client.GetReplayTarget(),
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "Earlier user", "turn-1"),
                    new AssistantChatEntry(
                        "assistant-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Earlier answer") },
                        "turn-1",
                        "openai",
                        "gpt-5"
                    ),
                }
            )
        );

        await client.SendMessageAsync("New question");

        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages!.Should().HaveCount(4);
        completionInvoker.CapturedMessages[1].Should().BeOfType<UserChatMessage>();
        completionInvoker.CapturedMessages[2].Should().BeOfType<AssistantChatMessage>();
        completionInvoker.CapturedMessages[3].Should().BeOfType<UserChatMessage>();
        completionInvoker.CapturedMessages[2].Content.Single().Text.Should().Be("Earlier answer");
        completionInvoker.CapturedMessages[3].Content.Single().Text.Should().Be("New question");
    }

    [Fact]
    public async Task LoadReplayTranscript_WithAssistantTextAndToolCall_ShouldKeepSingleAssistantHistoryMessage()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var completionInvoker = new CapturingChatCompletionInvoker();
        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        client.LoadReplayTranscript(
            new Mcp.Net.LLM.Replay.ProviderReplayTranscript(
                client.GetReplayTarget(),
                new ChatTranscriptEntry[]
                {
                    new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-3), "Use the tool", "turn-1"),
                    new AssistantChatEntry(
                        "assistant-1",
                        DateTimeOffset.UtcNow.AddMinutes(-2),
                        new AssistantContentBlock[]
                        {
                            new TextAssistantBlock("text-1", "Checking"),
                            new ToolCallAssistantBlock(
                                "tool-1",
                                "call_probe",
                                "record_probe",
                                new Dictionary<string, object?>
                                {
                                    ["status"] = "ok",
                                    ["code"] = 200d,
                                }
                            ),
                        },
                        "turn-1",
                        "openai",
                        "gpt-5"
                    ),
                    new ToolResultChatEntry(
                        "tool-result-1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        "call_probe",
                        "record_probe",
                        new ToolInvocationResult(
                            "call_probe",
                            "record_probe",
                            false,
                            new[] { "saved" },
                            structured: null,
                            resourceLinks: Array.Empty<ToolResultResourceLink>(),
                            metadata: null
                        ),
                        false,
                        "turn-1"
                    ),
                }
            )
        );

        await client.SendMessageAsync("continue");

        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages!.Should().HaveCount(5);
        completionInvoker.CapturedMessages[2].Should().BeOfType<AssistantChatMessage>();
        completionInvoker.CapturedMessages[3].Should().BeOfType<ToolChatMessage>();
        completionInvoker.CapturedMessages[4].Should().BeOfType<UserChatMessage>();

        var assistantMessage = (AssistantChatMessage)completionInvoker.CapturedMessages[2];
        assistantMessage.Content.Should().ContainSingle();
        assistantMessage.Content.Single().Text.Should().Be("Checking");
        assistantMessage.ToolCalls.Should().ContainSingle();
        assistantMessage.ToolCalls.Single().Id.Should().Be("call_probe");
        assistantMessage.ToolCalls.Single().FunctionName.Should().Be("record_probe");
    }

    [Fact]
    public async Task SendMessageAsync_WithAssistantTurnUpdates_ShouldReportPartialTextSnapshots()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var updateTimestamp = DateTimeOffset.UtcNow;
        var completionInvoker = new StreamingChatCompletionInvoker(
            CreateTextUpdate("stream-1", "Hel", updateTimestamp),
            CreateTextUpdate(
                "stream-1",
                "lo",
                updateTimestamp,
                ChatFinishReason.Stop,
                OpenAIChatModelFactory.ChatTokenUsage(
                    outputTokenCount: 2,
                    inputTokenCount: 6,
                    totalTokenCount: 8,
                    outputTokenDetails: OpenAIChatModelFactory.ChatOutputTokenUsageDetails(
                        reasoningTokenCount: 1
                    )
                )
            )
        );

        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        var streamedTurns = new List<ChatClientAssistantTurn>();
        var result = await client.SendMessageAsync(
            "hello",
            new CapturingProgress<ChatClientAssistantTurn>(streamedTurns)
        );

        completionInvoker.StreamingCalled.Should().BeTrue();
        completionInvoker.CapturedMessages.Should().NotBeNull();
        completionInvoker.CapturedMessages.Should().HaveCount(2);

        streamedTurns.Should().HaveCount(2);
        streamedTurns.Select(turn => turn.Id).Distinct().Should().ContainSingle();
        streamedTurns.Select(turn => ((TextAssistantBlock)turn.Blocks.Single()).Text)
            .Should()
            .ContainInOrder("Hel", "Hello");
        streamedTurns[^1].StopReason.Should().Be("stop");
        var streamedUsage = streamedTurns[^1].Usage;
        streamedUsage.Should().NotBeNull();
        streamedUsage!.InputTokens.Should().Be(6);
        streamedUsage.OutputTokens.Should().Be(2);
        streamedUsage.TotalTokens.Should().Be(8);
        streamedUsage.AdditionalCounts.Should().Contain("reasoningOutputTokens", 1);

        var assistantTurn = result.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.Id.Should().Be(streamedTurns[0].Id);
        assistantTurn.StopReason.Should().Be("stop");
        assistantTurn.Usage.Should().BeEquivalentTo(streamedTurns[^1].Usage);
        assistantTurn.Blocks.Should().ContainSingle();
        assistantTurn.Blocks[0]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("Hello");
    }

    [Fact]
    public async Task SendMessageAsync_WithAssistantTurnUpdates_ShouldAccumulateToolCallArguments()
    {
        var options = new ChatClientOptions
        {
            ApiKey = "test",
            Model = "gpt-5",
            SystemPrompt = "OpenAI configured prompt",
        };

        var updateTimestamp = DateTimeOffset.UtcNow;
        var completionInvoker = new StreamingChatCompletionInvoker(
            CreateTextUpdate("stream-2", "Checking", updateTimestamp),
            CreateToolUpdate("stream-2", "call-1", "search", """{"query":"weath""", updateTimestamp),
            CreateToolUpdate(
                "stream-2",
                "call-1",
                "search",
                """er"}""",
                updateTimestamp,
                ChatFinishReason.ToolCalls,
                OpenAIChatModelFactory.ChatTokenUsage(
                    outputTokenCount: 5,
                    inputTokenCount: 10,
                    totalTokenCount: 15
                )
            )
        );

        var client = new OpenAiChatClient(
            options,
            NullLogger<OpenAiChatClient>.Instance,
            completionInvoker
        );

        var streamedTurns = new List<ChatClientAssistantTurn>();
        var result = await client.SendMessageAsync(
            "find weather",
            new CapturingProgress<ChatClientAssistantTurn>(streamedTurns)
        );

        streamedTurns.Should().NotBeEmpty();
        var finalStreamedTurn = streamedTurns[^1];
        finalStreamedTurn.Blocks.Should().HaveCount(2);
        finalStreamedTurn.Blocks[0]
            .Should()
            .BeOfType<TextAssistantBlock>()
            .Which.Text.Should()
            .Be("Checking");
        finalStreamedTurn.Blocks[1]
            .Should()
            .BeOfType<ToolCallAssistantBlock>()
            .Which.Arguments["query"].Should()
            .Be("weather");

        var assistantTurn = result.Should().BeOfType<ChatClientAssistantTurn>().Subject;
        assistantTurn.Id.Should().Be(finalStreamedTurn.Id);
        assistantTurn.StopReason.Should().Be("tool_calls");
        assistantTurn.Usage.Should().NotBeNull();
        assistantTurn.Usage!.TotalTokens.Should().Be(15);
        assistantTurn.Blocks.Should().HaveCount(2);
        assistantTurn.Blocks[1]
            .Should()
            .BeOfType<ToolCallAssistantBlock>()
            .Which.ToolCallId.Should()
            .Be("call-1");
    }

    private static string ExtractSystemPrompt(IReadOnlyList<ChatMessage> messages)
    {
        messages[0].Should().BeOfType<SystemChatMessage>();
        return messages[0].Content.Single().Text;
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArguments(string argumentsJson)
    {
        var parseMethod = typeof(OpenAiChatClient).GetMethod(
            "ParseToolArguments",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        parseMethod.Should().NotBeNull();

        return (IReadOnlyDictionary<string, object?>)
            parseMethod!.Invoke(null, new object[] { argumentsJson })!;
    }

    private sealed class CapturingChatCompletionInvoker : IOpenAiChatCompletionInvoker
    {
        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }
        public ChatCompletionOptions? CapturedOptions { get; private set; }

        public ChatCompletion CompleteChat(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options
        )
        {
            CapturedMessages = messages.ToList();
            CapturedOptions = options;
            throw new InvalidOperationException("Capture complete");
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            CancellationToken cancellationToken
        )
        {
            CapturedMessages = messages.ToList();
            CapturedOptions = options;
            throw new InvalidOperationException("Streaming capture not expected");
        }
    }

    private sealed class ReturningChatCompletionInvoker : IOpenAiChatCompletionInvoker
    {
        private readonly ChatCompletion _completion;

        public ReturningChatCompletionInvoker(ChatCompletion completion)
        {
            _completion = completion;
        }

        public ChatCompletion CompleteChat(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options
        ) => _completion;

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Streaming completion not expected");
    }

    private sealed class StreamingChatCompletionInvoker : IOpenAiChatCompletionInvoker
    {
        private readonly IReadOnlyList<StreamingChatCompletionUpdate> _updates;

        public StreamingChatCompletionInvoker(params StreamingChatCompletionUpdate[] updates)
        {
            _updates = updates;
        }

        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }
        public ChatCompletionOptions? CapturedOptions { get; private set; }

        public bool StreamingCalled { get; private set; }

        public ChatCompletion CompleteChat(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options
        )
        {
            CapturedMessages = messages.ToList();
            CapturedOptions = options;
            throw new InvalidOperationException("Non-streaming completion not expected");
        }

        public async IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            ChatClient client,
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            StreamingCalled = true;
            CapturedMessages = messages.ToList();
            CapturedOptions = options;

            foreach (var update in _updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
        }
    }

    private sealed class CapturingProgress<T> : IProgress<T>
    {
        private readonly ICollection<T> _items;

        public CapturingProgress(ICollection<T> items)
        {
            _items = items;
        }

        public void Report(T value)
        {
            _items.Add(value);
        }
    }

    private static StreamingChatCompletionUpdate CreateTextUpdate(
        string id,
        string text,
        DateTimeOffset createdAt,
        ChatFinishReason? finishReason = null,
        ChatTokenUsage? usage = null
    ) =>
        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            id,
            new ChatMessageContent([ChatMessageContentPart.CreateTextPart(text)]),
            null,
            Array.Empty<StreamingChatToolCallUpdate>(),
            ChatMessageRole.Assistant,
            null,
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            finishReason,
            createdAt,
            "gpt-5",
            null,
            usage!
        );

    private static StreamingChatCompletionUpdate CreateToolUpdate(
        string id,
        string toolCallId,
        string toolName,
        string argumentsFragment,
        DateTimeOffset createdAt,
        ChatFinishReason? finishReason = null,
        ChatTokenUsage? usage = null
    ) =>
        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            id,
            new ChatMessageContent(Array.Empty<ChatMessageContentPart>()),
            null,
            [
                OpenAIChatModelFactory.StreamingChatToolCallUpdate(
                    0,
                    toolCallId,
                    ChatToolCallKind.Function,
                    toolName,
                    BinaryData.FromString(argumentsFragment)
                ),
            ],
            ChatMessageRole.Assistant,
            null,
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            Array.Empty<ChatTokenLogProbabilityDetails>(),
            finishReason,
            createdAt,
            "gpt-5",
            null,
            usage!
        );
}
