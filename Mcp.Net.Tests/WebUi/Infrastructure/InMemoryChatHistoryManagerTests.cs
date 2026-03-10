using System;
using System.Linq;
using FluentAssertions;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.WebUi.Infrastructure;

public class InMemoryChatHistoryManagerTests
{
    [Fact]
    public async Task UpsertTranscriptEntryAsync_ShouldReplaceExistingTranscriptEntryById()
    {
        var historyManager = new InMemoryChatHistoryManager(
            NullLogger<InMemoryChatHistoryManager>.Instance
        );

        await historyManager.CreateSessionAsync(
            "default",
            new ChatSessionMetadata
            {
                Id = "session-1",
                Title = "Session",
                Model = "gpt-5",
                Provider = LlmProvider.OpenAI,
            }
        );

        var partialEntry = new AssistantChatEntry(
            "assistant-1",
            DateTimeOffset.UtcNow,
            new AssistantContentBlock[]
            {
                new TextAssistantBlock("text-1", "Hel"),
            },
            "turn-1",
            "openai",
            "gpt-5"
        );

        var finalEntry = new AssistantChatEntry(
            "assistant-1",
            partialEntry.Timestamp,
            new AssistantContentBlock[]
            {
                new TextAssistantBlock("text-1", "Hello"),
            },
            "turn-1",
            "openai",
            "gpt-5"
        );

        await historyManager.AddTranscriptEntryAsync("session-1", partialEntry);
        await historyManager.UpsertTranscriptEntryAsync("session-1", finalEntry);

        var transcript = await historyManager.GetSessionTranscriptAsync("session-1");

        transcript.Should().HaveCount(1);
        transcript[0].Should().BeEquivalentTo(finalEntry);

        var metadata = await historyManager.GetSessionMetadataAsync("session-1");
        metadata.Should().NotBeNull();
        metadata!.LastMessagePreview.Should().Be("Hello");
    }
}
