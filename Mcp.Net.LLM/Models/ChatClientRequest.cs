namespace Mcp.Net.LLM.Models;

public sealed class ChatClientRequest
{
    public string SystemPrompt { get; }

    public IReadOnlyList<ChatTranscriptEntry> Transcript { get; }

    public IReadOnlyList<ChatClientTool> Tools { get; }

    public ChatRequestOptions? Options { get; }

    public ChatClientRequest(
        string? systemPrompt,
        IReadOnlyList<ChatTranscriptEntry> transcript,
        IReadOnlyList<ChatClientTool>? tools = null,
        ChatRequestOptions? options = null
    )
    {
        SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? string.Empty : systemPrompt;
        Transcript = transcript?.ToArray() ?? throw new ArgumentNullException(nameof(transcript));
        Tools = tools?.ToArray() ?? Array.Empty<ChatClientTool>();
        Options = options == null
            ? null
            : new ChatRequestOptions
            {
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxOutputTokens,
                ToolChoice = options.ToolChoice,
            };
    }
}
