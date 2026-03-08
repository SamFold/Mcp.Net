using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.Transport.Stdio;
using Mcp.Net.Server.Transport.Sse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SseConnectionManagerType = Mcp.Net.Server.Transport.Sse.SseTransportHost;

namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Hosted service for managing the MCP server lifecycle in ASP.NET Core applications.
/// Handles server initialization, health monitoring, and graceful shutdown.
/// </summary>
public class McpServerHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServerHostedService> _logger;
    private readonly SseConnectionManagerType? _connectionManager;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly McpServer _server;
    private readonly StdioServerOptions? _stdioOptions;
    private Task? _monitoringTask;
    private Task? _stdioIngressTask;
    private StdioTransport? _stdioTransport;
    private bool _disposed;
    private readonly ServerInfo _serverInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerHostedService"/> class.
    /// </summary>
    public McpServerHostedService(
        McpServer server,
        IServiceProvider serviceProvider,
        IHostApplicationLifetime appLifetime,
        ILogger<McpServerHostedService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _appLifetime = appLifetime ?? throw new ArgumentNullException(nameof(appLifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverInfo = server.ServerInfo;
        _connectionManager = _serviceProvider.GetService<SseConnectionManagerType>();
        _stdioOptions = _serviceProvider.GetService<IOptions<StdioServerOptions>>()?.Value;
    }

    /// <summary>
    /// Starts the MCP server when the host starts.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MCP server (version {Version})...", _serverInfo.Version);

        try
        {
            // Register application stopping callback
            _appLifetime.ApplicationStopping.Register(OnApplicationStopping);

            if (_connectionManager == null && _stdioOptions != null)
            {
                await StartStdioTransportAsync(cancellationToken);
            }
            
            // Start a background task to monitor server health
            _monitoringTask = MonitorServerHealthAsync(_stoppingCts.Token);
            
            // Log server capabilities
            LogServerCapabilities();
            
            _logger.LogInformation("MCP server started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting MCP server");
            throw;
        }
    }

    /// <summary>
    /// Stops the MCP server when the host stops.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MCP server...");
        
        // Signal cancellation to the monitoring task
        if (!_stoppingCts.IsCancellationRequested)
        {
            _stoppingCts.Cancel();
        }
        
        // Wait for the monitoring task to complete with a timeout
        if (_monitoringTask != null)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completedTask = await Task.WhenAny(_monitoringTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Server monitoring task did not complete in time");
            }
        }

        if (_stdioTransport != null)
        {
            await StopStdioTransportAsync(cancellationToken);
        }
        
        // Clean up connection manager if needed
        if (_connectionManager != null)
        {
            _connectionManager.CloseAllConnections();
            _logger.LogInformation("All SSE connections closed");
        }
        
        _logger.LogInformation("MCP server stopped");
    }
    
    /// <summary>
    /// Handles application stopping event by performing cleanup.
    /// </summary>
    private void OnApplicationStopping()
    {
        _logger.LogDebug("Application stopping event received");
        
        try
        {
            // Perform any cleanup required before the application fully stops
            if (_connectionManager != null)
            {
                // Send a closing message to all clients if needed
                _logger.LogDebug("Notifying clients about server shutdown");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during application stopping event");
        }
    }
    
    /// <summary>
    /// Monitors server health in a background task.
    /// </summary>
    private async Task MonitorServerHealthAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Server health monitoring started");
        
        try
        {
            // Monitor server health periodically
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check connection count if connection manager is available
                if (_connectionManager != null)
                {
                    _logger.LogDebug("SSE transport host is active.");
                }
                
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            _logger.LogDebug("Server health monitoring canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in server health monitoring");
        }
        finally
        {
            _logger.LogDebug("Server health monitoring stopped");
        }
    }
    
    /// <summary>
    /// Logs information about server capabilities.
    /// </summary>
    private void LogServerCapabilities()
    {
        _logger.LogInformation("Server name: {ServerName}", _serverInfo.Name);
        _logger.LogInformation("Server version: {ServerVersion}", _serverInfo.Version);
        
        // Log available tools
        var toolNames = new List<string>();
        // We don't have direct access to the tools, so we'll log what we can
        _logger.LogInformation("Server is ready to accept connections");
        
        // Log connection information if available
        if (_connectionManager != null)
        {
            _logger.LogInformation("Server listening for SSE connections");
        }

        if (_stdioTransport != null)
        {
            _logger.LogInformation("Server listening on stdio");
        }
    }

    private async Task StartStdioTransportAsync(CancellationToken cancellationToken)
    {
        _stdioOptions!.Validate();

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var inputStream = _stdioOptions.UseStandardIO
            ? Console.OpenStandardInput()
            : _stdioOptions.InputStream!;
        var outputStream = _stdioOptions.UseStandardIO
            ? Console.OpenStandardOutput()
            : _stdioOptions.OutputStream!;

        _stdioTransport = new StdioTransport(
            "stdio",
            inputStream,
            outputStream,
            loggerFactory.CreateLogger<StdioTransport>()
        );
        _stdioTransport.OnClose += () => _stoppingCts.Cancel();
        _stdioTransport.OnError += _ => _stoppingCts.Cancel();

        cancellationToken.ThrowIfCancellationRequested();
        await _server.ConnectAsync(_stdioTransport);

        var ingressHost = new StdioIngressHost(
            _server,
            _stdioTransport,
            loggerFactory.CreateLogger<StdioIngressHost>()
        );
        _stdioIngressTask = ingressHost.RunAsync(_stoppingCts.Token);
    }

    private async Task StopStdioTransportAsync(CancellationToken cancellationToken)
    {
        if (_stdioTransport != null)
        {
            try
            {
                await _stdioTransport.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing stdio transport");
            }
        }

        if (_stdioIngressTask == null)
        {
            return;
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        var completedTask = await Task.WhenAny(_stdioIngressTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            _logger.LogWarning("Stdio ingress task did not complete in time");
            return;
        }

        try
        {
            await _stdioIngressTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Stdio ingress task canceled");
        }
    }
    
    /// <summary>
    /// Disposes resources used by the service.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// Disposes resources used by the service.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _stoppingCts.Dispose();
            }
            
            _disposed = true;
        }
    }
}
