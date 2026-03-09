using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Events;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Replay;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Mcp.Net.Tests.LLM.Core;

public class ChatSessionTests
{
    [Fact]
    public async Task SendUserMessageAsync_ShouldAppendUserTranscriptEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendMessageAsync("Hi there"))
            .ReturnsAsync(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Hello from the model") }
                )
            );

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
            .Setup(c => c.SendMessageAsync("Hi there"))
            .ReturnsAsync(
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
                    }
                )
            );

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
        assistantEntry.Blocks.Should().HaveCount(2);
        assistantEntry.Blocks[0].Should().BeOfType<ReasoningAssistantBlock>();
        assistantEntry.Blocks[1].Should().BeOfType<TextAssistantBlock>();
    }

    [Fact]
    public async Task SendUserMessageAsync_ProviderFailure_ShouldAppendErrorEntry()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();

        llmClient
            .Setup(c => c.SendMessageAsync("Hi there"))
            .ReturnsAsync(
                new ChatClientFailure(
                    ChatErrorSource.Provider,
                    "Provider failure",
                    Code: "provider_error",
                    Provider: "openai",
                    Model: "gpt-5"
                )
            );

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
            .Setup(c => c.SendMessageAsync("Please add numbers"))
            .ReturnsAsync(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { toolInvocationBlock }
                )
            );

        llmClient
            .Setup(c => c.SendToolResultsAsync(It.IsAny<IEnumerable<ToolInvocationResult>>()))
            .ReturnsAsync(
                new ChatClientAssistantTurn(
                    "turn-2",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Result is 5") }
                )
            );

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
            .Setup(c => c.SendMessageAsync("Hi there"))
            .ReturnsAsync(
                new ChatClientAssistantTurn(
                    "turn-1",
                    "openai",
                    "gpt-5",
                    new AssistantContentBlock[] { new TextAssistantBlock("text-1", "Hello from the model") }
                )
            );

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
    public async Task LoadTranscriptAsync_ShouldPopulateTranscriptAndReplayClientHistory()
    {
        var llmClient = new Mock<IChatClient>();
        var mcpClient = new Mock<IMcpClient>();
        var toolRegistry = new Mock<IToolRegistry>();
        var replayTransformer = new Mock<IChatTranscriptReplayTransformer>();

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

        var replayTarget = new ReplayTarget("openai", "gpt-5");
        var replayTranscript = new ProviderReplayTranscript(replayTarget, transcript);

        llmClient.Setup(c => c.GetReplayTarget()).Returns(replayTarget);
        replayTransformer
            .Setup(t => t.Transform(
                It.Is<IReadOnlyList<ChatTranscriptEntry>>(entries => entries.SequenceEqual(transcript)),
                replayTarget
            ))
            .Returns(replayTranscript);

        var session = new ChatSession(
            llmClient.Object,
            mcpClient.Object,
            toolRegistry.Object,
            NullLogger<ChatSession>.Instance,
            replayTransformer.Object
        );

        await session.LoadTranscriptAsync(transcript);

        session.Transcript.Should().Equal(transcript);
        llmClient.Verify(c => c.LoadReplayTranscript(replayTranscript), Times.Once);
    }
}
