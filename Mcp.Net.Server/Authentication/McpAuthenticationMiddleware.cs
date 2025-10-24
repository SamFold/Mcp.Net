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
        string reason = authResult.FailureReason ?? authResult.ErrorDescription ?? "Unknown reason";
        string clientIp = GetClientIpAddress(context);

        _logger.LogWarning(
            "Authentication failed for {Path} from {IpAddress}: {Reason}",
            path,
            clientIp,
            reason
        );

        var statusCode = authResult.StatusCode;
        if (statusCode <= 0)
        {
            statusCode = StatusCodes.Status401Unauthorized;
        }

        context.Response.StatusCode = statusCode;

        if (statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            var headerValue = BuildWwwAuthenticateHeader(context, authResult);
            if (!string.IsNullOrEmpty(headerValue))
            {
                context.Response.Headers["WWW-Authenticate"] = headerValue;
            }
        }

        // Write a structured error response
        await WriteErrorResponse(context, authResult);
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
    /// Writes a structured error response based on the authentication result.
    /// </summary>
    private static async Task WriteErrorResponse(HttpContext context, AuthResult authResult)
    {
        var response = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["error"] = authResult.ErrorCode ?? "unauthorized",
            ["error_description"] =
                authResult.ErrorDescription ?? authResult.FailureReason ?? "Unauthorized request.",
            ["status"] = context.Response.StatusCode,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
        };

        if (!string.IsNullOrWhiteSpace(authResult.ErrorUri))
        {
            response["error_uri"] = authResult.ErrorUri;
        }

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

    private static string? BuildMetadataUrl(HttpContext context, OAuthResourceServerOptions oauthOptions)
    {
        if (
            Uri.TryCreate(oauthOptions.ResourceMetadataPath, UriKind.Absolute, out var absoluteUri)
        )
        {
            if (
                string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    absoluteUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return absoluteUri.ToString();
            }

            // For non-HTTP absolute URIs (e.g., file://), reuse the authority derived below but keep the pathname.
            return CombineAuthorityWithPath(context, oauthOptions, absoluteUri.AbsolutePath);
        }

        return CombineAuthorityWithPath(context, oauthOptions, oauthOptions.ResourceMetadataPath);
    }

    private string BuildWwwAuthenticateHeader(HttpContext context, AuthResult authResult)
    {
        var scheme =
            _authHandler?.SchemeName
            ?? (!string.IsNullOrWhiteSpace(_options.SchemeName) ? _options.SchemeName : "MCP");

        var parameters = new List<string>();

        if (_options is OAuthResourceServerOptions oauthOptions)
        {
            if (!string.IsNullOrWhiteSpace(oauthOptions.Resource))
            {
                parameters.Add(FormWwwAuthenticateTuple("resource", oauthOptions.Resource!));
            }

            var metadataUrl = BuildMetadataUrl(context, oauthOptions);
            if (!string.IsNullOrEmpty(metadataUrl))
            {
                parameters.Add(FormWwwAuthenticateTuple("resource_metadata", metadataUrl));
            }
        }

        if (!string.IsNullOrWhiteSpace(authResult.ErrorCode))
        {
            parameters.Add(FormWwwAuthenticateTuple("error", authResult.ErrorCode!));
        }

        if (!string.IsNullOrWhiteSpace(authResult.ErrorDescription))
        {
            parameters.Add(
                FormWwwAuthenticateTuple(
                    "error_description",
                    authResult.ErrorDescription!.Replace("\"", "'", StringComparison.Ordinal)
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(authResult.ErrorUri))
        {
            parameters.Add(FormWwwAuthenticateTuple("error_uri", authResult.ErrorUri!));
        }

        if (parameters.Count == 0)
        {
            return scheme;
        }

        return $"{scheme} {string.Join(", ", parameters)}";
    }

    private static string FormWwwAuthenticateTuple(string name, string value) =>
        $"{name}=\"{value}\"";

    private static string? CombineAuthorityWithPath(
        HttpContext context,
        OAuthResourceServerOptions oauthOptions,
        string candidatePath
    )
    {
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

        var path = candidatePath.StartsWith("/")
            ? candidatePath
            : "/" + candidatePath;

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
