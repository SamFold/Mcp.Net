using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Mcp.Net.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Mcp.Net.Tests.Server.Authentication;

public class OAuthAuthenticationHandlerTests
{
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("test-signing-key-12345678901234567890")
    );

    [Fact]
    public async Task AuthenticateAsync_ReturnsSuccess_ForValidBearerToken()
    {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] =
            $"Bearer {CreateToken("https://localhost:5000/mcp")}";

        var result = await handler.AuthenticateAsync(context);

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be("user-123");
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WhenTokenMissing()
    {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();

        var result = await handler.AuthenticateAsync(context);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        result.ErrorCode.Should().Be("invalid_token");
        result.ErrorDescription.Should().Contain("Request did not include bearer token");
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WhenAudienceMismatch()
    {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] =
            $"Bearer {CreateToken("https://localhost:5000/other")}";

        var result = await handler.AuthenticateAsync(context);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        result.ErrorCode.Should().Be("invalid_token");
        result.ErrorDescription.Should().Contain("invalid");
    }

    [Fact]
    public async Task AuthenticateAsync_AllowsQueryToken_WhenEnabled()
    {
        var handler = CreateHandler(options => options.AllowQueryStringTokens = true);
        var token = CreateToken("https://localhost:5000/mcp", subject: "query-user");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?access_token={token}");

        var result = await handler.AuthenticateAsync(context);

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be("query-user");
    }

    private static OAuthAuthenticationHandler CreateHandler(
        Action<OAuthResourceServerOptions>? configure = null
    )
    {
        var options = new OAuthResourceServerOptions
        {
            Resource = "https://localhost:5000/mcp",
            ValidateIssuer = false,
        };
        options.AddSigningKey(SigningKey);
        configure?.Invoke(options);
        return new OAuthAuthenticationHandler(
            options,
            NullLogger<OAuthAuthenticationHandler>.Instance
        );
    }

    private static string CreateToken(
        string audience,
        string subject = "user-123",
        string issuer = "https://issuer.example"
    )
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Audience = audience,
            Issuer = issuer,
            Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, subject) }),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return handler.CreateEncodedJwt(descriptor);
    }
}
