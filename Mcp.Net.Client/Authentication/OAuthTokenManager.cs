using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Manages OAuth tokens for MCP transports, handling caching and refresh orchestration.
/// </summary>
public sealed class OAuthTokenManager
{
    private readonly IOAuthTokenProvider? _tokenProvider;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, TokenEntry> _tokenCache = new();
    private const int MaxRefreshFailures = 3;

    public OAuthTokenManager(IOAuthTokenProvider? tokenProvider, ILogger? logger = null)
    {
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Attempts to retrieve a cached access token for the supplied resource.
    /// </summary>
    /// <param name="resource">Resource URI for which to retrieve a token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token when available; otherwise <c>null</c>.</returns>
    public async Task<string?> GetAccessTokenAsync(
        Uri resource,
        CancellationToken cancellationToken
    )
    {
        if (_tokenProvider == null)
        {
            return null;
        }

        var entry = _tokenCache.GetOrAdd(GetResourceKey(resource), _ => new TokenEntry());

        await entry.Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (entry.Token == null)
            {
                return null;
            }

            if (!entry.Token.IsExpired())
            {
                return entry.Token.AccessToken;
            }

            if (entry.RefreshFailureCount >= MaxRefreshFailures)
            {
                _logger.LogWarning(
                    "Skipping token refresh for {Resource} after {Attempts} failed attempt(s).",
                    resource,
                    entry.RefreshFailureCount
                );
                entry.Token = null;
                return null;
            }

            _logger.LogDebug("Access token expired for {Resource}, attempting refresh.", resource);
            var challenge = entry.LastChallenge;
            if (challenge == null)
            {
                return null;
            }

            var refreshed = await _tokenProvider.RefreshTokenAsync(
                new OAuthTokenRequestContext(resource, challenge),
                entry.Token,
                cancellationToken
            );

            if (refreshed != null && !string.IsNullOrEmpty(refreshed.AccessToken))
            {
                entry.Token = refreshed;
                entry.RefreshFailureCount = 0;
                return refreshed.AccessToken;
            }

            entry.RefreshFailureCount++;
            entry.Token = null;
            return null;
        }
        finally
        {
            entry.Semaphore.Release();
        }
    }

    /// <summary>
    /// Handles an unauthorized response by acquiring a new token using the supplied challenge.
    /// </summary>
    /// <param name="resource">Resource URI requiring authorization.</param>
    /// <param name="challenge">Challenge returned by the resource server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a token was successfully acquired; otherwise <c>false</c>.</returns>
    public async Task<bool> HandleUnauthorizedAsync(
        Uri resource,
        OAuthChallenge challenge,
        CancellationToken cancellationToken
    )
    {
        if (_tokenProvider == null)
        {
            _logger.LogDebug("No OAuth token provider configured; cannot satisfy challenge.");
            return false;
        }

        var key = GetResourceKey(resource);
        var entry = _tokenCache.GetOrAdd(key, _ => new TokenEntry());
        await entry.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var context = new OAuthTokenRequestContext(resource, challenge);
            var token = await _tokenProvider.AcquireTokenAsync(context, cancellationToken);
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                _logger.LogWarning(
                    "OAuth token acquisition failed for resource {Resource}.",
                    resource
                );
                return false;
            }

            entry.Token = token;
            entry.LastChallenge = challenge;
            entry.RefreshFailureCount = 0;
            _logger.LogInformation("OAuth token acquired for resource {Resource}.", resource);
            return true;
        }
        finally
        {
            entry.Semaphore.Release();
        }
    }

    private static string GetResourceKey(Uri resource)
    {
        var builder = new UriBuilder(resource) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private sealed class TokenEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public OAuthTokenResponse? Token { get; set; }
        public OAuthChallenge? LastChallenge { get; set; }
        public int RefreshFailureCount { get; set; }
    }
}
