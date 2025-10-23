using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Examples.Shared;

namespace Mcp.Net.Examples.SimpleClient.Authorization;

/// <summary>
/// Helper that exercises OAuth 2.0 dynamic client registration against the demo authorization server.
/// </summary>
internal static class DemoDynamicClientRegistrar
{
    private static readonly JsonSerializerOptions s_serializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<DynamicClientRegistrationResult> RegisterAsync(
        Uri baseUri,
        string? clientName,
        Uri redirectUri,
        IReadOnlyCollection<string> scopes,
        HttpClient httpClient,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(httpClient);

        // Leverage the shared discovery service so registration respects resource metadata indirection.
        var options = DemoOAuthDefaults.CreateClientOptions(baseUri);
        options.AuthorizationServerMetadataAddress = DemoOAuthDefaults.BuildMetadataUri(baseUri);

        var discoveryService = new OAuthDiscoveryService(httpClient);
        var metadata = await discoveryService.GetMetadataAsync(options, cancellationToken);
        if (metadata.RegistrationEndpoint == null)
        {
            throw new InvalidOperationException("Authorization server does not advertise a registration_endpoint.");
        }

        var requestPayload = new
        {
            redirect_uris = new[] { redirectUri.ToString() },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
            client_name = clientName,
            scope = scopes.Count > 0 ? string.Join(" ", scopes) : null,
            require_pkce = true,
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(requestPayload, s_serializerOptions),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await httpClient.PostAsync(
            metadata.RegistrationEndpoint,
            content,
            cancellationToken
        );

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Dynamic client registration failed with status {(int)response.StatusCode}: {body}"
            );
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var clientId = root.GetProperty("client_id").GetString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Registration response did not include a client_id.");
        }

        string? clientSecret = null;
        if (root.TryGetProperty("client_secret", out var secretProperty))
        {
            clientSecret = secretProperty.GetString();
        }

        var registeredRedirects = new List<Uri>();
        if (root.TryGetProperty("redirect_uris", out var redirectProperty))
        {
            foreach (var redirect in redirectProperty.EnumerateArray())
            {
                if (Uri.TryCreate(redirect.GetString(), UriKind.Absolute, out var uri))
                {
                    registeredRedirects.Add(uri);
                }
            }
        }

        if (registeredRedirects.Count == 0)
        {
            registeredRedirects.Add(redirectUri);
        }

        return new DynamicClientRegistrationResult(clientId, clientSecret, registeredRedirects);
    }
}

internal sealed record DynamicClientRegistrationResult(
    string ClientId,
    string? ClientSecret,
    IReadOnlyList<Uri> RedirectUris
);
