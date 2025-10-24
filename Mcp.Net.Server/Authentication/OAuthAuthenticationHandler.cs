using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Mcp.Net.Server.Authentication;

/// <summary>
/// OAuth bearer authentication handler that validates tokens according to the MCP OAuth resource server requirements.
/// </summary>
internal sealed class OAuthAuthenticationHandler : IAuthHandler
{
    private readonly OAuthResourceServerOptions _options;
    private readonly ILogger<OAuthAuthenticationHandler> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private TokenValidationParameters? _tokenValidationParameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">OAuth resource server configuration.</param>
    /// <param name="logger">Logger used for diagnostic output.</param>
    public OAuthAuthenticationHandler(
        OAuthResourceServerOptions options,
        ILogger<OAuthAuthenticationHandler> logger
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _tokenHandler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };

        if (!string.IsNullOrWhiteSpace(_options.Authority))
        {
            var metadataAddress = BuildMetadataAddress(_options.Authority);
            var documentRetriever = new HttpDocumentRetriever
            {
                RequireHttps = !_options.AllowInsecureMetadataEndpoints,
            };

            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                documentRetriever
            );
        }
    }

    /// <summary>
    /// Gets the configured OAuth options.
    /// </summary>
    public OAuthResourceServerOptions Options => _options;

    /// <inheritdoc />
    public string SchemeName => _options.SchemeName;

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateAsync(HttpContext context)
    {
        if (!TryGetBearerToken(context, out var token, out var failureResult))
        {
            return failureResult!;
        }

        try
        {
            var validationParameters = await GetValidationParametersAsync(context.RequestAborted);
            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);

            if (!ValidateResourceIndicator(principal))
            {
                _logger.LogWarning(
                    "Bearer token audience does not include required resource {Resource}",
                    _options.Resource
                );
                return AuthResult.Fail(
                    "Bearer token audience does not match the MCP resource.",
                    StatusCodes.Status403Forbidden,
                    "insufficient_scope",
                    "Access token is not valid for the requested MCP resource."
                );
            }

            var userId = DetermineUserId(principal);
            var claims = ExtractClaims(principal);

            return AuthResult.Success(userId, claims);
        }
        catch (SecurityTokenSignatureKeyNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Signing key not found while validating bearer token. Triggering metadata refresh."
            );
            _configurationManager?.RequestRefresh();
            return AuthResult.Fail(
                "Unable to validate bearer token signature.",
                StatusCodes.Status401Unauthorized,
                "invalid_token",
                "Resource server is unable to validate the bearer token signature."
            );
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogInformation(ex, "Bearer token expired.");
            return AuthResult.Fail(
                "Bearer token has expired.",
                StatusCodes.Status401Unauthorized,
                "invalid_token",
                "Bearer token has expired."
            );
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Bearer token validation failed: {Message}", ex.Message);
            return AuthResult.Fail(
                "Bearer token is invalid.",
                StatusCodes.Status401Unauthorized,
                "invalid_token",
                "Bearer token is invalid."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during OAuth authentication.");
            return AuthResult.Fail(
                "OAuth authentication error.",
                StatusCodes.Status500InternalServerError,
                "server_error",
                "Unexpected error encountered while validating the bearer token."
            );
        }
    }

    private bool TryGetBearerToken(
        HttpContext context,
        out string token,
        out AuthResult? failureResult
    )
    {
        token = string.Empty;
        failureResult = null;

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
        {
            var headerValue = authHeaderValues.ToString();
            if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                failureResult = AuthResult.Fail(
                    "Authorization header must use the Bearer scheme.",
                    StatusCodes.Status401Unauthorized,
                    "invalid_request",
                    "Authorization header must use the Bearer scheme."
                );
                return false;
            }

            var candidate = headerValue.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                failureResult = AuthResult.Fail(
                    "Bearer token is missing.",
                    StatusCodes.Status401Unauthorized,
                    "invalid_token",
                    "Bearer token is missing."
                );
                return false;
            }

            token = candidate;
            return true;
        }

        if (_options.AllowQueryStringTokens)
        {
            var queryToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(queryToken))
            {
                token = queryToken;
                return true;
            }
        }

        failureResult = AuthResult.Fail(
            "Missing bearer token.",
            StatusCodes.Status401Unauthorized,
            "invalid_token",
            "Request did not include bearer token credentials."
        );
        return false;
    }

    private async Task<TokenValidationParameters> GetValidationParametersAsync(
        CancellationToken cancellationToken
    )
    {
        if (_tokenValidationParameters != null)
        {
            return _tokenValidationParameters;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tokenValidationParameters != null)
            {
                return _tokenValidationParameters;
            }

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = _options.ValidateIssuer,
                ValidateAudience = _options.ValidateAudience,
                ValidateLifetime = true,
                ClockSkew = _options.TokenClockSkew,
                RequireSignedTokens = true,
                NameClaimType = ClaimTypes.NameIdentifier,
            };

            if (_options.ValidateAudience)
            {
                if (_options.ValidAudiences.Count > 0)
                {
                    parameters.ValidAudiences = _options.ValidAudiences;
                }
                else if (!string.IsNullOrWhiteSpace(_options.Resource))
                {
                    parameters.ValidAudience = _options.Resource;
                }
            }

            if (_options.ValidateIssuer)
            {
                if (_options.ValidIssuers.Count > 0)
                {
                    parameters.ValidIssuers = _options.ValidIssuers;
                }
                else if (!string.IsNullOrWhiteSpace(_options.Authority))
                {
                    parameters.ValidIssuer = _options.Authority.TrimEnd('/');
                }
            }

            var signingKeys = new List<SecurityKey>(_options.SigningKeys);

            if (_configurationManager != null)
            {
                var configuration = await _configurationManager
                    .GetConfigurationAsync(cancellationToken)
                    .ConfigureAwait(false);

                signingKeys.AddRange(configuration.SigningKeys);

                if (
                    _options.ValidateIssuer
                    && parameters.ValidIssuer == null
                    && (_options.ValidIssuers.Count == 0)
                )
                {
                    parameters.ValidIssuer = configuration.Issuer;
                }
            }

            if (signingKeys.Count == 0)
            {
                throw new InvalidOperationException(
                    "OAuth authentication requires either an authority or explicitly configured signing keys."
                );
            }

            parameters.IssuerSigningKeys = signingKeys;

            _tokenValidationParameters = parameters;
            return parameters;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private bool ValidateResourceIndicator(ClaimsPrincipal principal)
    {
        if (
            !_options.EnforceResourceIndicator
            || !_options.ValidateAudience
            || string.IsNullOrWhiteSpace(_options.Resource)
        )
        {
            return true;
        }

        var expected = NormalizeAudience(_options.Resource);
        if (expected == null)
        {
            return true;
        }

        var audiences = principal.Claims
            .Where(claim => string.Equals(claim.Type, JwtRegisteredClaimNames.Aud, StringComparison.OrdinalIgnoreCase) || claim.Type == "aud")
            .Select(claim => NormalizeAudience(claim.Value))
            .Where(value => value != null);

        return audiences.Any(audience => string.Equals(audience, expected, StringComparison.Ordinal));
    }

    private static string DetermineUserId(ClaimsPrincipal principal)
    {
        var subject =
            principal.FindFirst(ClaimTypes.NameIdentifier)
            ?? principal.FindFirst("sub")
            ?? (principal.Identity?.Name is { Length: > 0 } name
                ? new Claim(ClaimTypes.NameIdentifier, name)
                : null);

        return subject?.Value ?? "oauth-user";
    }

    private static Dictionary<string, string> ExtractClaims(ClaimsPrincipal principal)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ClaimTypes.NameIdentifier,
            "sub",
        };

        return principal.Claims
            .Where(claim => !excluded.Contains(claim.Type))
            .GroupBy(claim => claim.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
    }

    private static string? NormalizeAudience(string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            return null;
        }

        if (Uri.TryCreate(audience, UriKind.Absolute, out var uri))
        {
            return uri.ToString().TrimEnd('/');
        }

        return audience.Trim().TrimEnd('/');
    }

    private static string BuildMetadataAddress(string authority)
    {
        var trimmed = authority.TrimEnd('/');

        return trimmed.EndsWith(
                "/.well-known/openid-configuration",
                StringComparison.OrdinalIgnoreCase
            )
            ? trimmed
            : $"{trimmed}/.well-known/openid-configuration";
    }
}
