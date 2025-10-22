using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Capabilities;

/// <summary>
/// Client capabilities for MCP protocol
/// </summary>
public class ClientCapabilities
{
    /// <summary>
    /// Experimental capabilities
    /// </summary>
    [JsonPropertyName("experimental")]
    public object? Experimental { get; set; }

    /// <summary>
    /// Sampling capabilities
    /// </summary>
    [JsonPropertyName("sampling")]
    public object? Sampling { get; set; }

    /// <summary>
    /// Elicitation capabilities
    /// </summary>
    [JsonPropertyName("elicitation")]
    public object? Elicitation { get; set; }

    /// <summary>
    /// Root capabilities
    /// </summary>
    [JsonPropertyName("roots")]
    public RootsCapabilities? Roots { get; set; }
}

/// <summary>
/// Root capabilities
/// </summary>
public class RootsCapabilities
{
    /// <summary>
    /// Whether the client supports issuing notifications for changes to the roots list
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}
