using Mcp.Net.Client;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.WebUi.Authentication;

namespace Mcp.Net.WebUi.Startup.Factories;

public static class McpClientFactory
{
    public static async Task<IMcpClient> CreateClientAsync(
        IConfiguration configuration,
        ILogger logger,
        IMcpClientBuilderConfigurator authConfigurator,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(authConfigurator);

        var mcpServerUrl = configuration["McpServer:Url"] ?? "http://localhost:5000/";

        try
        {
            logger.LogInformation("Connecting to MCP server at {Url}", mcpServerUrl);

            var clientBuilder = new McpClientBuilder().UseSseTransport(mcpServerUrl);
            await authConfigurator.ConfigureAsync(clientBuilder, cancellationToken)
                .ConfigureAwait(false);

            return await clientBuilder.BuildAndInitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to MCP server at {Url}", mcpServerUrl);
            throw;
        }
    }
}
