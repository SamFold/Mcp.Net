using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Net.Core.Models.Content;

namespace Mcp.Net.Core.Models.Tools
{
    public class ToolCallResult
    {
        [JsonPropertyName("content")]
        public IEnumerable<ContentBase> Content { get; set; } = Array.Empty<ContentBase>();

        [JsonPropertyName("isError")]
        public bool IsError { get; set; }

        [JsonPropertyName("structuredContent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? StructuredContent { get; set; }

        [JsonPropertyName("_meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Meta { get; set; }
    }
}
