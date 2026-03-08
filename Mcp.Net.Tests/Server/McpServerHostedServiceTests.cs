using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mcp.Net.Tests.Server;

public class McpServerHostedServiceTests
{
    [Fact]
    public async Task StartAsync_Should_LogConfiguredServerIdentity()
    {
        var server = new McpServer(
            new ServerInfo
            {
                Name = "Configured Server",
                Version = "9.8.7",
            },
            new InMemoryConnectionManager(NullLoggerFactory.Instance),
            new ServerOptions(),
            loggerFactory: NullLoggerFactory.Instance
        );

        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var appLifetime = new TestHostApplicationLifetime();
        var logger = new Mock<ILogger<McpServerHostedService>>();

        var hostedService = new McpServerHostedService(
            server,
            serviceProvider,
            appLifetime,
            logger.Object
        );

        await hostedService.StartAsync(CancellationToken.None);

        logger.Verify(
            x =>
                x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (state, _) => state.ToString() == "Server name: Configured Server"
                    ),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );

        logger.Verify(
            x =>
                x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (state, _) => state.ToString() == "Server version: 9.8.7"
                    ),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}
