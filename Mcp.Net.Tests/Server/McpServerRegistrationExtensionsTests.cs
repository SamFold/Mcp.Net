using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Elicitation;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.Extensions.Transport;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Transport.Sse;
using Mcp.Net.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        await server.ProcessJsonRpcRequest(
            new JsonRpcRequestMessage(
                "2.0",
                "init-elicitation",
                "initialize",
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new
                    {
                        clientInfo = new ClientInfo { Name = "Test Client", Version = "1.0" },
                        capabilities = new { elicitation = new { } },
                        protocolVersion = McpServer.LatestProtocolVersion,
                    }
                )
            ),
            transport.Id()
        );

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

    [Fact]
    public void AddMcpStdioTransport_WithOptionsInstance_ShouldPreserveConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        using var inputStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var outputStream = new MemoryStream();

        var transportOptions = new StdioServerOptions
        {
            Name = "Stdio Options Server",
            Title = "Stdio Options Title",
            Version = "3.2.1",
            Instructions = "Use stdio options.",
            UseStandardIO = false,
            InputStream = inputStream,
            OutputStream = outputStream,
            Capabilities = new Mcp.Net.Core.Models.Capabilities.ServerCapabilities(),
            ToolRegistration = new ToolRegistrationOptions
            {
                IncludeEntryAssembly = false,
                ValidateToolMethods = false,
                EnableDetailedLogging = true,
            },
        };

        transportOptions.Authentication.Enabled = false;
        transportOptions.Authentication.NoAuthExplicitlyConfigured = true;

        services.AddMcpStdioTransport(transportOptions);

        using var provider = services.BuildServiceProvider();

        var resolvedOptions = provider.GetRequiredService<IOptions<StdioServerOptions>>().Value;

        resolvedOptions.Name.Should().Be("Stdio Options Server");
        resolvedOptions.Title.Should().Be("Stdio Options Title");
        resolvedOptions.Version.Should().Be("3.2.1");
        resolvedOptions.Instructions.Should().Be("Use stdio options.");
        resolvedOptions.UseStandardIO.Should().BeFalse();
        resolvedOptions.InputStream.Should().BeSameAs(inputStream);
        resolvedOptions.OutputStream.Should().BeSameAs(outputStream);
        resolvedOptions.Authentication.Enabled.Should().BeFalse();
        resolvedOptions.Authentication.NoAuthExplicitlyConfigured.Should().BeTrue();
        resolvedOptions.ToolRegistration.IncludeEntryAssembly.Should().BeFalse();
        resolvedOptions.ToolRegistration.ValidateToolMethods.Should().BeFalse();
        resolvedOptions.ToolRegistration.EnableDetailedLogging.Should().BeTrue();
    }

    [Fact]
    public void AddMcpStdioTransport_ShouldRegisterHostedService()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddMcpCore(options =>
        {
            options.Name = "Hosted Stdio Server";
            options.Version = "1.0.0";
        });
        services.AddMcpStdioTransport(options =>
        {
            options.Name = "Hosted Stdio Server";
            options.Version = "1.0.0";
        });

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(McpServerHostedService)
        );
    }

    [Fact]
    public async Task AddMcpStdioTransport_WithCustomStreams_ShouldStartHostedServer()
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();

        using var host = new HostBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddMcpCore(options =>
                {
                    options.Name = "Hosted Stdio Server";
                    options.Version = "1.0.0";
                });
                services.AddMcpStdioTransport(options =>
                {
                    options.Name = "Hosted Stdio Server";
                    options.Version = "1.0.0";
                    options.UseStandardIO = false;
                    options.InputStream = inputPipe.Reader.AsStream();
                    options.OutputStream = outputPipe.Writer.AsStream();
                });
            })
            .Build();

        await host.StartAsync();

        await WriteLineAsync(inputPipe.Writer, CreateInitializeRequestJson("hosted-stdio-init"));
        var responseLine = await ReadLineAsync(outputPipe.Reader, TimeSpan.FromSeconds(5));

        responseLine.Should().NotBeNull();

        using var response = JsonDocument.Parse(responseLine!);
        response.RootElement.GetProperty("id").GetString().Should().Be("hosted-stdio-init");
        response.RootElement
            .GetProperty("result")
            .GetProperty("serverInfo")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("Hosted Stdio Server");

        await host.StopAsync();
    }

    [Fact]
    public async Task AddMcpCore_WithBuilder_ShouldPreserveBuilderConfiguredServerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        var builder = Mcp.Net.Server.ServerBuilder.McpServerBuilder.ForSse();
        builder
            .WithName("Builder Configured Server")
            .WithTitle("Builder Configured Title")
            .WithVersion("2.3.4")
            .WithInstructions("Use the configured instructions.")
            .WithNoAuth();

        services.AddMcpCore(builder);

        using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();
        var resolvedOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var initializeResponse = await server.ProcessJsonRpcRequest(
            CreateInitializeRequest("builder-init"),
            "builder-session"
        );
        var initializeResult = System.Text.Json.JsonSerializer.SerializeToElement(
            initializeResponse.Result
        );

        resolvedOptions.Name.Should().Be("Builder Configured Server");
        resolvedOptions.Title.Should().Be("Builder Configured Title");
        resolvedOptions.Version.Should().Be("2.3.4");
        resolvedOptions.Instructions.Should().Be("Use the configured instructions.");
        initializeResult.GetProperty("serverInfo").GetProperty("name").GetString()
            .Should()
            .Be("Builder Configured Server");
        initializeResult.GetProperty("serverInfo").GetProperty("title").GetString()
            .Should()
            .Be("Builder Configured Title");
        initializeResult.GetProperty("serverInfo").GetProperty("version").GetString()
            .Should()
            .Be("2.3.4");
        initializeResult.GetProperty("instructions").GetString()
            .Should()
            .Be("Use the configured instructions.");
    }

    [Fact]
    public void AddMcpStdioTransport_WithBuilder_ShouldPreserveBuilderConfiguredServerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        var builder = Mcp.Net.Server.ServerBuilder.McpServerBuilder.ForStdio();
        builder
            .WithName("Builder Stdio Server")
            .WithTitle("Builder Stdio Title")
            .WithVersion("7.8.9")
            .WithInstructions("Use stdio builder instructions.")
            .WithNoAuth();

        services.AddMcpStdioTransport(builder);

        using var provider = services.BuildServiceProvider();

        var resolvedOptions = provider.GetRequiredService<IOptions<StdioServerOptions>>().Value;

        resolvedOptions.Name.Should().Be("Builder Stdio Server");
        resolvedOptions.Title.Should().Be("Builder Stdio Title");
        resolvedOptions.Version.Should().Be("7.8.9");
        resolvedOptions.Instructions.Should().Be("Use stdio builder instructions.");
    }

    private static JsonRpcRequestMessage CreateInitializeRequest(string requestId)
    {
        var paramsElement = System.Text.Json.JsonSerializer.SerializeToElement(
            new
            {
                clientInfo = new { name = "Test Client", version = "1.0" },
                capabilities = new { },
                protocolVersion = McpServer.LatestProtocolVersion,
            }
        );

        return new JsonRpcRequestMessage("2.0", requestId, "initialize", paramsElement);
    }

    private static string CreateInitializeRequestJson(string requestId)
    {
        return JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "Hosted Test Client", version = "1.0" },
                    capabilities = new { },
                    protocolVersion = McpServer.LatestProtocolVersion,
                },
            }
        );
    }

    private static async Task WriteLineAsync(PipeWriter writer, string line)
    {
        await writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        await writer.FlushAsync();
    }

    private static async Task<string?> ReadLineAsync(PipeReader reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (true)
        {
            var result = await reader.ReadAsync(cts.Token);
            var buffer = result.Buffer;

            if (TryReadLine(ref buffer, out var line))
            {
                var message = Encoding.UTF8.GetString(line.ToArray());
                reader.AdvanceTo(buffer.Start, buffer.End);
                return message;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                return null;
            }
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position == null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }
}
