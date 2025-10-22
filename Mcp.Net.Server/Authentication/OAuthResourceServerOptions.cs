using System.Collections.ObjectModel;
using Microsoft.IdentityModel.Tokens;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Options describing the MCP server's OAuth resource server configuration.
/// </summary>
/// <remarks>
/// These options capture the metadata required by MCP specification revision 2025-06-18,
/// allowing the server to expose OAuth protected resource metadata and validate client tokens.
/// </remarks>
public class OAuthResourceServerOptions : AuthOptions
{
    private readonly List<string> _authorizationServers = new();
    private readonly List<string> _validAudiences = new();
    private readonly List<string> _validIssuers = new();
    private readonly List<SecurityKey> _signingKeys = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthResourceServerOptions"/> class.
    /// </summary>
    public OAuthResourceServerOptions()
    {
        SchemeName = "Bearer";
    }

    /// <summary>
    /// Gets or sets the canonical resource identifier for this MCP server (typically the MCP endpoint URL).
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>
    /// Gets or sets the path that will expose OAuth protected resource metadata.
    /// </summary>
    public string ResourceMetadataPath { get; set; } = "/.well-known/oauth-protected-resource";

    /// <summary>
    /// Gets the list of authorization server issuer identifiers (metadata endpoints) associated with this resource.
    /// </summary>
    public IList<string> AuthorizationServers =>
        new ReadOnlyCollection<string>(_authorizationServers);

    /// <summary>
    /// Adds an authorization server metadata URL.
    /// </summary>
    /// <param name="metadataUrl">The metadata URL to register.</param>
    public void AddAuthorizationServer(string metadataUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataUrl);

        if (!_authorizationServers.Contains(metadataUrl, StringComparer.OrdinalIgnoreCase))
        {
            _authorizationServers.Add(metadataUrl);
        }
    }

    /// <summary>
    /// Gets whether the server should validate the issuer on inbound bearer tokens.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Gets or sets the primary authority used for issuer discovery.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Gets the list of accepted issuer values.
    /// </summary>
    public IList<string> ValidIssuers => new ReadOnlyCollection<string>(_validIssuers);

    /// <summary>
    /// Gets the collection of signing keys used to validate bearer tokens when discovery is not available.
    /// </summary>
    public IList<SecurityKey> SigningKeys => new ReadOnlyCollection<SecurityKey>(_signingKeys);

    /// <summary>
    /// Adds an accepted issuer value.
    /// </summary>
    /// <param name="issuer">Issuer identifier to accept.</param>
    public void AddValidIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        if (!_validIssuers.Contains(issuer, StringComparer.Ordinal))
        {
            _validIssuers.Add(issuer);
        }
    }

    /// <summary>
    /// Adds an accepted signing key for bearer token validation.
    /// </summary>
    /// <param name="key">The signing key to register.</param>
    public void AddSigningKey(SecurityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_signingKeys.Contains(key))
        {
            _signingKeys.Add(key);
        }
    }

    /// <summary>
    /// Gets whether the server should validate the token audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Gets the list of acceptable audiences for bearer tokens.
    /// </summary>
    public IList<string> ValidAudiences => new ReadOnlyCollection<string>(_validAudiences);

    /// <summary>
    /// Adds an accepted audience value.
    /// </summary>
    /// <param name="audience">Audience identifier to accept.</param>
    public void AddValidAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        if (!_validAudiences.Contains(audience, StringComparer.Ordinal))
        {
            _validAudiences.Add(audience);
        }
    }

    /// <summary>
    /// Gets or sets whether to enforce resource indicator validation specific to MCP.
    /// </summary>
    public bool EnforceResourceIndicator { get; set; } = true;

    /// <summary>
    /// Gets or sets the amount of clock skew tolerated during token validation.
    /// </summary>
    public TimeSpan TokenClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether metadata discovery over HTTP (non-HTTPS) endpoints is permitted.
    /// </summary>
    public bool AllowInsecureMetadataEndpoints { get; set; } = false;

    /// <summary>
    /// Gets or sets whether bearer tokens may be supplied via HTTP query string.
    /// </summary>
    public bool AllowQueryStringTokens { get; set; } = false;
}
