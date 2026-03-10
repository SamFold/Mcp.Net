using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.LLM.Replay;

public sealed class ChatTranscriptReplayTransformer : IChatTranscriptReplayTransformer
{
    public ProviderReplayTranscript Transform(
        IReadOnlyList<ChatTranscriptEntry> transcript,
        ReplayTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(target);

        var replayEntries = new List<ChatTranscriptEntry>(transcript.Count);
        var lastSafeEntryId = default(string);
        var toolResultIds = transcript
            .OfType<ToolResultChatEntry>()
            .Select(entry => entry.ToolCallId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in transcript)
        {
            switch (entry)
            {
                case ErrorChatEntry:
                    continue;

                case UserChatEntry user:
                    replayEntries.Add(Clone(user));
                    lastSafeEntryId = user.Id;
                    break;

                case ToolResultChatEntry toolResult:
                    replayEntries.Add(Clone(toolResult));
                    lastSafeEntryId = toolResult.Id;
                    break;

                case AssistantChatEntry assistant:
                    var transformedAssistant = TransformAssistantEntry(assistant, target);
                    var unmatchedToolCalls = transformedAssistant.Blocks
                        .OfType<ToolCallAssistantBlock>()
                        .Where(block => !toolResultIds.Contains(block.ToolCallId))
                        .ToList();

                    if (
                        unmatchedToolCalls.Count > 0
                        && target.UnmatchedToolCallReplayMode
                            == UnmatchedToolCallReplayMode.TruncateAtLastSafeEntry
                    )
                    {
                        return new ProviderReplayTranscript(
                            target,
                            replayEntries,
                            IsTruncated: true,
                            TruncatedAfterEntryId: lastSafeEntryId
                        );
                    }

                    if (transformedAssistant.Blocks.Count > 0)
                    {
                        replayEntries.Add(transformedAssistant);
                        lastSafeEntryId = transformedAssistant.Id;
                    }

                    foreach (var toolCall in unmatchedToolCalls)
                    {
                        var synthesizedToolResult = CreateMissingToolResultEntry(
                            assistant,
                            toolCall,
                            replayEntries.Count
                        );
                        replayEntries.Add(synthesizedToolResult);
                        lastSafeEntryId = synthesizedToolResult.Id;
                    }
                    break;
            }
        }

        return new ProviderReplayTranscript(target, replayEntries);
    }

    private static AssistantChatEntry TransformAssistantEntry(
        AssistantChatEntry assistant,
        ReplayTarget target
    )
    {
        var blocks = new List<AssistantContentBlock>(assistant.Blocks.Count);
        var sameProvider = Matches(assistant.Provider, target.Provider);
        var sameModel = sameProvider && Matches(assistant.Model, target.Model);

        foreach (var block in assistant.Blocks)
        {
            switch (block)
            {
                case TextAssistantBlock text:
                    blocks.Add(Clone(text));
                    break;

                case ToolCallAssistantBlock toolCall:
                    blocks.Add(Clone(toolCall));
                    break;

                case ReasoningAssistantBlock reasoning when sameModel:
                    blocks.Add(Clone(reasoning));
                    break;

                case ReasoningAssistantBlock reasoning when sameProvider:
                    if (
                        reasoning.Visibility == ReasoningVisibility.Visible
                        && !string.IsNullOrWhiteSpace(reasoning.Text)
                    )
                    {
                        blocks.Add(new TextAssistantBlock(reasoning.Id, reasoning.Text!));
                    }
                    break;

                case ReasoningAssistantBlock reasoning
                    when target.CrossProviderReasoningReplayMode
                        == CrossProviderReasoningReplayMode.ConvertVisibleToText
                        && reasoning.Visibility == ReasoningVisibility.Visible
                        && !string.IsNullOrWhiteSpace(reasoning.Text):
                    blocks.Add(new TextAssistantBlock(reasoning.Id, reasoning.Text!));
                    break;
            }
        }

        return new AssistantChatEntry(
            assistant.Id,
            assistant.Timestamp,
            blocks,
            assistant.TurnId,
            assistant.Provider,
            assistant.Model,
            assistant.StopReason,
            assistant.Usage
        );
    }

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static UserChatEntry Clone(UserChatEntry entry) =>
        new(entry.Id, entry.Timestamp, entry.Content, entry.TurnId);

    private static AssistantChatEntry Clone(AssistantChatEntry entry) =>
        new(
            entry.Id,
            entry.Timestamp,
            entry.Blocks.Select(Clone).ToArray(),
            entry.TurnId,
            entry.Provider,
            entry.Model,
            entry.StopReason,
            entry.Usage
        );

    private static ToolResultChatEntry Clone(ToolResultChatEntry entry) =>
        new(
            entry.Id,
            entry.Timestamp,
            entry.ToolCallId,
            entry.ToolName,
            entry.Result,
            entry.IsError,
            entry.TurnId,
            entry.Provider,
            entry.Model
        );

    private static AssistantContentBlock Clone(AssistantContentBlock block) =>
        block switch
        {
            TextAssistantBlock text => Clone(text),
            ReasoningAssistantBlock reasoning => Clone(reasoning),
            ToolCallAssistantBlock toolCall => Clone(toolCall),
            _ => throw new InvalidOperationException(
                $"Unsupported assistant content block type '{block.GetType().Name}'."
            ),
        };

    private static TextAssistantBlock Clone(TextAssistantBlock block) => new(block.Id, block.Text);

    private static ReasoningAssistantBlock Clone(ReasoningAssistantBlock block) =>
        new(block.Id, block.Text, block.Visibility, block.ReplayToken);

    private static ToolCallAssistantBlock Clone(ToolCallAssistantBlock block) =>
        new(block.Id, block.ToolCallId, block.ToolName, new Dictionary<string, object?>(block.Arguments));

    private static ToolResultChatEntry CreateMissingToolResultEntry(
        AssistantChatEntry assistant,
        ToolCallAssistantBlock toolCall,
        int replayIndex
    )
    {
        const string message =
            "Missing tool result for assistant tool call during replay. A synthetic error result was inserted.";

        var result = new ToolInvocationResult(
            toolCall.ToolCallId,
            toolCall.ToolName,
            isError: true,
            text: new[] { message },
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );

        return new ToolResultChatEntry(
            $"replay-tool-result-{assistant.Id}-{toolCall.ToolCallId}",
            assistant.Timestamp.AddTicks(replayIndex + 1),
            toolCall.ToolCallId,
            toolCall.ToolName,
            result,
            true,
            assistant.TurnId
        );
    }
}
