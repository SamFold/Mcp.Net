using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Mcp.Net.LLM.Models;

/// <summary>
/// Represents the outcome of executing a tool invocation against the MCP server.
/// </summary>
public sealed class ToolInvocationResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ToolInvocationResult(
        string toolCallId,
        string toolName,
        bool isError,
        IReadOnlyList<string> text,
        JsonElement? structured,
        IReadOnlyList<ToolResultResourceLink> resourceLinks,
        JsonElement? metadata
    )
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        IsError = isError;
        Text = new ReadOnlyCollection<string>(text.ToList());
        Structured = structured;
        ResourceLinks = new ReadOnlyCollection<ToolResultResourceLink>(resourceLinks.ToList());
        Metadata = metadata;
    }

    /// <summary>
    /// The identifier of the original tool call request.
    /// </summary>
    public string ToolCallId { get; }

    /// <summary>
    /// The name of the tool that was executed.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Indicates whether the server flagged the result as an error.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Human-readable snippets returned by the tool.
    /// </summary>
    public IReadOnlyList<string> Text { get; }

    /// <summary>
    /// Structured JSON payload supplied by the tool (when provided).
    /// </summary>
    public JsonElement? Structured { get; }

    /// <summary>
    /// Resource links returned by the tool.
    /// </summary>
    public IReadOnlyList<ToolResultResourceLink> ResourceLinks { get; }

    /// <summary>
    /// Arbitrary metadata attached to the result.
    /// </summary>
    public JsonElement? Metadata { get; }

    /// <summary>
    /// Serialises the result to a JSON string that can be forwarded to LLM providers.
    /// </summary>
    public string ToWireJson()
    {
        var payload = new ToolResultWirePayload
        {
            ToolName = ToolName,
            IsError = IsError,
            Text = Text,
            Structured = Structured,
            ResourceLinks = ResourceLinks.Count == 0 ? null : ResourceLinks,
            Metadata = Metadata,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public override string ToString() => $"{ToolName} ({ToolCallId})";

    private sealed class ToolResultWirePayload
    {
        public string ToolName { get; set; } = string.Empty;

        public bool IsError { get; set; }

        public IReadOnlyList<string> Text { get; set; } = Array.Empty<string>();

        public JsonElement? Structured { get; set; }

        public IReadOnlyList<ToolResultResourceLink>? ResourceLinks { get; set; }

        public JsonElement? Metadata { get; set; }
    }
}
