using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Examples.Shared;
using Mcp.Net.Examples.SimpleServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Linq;

namespace Mcp.Net.Tests.Server;

public class DemoOAuthServerTests
{
    [Fact]
    public async Task DynamicRegistration_ShouldRejectInsecureRedirect()
    {
        await using var host = await DemoOAuthTestHost.StartAsync();
        var httpClient = host.Client;

        var payload = JsonSerializer.Serialize(
            new
            {
                redirect_uris = new[] { "http://example.com/callback" },
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        var response = await httpClient.PostAsync(
            DemoOAuthDefaults.ClientRegistrationEndpointPath,
            new StringContent(payload, Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(response);
        error.Should().Be("invalid_request");
    }

    [Fact]
    public async Task AuthorizationCodeToken_ShouldFailWhenPkceMismatch()
    {
        await using var host = await DemoOAuthTestHost.StartAsync();

        var registration = await RegisterAsync(host.Client, new Uri("https://example-app.local/oauth/callback"));
        var (code, codeVerifier) = await AuthorizeAsync(
            host.Client,
            registration.ClientId,
            registration.RedirectUri,
            host.Configuration.ResourceUri
        );

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = registration.RedirectUri.ToString(),
            ["client_id"] = registration.ClientId,
            ["code_verifier"] = codeVerifier + "-mismatch",
            ["resource"] = host.Configuration.ResourceUri.ToString(),
        };

        var response = await host.Client.PostAsync(
            DemoOAuthDefaults.TokenEndpointPath,
            new FormUrlEncodedContent(form)
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(response);
        error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AuthorizationCodeToken_ShouldFailWhenResourceMismatch()
    {
        await using var host = await DemoOAuthTestHost.StartAsync();

        var registration = await RegisterAsync(host.Client, new Uri("https://example-app.local/oauth/callback"));
        var (code, codeVerifier) = await AuthorizeAsync(
            host.Client,
            registration.ClientId,
            registration.RedirectUri,
            host.Configuration.ResourceUri
        );

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = registration.RedirectUri.ToString(),
            ["client_id"] = registration.ClientId,
            ["code_verifier"] = codeVerifier,
            ["resource"] = "http://localhost/other",
        };

        var response = await host.Client.PostAsync(
            DemoOAuthDefaults.TokenEndpointPath,
            new FormUrlEncodedContent(form)
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(response);
        error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshToken_ShouldFailWhenResourceMismatch()
    {
        await using var host = await DemoOAuthTestHost.StartAsync();

        var registration = await RegisterAsync(host.Client, new Uri("https://example-app.local/oauth/callback"));
        var (code, codeVerifier) = await AuthorizeAsync(
            host.Client,
            registration.ClientId,
            registration.RedirectUri,
            host.Configuration.ResourceUri
        );

        var tokenDocument = await RedeemAuthorizationCodeAsync(
            host.Client,
            code,
            codeVerifier,
            registration.ClientId,
            registration.RedirectUri,
            host.Configuration.ResourceUri
        );

        tokenDocument.RootElement.TryGetProperty("refresh_token", out var refreshProperty).Should().BeTrue();
        var refreshToken = refreshProperty.GetString();
        refreshToken.Should().NotBeNullOrWhiteSpace();

        var refreshForm = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
            ["client_id"] = registration.ClientId,
            ["resource"] = "http://localhost/other",
        };

        var response = await host.Client.PostAsync(
            DemoOAuthDefaults.TokenEndpointPath,
            new FormUrlEncodedContent(refreshForm)
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(response);
        error.Should().Be("invalid_grant");
    }

    private static async Task<(string ClientId, Uri RedirectUri)> RegisterAsync(HttpClient client, Uri redirectUri)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                redirect_uris = new[] { redirectUri.ToString() },
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none",
                client_name = "Integration Test Client",
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        var response = await client.PostAsync(
            DemoOAuthDefaults.ClientRegistrationEndpointPath,
            new StringContent(payload, Encoding.UTF8, "application/json")
        );

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clientId = document.RootElement.GetProperty("client_id").GetString()!;
        var registeredRedirect = document.RootElement
            .GetProperty("redirect_uris")
            .EnumerateArray()
            .First()
            .GetString()!;

        return (clientId, new Uri(registeredRedirect));
    }

    private static async Task<(string Code, string CodeVerifier)> AuthorizeAsync(
        HttpClient client,
        string clientId,
        Uri redirectUri,
        Uri resource
    )
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var state = Guid.NewGuid().ToString("N");

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["resource"] = resource.ToString(),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["scope"] = string.Join(" ", DemoOAuthDefaults.Scopes),
        };

        var authorizeUri = QueryHelpers.AddQueryString(
            DemoOAuthDefaults.AuthorizationEndpointPath,
            query!
        );

        var response = await client.GetAsync(authorizeUri);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location;
        location.Should().NotBeNull();
        var parameters = QueryHelpers.ParseQuery(location!.Query);
        parameters["state"].ToString().Should().Be(state);
        var code = parameters["code"].ToString();
        code.Should().NotBeNullOrEmpty();
        return (code, codeVerifier);
    }

    private static async Task<JsonDocument> RedeemAuthorizationCodeAsync(
        HttpClient client,
        string code,
        string codeVerifier,
        string clientId,
        Uri redirectUri,
        Uri resource
    )
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri.ToString(),
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
            ["resource"] = resource.ToString(),
        };

        var response = await client.PostAsync(
            DemoOAuthDefaults.TokenEndpointPath,
            new FormUrlEncodedContent(form)
        );

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
    }

    private static string GenerateCodeVerifier()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string ComputeCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class DemoOAuthTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public DemoOAuthConfiguration Configuration { get; }

        private DemoOAuthTestHost(WebApplication app, HttpClient client, DemoOAuthConfiguration configuration)
        {
            _app = app;
            Client = client;
            Configuration = configuration;
        }

        public static async Task<DemoOAuthTestHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
            });

            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();

            var app = builder.Build();
            var configuration = DemoOAuthServer.CreateConfiguration(new Uri("http://localhost:5000"));
            DemoOAuthServer.MapEndpoints(app, configuration);

            await app.StartAsync();

            var client = app.GetTestServer().CreateClient();
            client.BaseAddress = new Uri("http://localhost");

            return new DemoOAuthTestHost(app, client, configuration);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
