using System;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Encapsulates contextual information required when acquiring an OAuth token.
/// </summary>
public sealed class OAuthTokenRequestContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthTokenRequestContext"/> class.
    /// </summary>
    /// <param name="resource">Canonical URI of the MCP resource server.</param>
    /// <param name="challenge">Parsed WWW-Authenticate challenge supplied by the server.</param>
    public OAuthTokenRequestContext(Uri resource, OAuthChallenge challenge)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Challenge = challenge ?? throw new ArgumentNullException(nameof(challenge));
    }

    /// <summary>
    /// Gets the canonical resource URI for which a token is being requested.
    /// </summary>
    public Uri Resource { get; }

    /// <summary>
    /// Gets the parsed challenge issued by the resource server.
    /// </summary>
    public OAuthChallenge Challenge { get; }
}
