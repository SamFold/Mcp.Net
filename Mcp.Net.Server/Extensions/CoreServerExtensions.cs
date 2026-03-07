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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<IElicitationServiceFactory>(sp =>
            new ElicitationServiceFactory(
                sp.GetRequiredService<McpServer>(),
                sp.GetService<ILoggerFactory>()
            )
        );

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
        return services.AddMcpCore(opt => CopyOptions(options, opt));
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
        var server = builder.Build();

        // Create options from the builder
        var options = new McpServerOptions
        {
            Name = builder.ConfiguredName,
            Title = builder.ConfiguredTitle ?? builder.ConfiguredName,
            Version = builder.ConfiguredVersion,
            Instructions = builder.ConfiguredInstructions,
            LogLevel = builder.LogLevel,
            UseConsoleLogging = builder.UseConsoleLogging,
            LogFilePath = builder.LogFilePath,
            NoAuthExplicitlyConfigured = builder.AuthHandler is NoAuthenticationHandler,
            Capabilities = builder.ConfiguredCapabilities,
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

        // Register the pre-built server and its shared connection manager.
        services.AddSingleton(server);
        services.TryAddSingleton<IConnectionManager>(server.ConnectionManager);
        services.TryAddSingleton<IElicitationServiceFactory>(sp =>
            new ElicitationServiceFactory(
                sp.GetRequiredService<McpServer>(),
                sp.GetService<ILoggerFactory>()
            )
        );

        // Register the options for other services to use
        services.AddSingleton(options);
        services.Configure<McpServerOptions>(opt =>
        {
            CopyOptions(options, opt);
        });

        return services;
    }

    private static void CopyOptions(McpServerOptions source, McpServerOptions destination)
    {
        destination.Name = source.Name;
        destination.Title = source.Title;
        destination.Version = source.Version;
        destination.Instructions = source.Instructions;
        destination.Logging = CloneLoggingOptions(source.Logging);
        destination.Authentication = CloneAuthOptions(source.Authentication);
        destination.ToolRegistration = CloneToolRegistrationOptions(source.ToolRegistration);
        destination.LogLevel = source.LogLevel;
        destination.UseConsoleLogging = source.UseConsoleLogging;
        destination.LogFilePath = source.LogFilePath;
        destination.NoAuthExplicitlyConfigured = source.NoAuthExplicitlyConfigured;
        destination.ToolAssemblyPaths = new List<string>(source.ToolAssemblyPaths);
        destination.Capabilities = source.Capabilities;
    }

    private static LoggingOptions CloneLoggingOptions(LoggingOptions source)
    {
        return new LoggingOptions
        {
            MinimumLogLevel = source.MinimumLogLevel,
            UseConsoleLogging = source.UseConsoleLogging,
            UseStdio = source.UseStdio,
            LogFilePath = source.LogFilePath,
            PrettyConsoleOutput = source.PrettyConsoleOutput,
            FileRollingInterval = source.FileRollingInterval,
            FileSizeLimitBytes = source.FileSizeLimitBytes,
            RetainedFileCountLimit = source.RetainedFileCountLimit,
            ComponentLogLevels = new Dictionary<string, Microsoft.Extensions.Logging.LogLevel>(
                source.ComponentLogLevels
            ),
        };
    }

    private static AuthOptions CloneAuthOptions(AuthOptions source)
    {
        return new AuthOptions
        {
            Enabled = source.Enabled,
            SchemeName = source.SchemeName,
            SecuredPaths = new List<string>(source.SecuredPaths),
            EnableLogging = source.EnableLogging,
            NoAuthExplicitlyConfigured = source.NoAuthExplicitlyConfigured,
            AuthHandler = source.AuthHandler,
        };
    }

    private static ToolRegistrationOptions CloneToolRegistrationOptions(
        ToolRegistrationOptions source
    )
    {
        return new ToolRegistrationOptions
        {
            IncludeEntryAssembly = source.IncludeEntryAssembly,
            Assemblies = new List<Assembly>(source.Assemblies),
            ValidateToolMethods = source.ValidateToolMethods,
            EnableDetailedLogging = source.EnableDetailedLogging,
        };
    }
}
