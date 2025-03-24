using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Capabilities;

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}
