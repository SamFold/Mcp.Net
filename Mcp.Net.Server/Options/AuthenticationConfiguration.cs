using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Mcp.Net.Server.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Linq;

namespace Mcp.Net.Server.Options;

/// <summary>
/// Binds authentication settings from configuration sources (appsettings, environment variables).
/// </summary>
public class AuthenticationConfiguration
{
    /// <summary>
    /// Gets or sets whether authentication is enabled. When <c>false</c>, requests skip authentication unless explicitly disabled.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether authentication events should be logged.
    /// </summary>
    public bool? EnableLogging { get; set; }

    /// <summary>
    /// Gets or sets an explicit scheme name (defaults to <c>McpAuth</c> or <c>Bearer</c> for OAuth).
    /// </summary>
    public string? SchemeName { get; set; }

    /// <summary>
    /// Gets or sets the request paths requiring authentication.
    /// </summary>
    public List<string>? SecuredPaths { get; set; }

    /// <summary>
    /// Gets or sets whether authentication should be disabled entirely.
    /// </summary>
    public bool? Disable { get; set; }

    /// <summary>
    /// Gets or sets OAuth resource server configuration.
    /// </summary>
    public OAuthAuthenticationConfiguration? OAuth { get; set; }

    /// <summary>
    /// Applies the configuration to an authentication builder.
    /// </summary>
    /// <param name="builder">Auth builder to configure.</param>
    /// <param name="logger">Logger for configuration diagnostics.</param>
    public void ApplyTo(AuthBuilder builder, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (Disable == true)
        {
            builder.WithNoAuth();
            return;
        }

        if (Enabled.HasValue || EnableLogging.HasValue || SecuredPaths is { Count: > 0 } || SchemeName != null)
        {
            builder.WithOptions(options =>
            {
                if (Enabled.HasValue)
                {
                    options.Enabled = Enabled.Value;
                }

                if (EnableLogging.HasValue)
                {
                    options.EnableLogging = EnableLogging.Value;
                }

                if (SecuredPaths is { Count: > 0 })
                {
                    options.SecuredPaths = new List<string>(SecuredPaths);
                }

                if (!string.IsNullOrWhiteSpace(SchemeName))
                {
                    options.SchemeName = SchemeName!;
                }
            });
        }

        if (OAuth is not null)
        {
            builder.WithOAuth(o => OAuth.ApplyTo(o, logger));
        }
    }
}

/// <summary>
/// Represents configuration values for OAuth resource server integration.
/// </summary>
public class OAuthAuthenticationConfiguration
{
    /// <summary>
    /// Gets or sets the canonical resource identifier for the MCP endpoint.
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>
    /// Gets or sets the relative or absolute URI that exposes protected resource metadata.
    /// </summary>
    public string? ResourceMetadataPath { get; set; }

    /// <summary>
    /// Gets or sets the authorization server authority (used for OpenID Connect discovery).
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Gets or sets whether insecure metadata endpoints are permitted.
    /// </summary>
    public bool? AllowInsecureMetadataEndpoints { get; set; }

    /// <summary>
    /// Gets or sets whether bearer tokens may appear in the query string.
    /// </summary>
    public bool? AllowQueryStringTokens { get; set; }

    /// <summary>
    /// Gets or sets whether the resource server should validate token audiences.
    /// </summary>
    public bool? ValidateAudience { get; set; }

    /// <summary>
    /// Gets or sets whether the resource server should validate token issuers.
    /// </summary>
    public bool? ValidateIssuer { get; set; }

    /// <summary>
    /// Gets or sets whether RFC 8707 resource indicator enforcement is enabled.
    /// </summary>
    public bool? EnforceResourceIndicator { get; set; }

    /// <summary>
    /// Gets or sets the acceptable clock skew (in minutes) for token validation.
    /// </summary>
    public double? TokenClockSkewMinutes { get; set; }

    /// <summary>
    /// Gets or sets authorization server metadata endpoints associated with the resource.
    /// </summary>
    public List<string>? AuthorizationServers { get; set; }

    /// <summary>
    /// Gets or sets accepted audience values for bearer tokens.
    /// </summary>
    public List<string>? ValidAudiences { get; set; }

    /// <summary>
    /// Gets or sets accepted issuer values for bearer tokens.
    /// </summary>
    public List<string>? ValidIssuers { get; set; }

    /// <summary>
    /// Gets or sets symmetric signing keys (base64 encoded) used when discovery is unavailable.
    /// </summary>
    public List<string>? SigningKeys { get; set; }

    /// <summary>
    /// Applies the configuration to the supplied OAuth options.
    /// </summary>
    /// <param name="options">Target options.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public void ApplyTo(OAuthResourceServerOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(Resource))
        {
            options.Resource = Resource;
        }

        if (!string.IsNullOrWhiteSpace(ResourceMetadataPath))
        {
            options.ResourceMetadataPath = ResourceMetadataPath!;
        }

        if (!string.IsNullOrWhiteSpace(Authority))
        {
            options.Authority = Authority!.Trim();
        }

        if (AllowInsecureMetadataEndpoints.HasValue)
        {
            options.AllowInsecureMetadataEndpoints = AllowInsecureMetadataEndpoints.Value;
        }

        if (AllowQueryStringTokens.HasValue)
        {
            options.AllowQueryStringTokens = AllowQueryStringTokens.Value;
        }

        if (ValidateAudience.HasValue)
        {
            options.ValidateAudience = ValidateAudience.Value;
        }

        if (ValidateIssuer.HasValue)
        {
            options.ValidateIssuer = ValidateIssuer.Value;
        }

        if (EnforceResourceIndicator.HasValue)
        {
            options.EnforceResourceIndicator = EnforceResourceIndicator.Value;
        }

        if (TokenClockSkewMinutes.HasValue)
        {
            options.TokenClockSkew = TimeSpan.FromMinutes(TokenClockSkewMinutes.Value);
        }

        if (AuthorizationServers is { Count: > 0 })
        {
            foreach (var server in AuthorizationServers.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                options.AddAuthorizationServer(server);
            }
        }

        if (ValidAudiences is { Count: > 0 })
        {
            foreach (var audience in ValidAudiences.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                options.AddValidAudience(audience);
            }
        }

        if (ValidIssuers is { Count: > 0 })
        {
            foreach (var issuer in ValidIssuers.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                options.AddValidIssuer(issuer);
            }
        }

        if (SigningKeys is { Count: > 0 })
        {
            foreach (var keyValue in SigningKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (TryCreateSymmetricKey(keyValue.Trim(), out var key, logger))
                {
                    options.AddSigningKey(key);
                }
            }
        }
    }

    private static bool TryCreateSymmetricKey(
        string encodedValue,
        [NotNullWhen(true)] out SecurityKey? key,
        ILogger? logger
    )
    {
        key = null;

        try
        {
            // Accept either standard Base64 or Base64Url encodings.
            byte[] rawBytes;
            try
            {
                rawBytes = Convert.FromBase64String(encodedValue);
            }
            catch (FormatException)
            {
                rawBytes = Base64UrlEncoder.DecodeBytes(encodedValue);
            }

            if (rawBytes.Length < 16)
            {
                logger?.LogWarning(
                    "Ignoring configured signing key because it is less than 128 bits in length."
                );
                return false;
            }

            key = new SymmetricSecurityKey(rawBytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            logger?.LogError(
                ex,
                "Failed to parse configured signing key. Ensure the value is a valid Base64 or Base64Url string."
            );
            return false;
        }
    }
}
