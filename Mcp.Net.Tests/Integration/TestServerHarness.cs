using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Transport.Sse;
using Mcp.Net.Server.Transport.Stdio;
using SseTransportConnectionManager = Mcp.Net.Server.Transport.Sse.SseTransportHost;
using Mcp.Net.Server.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Tests.Integration;

internal static class IntegrationTestServerFactory
{
    public static Task<SseIntegrationTestServer> StartSseServerAsync(
        Action<McpServer>? configureServer = null,
        CancellationToken cancellationToken = default
    ) => SseIntegrationTestServer.StartAsync(configureServer, cancellationToken);

    public static Task<StdioIntegrationTestServer> StartStdioServerAsync(
        Action<McpServer>? configureServer = null,
        CancellationToken cancellationToken = default
    ) => StdioIntegrationTestServer.StartAsync(configureServer, cancellationToken);

    internal static McpServer CreateServer(ILoggerFactory loggerFactory)
    {
        var serverBuilder = McpServerBuilder
            .ForSse()
            .WithName("IntegrationTestServer")
            .WithTitle("Integration Test Server")
            .WithVersion("1.0.0")
            .WithNoAuth();

        var serverOptions = new ServerOptions
        {
            Capabilities = new ServerCapabilities
            {
                Tools = new { },
                Resources = new { },
                Prompts = new { },
            },
        };

        return new McpServer(
            new ServerInfo
            {
                Name = "IntegrationTestServer",
                Title = "Integration Test Server",
                Version = "1.0.0",
            },
            serverOptions,
            loggerFactory
        );
    }
}

internal sealed class SseIntegrationTestServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _httpClient;
    private readonly SseTransportConnectionManager _connectionManager;

    private SseIntegrationTestServer(
        IHost host,
        HttpClient httpClient,
        McpServer server,
        SseTransportConnectionManager connectionManager
    )
    {
        _host = host;
        _httpClient = httpClient;
        Server = server;
        _connectionManager = connectionManager;
        ServerUrl = new Uri(_httpClient.BaseAddress!, "/mcp").ToString();
    }

    public McpServer Server { get; }

    public string ServerUrl { get; }

    public HttpClient CreateHttpClient() => _httpClient;

    public static async Task<SseIntegrationTestServer> StartAsync(
        Action<McpServer>? configureServer,
        CancellationToken cancellationToken
    )
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var server = IntegrationTestServerFactory.CreateServer(loggerFactory);
        configureServer?.Invoke(server);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(loggerFactory);

                    var authOptions = new AuthOptions().WithNoAuth();
                    services.AddSingleton(authOptions);

                    services.AddSingleton<McpServer>(server);

                    services.AddSingleton<IConnectionManager>(
                        _ => new InMemoryConnectionManager(loggerFactory, TimeSpan.FromMinutes(5))
                    );

                    services.AddSingleton<SseTransportConnectionManager>(sp =>
                        new SseTransportConnectionManager(
                            server,
                            loggerFactory,
                            sp.GetRequiredService<IConnectionManager>(),
                            authHandler: null,
                            allowedOrigins: null,
                            canonicalOrigin: null
                        )
                    );

                    services.AddSingleton<AuthOptions>(authOptions);
                    services.AddCors();
                    services.AddHealthChecks();
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseMcpServer(options =>
                    {
                        options.SsePath = "/mcp";
                        options.EnableCors = false;
                        options.HealthCheckPath = "/health";
                    });
                });
            });

        var host = await hostBuilder.StartAsync(cancellationToken);
        var testClient = host.GetTestClient();

        // Ensure BaseAddress is set so SseClientTransport resolves endpoint correctly.
        testClient.BaseAddress ??= new Uri("http://localhost");

        var connectionManager = host.Services.GetRequiredService<SseTransportConnectionManager>();

        return new SseIntegrationTestServer(host, testClient, server, connectionManager);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connectionManager.CloseAllConnectionsAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore cleanup errors to avoid masking test failures.
        }

        _httpClient.Dispose();
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}

internal sealed class StdioIntegrationTestServer : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly StdioTransport _serverTransport;
    private readonly Stream _serverInput;
    private readonly Stream _serverOutput;
    private readonly Stream _clientInput;
    private readonly Stream _clientOutput;
    private readonly Pipe _clientToServer;
    private readonly Pipe _serverToClient;

    private StdioIntegrationTestServer(
        McpServer server,
        ILoggerFactory loggerFactory,
        StdioTransport serverTransport,
        Stream serverInput,
        Stream serverOutput,
        Stream clientInput,
        Stream clientOutput,
        Pipe clientToServer,
        Pipe serverToClient
    )
    {
        Server = server;
        _loggerFactory = loggerFactory;
        _serverTransport = serverTransport;
        _serverInput = serverInput;
        _serverOutput = serverOutput;
        _clientInput = clientInput;
        _clientOutput = clientOutput;
        _clientToServer = clientToServer;
        _serverToClient = serverToClient;
    }

    public McpServer Server { get; }

    public Stream ClientInput => _clientInput;

    public Stream ClientOutput => _clientOutput;

    public Stream ServerInput => _serverInput;

    public Stream ServerOutput => _serverOutput;

    public static async Task<StdioIntegrationTestServer> StartAsync(
        Action<McpServer>? configureServer,
        CancellationToken cancellationToken
    )
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var serverBuilder = McpServerBuilder
            .ForStdio()
            .WithName("IntegrationTestServer")
            .WithTitle("Integration Test Server")
            .WithVersion("1.0.0")
            .WithNoAuth();

        var serverOptions = new ServerOptions
        {
            Capabilities = new ServerCapabilities
            {
                Tools = new { },
                Resources = new { },
                Prompts = new { },
            },
        };

        var server = new McpServer(
            new ServerInfo
            {
                Name = "IntegrationTestServer",
                Title = "Integration Test Server",
                Version = "1.0.0",
            },
            serverOptions,
            loggerFactory
        );

        configureServer?.Invoke(server);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var serverInput = clientToServer.Reader.AsStream(leaveOpen: true);
        var clientOutput = clientToServer.Writer.AsStream(leaveOpen: true);

        var serverOutput = serverToClient.Writer.AsStream(leaveOpen: true);
        var clientInput = serverToClient.Reader.AsStream(leaveOpen: true);

        var stdioTransport = new StdioTransport(
            "",
            serverInput,
            serverOutput,
            loggerFactory.CreateLogger<StdioTransport>()
        );

        await server.ConnectAsync(stdioTransport).ConfigureAwait(false);

        return new StdioIntegrationTestServer(
            server,
            loggerFactory,
            stdioTransport,
            serverInput,
            serverOutput,
            clientInput,
            clientOutput,
            clientToServer,
            serverToClient
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _serverTransport.CloseAsync().ConfigureAwait(false);

        await SafeDisposeAsync(_serverInput).ConfigureAwait(false);
        await SafeDisposeAsync(_serverOutput).ConfigureAwait(false);
        await SafeDisposeAsync(_clientInput).ConfigureAwait(false);
        await SafeDisposeAsync(_clientOutput).ConfigureAwait(false);

        await _clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
        await _clientToServer.Reader.CompleteAsync().ConfigureAwait(false);
        await _serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
        await _serverToClient.Reader.CompleteAsync().ConfigureAwait(false);

        _loggerFactory.Dispose();
    }

    private static async Task SafeDisposeAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore disposal failures to avoid masking test failures.
        }
    }
}
