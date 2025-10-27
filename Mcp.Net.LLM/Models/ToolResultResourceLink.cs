namespace Mcp.Net.LLM.Models;

/// <summary>
/// Simplified representation of a resource link returned by a tool.
/// </summary>
public sealed class ToolResultResourceLink
{
    public ToolResultResourceLink(
        string uri,
        string? name,
        string? description,
        string? contentType
    )
    {
        Uri = uri;
        Name = name;
        Description = description;
        ContentType = contentType;
    }

    public string Uri { get; }

    public string? Name { get; }

    public string? Description { get; }

    public string? ContentType { get; }
}
