using System.Text;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Compaction;

public sealed class EntryCountChatTranscriptCompactor : IChatTranscriptCompactor
{
    private const int MaxPreviewLength = 160;
    private readonly ChatTranscriptCompactionOptions _options;

    public static EntryCountChatTranscriptCompactor Default { get; } =
        new(new ChatTranscriptCompactionOptions());

    public EntryCountChatTranscriptCompactor(ChatTranscriptCompactionOptions? options = null)
    {
        _options = options ?? new ChatTranscriptCompactionOptions();

        if (_options.MaxEntryCount < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxEntryCount must be at least 2."
            );
        }

        if (_options.PreservedRecentEntryCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PreservedRecentEntryCount must be at least 1."
            );
        }

        if (_options.SummaryEntryCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "SummaryEntryCount must be at least 1."
            );
        }
    }

    public Task<IReadOnlyList<ChatTranscriptEntry>> CompactAsync(
        IReadOnlyList<ChatTranscriptEntry> transcript,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(transcript);
        cancellationToken.ThrowIfCancellationRequested();

        if (transcript.Count <= _options.MaxEntryCount)
        {
            return Task.FromResult(transcript);
        }

        var safeStartIndex = FindSafeStartIndex(transcript);
        if (safeStartIndex <= 0)
        {
            return Task.FromResult(transcript);
        }

        var entriesToCompact = transcript.Take(safeStartIndex).ToArray();
        if (entriesToCompact.Length == 0)
        {
            return Task.FromResult(transcript);
        }

        var compactedTranscript = new List<ChatTranscriptEntry>(transcript.Count - safeStartIndex + 1)
        {
            CreateSummaryEntry(entriesToCompact),
        };
        compactedTranscript.AddRange(transcript.Skip(safeStartIndex));

        return Task.FromResult<IReadOnlyList<ChatTranscriptEntry>>(compactedTranscript);
    }

    private int FindSafeStartIndex(IReadOnlyList<ChatTranscriptEntry> transcript)
    {
        var desiredStartIndex = Math.Max(0, transcript.Count - _options.PreservedRecentEntryCount);

        for (var index = desiredStartIndex; index > 0; index--)
        {
            if (transcript[index] is UserChatEntry)
            {
                return index;
            }
        }

        return transcript[0] is UserChatEntry ? 0 : -1;
    }

    private AssistantChatEntry CreateSummaryEntry(IReadOnlyList<ChatTranscriptEntry> entriesToCompact)
    {
        var summarizedEntries = entriesToCompact.TakeLast(_options.SummaryEntryCount).ToArray();
        var summaryLines = summarizedEntries
            .Select(DescribeEntry)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var lastEntry = entriesToCompact[^1];
        var summaryId = $"compaction-summary-{lastEntry.Id}";
        var textBlockId = $"compaction-summary-text-{lastEntry.Id}";

        var builder = new StringBuilder("Summary of earlier conversation");
        if (entriesToCompact.Count > summarizedEntries.Length)
        {
            builder.Append(" (showing the most recent summarized entries)");
        }

        if (summaryLines.Length > 0)
        {
            builder.Append(':');

            foreach (var line in summaryLines)
            {
                builder.AppendLine();
                builder.Append("- ");
                builder.Append(line);
            }
        }

        return new AssistantChatEntry(
            summaryId,
            lastEntry.Timestamp,
            new AssistantContentBlock[] { new TextAssistantBlock(textBlockId, builder.ToString()) },
            lastEntry.TurnId
        );
    }

    private static string DescribeEntry(ChatTranscriptEntry entry) =>
        entry switch
        {
            UserChatEntry user => $"User: {Truncate(user.Content)}",
            AssistantChatEntry assistant => DescribeAssistantEntry(assistant),
            ToolResultChatEntry toolResult => DescribeToolResultEntry(toolResult),
            ErrorChatEntry error => $"Error: {Truncate(error.Message)}",
            _ => string.Empty,
        };

    private static string DescribeAssistantEntry(AssistantChatEntry assistant)
    {
        var text = string.Join(
            " ",
            assistant
                .Blocks
                .OfType<TextAssistantBlock>()
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
        );
        var requestedTools = assistant
            .Blocks
            .OfType<ToolCallAssistantBlock>()
            .Select(block => block.ToolName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(text) && requestedTools.Length > 0)
        {
            return $"Assistant: {Truncate(text)} Requested tools: {string.Join(", ", requestedTools)}";
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            return $"Assistant: {Truncate(text)}";
        }

        if (requestedTools.Length > 0)
        {
            return $"Assistant requested tools: {string.Join(", ", requestedTools)}";
        }

        return "Assistant responded.";
    }

    private static string DescribeToolResultEntry(ToolResultChatEntry toolResult)
    {
        var preview = Truncate(string.Join(" ", toolResult.Result.Text));

        if (toolResult.IsError)
        {
            return string.IsNullOrWhiteSpace(preview)
                ? $"Tool {toolResult.ToolName} failed."
                : $"Tool {toolResult.ToolName} failed: {preview}";
        }

        return string.IsNullOrWhiteSpace(preview)
            ? $"Tool {toolResult.ToolName} returned a result."
            : $"Tool {toolResult.ToolName} returned: {preview}";
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        if (normalized.Length <= MaxPreviewLength)
        {
            return normalized;
        }

        return normalized[..(MaxPreviewLength - 3)] + "...";
    }
}
