using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat.Repositories;
using Mcp.Net.WebUi.DTOs;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.WebUi.Chat;

public class ChatRepositoryTranscriptDtoTests
{
    [Fact]
    public async Task GetChatMessagesAsync_ShouldReturnTypedTranscriptEntryDtosWithOrderedAssistantBlocks()
    {
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-3), "hello", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                DateTimeOffset.UtcNow.AddMinutes(-2),
                new AssistantContentBlock[]
                {
                    new ReasoningAssistantBlock(
                        "reasoning-1",
                        "Think first.",
                        ReasoningVisibility.Visible,
                        "sig-1"
                    ),
                    new TextAssistantBlock("text-1", "Hello"),
                    new ToolCallAssistantBlock(
                        "tool-1",
                        "call-1",
                        "search",
                        new Dictionary<string, object?> { ["query"] = "weather" }
                    ),
                },
                "turn-1",
                "anthropic",
                "claude-sonnet-4-6",
                "end_turn",
                new ChatUsage(
                    11,
                    7,
                    18,
                    new Dictionary<string, int> { ["cacheCreationInputTokens"] = 4 }
                )
            ),
            new ToolResultChatEntry(
                "tool-result-1",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "call-1",
                "search",
                new ToolInvocationResult(
                    "call-1",
                    "search",
                    false,
                    new[] { "sunny" },
                    structured: null,
                    resourceLinks: Array.Empty<ToolResultResourceLink>(),
                    metadata: null
                ),
                false,
                "turn-1",
                "anthropic",
                "claude-sonnet-4-6"
            ),
            new ErrorChatEntry(
                "error-1",
                DateTimeOffset.UtcNow,
                ChatErrorSource.Provider,
                "provider error",
                Code: "provider_error",
                Details: "boom",
                TurnId: "turn-2",
                Provider: "openai",
                Model: "gpt-5"
            ),
        };

        var historyManager = new Mock<IChatHistoryManager>();
        historyManager.Setup(m => m.GetSessionTranscriptAsync("session-1")).ReturnsAsync(transcript);

        var repository = new ChatRepository(
            NullLogger<ChatRepository>.Instance,
            historyManager.Object,
            CreateNotifier()
        );

        var messages = await repository.GetChatMessagesAsync("session-1");

        messages.Should().HaveCount(4);
        messages[0].Should().BeOfType<UserChatTranscriptEntryDto>();

        var assistant = messages[1].Should().BeOfType<AssistantChatTranscriptEntryDto>().Which;
        assistant.SessionId.Should().Be("session-1");
        assistant.Provider.Should().Be("anthropic");
        assistant.Model.Should().Be("claude-sonnet-4-6");
        assistant.StopReason.Should().Be("end_turn");
        assistant.Usage.Should().NotBeNull();
        assistant.Usage!.TotalTokens.Should().Be(18);
        assistant.Usage.AdditionalCounts.Should().Contain("cacheCreationInputTokens", 4);
        assistant.Blocks.Should().HaveCount(3);
        assistant.Blocks[0]
            .Should()
            .BeOfType<ReasoningAssistantContentBlockDto>()
            .Which.Visibility.Should()
            .Be("visible");
        assistant.Blocks[1]
            .Should()
            .BeOfType<TextAssistantContentBlockDto>()
            .Which.Text.Should()
            .Be("Hello");
        assistant.Blocks[2]
            .Should()
            .BeOfType<ToolCallAssistantContentBlockDto>()
            .Which.ToolCallId.Should()
            .Be("call-1");

        messages[2].Should().BeOfType<ToolResultChatTranscriptEntryDto>();
        messages[3].Should().BeOfType<ErrorChatTranscriptEntryDto>();

        JsonSerializer.Serialize<IReadOnlyList<ChatTranscriptEntryDto>>(messages)
            .Should()
            .Contain("\"kind\":\"assistant\"")
            .And.Contain("\"kind\":\"reasoning\"")
            .And.Contain("\"kind\":\"toolCall\"")
            .And.Contain("\"StopReason\":\"end_turn\"");
    }

    private static SessionNotifier CreateNotifier() =>
        new(
            new Mock<IHubContext<ChatHub>>().Object,
            NullLogger<SessionNotifier>.Instance
        );
}
