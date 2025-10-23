using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Examples.Shared;

namespace Mcp.Net.Examples.SimpleServer;

internal static class DemoOAuthServer
{
    private static readonly ConcurrentDictionary<string, AuthorizationCodeRecord> s_authorizationCodes =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, RefreshTokenRecord> s_refreshTokens =
        new(StringComparer.Ordinal);

    internal static DemoOAuthConfiguration CreateConfiguration(Uri baseUri)
    {
        var signingKey = DemoOAuthDefaults.CreateSigningKey();
        return new DemoOAuthConfiguration
        {
            BaseUri = baseUri,
            ResourceUri = DemoOAuthDefaults.BuildResourceUri(baseUri),
            ResourceMetadataUri = DemoOAuthDefaults.BuildResourceMetadataUri(baseUri),
            AuthorizationServerMetadataUri = DemoOAuthDefaults.BuildMetadataUri(baseUri),
            Issuer = DemoOAuthDefaults.ComputeIssuer(baseUri),
            SigningKey = signingKey,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
        };
    }

    internal static void ConfigureAuthentication(McpServerBuilder builder, DemoOAuthConfiguration config)
    {
        builder.WithAuthentication(auth =>
        {
            auth.WithOAuth(o =>
            {
                o.Resource = config.ResourceUri.ToString();
                o.ResourceMetadataPath = DemoOAuthDefaults.ProtectedResourceMetadataPath;
                o.Authority = null;
                o.AllowInsecureMetadataEndpoints = true;
                o.AddAuthorizationServer(config.AuthorizationServerMetadataUri.ToString());
                o.AddValidAudience(config.ResourceUri.ToString());
                o.AddValidIssuer(config.Issuer);
                o.AddSigningKey(config.SigningKey);
                o.ValidateIssuer = false;
            });
        });
    }

    internal static void MapEndpoints(WebApplication app, DemoOAuthConfiguration config)
    {
        var logger = app.Logger;

        app.MapGet(DemoOAuthDefaults.ProtectedResourceMetadataPath, () =>
        {
            return Results.Json(new
            {
                resource = config.ResourceUri.ToString(),
                authorization_servers = new[] { config.AuthorizationServerMetadataUri.ToString() },
            });
        });

        app.MapGet(DemoOAuthDefaults.AuthorizationServerMetadataPath, () =>
        {
            return Results.Json(new
            {
                issuer = config.Issuer,
                authorization_endpoint = new Uri(config.BaseUri, DemoOAuthDefaults.AuthorizationEndpointPath).ToString(),
                token_endpoint = new Uri(config.BaseUri, DemoOAuthDefaults.TokenEndpointPath).ToString(),
                jwks_uri = new Uri(config.BaseUri, DemoOAuthDefaults.JwksPath).ToString(),
                device_authorization_endpoint = new Uri(config.BaseUri, DemoOAuthDefaults.DeviceEndpointPath).ToString(),
            });
        });

        app.MapGet(DemoOAuthDefaults.JwksPath, () =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromSymmetricSecurityKey(config.SigningKey);
            return Results.Json(new { keys = new[] { jwk } });
        });

        app.MapPost(DemoOAuthDefaults.TokenEndpointPath, async context =>
        {
            var form = await context.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();

            if (string.Equals(grantType, "client_credentials", StringComparison.Ordinal))
            {
                var clientId = form["client_id"].ToString();
                var clientSecret = form["client_secret"].ToString();

                if (
                    !string.Equals(clientId, DemoOAuthDefaults.ClientId, StringComparison.Ordinal)
                    || !string.Equals(
                        clientSecret,
                        DemoOAuthDefaults.ClientSecret,
                        StringComparison.Ordinal
                    )
                )
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = "invalid_client" });
                    return;
                }

                var resource =
                    form.TryGetValue("resource", out var resourceValues)
                    && !string.IsNullOrWhiteSpace(resourceValues.ToString())
                        ? resourceValues.ToString()
                        : config.ResourceUri.ToString();

