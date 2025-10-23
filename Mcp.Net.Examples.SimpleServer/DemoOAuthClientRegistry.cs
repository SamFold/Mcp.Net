using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// In-memory client registry backing the demo OAuth authorization server.
/// </summary>
/// <remarks>
/// The implementation is intentionally simple—clients are stored in memory and discarded when
/// the process restarts. This is sufficient for the sample server while keeping behaviour close
/// to RFC 7591 so the integration exercises realistic dynamic registration flows.
/// </remarks>
internal sealed class DemoOAuthClientRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredClientRecord> _clients =
        new(StringComparer.Ordinal);

    private readonly Func<string> _clientIdFactory;

    public DemoOAuthClientRegistry(Func<string>? clientIdFactory = null)
    {
        _clientIdFactory = clientIdFactory ?? GenerateClientId;
    }

    /// <summary>
    /// Adds the provided client if it does not already exist, returning the stored record.
    /// </summary>
    public RegisteredClientRecord EnsureClient(RegisteredClientRecord client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return _clients.AddOrUpdate(client.ClientId, client, static (_, existing) => existing);
    }

    /// <summary>
    /// Registers a new public client using the supplied request payload.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public RegisteredClientRecord RegisterPublicClient(DynamicClientRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var redirectUris = ValidateRedirectUris(request.RedirectUris);
        var grantTypes = ValidateGrantTypes(request.GrantTypes);
        var responseTypes = ValidateResponseTypes(request.ResponseTypes);
        var tokenAuthMethod = ValidateTokenEndpointAuthMethod(request.TokenEndpointAuthMethod);

        var record = new RegisteredClientRecord(
            ClientId: _clientIdFactory(),
            ClientSecret: null,
            TokenEndpointAuthMethod: tokenAuthMethod,
            RedirectUris: redirectUris,
            GrantTypes: grantTypes,
            ResponseTypes: responseTypes,
            RequirePkce: request.RequirePkce ?? true,
            ClientName: request.ClientName,
            IssuedAt: DateTimeOffset.UtcNow,
            ClientSecretExpiresAt: null
        );

        if (!_clients.TryAdd(record.ClientId, record))
        {
            throw new InvalidOperationException("Failed to persist registered client.");
        }

        return record;
    }

    public bool TryGetClient(string clientId, out RegisteredClientRecord record) =>
        _clients.TryGetValue(clientId, out record!);

    public bool TryValidateSecret(string clientId, string? providedSecret, out RegisteredClientRecord record)
    {
        if (!_clients.TryGetValue(clientId, out record!))
        {
            return false;
        }

        if (record.ClientSecret == null)
        {
            return string.IsNullOrEmpty(providedSecret);
        }

        return string.Equals(record.ClientSecret, providedSecret, StringComparison.Ordinal);
    }

    public bool IsRedirectUriAllowed(string clientId, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        return _clients.TryGetValue(clientId, out var record) && record.AllowsRedirect(redirectUri);
    }

    private static IReadOnlyList<Uri> ValidateRedirectUris(IReadOnlyCollection<string>? redirectUris)
    {
        if (redirectUris == null || redirectUris.Count == 0)
        {
            throw new InvalidOperationException("redirect_uris must be provided.");
        }

        var validated = new List<Uri>(redirectUris.Count);
        foreach (var candidate in redirectUris)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException("redirect_uris must contain absolute URIs.");
            }

            if (!IsAllowedRedirectUri(uri))
            {
                throw new InvalidOperationException("redirect_uris must use HTTPS or loopback HTTP scheme.");
            }

            validated.Add(uri);
        }

        return validated;
    }

    private static IReadOnlyList<string> ValidateGrantTypes(IReadOnlyCollection<string>? grantTypes)
    {
        if (grantTypes == null || grantTypes.Count == 0)
        {
            return new[] { "authorization_code", "refresh_token" };
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "authorization_code",
            "refresh_token",
            "client_credentials",
        };

        foreach (var grant in grantTypes)
        {
            if (string.IsNullOrWhiteSpace(grant) || !allowed.Contains(grant))
            {
                throw new InvalidOperationException($"Unsupported grant_type '{grant}'.");
            }
        }

        return grantTypes.ToArray();
    }

    private static IReadOnlyList<string> ValidateResponseTypes(IReadOnlyCollection<string>? responseTypes)
    {
        if (responseTypes == null || responseTypes.Count == 0)
        {
            return new[] { "code" };
        }

        foreach (var responseType in responseTypes)
        {
            if (!string.Equals(responseType, "code", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported response_type '{responseType}'.");
            }
        }

        return responseTypes.ToArray();
    }

    private static string ValidateTokenEndpointAuthMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return "none";
        }

        if (!string.Equals(method, "none", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only public clients (token_endpoint_auth_method \"none\") are supported by the demo server.");
        }

        return method;
    }

    private static bool IsAllowedRedirectUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
    }

    private static string GenerateClientId() => Guid.NewGuid().ToString("N");
}

internal sealed class DynamicClientRegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; init; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("require_pkce")]
    public bool? RequirePkce { get; init; }
}

internal sealed record RegisteredClientRecord(
    string ClientId,
    string? ClientSecret,
    string TokenEndpointAuthMethod,
    IReadOnlyList<Uri> RedirectUris,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> ResponseTypes,
    bool RequirePkce,
    string? ClientName,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ClientSecretExpiresAt
)
{
    public bool SupportsGrant(string grantType) =>
        GrantTypes.Any(value => string.Equals(value, grantType, StringComparison.Ordinal));

    public bool AllowsRedirect(Uri redirectUri) =>
        RedirectUris.Any(uri =>
            Uri.Compare(
                uri,
                redirectUri,
                UriComponents.AbsoluteUri,
                UriFormat.Unescaped,
                StringComparison.Ordinal
            ) == 0
        );
}
