namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Base options class for authentication configuration
/// </summary>
/// <remarks>
/// This class serves as the base for all authentication option classes.
/// It provides common configuration that applies to all authentication schemes.
/// </remarks>
public class AuthOptions
{
    /// <summary>
    /// Gets or sets whether authentication is enabled
    /// </summary>
    /// <remarks>
    /// When set to false, authentication will be bypassed entirely.
    /// This is primarily intended for development scenarios.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the scheme name for this authentication method
    /// </summary>
    public string SchemeName { get; set; } = "McpAuth";

    /// <summary>
    /// Gets or sets the paths that require authentication
    /// </summary>
    /// <remarks>
    /// Paths matching these patterns will require authentication.
    /// For example, "/api/protected/*" would require authentication for all paths starting with "/api/protected/".
    /// </remarks>
    public List<string> SecuredPaths { get; set; } = new() { "/mcp" };

    /// <summary>
    /// Gets or sets whether to log authentication events
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets whether authentication is explicitly disabled.
    /// </summary>
    public bool NoAuthExplicitlyConfigured { get; set; } = false;

    /// <summary>
    /// Gets or sets the authentication handler.
    /// </summary>
    public IAuthHandler? AuthHandler { get; set; }

    /// <summary>
    /// Gets or sets the API key validator.
    /// </summary>
    public IApiKeyValidator? ApiKeyValidator { get; set; }

    /// <summary>
    /// Gets whether security is configured.
    /// </summary>
    public virtual bool IsSecurityConfigured =>
        NoAuthExplicitlyConfigured || AuthHandler != null || ApiKeyValidator != null;

    /// <summary>
    /// Configures the options with a custom authentication handler.
    /// </summary>
    /// <param name="authHandler">The authentication handler</param>
    /// <returns>The options instance for chaining</returns>
    public AuthOptions WithAuthentication(IAuthHandler authHandler)
    {
        AuthHandler = authHandler;
        return this;
    }

    /// <summary>
    /// Configures the options with a custom API key validator.
    /// </summary>
    /// <param name="validator">The API key validator</param>
    /// <returns>The options instance for chaining</returns>
    public AuthOptions WithApiKeyValidator(IApiKeyValidator validator)
    {
        ApiKeyValidator = validator;
        return this;
    }

    /// <summary>
    /// Disables authentication.
    /// </summary>
    /// <returns>The options instance for chaining</returns>
    public AuthOptions WithNoAuth()
    {
        NoAuthExplicitlyConfigured = true;
        Enabled = false;
        return this;
    }
}
