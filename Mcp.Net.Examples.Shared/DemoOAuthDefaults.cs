using System;
using Microsoft.IdentityModel.Tokens;
using Mcp.Net.Client.Authentication;

namespace Mcp.Net.Examples.Shared;

public static class DemoOAuthDefaults
{
    public const string ClientId = "demo-client";
    public const string ClientSecret = "demo-client-secret";
    public const string SigningKeyBase64 = "z3r8m2+7pDWUpAJZciV06P88GJEqn3Ejj6+UeJ8V+YA=";

    public const string AuthorizationEndpointPath = "/oauth/authorize";
    public const string TokenEndpointPath = "/oauth/token";
    public const string DeviceEndpointPath = "/oauth/device";
    public const string JwksPath = "/oauth/jwks";
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
    public const string ResourcePath = "/mcp";
    public static readonly Uri DefaultRedirectUri = new("https://example-app.local/oauth/callback");

    public static readonly string[] Scopes = new[] { "demo.read" };

    public static Uri BuildBaseUri(string hostname, int port)
    {
        return port switch
        {
            80 => new Uri($"http://{hostname}"),
            443 => new Uri($"https://{hostname}"),
            _ => new Uri($"http://{hostname}:{port}"),
        };
    }

    public static Uri BuildResourceUri(Uri baseUri) => new(baseUri, ResourcePath);

    public static Uri BuildMetadataUri(Uri baseUri) => new(baseUri, AuthorizationServerMetadataPath);

    public static Uri BuildResourceMetadataUri(Uri baseUri) => new(baseUri, ProtectedResourceMetadataPath);

    public static SymmetricSecurityKey CreateSigningKey() =>
        new(Convert.FromBase64String(SigningKeyBase64));

    public static OAuthClientOptions CreateClientOptions(Uri baseUri)
    {
        return new OAuthClientOptions
        {
            Resource = BuildResourceUri(baseUri),
            ClientId = ClientId,
            ClientSecret = ClientSecret,
            Scopes = Scopes,
            AuthorizationServerMetadataAddress = BuildMetadataUri(baseUri),
        };
    }

    public static string ComputeIssuer(Uri baseUri) => new Uri(baseUri, "/oauth").ToString().TrimEnd('/');
}
