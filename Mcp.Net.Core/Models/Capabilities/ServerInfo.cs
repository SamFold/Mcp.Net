using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Capabilities;

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
