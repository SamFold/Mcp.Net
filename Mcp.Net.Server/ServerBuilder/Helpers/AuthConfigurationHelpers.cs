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
        // If the builder has an API Key handler, it should have options
        if (builder.AuthHandler is ApiKeyAuthenticationHandler apiKeyHandler)
        {
            // Create AuthOptions from ApiKeyAuthOptions
            var apiKeyOptions = apiKeyHandler.Options;
            return new AuthOptions
            {
                Enabled = apiKeyOptions.Enabled,
                SchemeName = apiKeyOptions.SchemeName,
                SecuredPaths = apiKeyOptions.SecuredPaths,
                EnableLogging = apiKeyOptions.EnableLogging,
            };
        }

        // If using SSE transport, check if it has API key options
        if (
            builder.TransportBuilder is SseServerBuilder sseBuilder
            && sseBuilder.Options?.ApiKeyOptions != null
        )
        {
            var apiKeyOptions = sseBuilder.Options.ApiKeyOptions;
            return new AuthOptions
            {
                Enabled = apiKeyOptions.Enabled,
                SchemeName = apiKeyOptions.SchemeName,
                SecuredPaths = apiKeyOptions.SecuredPaths,
                EnableLogging = apiKeyOptions.EnableLogging,
            };
        }

        // If no specific options are available, return one with common secured paths
        return new AuthOptions
        {
            Enabled = true,
            SecuredPaths = new List<string> { "/mcp" },
            EnableLogging = true,
        };
    }
}
