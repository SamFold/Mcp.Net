using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Mcp.Net.Server.Logging;
using Mcp.Net.Server.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Factory for creating and initializing MCP servers based on configuration
/// </summary>
public class ServerFactory
{
    private readonly CommandLineOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServerFactory> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerFactory"/> class
    /// </summary>
    /// <param name="options">Command-line options</param>
    /// <param name="loggerFactory">The logger factory to use</param>
    /// <param name="configuration">The configuration to use</param>
    public ServerFactory(
        CommandLineOptions options,
        ILoggerFactory loggerFactory,
        IConfiguration configuration
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = _loggerFactory.CreateLogger<ServerFactory>();

        // Validate options when factory is created
        ValidateOptions();
    }

    /// <summary>
    /// Creates a new instance of the <see cref="ServerFactory"/> class from arguments
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <param name="loggerFactory">The logger factory to use</param>
    /// <param name="configuration">The configuration to use</param>
    public static ServerFactory FromArgs(
        string[] args,
        ILoggerFactory loggerFactory,
        IConfiguration configuration
    )
    {
        var options = CommandLineOptions.Parse(args);
        return new ServerFactory(options, loggerFactory, configuration);
    }

    /// <summary>
    /// Validate the command line options
    /// </summary>
    private void ValidateOptions()
    {
        if (_options.Validate(out var validationResults))
        {
            return;
        }

        // Log all validation errors
        foreach (var result in validationResults)
        {
            _logger.LogWarning(
                "Command line option validation error: {Error}",
                result.ErrorMessage
            );
        }

        // Continue with invalid options, but log a warning
        _logger.LogWarning(
            "Running with invalid command line options. Some features may not work correctly."
        );
    }

    /// <summary>
    /// Creates and runs the appropriate server type based on options
    /// </summary>
    public async Task RunServerAsync()
    {
        _logger.LogInformation(
            "MCP Server starting. UseStdio={UseStdio}, DebugMode={DebugMode}, LogPath={LogPath}",
            _options.UseStdio,
            _options.DebugMode,
            _options.LogPath
        );

        if (_options.UseStdio)
        {
            await RunStdioServerAsync();
        }
        else
        {
            await RunSseServerAsync();
        }
    }

    /// <summary>
    /// Creates and runs an SSE-based server
    /// </summary>
    private async Task RunSseServerAsync()
    {
        // Configure the cancellation token source for graceful shutdown
        var cancellationSource = new CancellationTokenSource();

        // Register for process termination events to enable graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            _logger.LogInformation("Shutdown signal received, beginning graceful shutdown");
            e.Cancel = true; // Prevent immediate termination
            cancellationSource.Cancel();
        };

        // Create server options using the command line options
        var serverOptions = new SseServerOptions
        {
            // Networking
            Hostname = _options.Hostname ?? "localhost",
            Port = _options.Port ?? 5000,
            Scheme = _options.Scheme ?? "http",

            // Server identity
            Name = _options.ServerName ?? "MCP Server",

            // Logging
            LogLevel = _options.MinimumLogLevel,
            LogFilePath = _options.LogPath,

            // Store original args
            Args = _options.Args,
        };

        // Create and configure the server using the options object
        var builder = McpServerBuilder.ForSse(serverOptions).WithLoggerFactory(_loggerFactory);

        ApplyConfiguredAuthentication(builder, serverOptions);

        // Add tool assemblies if provided
        if (_options.ToolAssemblies != null)
        {
            foreach (var assemblyPath in _options.ToolAssemblies)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    builder.ScanToolsFromAssembly(assembly);
                    _logger.LogInformation("Loaded tool assembly: {AssemblyPath}", assemblyPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load tool assembly: {AssemblyPath}",
                        assemblyPath
                    );
                }
            }
        }

        try
        {
            _logger.LogInformation("Starting SSE server on {ServerUrl}", serverOptions.BaseUrl);
            var server = await builder.StartAsync();

            // Wait until cancellation is requested
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationSource.Token);
            }
            catch (TaskCanceledException)
            {
                // This is expected when cancellation occurs
                _logger.LogInformation("Server shutdown initiated");
            }

            _logger.LogInformation("SSE server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running SSE server");
            throw;
        }
    }

    /// <summary>
    /// Creates and runs a stdio-based server
    /// </summary>
    private async Task RunStdioServerAsync()
    {
        // Create options from command line values
        var serverOptions = new McpServerOptions
        {
            // Server identity
            Name = _options.ServerName ?? "MCP Server (Stdio)",

            // Logging
            LogLevel = _options.MinimumLogLevel,
            LogFilePath = _options.LogPath,
        };

        // Configure the builder with all the needed options
        var builder = McpServerBuilder
            .ForStdio()
            .WithLoggerFactory(_loggerFactory)
            .WithName(serverOptions.Name);

        // Add tool assemblies if provided
        if (_options.ToolAssemblies != null)
        {
            foreach (var assemblyPath in _options.ToolAssemblies)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    builder.ScanToolsFromAssembly(assembly);
                    _logger.LogInformation("Loaded tool assembly: {AssemblyPath}", assemblyPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load tool assembly: {AssemblyPath}",
                        assemblyPath
                    );
                }
            }
        }

        // Set up a cancellation token for graceful shutdown
        using var cts = new CancellationTokenSource();

        // Register for process termination events
        Console.CancelKeyPress += (sender, e) =>
        {
            _logger.LogInformation("Shutdown signal received, beginning graceful shutdown");
            e.Cancel = true; // Prevent immediate termination
            cts.Cancel();
        };

        try
        {
            _logger.LogInformation("Starting stdio server");

            // Start the server and wait for it to complete
            var server = await builder.StartAsync();

            // The stdio transport will keep running until input is closed or process termination
            _logger.LogInformation("Stdio server started successfully");

            // Wait for cancellation (this is optional since the StartAsync may not return until stdio is closed)
            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
        }
        catch (TaskCanceledException)
        {
            // This is expected during cancellation
            _logger.LogInformation("Stdio server shutdown initiated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running stdio server");
            throw;
        }

        _logger.LogInformation("Stdio server stopped");
    }

    private void ApplyConfiguredAuthentication(
        McpServerBuilder builder,
        SseServerOptions serverOptions
    )
    {
        var authSection = _configuration.GetSection("Server:Authentication");
        if (!authSection.Exists())
        {
            return;
        }

        var configuration = new AuthenticationConfiguration();
        authSection.Bind(configuration);

        builder.WithAuthentication(auth => configuration.ApplyTo(auth, _logger));

        if (builder.ConfiguredAuthOptions != null)
        {
            serverOptions.Authentication = builder.ConfiguredAuthOptions;
        }

        if (
            builder.AuthHandler == null
            && configuration.Disable != true
            && configuration.OAuth == null
        )
        {
            _logger.LogWarning(
                "Server authentication configuration was present, but no authentication handler was activated. Verify the Server:Authentication settings."
            );
        }
    }
}
