using FluentAssertions;
using Mcp.Net.Agent.Compaction;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Tests.Agent.Compaction;

public class EntryCountChatTranscriptCompactorTests
{
    [Fact]
    public async Task CompactAsync_WhenTranscriptExceedsThreshold_ShouldInsertSummaryAndPreserveWholeRecentTurn()
    {
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
            new UserChatEntry("user-3", DateTimeOffset.UtcNow.AddMinutes(-1), "latest question", "turn-3"),
        };

        var compactor = new EntryCountChatTranscriptCompactor(
            new ChatTranscriptCompactionOptions
            {
                MaxEntryCount = 4,
                PreservedRecentEntryCount = 2,
                SummaryEntryCount = 6,
            }
        );

        var compacted = await compactor.CompactAsync(transcript);

        compacted.Should().HaveCount(4);
        compacted[0].Should().BeOfType<AssistantChatEntry>();
        compacted[1].Id.Should().Be("user-2");
        compacted[2].Id.Should().Be("assistant-2");
        compacted[3].Id.Should().Be("user-3");

        var summaryEntry = (AssistantChatEntry)compacted[0];
        var summaryText = summaryEntry.Blocks.OfType<TextAssistantBlock>().Single().Text;
        summaryText.Should().Contain("Summary of earlier conversation");
        summaryText.Should().Contain("User: first question");
        summaryText.Should().Contain("Assistant: first answer");
    }

    [Fact]
    public async Task CompactAsync_WhenTranscriptIsWithinThreshold_ShouldReturnOriginalEntries()
    {
        var transcript = new ChatTranscriptEntry[]
        {
            new UserChatEntry("user-1", DateTimeOffset.UtcNow.AddMinutes(-2), "hello", "turn-1"),
            new AssistantChatEntry(
                "assistant-1",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                new AssistantContentBlock[] { new TextAssistantBlock("text-1", "hi") },
                "turn-1"
            ),
        };

        var compactor = new EntryCountChatTranscriptCompactor(
            new ChatTranscriptCompactionOptions
            {
                MaxEntryCount = 4,
                PreservedRecentEntryCount = 2,
            }
        );

        var compacted = await compactor.CompactAsync(transcript);

        compacted.Should().Equal(transcript);
    }
}
