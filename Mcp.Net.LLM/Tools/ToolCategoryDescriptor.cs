using System.Collections.Generic;

namespace Mcp.Net.LLM.Tools;

/// <summary>
/// Describes the tools associated with a specific category and the metadata used for UI ordering.
/// </summary>
public sealed record ToolCategoryDescriptor(
    string Key,
    string DisplayName,
    double? Order,
    IReadOnlyList<string> ToolNames
);
