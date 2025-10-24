using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Result of an authentication attempt
/// </summary>
/// <remarks>
/// This class encapsulates the result of an authentication operation,
/// including success/failure status, user identity information, and any claims.
/// It's designed to be extensible for different authentication schemes.
/// </remarks>
public class AuthResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthResult"/> class.
    /// </summary>
    public AuthResult()
    {
        Claims = new Dictionary<string, string>();
        StatusCode = StatusCodes.Status401Unauthorized;
    }

    /// <summary>
    /// Creates a successful authentication result
    /// </summary>
    /// <param name="userId">The authenticated user ID</param>
    /// <param name="claims">Optional claims for the user</param>
    /// <returns>A successful authentication result</returns>
    public static AuthResult Success(string userId, Dictionary<string, string>? claims = null)
    {
        return new AuthResult
        {
            Succeeded = true,
            StatusCode = StatusCodes.Status200OK,
            UserId = userId,
            Claims = claims ?? new Dictionary<string, string>(),
        };
    }

    /// <summary>
    /// Creates a failed authentication result
    /// </summary>
    /// <param name="reason">The reason for the failure</param>
    /// <param name="statusCode">HTTP status code that should be returned to the client.</param>
    /// <param name="errorCode">Optional OAuth 2.1 compliant error code (e.g. invalid_token).</param>
    /// <param name="errorDescription">Optional human-readable description for logging and clients.</param>
    /// <param name="errorUri">Optional URI with additional error details.</param>
    /// <returns>A failed authentication result</returns>
    public static AuthResult Fail(
        string reason,
        int statusCode = StatusCodes.Status401Unauthorized,
        string? errorCode = null,
        string? errorDescription = null,
        string? errorUri = null
    )
    {
        return new AuthResult
        {
            Succeeded = false,
            FailureReason = reason,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            ErrorDescription = errorDescription,
            ErrorUri = errorUri,
        };
    }

    /// <summary>
    /// Whether the authentication was successful
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// User ID for the authenticated user, if successful
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Additional claims for the authenticated user
    /// </summary>
    public Dictionary<string, string> Claims { get; set; }

    /// <summary>
    /// Reason for failure, if authentication was not successful
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code that should be returned to the client.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.1 compatible error code (e.g. invalid_token, insufficient_scope).
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets an optional human-readable error description.
    /// </summary>
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// Gets or sets an optional URI with additional error information.
    /// </summary>
    public string? ErrorUri { get; set; }

    /// <summary>
    /// Converts this auth result to a ClaimsPrincipal
    /// </summary>
    /// <returns>A ClaimsPrincipal representing this authentication result</returns>
    public ClaimsPrincipal? ToClaimsPrincipal()
    {
        if (!Succeeded || string.IsNullOrEmpty(UserId))
        {
            return null;
        }

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, UserId) };

        // Add additional claims
        foreach (var claim in Claims)
        {
            claims.Add(new Claim(claim.Key, claim.Value));
        }

        var identity = new ClaimsIdentity(claims, "McpAuth");
        return new ClaimsPrincipal(identity);
    }
}
