using System;
using System.Collections.Generic;

namespace Mcp.Net.Client.Authentication;

/// <summary>
/// Common configuration shared across OAuth client flows.
/// </summary>
public class OAuthClientOptions
{
    /// <summary>
    /// Canonical MCP resource URI (required).
    /// </summary>
    public Uri Resource { get; set; } = null!;

    /// <summary>
    /// OAuth 2.1 client identifier (required).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional client secret for confidential clients.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Scopes requested during token acquisition.
    /// </summary>
    public IReadOnlyCollection<string> Scopes { get; set; } =
        Array.Empty<string>();

    /// <summary>
    /// Metadata discovery endpoint (e.g. https://example/.well-known/oauth-authorization-server).
    /// </summary>
    public Uri? AuthorizationServerMetadataAddress { get; set; }

    /// <summary>
    /// Optional protected resource metadata URI. When omitted, <see cref="Resource"/> is used.
    /// </summary>
    public Uri? ResourceMetadataAddress { get; set; }

    /// <summary>
    /// Redirect URI used for authorization-code flows.
    /// </summary>
    public Uri? RedirectUri { get; set; }
}
