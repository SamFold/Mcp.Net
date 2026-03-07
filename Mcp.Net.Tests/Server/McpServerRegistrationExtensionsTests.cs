using FluentAssertions;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Tests.Server;

public class McpServerRegistrationExtensionsTests
{
    [Fact]
    public async Task AddMcpServer_ShouldReuseSingleConnectionManagerForServerAndSseHost()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddMcpServer(builder =>
        {
            builder
                .WithName("Test Server")
                .WithVersion("1.0.0")
                .WithNoAuth();
        });

        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();
        var connectionManager = provider.GetRequiredService<IConnectionManager>();

        connectionManager.Should().BeSameAs(server.ConnectionManager);

        var transport = new MockTransport("session-1");
        await server.ConnectAsync(transport);

        var resolvedTransport = await connectionManager.GetTransportAsync(transport.Id());
        resolvedTransport.Should().BeSameAs(transport);
    }
}
