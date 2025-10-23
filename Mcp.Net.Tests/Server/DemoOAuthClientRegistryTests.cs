using System;
using System.Collections.Generic;
using FluentAssertions;
using Mcp.Net.Examples.SimpleServer;

namespace Mcp.Net.Tests.Server;

public class DemoOAuthClientRegistryTests
{
    [Fact]
    public void RegisterPublicClient_WithValidRequest_RegistersClient()
    {
        var registry = new DemoOAuthClientRegistry(() => "dynamic-client");
        var request = new DynamicClientRegistrationRequest
        {
            RedirectUris = new[] { "https://example-app.local/oauth/callback" },
            ClientName = "integration-test-client",
        };

        var record = registry.RegisterPublicClient(request);

        record.ClientId.Should().Be("dynamic-client");
        record.RequirePkce.Should().BeTrue();
        record.TokenEndpointAuthMethod.Should().Be("none");
        record.RedirectUris.Should().ContainSingle(uri => uri.ToString() == "https://example-app.local/oauth/callback");
        registry.TryGetClient("dynamic-client", out _).Should().BeTrue();
        registry.IsRedirectUriAllowed("dynamic-client", new Uri("https://example-app.local/oauth/callback"))
            .Should().BeTrue();
    }

    [Fact]
    public void RegisterPublicClient_WithInvalidRedirect_Throws()
    {
        var registry = new DemoOAuthClientRegistry(() => "dynamic-client");
        var request = new DynamicClientRegistrationRequest
        {
            RedirectUris = new[] { "http://example.com/redirect" }, // Non-loopback http should be rejected
        };

        Action act = () => registry.RegisterPublicClient(request);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*redirect_uris must use HTTPS or loopback HTTP scheme.*");
    }

    [Fact]
    public void TryValidateSecret_HonoursConfiguredSecret()
    {
        var registry = new DemoOAuthClientRegistry(() => "ignored");
        var record = new RegisteredClientRecord(
            ClientId: "confidential-client",
            ClientSecret: "top-secret",
            TokenEndpointAuthMethod: "client_secret_post",
            RedirectUris: new[] { new Uri("https://example-app.local/oauth/callback") },
            GrantTypes: new[] { "client_credentials" },
            ResponseTypes: Array.Empty<string>(),
            RequirePkce: false,
            ClientName: "Confidential Client",
            IssuedAt: DateTimeOffset.UtcNow,
            ClientSecretExpiresAt: null
        );

        registry.EnsureClient(record);

        registry.TryValidateSecret("confidential-client", "top-secret", out _).Should().BeTrue();
        registry.TryValidateSecret("confidential-client", "wrong-secret", out _).Should().BeFalse();
        registry.TryValidateSecret("unknown-client", null, out _).Should().BeFalse();
    }
}
