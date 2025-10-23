using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Prompts;

public class PromptArgument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Annotations { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Meta { get; set; }
}
