using System.Reflection;
using Mcp.Net.Core.Models;
using Mcp.Net.Core.Models.Capabilities;
using Microsoft.Extensions.Logging;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Services;
using Microsoft.Extensions.Options;
using Mcp.Net.Server.Interfaces;

namespace Mcp.Net.Server.Extensions;

/// <summary>
/// Extension methods for adding and configuring the MCP server core functionality.
/// </summary>
public static class CoreServerExtensions
{
    /// <summary>
    /// Adds an MCP server to the application middleware pipeline with default options.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseMcpServer(this IApplicationBuilder app)
    {
        // Use the configurable version from McpServerExtensions
        return McpServerExtensions.UseMcpServer(app);
    }

    /// <summary>
    /// Adds MCP core services to the service collection with options configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Options configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpCore(
        this IServiceCollection services,
        Action<McpServerOptions> configureOptions
    )
    {
        // Register options
        services.Configure(configureOptions);

        services.AddSingleton<IConnectionManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new InMemoryConnectionManager(loggerFactory);
        });

        services.AddSingleton<ServerCapabilities>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;
            opts.Capabilities ??= new ServerCapabilities();
            return opts.Capabilities;
        });

        services.AddSingleton<IToolService>(sp =>
            new ToolService(
                sp.GetRequiredService<ServerCapabilities>(),
                sp.GetRequiredService<ILogger<ToolService>>()
            )
        );

        services.AddSingleton<IResourceService>(sp =>
            new ResourceService(sp.GetRequiredService<ILogger<ResourceService>>())
        );

        services.AddSingleton<IPromptService>(sp =>
            new PromptService(
                sp.GetRequiredService<ServerCapabilities>(),
                sp.GetRequiredService<ILogger<PromptService>>()
            )
        );

        services.AddSingleton<ICompletionService>(sp =>
            new CompletionService(
                sp.GetRequiredService<ServerCapabilities>(),
                sp.GetRequiredService<ILogger<CompletionService>>()
            )
        );
        services.AddSingleton<IElicitationService, ElicitationService>();

        services.AddSingleton<McpServer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var connectionManager = sp.GetRequiredService<IConnectionManager>();
            var capabilities = sp.GetRequiredService<ServerCapabilities>();
            var toolService = sp.GetRequiredService<IToolService>();
            var resourceService = sp.GetRequiredService<IResourceService>();
            var promptService = sp.GetRequiredService<IPromptService>();
            var completionService = sp.GetRequiredService<ICompletionService>();

            return new McpServer(
                new ServerInfo
                {
                    Name = options.Name,
                    Title = options.Name,
                    Version = options.Version,
                },
                connectionManager,
                new ServerOptions
                {
                    Instructions = options.Instructions,
                    Capabilities = capabilities,
                },
                loggerFactory,
                toolService,
                resourceService,
                promptService,
                completionService
            );
        });

        return services;
    }

    /// <summary>
    /// Adds MCP core services to the service collection using options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">The server options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpCore(
        this IServiceCollection services,
        McpServerOptions options
    )
    {
        return services.AddMcpCore(opt =>
        {
            opt.Name = options.Name;
            opt.Version = options.Version;
            opt.Instructions = options.Instructions;
            opt.LogLevel = options.LogLevel;
            opt.UseConsoleLogging = options.UseConsoleLogging;
            opt.LogFilePath = options.LogFilePath;
            opt.NoAuthExplicitlyConfigured = options.NoAuthExplicitlyConfigured;
            opt.ToolAssemblyPaths = new List<string>(options.ToolAssemblyPaths);
            opt.Capabilities = options.Capabilities;
        });
    }

    /// <summary>
    /// Adds MCP core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="builder">The MCP server builder</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpCore(
        this IServiceCollection services,
        McpServerBuilder builder
    )
    {
        // Create options from the builder
        var options = new McpServerOptions
        {
            Name = "MCP Server", // Default name
            Version = "1.0.0", // Default version
            Instructions = null,
            LogLevel = builder.LogLevel,
            UseConsoleLogging = builder.UseConsoleLogging,
            LogFilePath = builder.LogFilePath,
            NoAuthExplicitlyConfigured = builder.AuthHandler is NoAuthenticationHandler,
            Capabilities = null,
        };

        // Add assemblies from the builder
        foreach (var assembly in builder._assemblies)
        {
            try
            {
                var assemblyPath = assembly.Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    options.ToolAssemblyPaths.Add(assemblyPath);
                }
            }
            catch (Exception)
            {
                // Ignore assemblies without a valid location
            }
        }

        // Register the server directly
        services.AddSingleton<McpServer>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("McpServerBuilder");

            logger.LogInformation("Building McpServer instance");
            var server = builder.Build();
            return server;
        });

        services.AddSingleton<IElicitationService, ElicitationService>();

        // Register the options for other services to use
        services.AddSingleton(options);
        services.Configure<McpServerOptions>(opt =>
        {
            opt.Name = options.Name;
            opt.Version = options.Version;
            opt.Instructions = options.Instructions;
            opt.LogLevel = options.LogLevel;
            opt.UseConsoleLogging = options.UseConsoleLogging;
            opt.LogFilePath = options.LogFilePath;
            opt.NoAuthExplicitlyConfigured = options.NoAuthExplicitlyConfigured;
            opt.ToolAssemblyPaths = new List<string>(options.ToolAssemblyPaths);
            opt.Capabilities = options.Capabilities;
        });

        return services;
    }
}
