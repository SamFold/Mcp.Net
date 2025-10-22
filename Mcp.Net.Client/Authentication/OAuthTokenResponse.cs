using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Represents an OAuth access token returned by an authorization server.
/// </summary>
public sealed class OAuthTokenResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthTokenResponse"/> class.
    /// </summary>
    /// <param name="accessToken">The access token string.</param>
    /// <param name="expiresAt">The UTC expiry instant, when known.</param>
    /// <param name="refreshToken">An optional refresh token.</param>
    /// <param name="tokenType">Token type (defaults to "Bearer").</param>
    public OAuthTokenResponse(
        string accessToken,
        DateTimeOffset? expiresAt = null,
        string? refreshToken = null,
        string tokenType = "Bearer"
    )
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        ExpiresAt = expiresAt;
        RefreshToken = refreshToken;
        TokenType = tokenType;
    }

    /// <summary>
    /// Gets the access token string.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// Gets the token type (typically "Bearer").
    /// </summary>
    public string TokenType { get; }

    /// <summary>
    /// Gets the expiry instant in UTC, when supplied by the authorization server.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// Gets the associated refresh token, when provided.
    /// </summary>
    public string? RefreshToken { get; }

    /// <summary>
    /// Determines whether the token has expired.
    /// </summary>
    /// <param name="skew">Optional skew to apply when evaluating expiry.</param>
    /// <returns><c>true</c> when the token is expired; otherwise <c>false</c>.</returns>
    public bool IsExpired(TimeSpan? skew = null)
    {
        if (ExpiresAt == null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var effectiveSkew = skew ?? TimeSpan.FromMinutes(1);
        return now >= ExpiresAt.Value - effectiveSkew;
    }
}
