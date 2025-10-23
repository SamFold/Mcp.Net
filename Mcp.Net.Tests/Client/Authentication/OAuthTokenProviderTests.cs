using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Authentication;
using Xunit;

namespace Mcp.Net.Tests.Client.Authentication;

public class OAuthTokenProviderTests
{
    [Fact]
    public async Task DeviceCodeProvider_ShouldReturnToken()
    {
        var handler = new QueueMessageHandler();
        handler.Enqueue(
            JsonContent(
                new
                {
                    token_endpoint = "https://auth.example.com/token",
                    device_authorization_endpoint = "https://auth.example.com/device",
                }
            )
        );
        handler.Enqueue(
            JsonContent(
                new
                {
                    device_code = "device",
                    user_code = "user",
                    verification_uri = "https://auth.example.com/verify",
                    expires_in = 600,
                    interval = 0,
                }
            )
        );
        handler.Enqueue(
            JsonContent(
                new
                {
                    access_token = "token",
                    token_type = "Bearer",
                    expires_in = 3600,
                }
            )
        );

        using var httpClient = new HttpClient(handler);
        var options = new OAuthClientOptions
        {
            Resource = new Uri("https://mcp.example.com"),
            ClientId = "client",
            AuthorizationServerMetadataAddress = new Uri(
                "https://auth.example.com/.well-known/oauth-authorization-server"
            ),
        };

        var provider = new DeviceCodeOAuthTokenProvider(options, httpClient);
        var context = new OAuthTokenRequestContext(
            new Uri("https://mcp.example.com"),
            new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer")
        );

        var token = await provider.AcquireTokenAsync(context, CancellationToken.None);
        token.Should().NotBeNull();
        token!.AccessToken.Should().Be("token");
    }

    [Fact]
    public async Task DeviceCodeProvider_ShouldThrowWhenExpired()
    {
        var handler = new QueueMessageHandler();
        handler.Enqueue(
            JsonContent(
                new
                {
                    token_endpoint = "https://auth.example.com/token",
                    device_authorization_endpoint = "https://auth.example.com/device",
                }
            )
        );
        handler.Enqueue(
            JsonContent(
                new
                {
                    device_code = "device",
                    user_code = "user",
                    verification_uri = "https://auth.example.com/verify",
                    expires_in = 600,
                    interval = 0,
                }
            )
        );
        handler.Enqueue(JsonContent(new { error = "expired_token" }, HttpStatusCode.BadRequest));

        using var httpClient = new HttpClient(handler);
        var options = new OAuthClientOptions
        {
            Resource = new Uri("https://mcp.example.com"),
            ClientId = "client",
            AuthorizationServerMetadataAddress = new Uri(
                "https://auth.example.com/.well-known/oauth-authorization-server"
            ),
        };

        var provider = new DeviceCodeOAuthTokenProvider(options, httpClient);
        var context = new OAuthTokenRequestContext(
            new Uri("https://mcp.example.com"),
            new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer")
        );

        await FluentActions
            .Invoking(() => provider.AcquireTokenAsync(context, CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ClientCredentialsProvider_ShouldReturnToken()
    {
        var handler = new QueueMessageHandler();
        handler.Enqueue(JsonContent(new { token_endpoint = "https://auth.example.com/token" }));
        handler.Enqueue(JsonContent(new { access_token = "client-token", token_type = "Bearer" }));

        using var httpClient = new HttpClient(handler);
        var options = new OAuthClientOptions
        {
            Resource = new Uri("https://mcp.example.com"),
            ClientId = "client",
            ClientSecret = "secret",
            AuthorizationServerMetadataAddress = new Uri(
                "https://auth.example.com/.well-known/oauth-authorization-server"
            ),
        };

        var provider = new ClientCredentialsOAuthTokenProvider(options, httpClient);
        var context = new OAuthTokenRequestContext(
            new Uri("https://mcp.example.com"),
            new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer")
        );

        var token = await provider.AcquireTokenAsync(context, CancellationToken.None);
        token.Should().NotBeNull();
        token!.AccessToken.Should().Be("client-token");
    }

    [Fact]
    public async Task AuthorizationCodeProvider_ShouldReturnToken()
    {
        var handler = new QueueMessageHandler();
        handler.Enqueue(
            JsonContent(
                new
                {
                    token_endpoint = "https://auth.example.com/token",
                    authorization_endpoint = "https://auth.example.com/authorize",
                }
            )
        );
        handler.Enqueue(
            JsonContent(
                new
                {
                    access_token = "auth-token",
                    token_type = "Bearer",
                    refresh_token = "refresh",
                }
            )
        );

        using var httpClient = new HttpClient(handler);
        var options = new OAuthClientOptions
        {
            Resource = new Uri("https://mcp.example.com"),
            ClientId = "client",
            RedirectUri = new Uri("https://app.example.com/callback"),
            AuthorizationServerMetadataAddress = new Uri(
                "https://auth.example.com/.well-known/oauth-authorization-server"
            ),
        };

        var provider = new AuthorizationCodePkceOAuthTokenProvider(
            options,
            httpClient,
            (request, _) => Task.FromResult(new AuthorizationCodeResult("code", request.State))
        );

        var context = new OAuthTokenRequestContext(
            new Uri("https://mcp.example.com"),
            new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer")
        );

        var token = await provider.AcquireTokenAsync(context, CancellationToken.None);
        token.Should().NotBeNull();
        token!.AccessToken.Should().Be("auth-token");
        token.RefreshToken.Should().Be("refresh");
    }

    [Fact]
    public async Task AuthorizationCodeProvider_ShouldThrowOnStateMismatch()
    {
        var handler = new QueueMessageHandler();
        handler.Enqueue(
            JsonContent(
                new
                {
                    token_endpoint = "https://auth.example.com/token",
                    authorization_endpoint = "https://auth.example.com/authorize",
                }
            )
        );

        using var httpClient = new HttpClient(handler);
        var options = new OAuthClientOptions
        {
            Resource = new Uri("https://mcp.example.com"),
            ClientId = "client",
            RedirectUri = new Uri("https://app.example.com/callback"),
            AuthorizationServerMetadataAddress = new Uri(
                "https://auth.example.com/.well-known/oauth-authorization-server"
            ),
        };

        var provider = new AuthorizationCodePkceOAuthTokenProvider(
            options,
            httpClient,
            (request, _) => Task.FromResult(new AuthorizationCodeResult("code", "other"))
        );

        var context = new OAuthTokenRequestContext(
            new Uri("https://mcp.example.com"),
            new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer")
        );

        await FluentActions
            .Invoking(() => provider.AcquireTokenAsync(context, CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    private static HttpResponseMessage JsonContent(
        object payload,
        HttpStatusCode statusCode = HttpStatusCode.OK
    )
    {
        var message = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json"
            ),
        };
        return message;
    }

    private sealed class QueueMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpResponseMessage> _responses = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException(
                    "No response configured for request: " + request.RequestUri
                );
            }

            return Task.FromResult(response);
        }
    }
}
