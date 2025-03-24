using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Content
{
    [JsonConverter(typeof(ContentBaseConverter))]
    public abstract class ContentBase
    {
        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }
}
