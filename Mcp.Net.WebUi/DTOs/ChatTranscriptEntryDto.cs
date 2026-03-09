using System.Text.Json.Serialization;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.WebUi.DTOs;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UserChatTranscriptEntryDto), "user")]
[JsonDerivedType(typeof(AssistantChatTranscriptEntryDto), "assistant")]
[JsonDerivedType(typeof(ToolResultChatTranscriptEntryDto), "toolResult")]
[JsonDerivedType(typeof(ErrorChatTranscriptEntryDto), "error")]
public abstract record ChatTranscriptEntryDto
{
    protected ChatTranscriptEntryDto(
        string id,
        string sessionId,
        DateTime timestamp,
        string? turnId = null,
        string? provider = null,
        string? model = null
    )
    {
        Id = id;
        SessionId = sessionId;
        Timestamp = timestamp;
        TurnId = turnId;
        Provider = provider;
        Model = model;
    }

    public string Id { get; }

    public string SessionId { get; }

    public DateTime Timestamp { get; }

    public string? TurnId { get; }

    public string? Provider { get; }

    public string? Model { get; }
}

public sealed record UserChatTranscriptEntryDto : ChatTranscriptEntryDto
{
    public UserChatTranscriptEntryDto(
        string id,
        string sessionId,
        DateTime timestamp,
        string content,
        string? turnId = null
    )
        : base(id, sessionId, timestamp, turnId)
    {
        Content = content;
    }

    public string Content { get; }
}

public sealed record AssistantChatTranscriptEntryDto : ChatTranscriptEntryDto
{
    public AssistantChatTranscriptEntryDto(
        string id,
        string sessionId,
        DateTime timestamp,
        IReadOnlyList<AssistantContentBlockDto> blocks,
        string? turnId = null,
        string? provider = null,
        string? model = null
    )
        : base(id, sessionId, timestamp, turnId, provider, model)
    {
        Blocks = blocks;
    }

    public IReadOnlyList<AssistantContentBlockDto> Blocks { get; }
}

public sealed record ToolResultChatTranscriptEntryDto : ChatTranscriptEntryDto
{
    public ToolResultChatTranscriptEntryDto(
        string id,
        string sessionId,
        DateTime timestamp,
        string toolCallId,
        string toolName,
        ToolInvocationResult result,
        bool isError,
        string? turnId = null,
        string? provider = null,
        string? model = null
    )
        : base(id, sessionId, timestamp, turnId, provider, model)
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        Result = result;
        IsError = isError;
    }

    public string ToolCallId { get; }

    public string ToolName { get; }

    public ToolInvocationResult Result { get; }

    public bool IsError { get; }
}

public sealed record ErrorChatTranscriptEntryDto : ChatTranscriptEntryDto
{
    public ErrorChatTranscriptEntryDto(
        string id,
        string sessionId,
        DateTime timestamp,
        string source,
        string message,
        string? code = null,
        string? details = null,
        string? relatedEntryId = null,
        bool isRetryable = false,
        string? turnId = null,
        string? provider = null,
        string? model = null
    )
        : base(id, sessionId, timestamp, turnId, provider, model)
    {
        Source = source;
        Message = message;
        Code = code;
        Details = details;
        RelatedEntryId = relatedEntryId;
        IsRetryable = isRetryable;
    }

    public string Source { get; }

    public string Message { get; }

    public string? Code { get; }

    public string? Details { get; }

    public string? RelatedEntryId { get; }

    public bool IsRetryable { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextAssistantContentBlockDto), "text")]
[JsonDerivedType(typeof(ReasoningAssistantContentBlockDto), "reasoning")]
[JsonDerivedType(typeof(ToolCallAssistantContentBlockDto), "toolCall")]
public abstract record AssistantContentBlockDto
{
    protected AssistantContentBlockDto(string id)
    {
        Id = id;
    }

    public string Id { get; }
}

public sealed record TextAssistantContentBlockDto : AssistantContentBlockDto
{
    public TextAssistantContentBlockDto(string id, string text)
        : base(id)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed record ReasoningAssistantContentBlockDto : AssistantContentBlockDto
{
    public ReasoningAssistantContentBlockDto(
        string id,
        string? text,
        string visibility,
        string? replayToken = null
    )
        : base(id)
    {
        Text = text;
        Visibility = visibility;
        ReplayToken = replayToken;
    }

    public string? Text { get; }

    public string Visibility { get; }

    public string? ReplayToken { get; }
}

public sealed record ToolCallAssistantContentBlockDto : AssistantContentBlockDto
{
    public ToolCallAssistantContentBlockDto(
        string id,
        string toolCallId,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments
    )
        : base(id)
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        Arguments = arguments;
    }

    public string ToolCallId { get; }

    public string ToolName { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }
}
