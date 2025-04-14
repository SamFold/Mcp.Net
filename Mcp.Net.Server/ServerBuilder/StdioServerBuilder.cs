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
        
        _logger.LogInformation("Server started with stdio transport");
        
        // Create a task that completes when the transport closes
        var tcs = new TaskCompletionSource<bool>();
        transport.OnClose += () => tcs.TrySetResult(true);
        
        // Wait for the transport to close
        await tcs.Task;
    }

    /// <inheritdoc />
    public IServerTransport BuildTransport()
    {
        _logger.LogDebug("Building stdio transport");
        
        var transport = new StdioTransport(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            _loggerFactory.CreateLogger<StdioTransport>()
        );

        // Configure transport event handlers
        transport.OnRequest += request =>
        {
            _logger.LogDebug(
                "JSON-RPC Request: Method={Method}, Id={Id}",
                request.Method,
                request.Id ?? "null"
            );
        };

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
