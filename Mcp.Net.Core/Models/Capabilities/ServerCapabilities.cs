using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Capabilities
{
    public class ServerCapabilities
    {
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Tools { get; set; }

        [JsonPropertyName("resources")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Resources { get; set; }

        [JsonPropertyName("prompts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Prompts { get; set; }

        [JsonPropertyName("logging")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Logging { get; set; }

        [JsonPropertyName("completions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Completions { get; set; }

        [JsonPropertyName("experimental")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Experimental { get; set; }
    }
}
