using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Tools
{
    public class Tool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("inputSchema")]
        public JsonElement InputSchema { get; set; }

        [JsonPropertyName("annotations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Annotations { get; set; }

        [JsonPropertyName("_meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Meta { get; set; }
    }
}
