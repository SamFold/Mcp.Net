using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mcp.Net.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Transport.Sse;

internal sealed class SseRequestSecurity
{
    private readonly HashSet<string> _allowedOrigins;
    private readonly IAuthHandler? _authHandler;

    public SseRequestSecurity(
        IEnumerable<string>? allowedOrigins,
        string? canonicalOrigin,
        IAuthHandler? authHandler
    )
    {
        _authHandler = authHandler;
        _allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (allowedOrigins != null)
        {
            foreach (var origin in allowedOrigins)
            {
                var normalized = NormalizeOrigin(origin);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    _allowedOrigins.Add(normalized);
                }
            }
        }

        var normalizedCanonical = NormalizeOrigin(canonicalOrigin);
        if (_allowedOrigins.Count == 0 && !string.IsNullOrEmpty(normalizedCanonical))
        {
            _allowedOrigins.Add(normalizedCanonical);
        }
    }

    public async Task<bool> ValidateOriginAsync(HttpContext context, ILogger logger)
    {
        if (_allowedOrigins.Count == 0)
        {
            return true;
        }

        var originHeader = context.Request.Headers["Origin"].ToString();
        var normalizedOrigin = NormalizeOrigin(originHeader);

        if (!string.IsNullOrEmpty(normalizedOrigin) && _allowedOrigins.Contains(normalizedOrigin))
        {
            return true;
        }

        var hostHeader = context.Request.Headers.Host.ToString();
        if (!string.IsNullOrWhiteSpace(hostHeader))
        {
            var scheme = string.IsNullOrWhiteSpace(context.Request.Scheme)
                ? "http"
                : context.Request.Scheme;
            var hostCandidate = NormalizeOrigin($"{scheme}://{hostHeader}");
            if (!string.IsNullOrEmpty(hostCandidate) && _allowedOrigins.Contains(hostCandidate))
            {
                if (string.IsNullOrEmpty(originHeader))
                {
                    logger.LogDebug(
                        "Origin header missing; accepted request because host {Host} is permitted.",
                        hostHeader
                    );
                }

                return true;
            }
        }

        logger.LogWarning(
            "Rejecting request due to invalid origin {Origin}. Allowed origins: {Allowed}",
            string.IsNullOrWhiteSpace(originHeader) ? "<missing>" : originHeader,
            string.Join(", ", _allowedOrigins)
        );

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "invalid_origin", message = "Origin is not permitted." }
            );
        }

        return false;
    }

    public async Task<(bool Success, AuthResult? Result)> AuthenticateAsync(
        HttpContext context,
        ILogger logger
    )
    {
        if (_authHandler == null)
        {
            return (true, null);
        }

        var result = await _authHandler.AuthenticateAsync(context);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Authentication failed: {Reason}",
                result.FailureReason
            );
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { error = "Unauthorized", message = result.FailureReason }
            );
            return (false, null);
        }

        return (true, result);
    }

    private static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            var leftPart = uri.GetLeftPart(UriPartial.Authority);
            return leftPart.TrimEnd('/').ToLowerInvariant();
        }

        return origin.Trim().TrimEnd('/').ToLowerInvariant();
    }
}
