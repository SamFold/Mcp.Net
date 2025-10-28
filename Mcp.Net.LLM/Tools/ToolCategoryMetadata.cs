using System;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Represents category metadata provided by the MCP server for a particular tool.
/// </summary>
internal sealed record ToolCategoryMetadata(string Key, string DisplayName, double? Order)
{
    public static ToolCategoryMetadata Create(string key, string? displayName, double? order)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Category key cannot be null or whitespace.", nameof(key));
        }

        return new ToolCategoryMetadata(
            key.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? key.Trim() : displayName!.Trim(),
            order
        );
    }
}
