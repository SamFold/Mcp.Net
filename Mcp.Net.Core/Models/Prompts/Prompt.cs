using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Prompts;

public class Prompt
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("arguments")]
    public PromptArgument[]? Arguments { get; set; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Annotations { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object?>? Meta { get; set; }
}
