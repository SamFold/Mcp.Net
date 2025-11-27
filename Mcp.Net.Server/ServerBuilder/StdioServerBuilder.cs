using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Transport.Stdio;

namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Builder for creating and configuring a stdio-based MCP server.
/// </summary>
public class StdioServerBuilder : IMcpServerBuilder, ITransportBuilder
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StdioServerBuilder> _logger;
    private static int _count = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerBuilder"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    public StdioServerBuilder(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<StdioServerBuilder>();
    }

    /// <inheritdoc />
    public McpServer Build()
    {
        // This should be handled by the main McpServerBuilder
        throw new InvalidOperationException("StdioServerBuilder doesn't implement Build directly. Use McpServerBuilder instead.");
    }

    /// <inheritdoc />
    public async Task StartAsync(McpServer server)
    {
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        _logger.LogInformation("Starting server with stdio transport");
        
        var transport = BuildTransport();
        await server.ConnectAsync(transport);

        var cts = new CancellationTokenSource();
        transport.OnClose += () => cts.Cancel();
        transport.OnError += _ => cts.Cancel();

        var ingressLogger = _loggerFactory.CreateLogger<Transport.Stdio.StdioIngressHost>();
        var ingressHost = new Transport.Stdio.StdioIngressHost(server, (Transport.Stdio.StdioTransport)transport, ingressLogger);
        var ingressTask = ingressHost.RunAsync(cts.Token);
        
        _logger.LogInformation("Server started with stdio transport");
        
        // Create a task that completes when the transport closes
        var tcs = new TaskCompletionSource<bool>();
        transport.OnClose += () => tcs.TrySetResult(true);
        
        // Wait for the transport to close
        await Task.WhenAny(tcs.Task, ingressTask);
        cts.Cancel();
        await ingressTask;
    }

    /// <inheritdoc />
    public IServerTransport BuildTransport()
    {
        _logger.LogDebug("Building stdio transport");

        var transport = new StdioTransport(
            (Interlocked.Increment(ref _count) - 1).ToString(),
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            _loggerFactory.CreateLogger<StdioTransport>()
        );

        transport.OnError += ex =>
        {
            _logger.LogError(ex, "Stdio transport error");
        };

        transport.OnClose += () =>
        {
            _logger.LogInformation("Stdio transport closed");
        };

        return transport;
    }
}
