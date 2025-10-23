using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Implements the OAuth 2.1 authorization-code grant with PKCE.
/// </summary>
public sealed class AuthorizationCodePkceOAuthTokenProvider : IOAuthTokenProvider
{
    private static readonly char[] s_base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~".ToCharArray();

    private readonly OAuthClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly OAuthDiscoveryService _discoveryService;
    private readonly Func<AuthorizationCodeRequest, CancellationToken, Task<AuthorizationCodeResult>> _interactionHandler;

    public AuthorizationCodePkceOAuthTokenProvider(
        OAuthClientOptions options,
        HttpClient httpClient,
        Func<AuthorizationCodeRequest, CancellationToken, Task<AuthorizationCodeResult>> interactionHandler
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _interactionHandler = interactionHandler ?? throw new ArgumentNullException(nameof(interactionHandler));
        _discoveryService = new OAuthDiscoveryService(_httpClient);
    }

    public async Task<OAuthTokenResponse?> AcquireTokenAsync(
        OAuthTokenRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var metadata = await _discoveryService.GetMetadataAsync(_options, cancellationToken);
        if (metadata.AuthorizationEndpoint == null)
        {
            throw new InvalidOperationException("Authorization server did not advertise an authorization endpoint.");
        }

        if (_options.RedirectUri == null)
        {
            throw new InvalidOperationException("RedirectUri must be configured for authorization code flow.");
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var state = GenerateState();

        var authorizationUri = BuildAuthorizationRequest(metadata.AuthorizationEndpoint, context.Resource, codeChallenge, state);

        var request = new AuthorizationCodeRequest(authorizationUri, state, _options.RedirectUri);
        var result = await _interactionHandler(request, cancellationToken);
        if (!string.Equals(result.State, state, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("State mismatch detected during authorization code flow.");
        }

        var formValues = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = result.Code,
            ["redirect_uri"] = _options.RedirectUri.ToString(),
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = codeVerifier,
            ["resource"] = context.Resource.ToString(),
        };

        if (_options.Scopes is { Count: > 0 })
        {
            formValues["scope"] = string.Join(" ", _options.Scopes);
        }

        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            formValues["client_secret"] = _options.ClientSecret;
        }

        using var response = await _httpClient.PostAsync(
            metadata.TokenEndpoint,
            new FormUrlEncodedContent(formValues),
            cancellationToken
        );

        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return OAuthTokenResponseFactory.Create(document.RootElement);
    }

    public async Task<OAuthTokenResponse?> RefreshTokenAsync(
        OAuthTokenRequestContext context,
        OAuthTokenResponse currentToken,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(currentToken.RefreshToken))
        {
            return await AcquireTokenAsync(context, cancellationToken);
        }

        var metadata = await _discoveryService.GetMetadataAsync(_options, cancellationToken);

        var formValues = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = currentToken.RefreshToken!,
            ["client_id"] = _options.ClientId,
            ["resource"] = context.Resource.ToString(),
        };

        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            formValues["client_secret"] = _options.ClientSecret;
        }

        using var response = await _httpClient.PostAsync(
            metadata.TokenEndpoint,
            new FormUrlEncodedContent(formValues),
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return OAuthTokenResponseFactory.Create(document.RootElement);
    }

    private Uri BuildAuthorizationRequest(
        Uri authorizationEndpoint,
        Uri resource,
        string codeChallenge,
        string state
    )
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri!.ToString(),
            ["resource"] = resource.ToString(),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };

        if (_options.Scopes is { Count: > 0 })
        {
            query["scope"] = string.Join(" ", _options.Scopes);
        }

        var baseUri = authorizationEndpoint.ToString();
        var builder = new StringBuilder(baseUri);
        builder.Append(baseUri.Contains('?') ? '&' : '?');
        var first = true;
        foreach (var kvp in query)
        {
            if (!first)
            {
                builder.Append('&');
            }
            first = false;
            builder.Append(Uri.EscapeDataString(kvp.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(kvp.Value));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static string GenerateCodeVerifier()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string GenerateState()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