                var token = IssueToken(config, subject: clientId, resource, issueRefreshToken: false);
                logger.LogInformation("Issued demo access token for {ClientId}", clientId);
                await context.Response.WriteAsJsonAsync(token.ToResponse());
                return;
            }

            if (string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
            {
                if (!await ProcessAuthorizationCodeGrantAsync(context, form, config, logger))
                {
                    return;
                }

                return;
            }
            else if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
            {
                if (!await ProcessRefreshTokenGrantAsync(context, form, config, logger))
                {
                    return;
                }

                return;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "unsupported_grant_type" });
            }
        });

        app.MapPost(DemoOAuthDefaults.DeviceEndpointPath, async context =>
        {
            await context.Response.WriteAsJsonAsync(new
            {
                error = "authorization_pending",
                error_description = "Device code flow not implemented in demo.",
            });
        });

        app.MapGet(DemoOAuthDefaults.AuthorizationEndpointPath, async context =>
        {
            var query = context.Request.Query;
            if (!string.Equals(query["response_type"], "code", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "unsupported_response_type" });
                return;
            }

            var clientId = query["client_id"].ToString();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_request", error_description = "client_id is required." });
                return;
            }

            var redirectUriValue = query["redirect_uri"].ToString();
            if (string.IsNullOrWhiteSpace(redirectUriValue) || !Uri.TryCreate(redirectUriValue, UriKind.Absolute, out var redirectUri))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_request", error_description = "redirect_uri must be an absolute URI." });
                return;
            }

            var codeChallenge = query["code_challenge"].ToString();
            var codeChallengeMethod = query["code_challenge_method"].ToString();
            if (string.IsNullOrWhiteSpace(codeChallenge) || !string.Equals(codeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_request", error_description = "PKCE S256 code challenge is required." });
                return;
            }

            var resource = query.TryGetValue("resource", out var resourceValues) && !string.IsNullOrWhiteSpace(resourceValues.ToString())
                ? resourceValues.ToString()
                : config.ResourceUri.ToString();

            var scope = query.TryGetValue("scope", out var scopeValues) ? scopeValues.ToString() : null;
            var state = query.TryGetValue("state", out var stateValues) ? stateValues.ToString() : null;

            var code = GenerateTokenValue();
            var record = new AuthorizationCodeRecord(
                clientId,
                redirectUri,
                resource,
                codeChallenge,
                codeChallengeMethod,
                scope,
                DateTimeOffset.UtcNow.AddMinutes(5),
                clientId
            );
            s_authorizationCodes[code] = record;

            var parameters = new Dictionary<string, string?> { ["code"] = code };
            if (!string.IsNullOrEmpty(state))
            {
                parameters["state"] = state!;
            }

            var redirectTarget = QueryHelpers.AddQueryString(redirectUri.ToString(), parameters);
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = redirectTarget;
        });
    }

    private static async Task<bool> ProcessAuthorizationCodeGrantAsync(
        HttpContext context,
        IFormCollection form,
        DemoOAuthConfiguration config,
        ILogger logger
    )
    {
        var clientId = form["client_id"].ToString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await WriteOAuthError(context, "invalid_client", "client_id is required.");
            return false;
        }

        var code = form["code"].ToString();
        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteOAuthError(context, "invalid_request", "authorization code is required.");
            return false;
        }

        if (!s_authorizationCodes.TryRemove(code, out var record))
        {
            await WriteOAuthError(context, "invalid_grant", "authorization code is invalid or has already been used.");
            return false;
        }

        if (record.ExpiresAt < DateTimeOffset.UtcNow)
        {
            await WriteOAuthError(context, "invalid_grant", "authorization code has expired.");
            return false;
        }

        if (!string.Equals(record.ClientId, clientId, StringComparison.Ordinal))
        {
            await WriteOAuthError(context, "invalid_client", "client mismatch.");
            return false;
        }

        var redirectUriValue = form["redirect_uri"].ToString();
        if (
            string.IsNullOrWhiteSpace(redirectUriValue)
            || !Uri.TryCreate(redirectUriValue, UriKind.Absolute, out var redirectUri)
        )
        {
            await WriteOAuthError(context, "invalid_request", "redirect_uri must be supplied.");
            return false;
        }

        if (!RedirectUrisMatch(redirectUri, record.RedirectUri))
        {
            await WriteOAuthError(context, "invalid_grant", "redirect_uri does not match the authorization request.");
            return false;
        }

        var codeVerifier = form["code_verifier"].ToString();
        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            await WriteOAuthError(context, "invalid_request", "code_verifier is required.");
            return false;
        }

        var expectedChallenge = ComputeCodeChallenge(codeVerifier);
        if (!string.Equals(expectedChallenge, record.CodeChallenge, StringComparison.Ordinal))
        {
            await WriteOAuthError(context, "invalid_grant", "PKCE verification failed.");
            return false;
        }

        var resourceParam = form["resource"].ToString();
        var resource = string.IsNullOrWhiteSpace(resourceParam) ? record.Resource : resourceParam;

        if (
            !string.IsNullOrWhiteSpace(resourceParam)
            && !string.Equals(resourceParam, record.Resource, StringComparison.Ordinal)
        )
        {
            await WriteOAuthError(context, "invalid_grant", "Requested resource does not match the authorization request.");
            return false;
        }

        var token = IssueToken(config, record.Subject, resource, issueRefreshToken: true);
        logger.LogInformation("Issued authorization-code token for {ClientId}", record.ClientId);
        await context.Response.WriteAsJsonAsync(token.ToResponse());
        return true;
    }

    private static async Task<bool> ProcessRefreshTokenGrantAsync(
        HttpContext context,
        IFormCollection form,
        DemoOAuthConfiguration config,
        ILogger logger
    )
    {
        var clientId = form["client_id"].ToString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await WriteOAuthError(context, "invalid_client", "client_id is required.");
            return false;
        }

        var refreshTokenValue = form["refresh_token"].ToString();
        if (string.IsNullOrWhiteSpace(refreshTokenValue))
        {
            await WriteOAuthError(context, "invalid_request", "refresh_token parameter is required.");
            return false;
        }

        if (!s_refreshTokens.TryRemove(refreshTokenValue, out var refreshRecord))
        {
            await WriteOAuthError(context, "invalid_grant", "refresh token is invalid or expired.");
            return false;
        }

        if (refreshRecord.ExpiresAt < DateTimeOffset.UtcNow)
        {
            await WriteOAuthError(context, "invalid_grant", "refresh token has expired.");
            return false;
        }

        if (!string.Equals(refreshRecord.ClientId, clientId, StringComparison.Ordinal))
        {
            await WriteOAuthError(context, "invalid_client", "refresh token client mismatch.");
            return false;
        }

        var resourceParam = form["resource"].ToString();
        var resource = string.IsNullOrWhiteSpace(resourceParam)
            ? refreshRecord.Resource
            : resourceParam;

        if (!string.Equals(resource, refreshRecord.Resource, StringComparison.Ordinal))
        {
            await WriteOAuthError(context, "invalid_grant", "refresh token resource does not match.");
            return false;
        }

        var token = IssueToken(config, refreshRecord.Subject, resource, issueRefreshToken: true);
        logger.LogInformation("Refreshed access token for {ClientId}", clientId);
        await context.Response.WriteAsJsonAsync(token.ToResponse());
        return true;
    }

    private static TokenEnvelope IssueToken(
        DemoOAuthConfiguration config,
        string subject,
        string resource,
        bool issueRefreshToken
    )
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(30);

        var jwt = new JwtSecurityToken(
            issuer: config.Issuer,
            audience: resource,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim("client_id", subject),
            },
            notBefore: now,
            expires: expires,
            signingCredentials: config.SigningCredentials
        );

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(jwt);

        string? refreshToken = null;
        if (issueRefreshToken)
        {
            refreshToken = GenerateTokenValue();
            s_refreshTokens[refreshToken] = new RefreshTokenRecord(
                subject,
                subject,
                resource,
                DateTimeOffset.UtcNow.AddHours(12)
            );
        }

        return new TokenEnvelope(accessToken, refreshToken, (int)(expires - now).TotalSeconds);
    }

    private static async Task WriteOAuthError(
        HttpContext context,
        string error,
        string description
    )
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error, error_description = description });
    }

    private static bool RedirectUrisMatch(Uri left, Uri right)
    {
        return Uri.Compare(
            left,
            right,
            UriComponents.AbsoluteUri,
            UriFormat.Unescaped,
            StringComparison.Ordinal
        ) == 0;
    }

    private static string ComputeCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string GenerateTokenValue()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

