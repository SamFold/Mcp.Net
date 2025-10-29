using Mcp.Net.Client;

namespace Mcp.Net.WebUi.Authentication;

/// <summary>
/// Applies authentication configuration to an <see cref="McpClientBuilder"/>.
/// </summary>
public interface IMcpClientBuilderConfigurator
{
    Task ConfigureAsync(McpClientBuilder builder, CancellationToken cancellationToken = default);
}
