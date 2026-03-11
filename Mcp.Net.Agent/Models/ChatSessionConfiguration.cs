using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Models;

public sealed record ChatSessionConfiguration
{
    public string SystemPrompt { get; init; } = string.Empty;

    public IReadOnlyList<Tool> Tools { get; init; } = Array.Empty<Tool>();

    public ChatRequestOptions? RequestDefaults { get; init; }
}
