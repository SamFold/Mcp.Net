using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

internal static class ToolInvocationResultFactory
{
    public static ToolInvocationResult CreateError(
        string toolCallId,
        string toolName,
        string? message
    )
    {
        var text = string.IsNullOrWhiteSpace(message) ? Array.Empty<string>() : new[] { message };

        return new ToolInvocationResult(
            toolCallId,
            toolName,
            true,
            text,
            structured: null,
            resourceLinks: Array.Empty<ToolResultResourceLink>(),
            metadata: null
        );
    }
}
