using System;
using System.Reflection;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Logging;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Services;

namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Builder for creating and configuring MCP servers with different transport types.
/// </summary>
public class McpServerBuilder
{
    private readonly IMcpServerBuilder _transportBuilder;
    private readonly ServerInfo _serverInfo = new();
    private LogLevel _logLevel = LogLevel.Information;
    private bool _useConsoleLogging = true;
    private string? _logFilePath = "mcp-server.log";
    internal readonly List<Assembly> _assemblies = new();
    private ServerOptions? _options;
    private readonly ServiceCollection _services = new();
    private IAuthHandler? _authHandler;
    private AuthOptions? _configuredAuthOptions;
    private ILoggerFactory? _loggerFactory;
    private bool _securityConfigured = false;
    private bool _noAuthExplicitlyConfigured = false;
    private IConnectionManager? _connectionManager;

    /// <summary>
    /// Creates a new server builder for stdio transport.
    /// </summary>
    /// <returns>A new McpServerBuilder configured for stdio transport</returns>
    public static McpServerBuilder ForStdio()
    {
        var loggerFactory = CreateDefaultLoggerFactory();
        return new McpServerBuilder(new StdioServerBuilder(loggerFactory));
    }

    /// <summary>
    /// Creates a new server builder for SSE transport.
    /// </summary>
    /// <returns>A new McpServerBuilder configured for SSE transport</returns>
    public static McpServerBuilder ForSse()
    {
        var loggerFactory = CreateDefaultLoggerFactory();
        return new McpServerBuilder(new SseServerBuilder(loggerFactory));
    }

    /// <summary>
    /// Creates a new server builder for SSE transport with the specified options.
    /// </summary>
    /// <param name="options">The options to configure the server with</param>
    /// <returns>A new McpServerBuilder configured for SSE transport with the provided options</returns>
    public static McpServerBuilder ForSse(Options.SseServerOptions options)
    {
        var loggerFactory = CreateDefaultLoggerFactory();
        return new McpServerBuilder(new SseServerBuilder(loggerFactory, options));
    }

    /// <summary>
    /// Creates a default logger factory with console logging.
    /// </summary>
    private static ILoggerFactory CreateDefaultLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerBuilder"/> class.
    /// </summary>
    /// <param name="transportBuilder">The transport-specific builder to use</param>
    private McpServerBuilder(IMcpServerBuilder transportBuilder)
    {
        _transportBuilder =
            transportBuilder ?? throw new ArgumentNullException(nameof(transportBuilder));
    }

    /// <summary>
    /// Gets the transport builder used by this server builder.
    /// </summary>
    internal IMcpServerBuilder TransportBuilder => _transportBuilder;

    /// <summary>
    /// Gets the authentication handler configured for this server.
    /// </summary>
    public IAuthHandler? AuthHandler => _authHandler;

    /// <summary>
    /// Gets the log level configured for this server.
    /// </summary>
    public LogLevel LogLevel => _logLevel;

    internal AuthOptions? ConfiguredAuthOptions => _configuredAuthOptions;

    /// <summary>
    /// Gets whether console logging is enabled for this server.
    /// </summary>
    public bool UseConsoleLogging => _useConsoleLogging;

    /// <summary>
    /// Gets the log file path configured for this server.
    /// </summary>
    public string? LogFilePath => _logFilePath;

    /// <summary>
    /// Configures the server with a specific name.
    /// </summary>
    /// <param name="name">The name of the server</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithName(string name)
    {
        _serverInfo.Name = name;
        return this;
    }

    /// <summary>
    /// Configures the server with a human-friendly title.
    /// </summary>
    /// <param name="title">The title clients should display.</param>
    /// <returns>The builder for chaining.</returns>
    public McpServerBuilder WithTitle(string title)
    {
        _serverInfo.Title = title;
        return this;
    }

    /// <summary>
    /// Configures the server with a specific version.
    /// </summary>
    /// <param name="version">The version of the server</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithVersion(string version)
    {
        _serverInfo.Version = version;
        return this;
    }

    /// <summary>
    /// Configures the server with specific instructions.
    /// </summary>
    /// <param name="instructions">The instructions for the server</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithInstructions(string instructions)
    {
        if (_options == null)
            _options = new ServerOptions();

        _options.Instructions = instructions;
        return this;
    }

    /// <summary>
    /// Configures the server with a specific port (SSE transport only).
    /// </summary>
    /// <param name="port">The port to use</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithPort(int port)
    {
        if (_transportBuilder is SseServerBuilder sseBuilder)
        {
            sseBuilder.WithPort(port);
        }
        else
        {
            throw new InvalidOperationException("WithPort is only valid for SSE transport");
        }

        return this;
    }

