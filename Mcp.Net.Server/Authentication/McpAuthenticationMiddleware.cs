using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Middleware that handles authentication for MCP endpoints
/// </summary>
/// <remarks>
/// This middleware applies authentication to secured endpoints.
/// It uses the configured <see cref="IAuthHandler"/> to authenticate requests
/// and sets up the HTTP context with the authentication result.
///
/// Security considerations:
/// - Configure secured paths carefully to protect sensitive endpoints
/// - Enable logging to track authentication attempts
/// - Consider using HTTPS in production environments
/// - Review authentication handlers for security best practices
/// </remarks>
public class McpAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuthHandler? _authHandler;
    private readonly ILogger<McpAuthenticationMiddleware> _logger;
    private readonly AuthOptions _options;
    private readonly Dictionary<string, bool> _securedPathCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationMiddleware"/> class
    /// </summary>
    /// <param name="next">The next middleware in the pipeline</param>
    /// <param name="logger">Logger for authentication events</param>
    /// <param name="authHandler">Optional authentication handler</param>
    /// <param name="options">Optional authentication options</param>
    public McpAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<McpAuthenticationMiddleware> logger,
        IAuthHandler? authHandler = null,
        AuthOptions? options = null
    )
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authHandler = authHandler;
        _options = options ?? new AuthOptions();

        LogConfigurationStatus();
    }

    /// <summary>
    /// Processes an HTTP request
    /// </summary>
    /// <param name="context">The HTTP context for the request</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        string path = context.Request.Path.Value ?? string.Empty;
        string method = context.Request.Method;

        // Check if authentication is needed for this endpoint
        if (!IsSecuredEndpoint(context.Request.Path))
        {
            LogUnsecuredAccess(path, method);
            await _next(context);
            return;
        }

        LogSecuredEndpointAccess(path, method);

        // If authentication is disabled or no handler is provided, skip authentication
        if (!_options.Enabled || _authHandler == null)
        {
            LogSkippedAuthentication(path, method);
            await _next(context);
            return;
        }

        try
        {
            // Authenticate the request
            var authResult = await _authHandler.AuthenticateAsync(context);

            if (!authResult.Succeeded)
            {
                await HandleFailedAuthentication(context, authResult, path);
                return;
            }

            // Authentication succeeded - set up context and continue
            SetupAuthenticatedContext(context, authResult);

            // Log successful authentication
            LogSuccessfulAuthentication(context, authResult);

            await _next(context);
        }
        catch (Exception ex)
        {
            // Log authentication error
            _logger.LogError(ex, "Authentication error for {Path}: {Message}", path, ex.Message);

            context.Response.StatusCode = 500;
            await WriteErrorResponse(
                context,
                "Authentication error",
                "An internal server error occurred during authentication"
            );
        }
    }

    /// <summary>
    /// Determines if a path requires authentication
    /// </summary>
    /// <param name="path">The request path</param>
    /// <returns>True if the path requires authentication</returns>
    private bool IsSecuredEndpoint(PathString path)
    {
        string pathValue = path.Value ?? string.Empty;

        // Check cache first for performance
        if (_securedPathCache.TryGetValue(pathValue, out bool isSecured))
        {
            return isSecured;
        }

        // If no secured paths are defined, nothing is secured
        if (_options.SecuredPaths.Count == 0)
        {
            _securedPathCache[pathValue] = false;
            return false;
        }

        // Check against each secured path pattern
        foreach (var securedPath in _options.SecuredPaths)
        {
            if (path.StartsWithSegments(securedPath, StringComparison.OrdinalIgnoreCase))
            {
                _securedPathCache[pathValue] = true;
                return true;
            }
        }

        // Cache the result
        _securedPathCache[pathValue] = false;
        return false;
    }

    /// <summary>
    /// Handles a failed authentication attempt
    /// </summary>
    private async Task HandleFailedAuthentication(
        HttpContext context,
        AuthResult authResult,
        string path
    )
    {
        // Log failure with appropriate level and details
        string reason = authResult.FailureReason ?? "Unknown reason";
        string clientIp = GetClientIpAddress(context);

        _logger.LogWarning(
            "Authentication failed for {Path} from {IpAddress}: {Reason}",
            path,
            clientIp,
            reason
        );

        // Set appropriate status code based on the type of failure
        context.Response.StatusCode = 401; // Default to 401 Unauthorized

        // Add WWW-Authenticate header to suggest to clients how to authenticate
        var scheme =
            _authHandler?.SchemeName
            ?? (!string.IsNullOrWhiteSpace(_options.SchemeName) ? _options.SchemeName : "MCP");

        if (_options is OAuthResourceServerOptions oauthOptions)
        {
            var metadataUrl = BuildMetadataUrl(context, oauthOptions);
            if (!string.IsNullOrEmpty(metadataUrl))
            {
                context.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{metadataUrl}\"";
            }
            else
            {
                context.Response.Headers.Append("WWW-Authenticate", scheme);
            }
        }
        else
        {
            context.Response.Headers.Append("WWW-Authenticate", scheme);
        }

        // Write a structured error response
        await WriteErrorResponse(context, "Unauthorized", reason);
    }

    /// <summary>
    /// Sets up the HTTP context for an authenticated request
    /// </summary>
    private void SetupAuthenticatedContext(HttpContext context, AuthResult authResult)
    {
        // Store auth result in context for later middleware
        context.Items["AuthResult"] = authResult;
        context.Items["AuthenticatedUserId"] = authResult.UserId;

        // Set up claims principal for ASP.NET Core authorization
        var principal = authResult.ToClaimsPrincipal();
        if (principal != null)
        {
            context.User = principal;
        }
    }

    /// <summary>
    /// Writes a structured error response
    /// </summary>
    private static async Task WriteErrorResponse(HttpContext context, string error, string message)
    {
        var response = new
        {
            error,
            message,
            status = context.Response.StatusCode,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            response,
            new JsonSerializerOptions { WriteIndented = true }
        );
    }

    /// <summary>
    /// Gets the client IP address from the request
    /// </summary>
    private static string GetClientIpAddress(HttpContext context)
    {
        // Try to get the X-Forwarded-For header first for clients behind proxies
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedFor))
        {
            string[] addresses = forwardedFor
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            return addresses.Length > 0 ? addresses[0].Trim() : "unknown";
        }

        // Fall back to connection remote IP address
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    #region Logging Methods

    private void LogConfigurationStatus()
    {
        if (_options.Enabled && _authHandler != null)
        {
            _logger.LogInformation(
                "Authentication middleware configured with scheme: {Scheme}, secured paths: {Paths}",
                _authHandler.SchemeName,
                string.Join(", ", _options.SecuredPaths)
            );
        }
        else if (!_options.Enabled)
        {
            _logger.LogWarning(
                "Authentication is DISABLED. All endpoints will be publicly accessible."
            );
        }
        else if (_authHandler == null)
        {
            _logger.LogWarning(
                "No authentication handler configured. Authentication will be skipped."
            );
        }
    }

    private static string? BuildMetadataUrl(
        HttpContext context,
        OAuthResourceServerOptions oauthOptions
    )
    {
        if (
            Uri.TryCreate(oauthOptions.ResourceMetadataPath, UriKind.Absolute, out var absoluteUri)
        )
        {
            return absoluteUri.ToString();
        }

        string? authority = null;
        if (
            !string.IsNullOrWhiteSpace(oauthOptions.Resource)
            && Uri.TryCreate(oauthOptions.Resource, UriKind.Absolute, out var resourceUri)
        )
        {
            authority = resourceUri.GetLeftPart(UriPartial.Authority);
        }
        else if (context.Request.Host.HasValue)
        {
            authority = $"{context.Request.Scheme}://{context.Request.Host}";
        }

        if (authority == null)
        {
            return null;
        }

        var path = oauthOptions.ResourceMetadataPath.StartsWith("/")
            ? oauthOptions.ResourceMetadataPath
            : "/" + oauthOptions.ResourceMetadataPath;

        return $"{authority.TrimEnd('/')}{path}";
    }

    private void LogUnsecuredAccess(string path, string method)
    {
        if (_options.EnableLogging && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Accessing unsecured endpoint: {Method} {Path}", method, path);
        }
    }

    private void LogSecuredEndpointAccess(string path, string method)
    {
        if (_options.EnableLogging && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Accessing secured endpoint: {Method} {Path}", method, path);
        }
    }

    private void LogSkippedAuthentication(string path, string method)
    {
        if (_options.EnableLogging)
        {
            string reason = !_options.Enabled
                ? "authentication is disabled"
                : "no authentication handler configured";

            _logger.LogWarning(
                "Authentication skipped for secured endpoint {Method} {Path} because {Reason}. "
                    + "This is potentially insecure. Configure authentication with WithAuthentication().",
                method,
                path,
                reason
            );
        }
    }

    private void LogSuccessfulAuthentication(HttpContext context, AuthResult authResult)
    {
        if (_options.EnableLogging)
        {
            string clientIp = GetClientIpAddress(context);
            string path = context.Request.Path.Value ?? string.Empty;
            string method = context.Request.Method;

            _logger.LogInformation(
                "Authentication succeeded for user {UserId} accessing {Method} {Path} from {IpAddress}",
                authResult.UserId ?? "anonymous",
                method,
                path,
                clientIp
            );
        }
    }

    #endregion
}
