using System.Reflection;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Elicitation;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.Extensions.Options;

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

        // Register the McpServer singleton
        services.AddSingleton<McpServer>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("McpServerBuilder");
            var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;

            logger.LogInformation(
                "Building McpServer instance with name: {ServerName}",
                options.Name
            );

            // Create a new builder and configure it from options
            var builder = McpServerBuilder
                .ForSse()
                .WithName(options.Name)
                .WithVersion(options.Version);

            if (options.Instructions != null)
            {
                builder.WithInstructions(options.Instructions);
            }

            // Add tool assemblies if specified
            foreach (var path in options.ToolAssemblyPaths)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(path);
                    builder.ScanToolsFromAssembly(assembly);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load assembly from path: {Path}", path);
                }
            }

            var server = builder.Build();
            return server;
        });

        services.AddSingleton<IElicitationService, ElicitationService>();

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