    /// <summary>
    /// Configures the server with a specific hostname (SSE transport only).
    /// </summary>
    /// <param name="hostname">The hostname to bind to</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithHostname(string hostname)
    {
        if (_transportBuilder is SseServerBuilder sseBuilder)
        {
            sseBuilder.WithHostname(hostname);
        }
        else
        {
            throw new InvalidOperationException("WithHostname is only valid for SSE transport");
        }

        return this;
    }

    // Removed old authentication methods in favor of WithAuthentication(Action<AuthBuilder>)

    /// <summary>
    /// Configures the server to not use any authentication.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithNoAuth()
    {
        return WithAuthentication(builder => builder.WithNoAuth());
    }

    /// <summary>
    /// Configures authentication using a fluent builder
    /// </summary>
    /// <param name="configure">Action to configure authentication</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// This method supports a clean, fluent approach to authentication configuration
    /// that works consistently with ASP.NET Core's dependency injection and middleware
    /// pipeline. The configured authentication will be automatically wired up
    /// when using the resulting server in an ASP.NET Core application.
    /// </remarks>
    public McpServerBuilder WithAuthentication(Action<AuthBuilder> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        _securityConfigured = true;

        // Create a logger factory if needed
        var loggerFactory = _loggerFactory ?? CreateLoggerFactory();

        // Create and configure the auth builder
        var authBuilder = new AuthBuilder(loggerFactory);
        configure(authBuilder);

        // Get the configured auth handler
        var authHandler = authBuilder.Build();

        // If auth is disabled, mark it as explicitly configured
        if (authBuilder.IsAuthDisabled)
        {
            _noAuthExplicitlyConfigured = true;
            return this;
        }

        // If no handler was configured, don't update anything
        if (authHandler == null)
        {
            return this;
        }

        // Store the resolved options and handler
        _authHandler = authHandler;
        _configuredAuthOptions = authBuilder.ConfiguredOptions;

        // Configure the SSE builder with the same authentication
        if (_transportBuilder is SseServerBuilder sseBuilder)
        {
            sseBuilder.WithAuthentication(builder =>
            {
                if (authHandler != null)
                {
                    builder.WithHandler(authHandler);
                }

                // If auth is disabled, disable it in the SSE builder too
                if (authBuilder.IsAuthDisabled)
                {
                    builder.WithNoAuth();
                }
            });
        }

        if (_configuredAuthOptions != null)
        {
            _services.AddSingleton(typeof(AuthOptions), _configuredAuthOptions);
            if (_configuredAuthOptions is OAuthResourceServerOptions oauthOptions)
            {
                _services.AddSingleton(oauthOptions);
            }
        }

        if (_authHandler != null)
        {
            _services.AddSingleton<IAuthHandler>(_authHandler);
        }

        return this;
    }

    /// <summary>
    /// Configures the server with a specific log level.
    /// </summary>
    /// <param name="level">The log level to use</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithLogLevel(LogLevel level)
    {
        _logLevel = level;
        return this;
    }

    /// <summary>
    /// Configures the server to use console logging.
    /// </summary>
    /// <param name="useConsole">Whether to log to console</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithConsoleLogging(bool useConsole = true)
    {
        _useConsoleLogging = useConsole;
        return this;
    }

    /// <summary>
    /// Configures the server with a specific log file path.
    /// </summary>
    /// <param name="filePath">The path to log to, or null to disable file logging</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithLogFile(string? filePath)
    {
        _logFilePath = filePath;
        return this;
    }

