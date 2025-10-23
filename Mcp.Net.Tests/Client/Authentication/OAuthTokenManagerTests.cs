using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Client.Authentication;
using Xunit;

namespace Mcp.Net.Tests.Client.Authentication;

public class OAuthTokenManagerTests
{
    [Fact]
    public async Task GetAccessToken_ShouldCacheValidToken()
    {
        var provider = new SequencedTokenProvider("token-1", "token-2");
        var manager = new OAuthTokenManager(provider);
        var resource = new Uri("https://mcp.example.com");
        var challenge = new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer");

        await manager.HandleUnauthorizedAsync(resource, challenge, CancellationToken.None);
        var token = await manager.GetAccessTokenAsync(resource, CancellationToken.None);
        token.Should().Be("token-1");

        var tokenAgain = await manager.GetAccessTokenAsync(resource, CancellationToken.None);
        tokenAgain.Should().Be("token-1");
    }

    [Fact]
    public async Task GetAccessToken_ShouldRefreshWhenExpired()
    {
        var provider = new SequencedTokenProvider("token-1", "token-2") { ExpiresImmediately = true };
        var manager = new OAuthTokenManager(provider);
        var resource = new Uri("https://mcp.example.com");
        var challenge = new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer");

        await manager.HandleUnauthorizedAsync(resource, challenge, CancellationToken.None);
        var token = await manager.GetAccessTokenAsync(resource, CancellationToken.None);
        token.Should().Be("token-2");
    }

    [Fact]
    public async Task HandleUnauthorized_ShouldReturnFalseWhenProviderFails()
    {
        var provider = new SequencedTokenProvider((string?)null);
        var manager = new OAuthTokenManager(provider);
        var resource = new Uri("https://mcp.example.com");
        var challenge = new OAuthChallenge("Bearer", new Dictionary<string, string>(), "Bearer");

        var result = await manager.HandleUnauthorizedAsync(resource, challenge, CancellationToken.None);
        result.Should().BeFalse();
    }

    private sealed class SequencedTokenProvider : IOAuthTokenProvider
    {
        private readonly Queue<string?> _tokens;
        public bool ExpiresImmediately { get; set; }

        public SequencedTokenProvider(params string?[] tokens)
        {
            _tokens = new Queue<string?>(tokens);
        }

        public Task<OAuthTokenResponse?> AcquireTokenAsync(
            OAuthTokenRequestContext context,
            CancellationToken cancellationToken
        )
        {
            if (_tokens.Count == 0)
            {
                return Task.FromResult<OAuthTokenResponse?>(null);
            }

            var token = _tokens.Dequeue();
            if (string.IsNullOrEmpty(token))
            {
                return Task.FromResult<OAuthTokenResponse?>(null);
            }

            var expires = ExpiresImmediately ? DateTimeOffset.UtcNow.AddSeconds(-1) : DateTimeOffset.UtcNow.AddMinutes(10);
            return Task.FromResult<OAuthTokenResponse?>(new OAuthTokenResponse(token, expires));
        }

        public Task<OAuthTokenResponse?> RefreshTokenAsync(
            OAuthTokenRequestContext context,
            OAuthTokenResponse currentToken,
            CancellationToken cancellationToken
        ) => AcquireTokenAsync(context, cancellationToken);
    }
}
