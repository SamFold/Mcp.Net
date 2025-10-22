using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.Extensions.DependencyInjection;

namespace Mcp.Net.Server.Extensions;

/// <summary>
/// Minimal authentication registration helpers for MCP servers.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Registers authentication services based on the server builder configuration.
    /// </summary>
    public static IServiceCollection AddMcpAuthentication(
        this IServiceCollection services,
        McpServerBuilder builder
    )
    {
        if (builder.AuthHandler != null)
        {
            services.AddSingleton<IAuthHandler>(builder.AuthHandler);
        }
        else
        {
            services.AddSingleton<IAuthHandler>(
                new NoAuthenticationHandler(new AuthOptions { Enabled = false })
            );
        }

        if (builder.ConfiguredAuthOptions != null)
        {
            services.AddSingleton(typeof(AuthOptions), builder.ConfiguredAuthOptions);

            if (builder.ConfiguredAuthOptions is OAuthResourceServerOptions oauthOptions)
            {
                services.AddSingleton(oauthOptions);
            }
        }

        return services;
    }
}
