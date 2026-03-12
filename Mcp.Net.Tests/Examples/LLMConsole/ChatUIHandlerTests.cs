using FluentAssertions;
using Mcp.Net.Agent.Events;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Examples.LLMConsole.UI;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Examples.LLMConsole;

[Collection(nameof(ConsoleOutputCollection))]
public class ChatUIHandlerTests
{
    [Fact]
    public void TranscriptChanged_ShouldPrintOnlyAssistantDeltaForStreamingUpdates()
    {
        var originalOut = Console.Out;
        var output = new StringWriter();

        try
        {
            Console.SetOut(output);

            var events = new TestChatSessionEvents();
            _ = new ChatUIHandler(new ChatUI(), events, NullLogger<ChatUIHandler>.Instance);
            output.GetStringBuilder().Clear();

            events.RaiseTranscriptChanged(
                new ChatTranscriptChangedEventArgs(
                    CreateAssistantEntry("assistant-1", "Hello"),
                    ChatTranscriptChangeKind.Added
                )
            );
            events.RaiseTranscriptChanged(
                new ChatTranscriptChangedEventArgs(
                    CreateAssistantEntry("assistant-1", "Hello world"),
                    ChatTranscriptChangeKind.Updated
                )
            );
            events.RaiseTranscriptChanged(
                new ChatTranscriptChangedEventArgs(
                    CreateAssistantEntry("assistant-1", "Hello world", stopReason: "stop"),
                    ChatTranscriptChangeKind.Updated
                )
            );
            events.RaiseActivityChanged(
                new ChatSessionActivityChangedEventArgs(ChatSessionActivity.Idle)
            );

            output.ToString().Should().Be($"Hello world{Environment.NewLine}{Environment.NewLine}");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static AssistantChatEntry CreateAssistantEntry(
        string id,
        string text,
        string? stopReason = null
    ) =>
        new(
            id,
            DateTimeOffset.UtcNow,
            new AssistantContentBlock[] { new TextAssistantBlock("text-1", text) },
            StopReason: stopReason
        );

    private sealed class TestChatSessionEvents : IChatSessionEvents
    {
        public event EventHandler<ChatTranscriptChangedEventArgs>? TranscriptChanged;

        public event EventHandler<ChatSessionActivityChangedEventArgs>? ActivityChanged;

        public event EventHandler<ToolCallActivityChangedEventArgs>? ToolCallActivityChanged
        {
            add { }
            remove { }
        }

        public void RaiseTranscriptChanged(ChatTranscriptChangedEventArgs args) =>
            TranscriptChanged?.Invoke(this, args);

        public void RaiseActivityChanged(ChatSessionActivityChangedEventArgs args) =>
            ActivityChanged?.Invoke(this, args);
    }
}

[CollectionDefinition(nameof(ConsoleOutputCollection), DisableParallelization = true)]
public sealed class ConsoleOutputCollection;