    /// <summary>
    /// Configures common log levels for specific components.
    /// </summary>
    /// <param name="toolsLevel">Log level for tools</param>
    /// <param name="transportLevel">Log level for transport</param>
    /// <param name="jsonRpcLevel">Log level for JSON-RPC</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder ConfigureCommonLogLevels(
        LogLevel toolsLevel = LogLevel.Information,
        LogLevel transportLevel = LogLevel.Information,
        LogLevel jsonRpcLevel = LogLevel.Information
    )
    {
        // This would typically be configured in the logger factory
        // For now, we'll just set the overall log level to the most verbose level
        _logLevel = new[] { toolsLevel, transportLevel, jsonRpcLevel }.Min();
        return this;
    }

    /// <summary>
    /// Configures the server to scan an assembly for tools.
    /// </summary>
    /// <param name="assembly">The assembly to scan</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder ScanToolsFromAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }

        return this;
    }

    /// <summary>
    /// Configures the server to scan an additional assembly for tools.
    /// </summary>
    /// <param name="assembly">The additional assembly to scan</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder ScanAdditionalToolsFromAssembly(Assembly assembly)
    {
        return ScanToolsFromAssembly(assembly);
    }

    /// <summary>
    /// Configures the server to scan an additional assembly for tools (alias for ScanToolsFromAssembly).
    /// </summary>
    /// <param name="assembly">The additional assembly to scan</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithAdditionalAssembly(Assembly assembly)
    {
        return ScanToolsFromAssembly(assembly);
    }

    /// <summary>
    /// Registers additional services for dependency injection.
    /// </summary>
    /// <param name="configureServices">The action to configure services</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder ConfigureServices(Action<IServiceCollection> configureServices)
    {
        configureServices(_services);
        return this;
    }

    /// <summary>
    /// Registers a specific logger factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use</param>
    /// <returns>The builder for chaining</returns>
    public McpServerBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Builds and starts the server asynchronously.
    /// </summary>
    /// <returns>A task representing the running server</returns>
    public async Task<McpServer> StartAsync()
    {
        var server = Build();
        await _transportBuilder.StartAsync(server);
        return server;
    }

    /// <summary>
    /// Builds the server without starting it.
    /// </summary>
    /// <returns>The built server instance</returns>
    public McpServer Build()
    {
        // Set up logging
        var loggerFactory = _loggerFactory ?? CreateLoggerFactory();

        // Set up server info/options
        var serverOptions =
            _options
            ?? new ServerOptions
            {
                Capabilities = new ServerCapabilities
                {
                    Tools = new { },
                    Resources = new { },
                    Prompts = new { },
                },
            };

        if (string.IsNullOrWhiteSpace(_serverInfo.Title))
        {
            _serverInfo.Title = _serverInfo.Name;
        }

        // Use security configuration if needed
        ConfigureSecurity();

        var connectionManager = GetOrCreateConnectionManager(loggerFactory);

        if (_transportBuilder is SseServerBuilder sseBuilder)
        {
            sseBuilder.SetConnectionManager(connectionManager);
        }

        var capabilities = serverOptions.Capabilities ?? new ServerCapabilities();
        serverOptions.Capabilities = capabilities;

        var toolService = new ToolService(capabilities, loggerFactory.CreateLogger<ToolService>());
        var resourceService = new ResourceService(loggerFactory.CreateLogger<ResourceService>());
        var promptService = new PromptService(capabilities, loggerFactory.CreateLogger<PromptService>());
        var completionService = new CompletionService(
            capabilities,
            loggerFactory.CreateLogger<CompletionService>()
        );

        var server = new McpServer(
            _serverInfo,
            connectionManager,
            serverOptions,
            loggerFactory,
            toolService,
            resourceService,
            promptService,
            completionService
        );

        // NOTE: We don't register tools here anymore.
        // Tool registration now happens exclusively in the DI container 
        // via McpServerRegistrationExtensions to ensure all tool registrations happen on the same server instance

        return server;
    }

    /// <summary>
    /// Configures security based on the builder settings.
    /// </summary>
    private void ConfigureSecurity()
    {
        // This method ensures the _securityConfigured and _noAuthExplicitlyConfigured fields are used
        if (_securityConfigured)
        {
            // Authentication explicitly configured
            var logger = (
                _loggerFactory ?? CreateDefaultLoggerFactory()
            ).CreateLogger<McpServerBuilder>();

            if (_noAuthExplicitlyConfigured)
            {
                logger.LogInformation("Authentication explicitly disabled");
            }
            else if (_authHandler != null)
            {
                logger.LogInformation(
                    "Using authentication handler: {HandlerType}",
                    _authHandler.GetType().Name
                );
            }
            else if (_configuredAuthOptions != null)
            {
                logger.LogInformation(
                    "Authentication configured with scheme: {Scheme}",
                    _configuredAuthOptions.SchemeName
                );
            }
        }
    }

    private IConnectionManager GetOrCreateConnectionManager(ILoggerFactory loggerFactory)
    {
        if (_connectionManager != null)
        {
            return _connectionManager;
        }

        _connectionManager = new InMemoryConnectionManager(
            loggerFactory,
            TimeSpan.FromMinutes(30)
        );
        return _connectionManager;
    }

    /// <summary>
    /// Creates a new logger factory with configured settings.
    /// </summary>
    private ILoggerFactory CreateLoggerFactory()
    {
        // If a logger factory was provided, use it
        if (_loggerFactory != null)
        {
            return _loggerFactory;
        }

        // Create options based on builder settings
        var options = new Options.LoggingOptions
        {
            MinimumLogLevel = _logLevel,
            UseConsoleLogging = _useConsoleLogging,
            LogFilePath = _logFilePath ?? "logs/mcp-server.log",
            UseStdio = !_useConsoleLogging,
        };

        // Create a logging provider with our options
        var loggingProvider = new Options.LoggingProvider(options);

        // Create and return the logger factory
        return loggingProvider.CreateLoggerFactory();
    }
}
