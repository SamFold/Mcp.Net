using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Describes a runtime tool invocation emitted by the provider-facing agent loop.
/// </summary>
public sealed record ToolInvocation
{
    public ToolInvocation(
        string toolCallId,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments
    )
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        Arguments = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(arguments));
    }

    public string ToolCallId { get; }

    public string ToolName { get; }

    public IReadOnlyDictionary<string, object?> Arguments { get; }
}
