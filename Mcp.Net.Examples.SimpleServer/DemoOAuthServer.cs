using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Examples.Shared;

namespace Mcp.Net.Examples.SimpleServer;

internal static class DemoOAuthServer
{
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

            if (grantType != "client_credentials")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "unsupported_grant_type" });
                return;
            }

            var clientId = form["client_id"].ToString();
            var clientSecret = form["client_secret"].ToString();

            if (!string.Equals(clientId, DemoOAuthDefaults.ClientId, StringComparison.Ordinal) ||
                !string.Equals(clientSecret, DemoOAuthDefaults.ClientSecret, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_client" });
                return;
            }

            var token = IssueToken(config, subject: clientId);
            logger.LogInformation("Issued demo access token for {ClientId}", clientId);
            await context.Response.WriteAsJsonAsync(token);
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
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await context.Response.WriteAsync("Authorization code flow not implemented in demo authorization server.");
        });
    }

    private static object IssueToken(DemoOAuthConfiguration config, string subject)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(30);

        var jwt = new JwtSecurityToken(
            issuer: config.Issuer,
            audience: config.ResourceUri.ToString(),
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
        var token = handler.WriteToken(jwt);

        return new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = (int)(expires - now).TotalSeconds,
        };
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
