namespace Mcp.Net.LLM.Models;

public enum ChatToolChoiceKind
{
    Auto,
    None,
    Required,
    Specific,
}

public sealed record ChatToolChoice
{
    public static ChatToolChoice Auto { get; } = new(ChatToolChoiceKind.Auto);

    public static ChatToolChoice None { get; } = new(ChatToolChoiceKind.None);

    public static ChatToolChoice Required { get; } = new(ChatToolChoiceKind.Required);

    public ChatToolChoiceKind Kind { get; }

    public string? ToolName { get; }

    private ChatToolChoice(ChatToolChoiceKind kind, string? toolName = null)
    {
        if (kind == ChatToolChoiceKind.Specific)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        }
        else if (!string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException(
                "Tool name is only valid for specific tool choices.",
                nameof(toolName)
            );
        }

        Kind = kind;
        ToolName = toolName;
    }

    public static ChatToolChoice ForTool(string toolName) =>
        new(ChatToolChoiceKind.Specific, toolName);
}
