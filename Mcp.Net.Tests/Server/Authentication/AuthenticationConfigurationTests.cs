using System.Text;
using FluentAssertions;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Mcp.Net.Tests.Server.Authentication;

public class AuthenticationConfigurationTests
{
    [Fact]
    public void ApplyTo_WithCompleteOAuthConfiguration_ConfiguresAuthBuilderWithOAuthHandler()
    {
        // Arrange
        var signingKeyBytes = Encoding.UTF8.GetBytes("demo-signing-key-12345678901234567890");
        var configuration = new AuthenticationConfiguration
        {
            Enabled = true,
            EnableLogging = true,
            SchemeName = "Bearer",
            SecuredPaths = new List<string> { "/mcp" },
            OAuth = new OAuthAuthenticationConfiguration
            {
                Resource = "https://example.com/mcp",
                ResourceMetadataPath = "https://example.com/.well-known/oauth-protected-resource",
                Authority = "https://issuer.example",
                AllowInsecureMetadataEndpoints = true,
                AllowQueryStringTokens = true,
                ValidateAudience = true,
                ValidateIssuer = true,
                EnforceResourceIndicator = true,
                TokenClockSkewMinutes = 2,
                AuthorizationServers = new List<string>
                {
                    "https://issuer.example/.well-known/oauth-authorization-server",
                },
                ValidAudiences = new List<string> { "https://example.com/mcp" },
                ValidIssuers = new List<string> { "https://issuer.example" },
                SigningKeys = new List<string> { Convert.ToBase64String(signingKeyBytes) },
            },
        };

        var builder = new AuthBuilder(NullLoggerFactory.Instance);

        // Act
        configuration.ApplyTo(builder, NullLogger<AuthenticationConfiguration>.Instance);
        var handler = builder.Build();

        // Assert
        handler.Should().BeOfType<OAuthAuthenticationHandler>();

        var options = builder.ConfiguredOptions.Should().BeOfType<OAuthResourceServerOptions>().Subject;
        options.Resource.Should().Be("https://example.com/mcp");
        options.ResourceMetadataPath.Should().Be("https://example.com/.well-known/oauth-protected-resource");
        options.Authority.Should().Be("https://issuer.example");
        options.AllowInsecureMetadataEndpoints.Should().BeTrue();
        options.AllowQueryStringTokens.Should().BeTrue();
        options.ValidateAudience.Should().BeTrue();
        options.ValidateIssuer.Should().BeTrue();
        options.EnforceResourceIndicator.Should().BeTrue();
        options.TokenClockSkew.Should().Be(TimeSpan.FromMinutes(2));
        options.AuthorizationServers.Should().ContainSingle()
            .Which.Should().Be("https://issuer.example/.well-known/oauth-authorization-server");
        options.ValidAudiences.Should().Contain("https://example.com/mcp");
        options.ValidIssuers.Should().Contain("https://issuer.example");
        options.SigningKeys.Should().ContainSingle()
            .Which.Should().BeOfType<SymmetricSecurityKey>();
        options.SecuredPaths.Should().Contain("/mcp");
        options.Enabled.Should().BeTrue();
        options.EnableLogging.Should().BeTrue();
        options.SchemeName.Should().Be("Bearer");
    }

    [Fact]
    public void ApplyTo_WithDisableFlag_DisablesAuthenticationAndSkipsHandler()
    {
        // Arrange
        var configuration = new AuthenticationConfiguration
        {
            Disable = true,
        };
        var builder = new AuthBuilder(NullLoggerFactory.Instance);

        // Act
        configuration.ApplyTo(builder, NullLogger<AuthenticationConfiguration>.Instance);
        var handler = builder.Build();

        // Assert
        handler.Should().BeNull();
        builder.IsAuthDisabled.Should().BeTrue();
        builder.ConfiguredOptions.Enabled.Should().BeFalse();
    }
}
