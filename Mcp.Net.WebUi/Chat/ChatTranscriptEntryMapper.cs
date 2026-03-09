using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.DTOs;

namespace Mcp.Net.WebUi.Chat;

internal static class ChatTranscriptEntryMapper
{
    public static ChatMessageDto ToMessageDto(string sessionId, ChatTranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ChatMessageDto
        {
            Id = entry.Id,
            SessionId = sessionId,
            Type = ToMessageType(entry),
            Content = ToDisplayContent(entry),
            Timestamp = entry.Timestamp.UtcDateTime,
            Metadata = ToMetadata(entry),
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

    private static string ToMessageType(ChatTranscriptEntry entry) =>
        entry switch
        {
            AssistantChatEntry => "assistant",
            ToolResultChatEntry => "toolresult",
            ErrorChatEntry => "error",
            UserChatEntry => "user",
            _ => entry.Kind.ToString().ToLowerInvariant(),
        };

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
            _ => string.Empty,
        };

    private static Dictionary<string, object> ToMetadata(ChatTranscriptEntry entry)
    {
        var metadata = new Dictionary<string, object>
        {
            ["kind"] = entry.Kind.ToString().ToLowerInvariant(),
        };

        if (!string.IsNullOrWhiteSpace(entry.TurnId))
        {
            metadata["turnId"] = entry.TurnId;
        }

        if (!string.IsNullOrWhiteSpace(entry.Provider))
        {
            metadata["provider"] = entry.Provider;
        }

        if (!string.IsNullOrWhiteSpace(entry.Model))
        {
            metadata["model"] = entry.Model;
        }

        switch (entry)
        {
            case AssistantChatEntry assistant:
                metadata["blocks"] = assistant.Blocks.Select(ToMetadata).Cast<object>().ToList();
                break;
            case ToolResultChatEntry toolResult:
                metadata["toolCallId"] = toolResult.ToolCallId;
                metadata["toolName"] = toolResult.ToolName;
                metadata["isError"] = toolResult.IsError;
                break;
            case ErrorChatEntry error:
                metadata["source"] = error.Source.ToString().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(error.Code))
                {
                    metadata["code"] = error.Code;
                }
                if (!string.IsNullOrWhiteSpace(error.Details))
                {
                    metadata["details"] = error.Details;
                }
                break;
        }

        return metadata;
    }

    private static Dictionary<string, object> ToMetadata(AssistantContentBlock block)
    {
        var metadata = new Dictionary<string, object>
        {
            ["id"] = block.Id,
            ["kind"] = block.Kind.ToString().ToLowerInvariant(),
        };

        switch (block)
        {
            case TextAssistantBlock text:
                metadata["text"] = text.Text;
                break;
            case ReasoningAssistantBlock reasoning:
                metadata["text"] = reasoning.Text ?? string.Empty;
                metadata["visibility"] = reasoning.Visibility.ToString().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(reasoning.ReplayToken))
                {
                    metadata["replayToken"] = reasoning.ReplayToken;
                }
                break;
            case ToolCallAssistantBlock toolCall:
                metadata["toolCallId"] = toolCall.ToolCallId;
                metadata["toolName"] = toolCall.ToolName;
                metadata["arguments"] = toolCall.Arguments;
                break;
        }

        return metadata;
    }
}
