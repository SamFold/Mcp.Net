using System.Text.Json;

namespace Mcp.Net.LLM.Models;

public sealed class ChatClientRequest
{
    public string SystemPrompt { get; }

    public IReadOnlyList<ChatTranscriptEntry> Transcript { get; }

    public IReadOnlyList<ChatClientTool> Tools { get; }

    public ChatClientRequest(
        string? systemPrompt,
        IReadOnlyList<ChatTranscriptEntry> transcript,
        IReadOnlyList<ChatClientTool>? tools = null
    )
    {
        SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? string.Empty : systemPrompt;
        Transcript = transcript?.ToArray() ?? throw new ArgumentNullException(nameof(transcript));
        Tools = tools?.ToArray() ?? Array.Empty<ChatClientTool>();
    }
}
