using System.Linq;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.Extensions.Transport;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.Transport.Sse;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task AddMcpCore_WithSseTransport_ShouldResolveSessionBoundElicitationFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddMcpCore(options =>
        {
            options.Name = "Core Test Server";
            options.Version = "1.0.0";
        });
        services.AddMcpSseTransport(options =>
        {
            options.Name = "Core Test Server";
            options.Version = "1.0.0";
        });

        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();
        var sseHost = provider.GetRequiredService<SseTransportHost>();
        var connectionManager = provider.GetRequiredService<IConnectionManager>();
        var elicitationFactory = provider.GetRequiredService<IElicitationServiceFactory>();

        sseHost.Should().NotBeNull();
        connectionManager.Should().NotBeNull();
        provider.GetService<IElicitationService>().Should().BeNull();

        var transport = new MockTransport("session-elicitation");
        await server.ConnectAsync(transport);

        var prompt = new ElicitationPrompt(
            "Provide the inquisitor name",
            new ElicitationSchema().AddProperty(
                "name",
                ElicitationSchemaProperty.ForString(title: "Name"),
                required: true
            )
        );

        var service = elicitationFactory.Create(transport.Id());
        var requestTask = service.RequestAsync(prompt);

        await Task.Delay(10);

        transport.SentRequests.Should().ContainSingle();
        var request = transport.SentRequests.Single();
        request.Method.Should().Be("elicitation/create");

        await server.HandleClientResponseAsync(
            transport.Id(),
            new JsonRpcResponseMessage(
                "2.0",
                request.Id,
                new
                {
                    action = "accept",
                    content = new { name = "Istvaan" },
                },
                null
            )
        );

        var result = await requestTask;
        result.Action.Should().Be(ElicitationAction.Accept);
        result.Content.Should().NotBeNull();
        result.Content!.Value.GetProperty("name").GetString().Should().Be("Istvaan");
    }

    [Fact]
    public void AddMcpSseTransport_WithOptionsInstance_ShouldPreserveRoutingAndSecuritySettings()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddMcpCore(options =>
        {
            options.Name = "Options Test Server";
            options.Version = "1.0.0";
        });

        var transportOptions = new SseServerOptions
        {
            Name = "Options Test Server",
            Version = "1.0.0",
            Scheme = "https",
            Hostname = "api.example.test",
            Port = 9443,
            SsePath = "/custom-mcp",
            HealthCheckPath = "/custom-health",
            EnableCors = false,
            AllowedOrigins = new[] { "https://client.example.test" },
            ConnectionTimeout = TimeSpan.FromMinutes(5),
            Args = new[] { "--flag" },
        };

        services.AddMcpSseTransport(transportOptions);

        using var provider = services.BuildServiceProvider();

        var resolvedOptions = provider.GetRequiredService<IOptions<SseServerOptions>>().Value;

        resolvedOptions.Scheme.Should().Be("https");
        resolvedOptions.Hostname.Should().Be("api.example.test");
        resolvedOptions.Port.Should().Be(9443);
        resolvedOptions.SsePath.Should().Be("/custom-mcp");
        resolvedOptions.HealthCheckPath.Should().Be("/custom-health");
        resolvedOptions.EnableCors.Should().BeFalse();
        resolvedOptions.AllowedOrigins.Should().Equal("https://client.example.test");
        resolvedOptions.ConnectionTimeout.Should().Be(TimeSpan.FromMinutes(5));
        resolvedOptions.Args.Should().Equal("--flag");
    }
}
