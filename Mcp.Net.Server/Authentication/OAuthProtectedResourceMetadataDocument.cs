using System.Text.Json.Serialization;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Represents the JSON payload returned from the OAuth protected resource metadata endpoint.
/// </summary>
internal sealed class OAuthProtectedResourceMetadataDocument
{
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("authorization_servers")]
    public IReadOnlyList<string>? AuthorizationServers { get; init; }

    [JsonPropertyName("resource_name")]
    public string? ResourceName { get; init; }
}
