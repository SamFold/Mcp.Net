using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Mcp.Net.LLM.Models;

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

    public ToolInvocationResult CreateResult(
        IEnumerable<string>? text = null,
        JsonElement? structured = null,
        IEnumerable<ToolResultResourceLink>? resourceLinks = null,
        JsonElement? metadata = null,
        bool isError = false
    ) =>
        ToolInvocationResults.Create(
            ToolCallId,
            ToolName,
            isError,
            text,
            structured,
            resourceLinks,
            metadata
        );

    public ToolInvocationResult CreateTextResult(params string[] text) =>
        ToolInvocationResults.Success(ToolCallId, ToolName, text);

    public ToolInvocationResult CreateErrorResult(params string[] text) =>
        ToolInvocationResults.Error(ToolCallId, ToolName, text);
}
