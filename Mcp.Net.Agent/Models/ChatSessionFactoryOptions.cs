using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Tools;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Models;

public sealed record ChatSessionFactoryOptions
{
    public string SystemPrompt { get; init; } = string.Empty;

    public ChatRequestOptions? RequestDefaults { get; init; }

    public int MaxToolCallRounds { get; init; } = ChatSessionConfiguration.DefaultMaxToolCallRounds;

    public IReadOnlyList<ILocalTool> LocalTools { get; init; } = Array.Empty<ILocalTool>();

    public IMcpClient? McpClient { get; init; }

    public IChatTranscriptCompactor? TranscriptCompactor { get; init; }
}
