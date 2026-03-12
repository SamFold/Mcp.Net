using System.Text.Json;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Convenience helpers for constructing <see cref="ToolInvocationResult"/> instances for local tools.
/// </summary>
public static class ToolInvocationResults
{
    public static ToolInvocationResult Create(
        string toolCallId,
        string toolName,
        bool isError = false,
        IEnumerable<string>? text = null,
        JsonElement? structured = null,
        IEnumerable<ToolResultResourceLink>? resourceLinks = null,
        JsonElement? metadata = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var fragments = text?.Where(static fragment => fragment is not null).ToArray()
            ?? Array.Empty<string>();
        var links = resourceLinks?.ToArray() ?? Array.Empty<ToolResultResourceLink>();

        return new ToolInvocationResult(
            toolCallId,
            toolName,
            isError,
            fragments,
            CloneJsonElement(structured),
            links,
            CloneJsonElement(metadata)
        );
    }

    public static ToolInvocationResult Success(
        string toolCallId,
        string toolName,
        params string[] text
    ) => Create(toolCallId, toolName, text: text);

    public static ToolInvocationResult Error(
        string toolCallId,
        string toolName,
        params string[] text
    ) => Create(toolCallId, toolName, isError: true, text: text);

    private static JsonElement? CloneJsonElement(JsonElement? element) => element?.Clone();
}
