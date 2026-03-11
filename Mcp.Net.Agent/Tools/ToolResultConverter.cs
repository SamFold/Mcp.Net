using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Converts MCP tool-call results into the LLM-local <see cref="ToolInvocationResult"/> shape.
/// </summary>
public static class ToolResultConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a <see cref="ToolInvocationResult"/> from the MCP tool call response.
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
}
