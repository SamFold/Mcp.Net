using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.WebUi.Chat;

internal static class ChatTranscriptEntryMapper
{
    public static ChatTranscriptEntryDto ToDto(string sessionId, ChatTranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry switch
        {
            UserChatEntry user => new UserChatTranscriptEntryDto(
                user.Id,
                sessionId,
                user.Timestamp.UtcDateTime,
                user.Content,
                user.TurnId
            ),
            AssistantChatEntry assistant => new AssistantChatTranscriptEntryDto(
                assistant.Id,
                sessionId,
                assistant.Timestamp.UtcDateTime,
                assistant.Blocks.Select(ToDto).ToArray(),
                assistant.TurnId,
                assistant.Provider,
                assistant.Model,
                assistant.StopReason,
                assistant.Usage
            ),
            ToolResultChatEntry toolResult => new ToolResultChatTranscriptEntryDto(
                toolResult.Id,
                sessionId,
                toolResult.Timestamp.UtcDateTime,
                toolResult.ToolCallId,
                toolResult.ToolName,
                toolResult.Result,
                toolResult.IsError,
                toolResult.TurnId,
                toolResult.Provider,
                toolResult.Model
            ),
            ErrorChatEntry error => new ErrorChatTranscriptEntryDto(
                error.Id,
                sessionId,
                error.Timestamp.UtcDateTime,
                error.Source.ToString().ToLowerInvariant(),
                error.Message,
                error.Code,
                error.Details,
                error.RelatedEntryId,
                error.IsRetryable,
                error.TurnId,
                error.Provider,
                error.Model
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported transcript entry type '{entry.GetType().Name}'."
            ),
        };
    }

    public static string ToPreview(ChatTranscriptEntry entry, int maxLength = 50)
    {
        var content = ToDisplayContent(entry);
        if (content.Length <= maxLength)
        {
            return content;
        }

        return content[..(maxLength - 3)] + "...";
    }

    private static string ToDisplayContent(ChatTranscriptEntry entry) =>
        entry switch
        {
            UserChatEntry user => user.Content,
            AssistantChatEntry assistant => string.Join(
                Environment.NewLine,
                assistant.Blocks.Select(ToDisplayContent).Where(text => !string.IsNullOrWhiteSpace(text))
            ),
            ToolResultChatEntry toolResult => toolResult.Result.Text.Count > 0
                ? string.Join(Environment.NewLine, toolResult.Result.Text)
                : (toolResult.IsError ? "Tool execution failed" : "Tool execution completed"),
            ErrorChatEntry error => error.Message,
            _ => string.Empty,
        };

    private static string ToDisplayContent(AssistantContentBlock block) =>
        block switch
        {
            TextAssistantBlock text => text.Text,
            ReasoningAssistantBlock reasoning when !string.IsNullOrWhiteSpace(reasoning.Text)
                => reasoning.Text!,
            ToolCallAssistantBlock toolCall => $"Calling tool: {toolCall.ToolName}",
            ImageAssistantBlock => "[image]",
            _ => string.Empty,
        };

    private static AssistantContentBlockDto ToDto(AssistantContentBlock block) =>
        block switch
        {
            TextAssistantBlock text => new TextAssistantContentBlockDto(text.Id, text.Text),
            ReasoningAssistantBlock reasoning => new ReasoningAssistantContentBlockDto(
                reasoning.Id,
                reasoning.Text,
                reasoning.Visibility.ToString().ToLowerInvariant(),
                reasoning.ReplayToken
            ),
            ToolCallAssistantBlock toolCall => new ToolCallAssistantContentBlockDto(
                toolCall.Id,
                toolCall.ToolCallId,
                toolCall.ToolName,
                toolCall.Arguments
            ),
            ImageAssistantBlock image => new ImageAssistantContentBlockDto(
                image.Id,
                image.MediaType,
                image.Data.ToArray()
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported assistant content block type '{block.GetType().Name}'."
            ),
        };
}
