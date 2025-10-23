using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Retrieves OAuth 2.1 metadata documents for protected resources and authorization servers.
/// </summary>
public class OAuthDiscoveryService
{
    private readonly HttpClient _httpClient;

    public OAuthDiscoveryService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<OAuthDiscoveryDocument> GetMetadataAsync(
        OAuthClientOptions options,
        CancellationToken cancellationToken
    )
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var authorizationMetadataUri = options.AuthorizationServerMetadataAddress;
        if (authorizationMetadataUri == null)
        {
            authorizationMetadataUri = await ResolveAuthorizationMetadataAsync(options, cancellationToken);
        }

        if (authorizationMetadataUri == null)
        {
            throw new InvalidOperationException(
                "Unable to resolve authorization server metadata address."
            );
        }

        using var response = await _httpClient.GetAsync(authorizationMetadataUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("token_endpoint", out var tokenEndpointProperty))
        {
            throw new InvalidOperationException("Authorization server metadata missing token_endpoint.");
        }

        var discovery = new OAuthDiscoveryDocument
        {
            TokenEndpoint = new Uri(tokenEndpointProperty.GetString()!, UriKind.Absolute),
            DeviceAuthorizationEndpoint = TryReadAbsoluteUri(root, "device_authorization_endpoint"),
            AuthorizationEndpoint = TryReadAbsoluteUri(root, "authorization_endpoint"),
        };

        return discovery;
    }

    private async Task<Uri?> ResolveAuthorizationMetadataAsync(
        OAuthClientOptions options,
        CancellationToken cancellationToken
    )
    {
        if (options.ResourceMetadataAddress == null)
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(options.ResourceMetadataAddress, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("authorization_servers", out var serversProperty))
        {
            return null;
        }

        foreach (var element in serversProperty.EnumerateArray())
        {
            var authorizationServer = element.GetString();
            if (!string.IsNullOrWhiteSpace(authorizationServer))
            {
                return new Uri(authorizationServer, UriKind.Absolute);
            }
        }

        return null;
    }

    private static Uri? TryReadAbsoluteUri(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        var raw = property.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : new Uri(raw, UriKind.Absolute);
    }
}
