using Mcp.Net.Client;
using Mcp.Net.Client.Interfaces;

namespace Mcp.Net.WebUi.Startup.Factories;

public static class McpClientFactory
{
    public static IMcpClient CreateClient(
        IConfiguration configuration,
        string[] args,
        ILogger logger
    )
    {
        var mcpServerUrl = configuration["McpServer:Url"] ?? "http://localhost:5000/";
        var mcpServerApiKey = configuration["McpServer:ApiKey"];

        var noAuth =
            args.Contains("--no-auth") || configuration.GetValue<bool>("McpServer:NoAuth", false);

        try
        {
            var clientBuilder = new McpClientBuilder().UseSseTransport(mcpServerUrl);

            if (!noAuth && !string.IsNullOrEmpty(mcpServerApiKey))
            {
                logger.LogWarning(
                    "API key authentication is no longer supported; connecting to {Url} without credentials.",
                    mcpServerUrl
                );
            }

            logger.LogInformation(
                "Connecting to MCP server at {Url} without authentication",
                mcpServerUrl
            );

            return clientBuilder.BuildAndInitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to MCP server at {Url}", mcpServerUrl);
            throw;
        }
    }
}
