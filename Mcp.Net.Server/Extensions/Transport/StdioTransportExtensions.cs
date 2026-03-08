using Mcp.Net.Server.Options;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mcp.Net.Server.Extensions.Transport;

/// <summary>
/// Extension methods for configuring STDIO transport services.
/// </summary>
public static class StdioTransportExtensions
{
    /// <summary>
    /// Adds stdio transport services to the service collection with options configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Options configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpStdioTransport(
        this IServiceCollection services,
        Action<StdioServerOptions> configureOptions
    )
    {
        // Register options
        services.Configure(configureOptions);

        // Register hosted service
        services.AddMcpServerHostedService();

        // Register server configuration
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StdioServerOptions>>().Value;
            return new McpServerConfiguration
            {
                Hostname = "localhost", // Default for stdio
            };
        });

        return services;
    }

    /// <summary>
    /// Adds stdio transport services to the service collection using options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">The stdio server options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpStdioTransport(
        this IServiceCollection services,
        StdioServerOptions options
    )
    {
        return services.AddMcpStdioTransport(opt =>
        {
            CopyOptions(options, opt);
        });
    }

    /// <summary>
    /// Adds stdio transport services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="builder">The MCP server builder</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpStdioTransport(
        this IServiceCollection services,
        McpServerBuilder builder
    )
    {
        // Create options from the builder
        var options = new StdioServerOptions
        {
            Name = builder.ConfiguredName,
            Title = builder.ConfiguredTitle ?? builder.ConfiguredName,
            Version = builder.ConfiguredVersion,
            Instructions = builder.ConfiguredInstructions,
            LogLevel = builder.LogLevel,
            LogFilePath = builder.LogFilePath,
            UseConsoleLogging = builder.UseConsoleLogging,
            Capabilities = builder.ConfiguredCapabilities,
        };

        return services.AddMcpStdioTransport(options);
    }

    private static void CopyOptions(StdioServerOptions source, StdioServerOptions destination)
    {
        destination.Name = source.Name;
        destination.Title = source.Title;
        destination.Version = source.Version;
        destination.Instructions = source.Instructions;
        destination.Logging = CloneLoggingOptions(source.Logging);
        destination.Authentication = CloneAuthOptions(source.Authentication);
        destination.ToolRegistration = CloneToolRegistrationOptions(source.ToolRegistration);
        destination.LogLevel = source.LogLevel;
        destination.LogFilePath = source.LogFilePath;
        destination.UseConsoleLogging = source.UseConsoleLogging;
        destination.Capabilities = source.Capabilities;
        destination.NoAuthExplicitlyConfigured = source.NoAuthExplicitlyConfigured;
        destination.ToolAssemblyPaths = new List<string>(source.ToolAssemblyPaths);
        destination.UseStandardIO = source.UseStandardIO;
        destination.InputStream = source.InputStream;
        destination.OutputStream = source.OutputStream;
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
            Assemblies = new List<System.Reflection.Assembly>(source.Assemblies),
            ValidateToolMethods = source.ValidateToolMethods,
            EnableDetailedLogging = source.EnableDetailedLogging,
        };
    }
}
