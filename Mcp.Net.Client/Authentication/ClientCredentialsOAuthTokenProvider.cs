using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Implements the OAuth 2.1 client credentials flow for confidential clients.
/// </summary>
public sealed class ClientCredentialsOAuthTokenProvider : IOAuthTokenProvider
{
    private readonly OAuthClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly OAuthDiscoveryService _discoveryService;

    public ClientCredentialsOAuthTokenProvider(OAuthClientOptions options, HttpClient httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _discoveryService = new OAuthDiscoveryService(_httpClient);
    }

    public async Task<OAuthTokenResponse?> AcquireTokenAsync(
        OAuthTokenRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var metadata = await _discoveryService.GetMetadataAsync(_options, cancellationToken);

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["resource"] = context.Resource.ToString(),
        };

        if (_options.Scopes is { Count: > 0 })
        {
            payload["scope"] = string.Join(" ", _options.Scopes);
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            payload["client_secret"] = _options.ClientSecret;
        }

        using var response = await _httpClient.PostAsync(
            metadata.TokenEndpoint,
            new FormUrlEncodedContent(payload),
            cancellationToken
        );

        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return OAuthTokenResponseFactory.Create(document.RootElement);
    }

    public Task<OAuthTokenResponse?> RefreshTokenAsync(
        OAuthTokenRequestContext context,
        OAuthTokenResponse currentToken,
        CancellationToken cancellationToken
    ) => AcquireTokenAsync(context, cancellationToken);
}
