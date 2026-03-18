namespace Mcp.Net.LLM.Models;

public enum AssistantContentBlockKind
{
    Text,
    Reasoning,
    ToolCall,
    Image,
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

public sealed record ImageAssistantBlock : AssistantContentBlock
{
    public ImageAssistantBlock(string id, BinaryData data, string mediaType)
        : base(id, AssistantContentBlockKind.Image)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException("Image media type is required.", nameof(mediaType));
        }

        Data = data;
        MediaType = mediaType;
    }

    public BinaryData Data { get; }

    public string MediaType { get; }
}
