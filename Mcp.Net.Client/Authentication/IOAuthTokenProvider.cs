using System.Threading;
using System.Threading.Tasks;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Defines the contract for supplying OAuth access tokens to MCP transports.
/// </summary>
public interface IOAuthTokenProvider
{
    /// <summary>
    /// Acquires a new access token for the specified resource using the supplied challenge metadata.
    /// </summary>
    /// <param name="context">Context describing the resource and server challenge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OAuthTokenResponse"/> containing the access token, or <c>null</c> when acquisition fails.</returns>
    Task<OAuthTokenResponse?> AcquireTokenAsync(
        OAuthTokenRequestContext context,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to refresh an existing access token.
    /// </summary>
    /// <param name="context">Context describing the resource and server challenge.</param>
    /// <param name="currentToken">Current token previously issued.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A refreshed token when successful; otherwise <c>null</c>.</returns>
    Task<OAuthTokenResponse?> RefreshTokenAsync(
        OAuthTokenRequestContext context,
        OAuthTokenResponse currentToken,
        CancellationToken cancellationToken
    );
}
