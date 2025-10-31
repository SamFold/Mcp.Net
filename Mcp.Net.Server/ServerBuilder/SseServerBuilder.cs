using System.Linq;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.Transport.Sse;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using SseConnectionManagerType = Mcp.Net.Server.Transport.Sse.SseTransportHost;

namespace Mcp.Net.Server.ServerBuilder;

/// <summary>
/// Builder for creating and configuring an SSE-based MCP server.
/// </summary>
public class SseServerBuilder : IMcpServerBuilder, ITransportBuilder
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SseServerBuilder> _logger;
    private readonly SseServerOptions _options = new();
    private readonly Dictionary<string, string> _customSettings = new();
    private IConfiguration? _configuration;

    /// <summary>
    /// Gets the hostname for the SSE server.
    /// </summary>
    public string Hostname => _options.Hostname;

    /// <summary>
    /// Gets the port for the SSE server.
    /// </summary>
    public int HostPort => _options.Port;

    /// <summary>
    /// Gets the base URL for the SSE server.
    /// </summary>
    public string BaseUrl => _options.BaseUrl;

    /// <summary>
    /// Gets the options configured for this builder.
    /// </summary>
    public SseServerOptions Options => _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseServerBuilder"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    public SseServerBuilder(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<SseServerBuilder>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseServerBuilder"/> class with preconfigured options.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    /// <param name="options">Preconfigured server options</param>
    public SseServerBuilder(ILoggerFactory loggerFactory, SseServerOptions options)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = _loggerFactory.CreateLogger<SseServerBuilder>();
    }

    /// <summary>
    /// Configures the SSE server with a specific hostname.
    /// </summary>
    /// <param name="hostname">The hostname to use</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithHostname(string hostname)
    {
        _options.Hostname = hostname ?? throw new ArgumentNullException(nameof(hostname));
        return this;
    }

    /// <summary>
    /// Configures the SSE server with a specific port.
    /// </summary>
    /// <param name="port">The port to use</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithPort(int port)
    {
        if (port <= 0)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be greater than zero");

        _options.Port = port;
        return this;
    }

    // Removed old authentication methods in favor of WithAuthentication(Action<AuthBuilder>)

    /// <summary>
    /// Configures authentication using a fluent builder for the SSE server
    /// </summary>
    /// <param name="configure">Action to configure authentication</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithAuthentication(Action<AuthBuilder> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        // Create and configure the auth builder
        var authBuilder = new AuthBuilder(_loggerFactory);
        configure(authBuilder);

        // Get the configured auth handler
        var authHandler = authBuilder.Build();

        _options.Authentication = authBuilder.ConfiguredOptions;

        if (authHandler != null)
        {
            _options.Authentication.WithAuthentication(authHandler);
        }

        return this;
    }

    /// <summary>
    /// Configures the SSE server to not use any authentication
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithNoAuth()
    {
        return WithAuthentication(builder => builder.WithNoAuth());
    }

    /// <summary>
    /// Configures the SSE server with command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithArgs(string[] args)
    {
        _options.Args = args ?? Array.Empty<string>();
        return this;
    }

    /// <summary>
    /// Adds a custom setting for the SSE server.
    /// </summary>
    /// <param name="key">The setting key</param>
    /// <param name="value">The setting value</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithSetting(string key, string value)
    {
        _customSettings[key] = value;
        _options.CustomSettings[key] = value;
        return this;
    }

    /// <summary>
    /// Configures the SSE server with the specified options.
    /// </summary>
    /// <param name="options">The options to use</param>
    /// <returns>The builder for chaining</returns>
    public SseServerBuilder WithOptions(SseServerOptions options)
    {
        // Copy values from the provided options to our internal options
        _options.Hostname = options.Hostname;
        _options.Port = options.Port;
        _options.Scheme = options.Scheme;
        _options.Name = options.Name;
        _options.Version = options.Version;
        _options.Instructions = options.Instructions;
        _options.LogLevel = options.LogLevel;
        _options.UseConsoleLogging = options.UseConsoleLogging;
        _options.LogFilePath = options.LogFilePath;
        _options.Authentication = options.Authentication;
        _options.SsePath = options.SsePath;
        _options.MessagesPath = options.MessagesPath;
        _options.HealthCheckPath = options.HealthCheckPath;
        _options.EnableCors = options.EnableCors;
        _options.AllowedOrigins = options.AllowedOrigins;
        _options.ConnectionTimeoutMinutes = options.ConnectionTimeoutMinutes;
        _options.CanonicalOrigin = options.CanonicalOrigin;

        // Copy custom settings
        foreach (var setting in options.CustomSettings)
        {
            _options.CustomSettings[setting.Key] = setting.Value;
            _customSettings[setting.Key] = setting.Value;
        }

        // Copy arguments if any
        if (options.Args.Length > 0)
        {
            _options.Args = options.Args;
        }

        return this;
    }

    /// <inheritdoc />
    public McpServer Build()
    {
        // This should be handled by the main McpServerBuilder
        throw new InvalidOperationException(
            "SseServerBuilder doesn't implement Build directly. Use McpServerBuilder instead."
        );
    }

    /// <inheritdoc />
    public async Task StartAsync(McpServer server)
    {
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        _logger.LogInformation("Starting MCP server with SSE transport on {BaseUrl}", BaseUrl);

        var webApp = ConfigureWebApplication(_options.Args, server);

        var serverConfig = ConfigureServerEndpoints(webApp, _options.Args);

        _logger.LogInformation("Starting web server on {ServerUrl}", serverConfig.ServerUrl);

        // Start the web server
        await webApp.StartAsync();

        _logger.LogInformation("SSE server started successfully");

        // Create a task that completes when the application is stopped
        var shutdownTcs = new TaskCompletionSource<bool>();
        webApp.Lifetime.ApplicationStopping.Register(() => shutdownTcs.TrySetResult(true));

        // Wait for application to stop
        await shutdownTcs.Task;
    }

    /// <inheritdoc />
    public IServerTransport BuildTransport()
    {
        // SSE transport is built and managed by ASP.NET Core integration
        throw new InvalidOperationException(
            "SSE transport is built and managed by ASP.NET Core integration"
        );
    }

    /// <summary>
    /// Configures the web application with services and endpoints.
    /// </summary>
    private WebApplication ConfigureWebApplication(string[] args, McpServer server)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureAppSettings(builder, args);

        ConfigureWebAppLogging(builder);

        ConfigureMcpServices(builder, server);

        var app = builder.Build();

        ConfigureMiddleware(app);
        ConfigureEndpoints(app);

        return app;
    }

    /// <summary>
    /// Configures application settings from various sources.
    /// </summary>
    private void ConfigureAppSettings(WebApplicationBuilder builder, string[] args)
    {
        // Add configuration from multiple sources with priority:
        // 1. Command line args (highest)
        // 2. Environment variables
        // 3. appsettings.json (lowest)
        builder.Configuration.AddJsonFile("appsettings.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("MCP_");
        builder.Configuration.AddCommandLine(args);

        // Add custom settings
        foreach (var setting in _customSettings)
        {
            builder.Configuration[setting.Key] = setting.Value;
        }
    }

    /// <summary>
    /// Configures logging for the web application.
    /// </summary>
    private void ConfigureWebAppLogging(WebApplicationBuilder builder)
    {
        // Use our custom logger factory
        builder.Logging.ClearProviders();
        builder.Logging.Services.AddSingleton(_loggerFactory);
    }

    /// <summary>
    /// Configures MCP services for the web application.
    /// </summary>
    private void ConfigureMcpServices(WebApplicationBuilder builder, McpServer server)
    {
        // Register server in DI
        builder.Services.AddSingleton(server);
        builder.Services.AddSingleton(_options);

        if (_options.Authentication is not null)
        {
            builder.Services.AddSingleton(typeof(AuthOptions), _options.Authentication);

            if (_options.Authentication is OAuthResourceServerOptions oauthOptions)
            {
                builder.Services.AddSingleton(oauthOptions);
            }
        }

        // Register connection managers
        builder.Services.AddSingleton<IConnectionManager, InMemoryConnectionManager>(
            provider => new InMemoryConnectionManager(_loggerFactory, TimeSpan.FromMinutes(30))
        );

        builder.Services.AddSingleton<SseConnectionManagerType>(provider =>
        {
            var connectionManager = provider.GetRequiredService<IConnectionManager>();
            return new SseConnectionManagerType(
                server,
                _loggerFactory,
                connectionManager,
                _options.Authentication?.AuthHandler,
                _options.AllowedOrigins,
                _options.CanonicalOrigin
            );
        });

        // Register authentication if configured
        if (_options.Authentication?.AuthHandler != null)
        {
            builder.Services.AddSingleton(_options.Authentication.AuthHandler);
        }

        // Add health checks
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());

        // Add additional services
        builder.Services.AddCors();

        // Register server configuration
        var logger = _loggerFactory.CreateLogger("ServerConfig");

        // Use the builder's configuration or create a new one
        var config = _configuration ?? builder.Configuration;
        builder.Services.AddSingleton(new Server.ServerConfiguration(config, logger));
    }

    /// <summary>
    /// Configures middleware for the web application.
    /// </summary>
    private void ConfigureMiddleware(WebApplication app)
    {
        // Configure CORS
        app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

        // Add health check endpoints
        app.UseHealthChecks("/health");
        app.UseHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }
        );
        app.UseHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => true });

        // Use MCP server middleware
        app.UseMcpServer();
    }

    /// <summary>
    /// Configures endpoints for the web application.
    /// </summary>
    private void ConfigureEndpoints(WebApplication app)
    {
        if (_options.Authentication is OAuthResourceServerOptions oauthOptions)
        {
            var metadataPath = EnsureLeadingSlash(oauthOptions.ResourceMetadataPath);

            app.MapGet(
                metadataPath,
                async context =>
                {
                    if (string.IsNullOrWhiteSpace(oauthOptions.Resource))
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(
                            new
                            {
                                error = "resource_unavailable",
                                message = "OAuth resource metadata is not configured.",
                            }
                        );
                        return;
                    }

                    var document = new OAuthProtectedResourceMetadataDocument
                    {
                        Resource = oauthOptions.Resource!,
                        AuthorizationServers =
                            oauthOptions.AuthorizationServers.Count > 0
                                ? oauthOptions.AuthorizationServers.ToArray()
                                : null,
                        ResourceName = _options.Name,
                    };

                    await context.Response.WriteAsJsonAsync(document);
                }
            );
        }

        // SSE and message endpoints are configured by UseMcpServer middleware
        _logger.LogDebug("SSE endpoints configured");
    }

    private static string EnsureLeadingSlash(string path) =>
        path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;

    /// <summary>
    /// Configures server endpoints using the configuration.
    /// </summary>
    private Server.ServerConfiguration ConfigureServerEndpoints(WebApplication app, string[] args)
    {
        var serverLogger = _loggerFactory.CreateLogger("ServerConfig");

        // Store configuration for later use
        _configuration = app.Configuration;

        // Create a server configuration using our existing options
        var serverConfig = new Server.ServerConfiguration(
            _options,
            app.Configuration,
            serverLogger
        );

        // Update the configuration with command line args and other sources
        serverConfig.Configure(args);

        var canonicalResource = $"{serverConfig.ServerUrl.TrimEnd('/')}{_options.SsePath}";

        if (Uri.TryCreate(canonicalResource, UriKind.Absolute, out var canonicalUri))
        {
            var canonicalOrigin = canonicalUri.GetLeftPart(UriPartial.Authority);
            _options.CanonicalOrigin = canonicalOrigin;

            if (_options.AllowedOrigins is null || _options.AllowedOrigins.Length == 0)
            {
                _options.AllowedOrigins = new[] { canonicalOrigin };
            }
        }

        if (_options.Authentication is OAuthResourceServerOptions oauthOptions)
        {
            if (string.IsNullOrWhiteSpace(oauthOptions.Resource))
            {
                oauthOptions.Resource = canonicalResource;
            }

            if (
                oauthOptions.ValidateAudience
                && !oauthOptions.ValidAudiences.Any()
                && !string.IsNullOrWhiteSpace(oauthOptions.Resource)
            )
            {
                oauthOptions.AddValidAudience(oauthOptions.Resource);
            }
        }

        return serverConfig;
    }
}
