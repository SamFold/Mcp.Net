using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Content
{
    public class ResourceLinkContent : ContentBase
    {
        public override string Type => "resource_link";

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
    }
}
