namespace Mcp.Net.LLM.Models;

public enum AssistantContentBlockKind
{
    Text,
    Reasoning,
    ToolCall,
}

public enum ReasoningVisibility
{
    Visible,
    Redacted,
    Opaque,
}

public abstract record AssistantContentBlock(string Id, AssistantContentBlockKind Kind);

public sealed record TextAssistantBlock(string Id, string Text)
    : AssistantContentBlock(Id, AssistantContentBlockKind.Text);

public sealed record ReasoningAssistantBlock(
    string Id,
    string? Text,
    ReasoningVisibility Visibility,
    string? ReplayToken = null
) : AssistantContentBlock(Id, AssistantContentBlockKind.Reasoning);

public sealed record ToolCallAssistantBlock(
    string Id,
    string ToolCallId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments
) : AssistantContentBlock(Id, AssistantContentBlockKind.ToolCall);
