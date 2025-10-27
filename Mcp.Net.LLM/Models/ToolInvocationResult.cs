using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;

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
    /// Creates a result payload from the MCP tool call response.
    /// </summary>
    public static ToolInvocationResult FromMcpResult(
        string toolCallId,
        string toolName,
        ToolCallResult result
    )
    {
        var textFragments = ExtractText(result.Content);
        var resourceLinks = ExtractResourceLinks(result);

        JsonElement? structured = result.Structured;
        JsonElement? metadata = result.Meta is null
            ? null
            : JsonSerializer.SerializeToElement(result.Meta, JsonOptions);

        return new ToolInvocationResult(
            toolCallId,
            toolName,
            result.IsError,
            textFragments,
            structured,
            resourceLinks,
            metadata
        );
    }

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

    private static IReadOnlyList<string> ExtractText(IEnumerable<ContentBase> content)
    {
        var fragments = new List<string>();

        foreach (var item in content ?? Array.Empty<ContentBase>())
        {
            switch (item)
            {
                case TextContent text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        fragments.Add(text.Text);
                    }
                    break;

                case ResourceLinkContent linkContent:
                    if (!string.IsNullOrWhiteSpace(linkContent.Name))
                    {
                        fragments.Add(linkContent.Name);
                    }
                    break;

                default:
                    fragments.Add(JsonSerializer.Serialize(item, item.GetType(), JsonOptions));
                    break;
            }
        }

        return fragments;
    }

    private static IReadOnlyList<ToolResultResourceLink> ExtractResourceLinks(ToolCallResult result)
    {
        if (result.ResourceLinks is null || result.ResourceLinks.Count == 0)
        {
            return Array.Empty<ToolResultResourceLink>();
        }

        return result
            .ResourceLinks
            .Select(link => new ToolResultResourceLink(
                link.Uri,
                link.Name,
                link.Description,
                link.MimeType
            ))
            .ToList();
    }

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
