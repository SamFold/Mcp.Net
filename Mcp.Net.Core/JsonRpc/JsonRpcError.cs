using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.JsonRpc;

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
