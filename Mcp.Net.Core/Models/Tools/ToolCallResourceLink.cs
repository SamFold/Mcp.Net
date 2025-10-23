using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Tools;

/// <summary>
/// Represents a lightweight reference to a resource produced by a tool invocation.
/// </summary>
public class ToolCallResourceLink
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Annotations { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Meta { get; set; }
}
