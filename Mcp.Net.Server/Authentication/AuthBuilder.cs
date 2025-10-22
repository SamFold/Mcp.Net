using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Builder for configuring authentication for MCP servers.
/// </summary>
public class AuthBuilder
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AuthBuilder> _logger;
    private readonly AuthOptions _baseOptions = new();
    private IAuthHandler? _authHandler;
    private OAuthResourceServerOptions? _oauthOptions;
    private AuthOptions? _configuredOptions;
    private bool _authDisabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthBuilder"/> class.
    /// </summary>
    /// <param name="loggerFactory">Factory used to create loggers.</param>
    public AuthBuilder(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<AuthBuilder>();
    }

    /// <summary>
    /// Gets whether authentication has been explicitly disabled.
    /// </summary>
    public bool IsAuthDisabled => _authDisabled;

    /// <summary>
    /// Gets the configured authentication handler, when one has been supplied.
    /// </summary>
    public IAuthHandler? AuthHandler => _authHandler;

    /// <summary>
    /// Gets the most specific authentication options configured by this builder.
    /// </summary>
    public AuthOptions ConfiguredOptions =>
        _configuredOptions ?? (AuthOptions?)_oauthOptions ?? _baseOptions;

    /// <summary>
    /// Applies common authentication options.
    /// </summary>
    public AuthBuilder WithOptions(Action<AuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_baseOptions);
        _configuredOptions = _baseOptions;
        return this;
    }

    /// <summary>
    /// Registers secured paths that require authentication.
    /// </summary>
    public AuthBuilder WithSecuredPaths(params string[] paths)
    {
        if (paths == null || paths.Length == 0)
        {
            throw new ArgumentException("At least one path must be specified.", nameof(paths));
        }

        _baseOptions.SecuredPaths = new List<string>(paths);
        return this;
    }

    /// <summary>
    /// Disables authentication entirely.
    /// </summary>
    public AuthBuilder WithNoAuth()
    {
        _authDisabled = true;
        _baseOptions.Enabled = false;
        _configuredOptions = _baseOptions;
        _logger.LogWarning("Authentication has been disabled; all requests will be allowed.");
        return this;
    }

    /// <summary>
    /// Registers a pre-built authentication handler instance.
    /// </summary>
    public AuthBuilder WithHandler(IAuthHandler authHandler)
    {
        _authHandler = authHandler ?? throw new ArgumentNullException(nameof(authHandler));
        _configuredOptions = _baseOptions;
        _logger.LogInformation(
            "Using custom authentication handler: {Handler}",
            authHandler.GetType().Name
        );
        return this;
    }

    /// <summary>
    /// Configures OAuth-based bearer authentication.
    /// </summary>
    public AuthBuilder WithOAuth(Action<OAuthResourceServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _oauthOptions ??= new OAuthResourceServerOptions();
        configure(_oauthOptions);
        _configuredOptions = _oauthOptions;
        return this;
    }

    /// <summary>
    /// Materialises the configured authentication handler.
    /// </summary>
    public IAuthHandler? Build()
    {
        if (_authDisabled)
        {
            return null;
        }

        if (_authHandler != null)
        {
            return _authHandler;
        }

        if (_oauthOptions != null)
        {
            var options = _oauthOptions;
            options.Enabled = _baseOptions.Enabled;
            options.EnableLogging = _baseOptions.EnableLogging;
            options.SecuredPaths = _baseOptions.SecuredPaths;
            _configuredOptions = options;

            var handler = new OAuthAuthenticationHandler(
                options,
                _loggerFactory.CreateLogger<OAuthAuthenticationHandler>()
            );

            _logger.LogInformation("Configured OAuth bearer authentication.");
            return handler;
        }

        if (_baseOptions.Enabled)
        {
            _logger.LogInformation(
                "No authentication scheme configured; defaulting to disabled authentication."
            );
        }

        return null;
    }
}