internal sealed class DemoOAuthConfiguration
{
    public required Uri BaseUri { get; init; }
    public required Uri ResourceUri { get; init; }
    public required Uri ResourceMetadataUri { get; init; }
    public required Uri AuthorizationServerMetadataUri { get; init; }
    public required string Issuer { get; init; }
    public required SymmetricSecurityKey SigningKey { get; init; }
    public required SigningCredentials SigningCredentials { get; init; }
}

internal sealed record AuthorizationCodeRecord(
    string ClientId,
    Uri RedirectUri,
    string Resource,
    string CodeChallenge,
    string CodeChallengeMethod,
    string? Scope,
    DateTimeOffset ExpiresAt,
    string Subject = ""
);

internal sealed record RefreshTokenRecord(
    string ClientId,
    string Subject,
    string Resource,
    DateTimeOffset ExpiresAt
);

internal sealed record TokenEnvelope(string AccessToken, string? RefreshToken, int ExpiresIn)
{
    public object ToResponse() =>
        RefreshToken == null
            ? new
            {
                access_token = AccessToken,
                token_type = "Bearer",
                expires_in = ExpiresIn,
            }
            : new
            {
                access_token = AccessToken,
                token_type = "Bearer",
                expires_in = ExpiresIn,
                refresh_token = RefreshToken,
            };
}
