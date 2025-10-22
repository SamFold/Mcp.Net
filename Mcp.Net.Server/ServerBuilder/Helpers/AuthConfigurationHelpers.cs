using Mcp.Net.Server.Authentication;

namespace Mcp.Net.Server.ServerBuilder.Helpers;

/// <summary>
/// Helper methods for working with authentication configuration.
/// </summary>
internal static class AuthConfigurationHelpers
{
    /// <summary>
    /// Extract AuthOptions from the builder if available
    /// </summary>
    /// <param name="builder">The MCP server builder</param>
    /// <returns>The auth options, or null if not available</returns>
    public static AuthOptions? GetAuthOptionsFromBuilder(McpServerBuilder builder)
    {
        return builder.ConfiguredAuthOptions
            ?? new AuthOptions
            {
                Enabled = true,
                SecuredPaths = new List<string> { "/mcp" },
                EnableLogging = true,
            };
    }
}
