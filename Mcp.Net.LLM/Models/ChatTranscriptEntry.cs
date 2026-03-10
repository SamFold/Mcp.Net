namespace Mcp.Net.LLM.Models;

public enum ChatTranscriptEntryKind
{
    User,
    Assistant,
    ToolResult,
    Error,
}

public abstract record ChatTranscriptEntry(
    string Id,
    ChatTranscriptEntryKind Kind,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
);

public sealed record UserChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    string Content,
    string? TurnId = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.User, Timestamp, TurnId);

public sealed record AssistantChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    IReadOnlyList<AssistantContentBlock> Blocks,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null,
    string? StopReason = null,
    ChatUsage? Usage = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.Assistant, Timestamp, TurnId, Provider, Model);

public sealed record ToolResultChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    string ToolCallId,
    string ToolName,
    ToolInvocationResult Result,
    bool IsError,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.ToolResult, Timestamp, TurnId, Provider, Model);

public sealed record ErrorChatEntry(
    string Id,
    DateTimeOffset Timestamp,
    ChatErrorSource Source,
    string Message,
    string? Code = null,
    string? Details = null,
    string? RelatedEntryId = null,
    bool IsRetryable = false,
    string? TurnId = null,
    string? Provider = null,
    string? Model = null
) : ChatTranscriptEntry(Id, ChatTranscriptEntryKind.Error, Timestamp, TurnId, Provider, Model);
