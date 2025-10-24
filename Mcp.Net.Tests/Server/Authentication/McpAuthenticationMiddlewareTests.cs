using System.IO;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Tests.Server.Authentication;

public class McpAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenAuthenticationFails_WritesOAuthErrorResponseAndHeaders()
    {
        // Arrange
        var options = new OAuthResourceServerOptions
        {
            Resource = "https://example.com/mcp",
            ResourceMetadataPath = "https://example.com/.well-known/oauth-protected-resource",
        };

        var failure = AuthResult.Fail(
            "Bearer token expired",
            StatusCodes.Status401Unauthorized,
            "invalid_token",
            "Bearer token expired",
            "https://errors.example/expired"
        );

        var handler = new StubAuthHandler("Bearer", _ => Task.FromResult(failure));
        var context = CreateHttpContext();
        var nextCalled = false;

        var middleware = new McpAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<McpAuthenticationMiddleware>.Instance,
            handler,
            options
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse("pipeline should short-circuit on failed authentication");
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers["WWW-Authenticate"].ToString().Should().Be(
            "Bearer resource=\"https://example.com/mcp\", resource_metadata=\"https://example.com/.well-known/oauth-protected-resource\", error=\"invalid_token\", error_description=\"Bearer token expired\", error_uri=\"https://errors.example/expired\""
        );

        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        var root = document.RootElement;
        root.GetProperty("error").GetString().Should().Be("invalid_token");
        root.GetProperty("error_description").GetString().Should().Contain("expired");
        root.GetProperty("error_uri").GetString().Should().Be("https://errors.example/expired");
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WhenAuthenticationFailsWithForbiddenStatus_UsesProvidedStatus()
    {
        // Arrange
        var options = new OAuthResourceServerOptions
        {
            Resource = "https://example.com/mcp",
            ResourceMetadataPath = "https://example.com/.well-known/oauth-protected-resource",
        };

        var failure = AuthResult.Fail(
            "Scope insufficient",
            StatusCodes.Status403Forbidden,
            "insufficient_scope",
            "Scope insufficient",
            "https://errors.example/insufficient-scope"
        );

        var handler = new StubAuthHandler("Bearer", _ => Task.FromResult(failure));
        var context = CreateHttpContext();
        var nextCalled = false;

        var middleware = new McpAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<McpAuthenticationMiddleware>.Instance,
            handler,
            options
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Headers["WWW-Authenticate"].ToString().Should().Contain("insufficient_scope");

        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        var root = document.RootElement;
        root.GetProperty("error").GetString().Should().Be("insufficient_scope");
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class StubAuthHandler : IAuthHandler
    {
        private readonly Func<HttpContext, Task<AuthResult>> _callback;

        public StubAuthHandler(string schemeName, Func<HttpContext, Task<AuthResult>> callback)
        {
            SchemeName = schemeName;
            _callback = callback;
        }

        public string SchemeName { get; }

        public Task<AuthResult> AuthenticateAsync(HttpContext context) => _callback(context);
    }
}
