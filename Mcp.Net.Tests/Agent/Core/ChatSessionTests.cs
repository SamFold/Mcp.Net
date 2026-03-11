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
using Xunit;

namespace Mcp.Net.Tests.Agent.Core;

public class ChatSessionTests
{
    [Fact]
    public async Task SendUserMessageAsync_ShouldAppendUserTranscriptEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
        var mcpClient = new Mock<IMcpClient>();
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
                    cancellationToken.Should().Be(CancellationToken.None);
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
    public async Task SendUserMessageAsync_ProviderFailure_ShouldAppendErrorEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
    public async Task SendUserMessageAsync_ToolExecution_ShouldEmitActivityAndAppendToolResultEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

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

        toolRegistry.Setup(r => r.GetToolByName("calculator_add")).Returns(tool);

        var toolCallResult = new ToolCallResult
        {
            IsError = false,
            Content = new ContentBase[] { new TextContent { Text = "5" } },
        };

        mcpClient
            .Setup(c => c.CallTool("calculator_add", It.IsAny<object?>()))
            .ReturnsAsync(toolCallResult);

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
    public async Task SendUserMessageAsync_ShouldTransitionActivityWaitingForProviderThenIdle()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        var activities = new List<ChatSessionActivity>();
        session.ActivityChanged += (_, args) => activities.Add(args.Activity);

        await session.SendUserMessageAsync("Hi there");

        activities.Should().ContainInOrder(ChatSessionActivity.WaitingForProvider, ChatSessionActivity.Idle);
    }

    [Fact]
    public async Task LoadTranscriptAsync_ShouldPopulateTranscriptWithoutCallingProvider()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        await session.LoadTranscriptAsync(transcript);

        session.Transcript.Should().Equal(transcript);
        llmClient.VerifyNoOtherCalls();
    }

    [Fact]
    public void SetSystemPrompt_ShouldUpdateSessionState()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        session.SetSystemPrompt("Be concise.");

        session.GetSystemPrompt().Should().Be("Be concise.");
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldIncludeConfiguredExecutionDefaultsInProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        session.SetExecutionDefaults(
            new AgentExecutionDefaults
            {
                Temperature = 0.25f,
                MaxOutputTokens = 1536,
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.25f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(1536);
    }

    [Fact]
    public async Task CreateFromAgentAsync_ShouldHydrateLegacyExecutionDefaultsIntoProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var factory = new Mock<IAgentFactory>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.SetupGet(r => r.EnabledTools).Returns(Array.Empty<Tool>());
        toolRegistry.SetupGet(r => r.AllTools).Returns(Array.Empty<Tool>());

        var agent = new AgentDefinition
        {
            Provider = LlmProvider.OpenAI,
            ModelName = "gpt-5",
            SystemPrompt = "Be concise.",
            Parameters = new Dictionary<string, object>
            {
                ["temperature"] = 0.4f,
                ["max_tokens"] = 2048,
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

        factory
            .Setup(f => f.CreateClientFromAgentDefinitionAsync(agent))
            .ReturnsAsync(llmClient.Object);

        var session = await ChatSession.CreateFromAgentAsync(
            agent,
            factory.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.Temperature.Should().Be(0.4f);
        capturedRequest.Options.MaxOutputTokens.Should().Be(2048);
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldIncludeConfiguredToolChoiceInProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        session.SetExecutionDefaults(
            new AgentExecutionDefaults
            {
                ToolChoice = ChatToolChoice.ForTool("search"),
            }
        );

        await session.SendUserMessageAsync("hello");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Options.Should().NotBeNull();
        capturedRequest.Options!.ToolChoice.Should().BeEquivalentTo(ChatToolChoice.ForTool("search"));
    }

    [Fact]
    public async Task SendUserMessageAsync_ShouldBuildProviderRequestFromSessionState()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

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
        var mcpClient = new Mock<IMcpClient>();
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

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance,
            compactor
        );

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
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-1), "hello", "turn-1"),
        };

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );

        await session.LoadTranscriptAsync(transcript);

        session.ResetConversation();

        session.Transcript.Should().BeEmpty();
        llmClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RegisterTools_ShouldAffectNextProviderRequest()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{}");
        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance
        );
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
}
