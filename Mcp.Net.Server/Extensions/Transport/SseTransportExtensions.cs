using System;
using Mcp.Net.Server.ConnectionManagers;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Transport.Sse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Mcp.Net.Server.Extensions.Transport;

/// <summary>
/// Extension methods for configuring SSE transport services.
/// </summary>
public static class SseTransportExtensions
{
    /// <summary>
    /// Adds SSE transport services to the service collection with options configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Options configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpSseTransport(
        this IServiceCollection services,
        Action<SseServerOptions> configureOptions
    )
    {
        // Register options
        services.Configure(configureOptions);

        // Reuse an existing connection manager when the server registration already created one.
        services.TryAddSingleton<IConnectionManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = sp.GetRequiredService<IOptions<SseServerOptions>>().Value;

            // Create a connection manager with timeout from options or default
            var connectionTimeout = options.ConnectionTimeout.GetValueOrDefault(
                TimeSpan.FromMinutes(30)
            );

            return new InMemoryConnectionManager(loggerFactory, connectionTimeout);
        });

        services.AddSingleton<SseTransportHost>(sp =>
        {
            var server = sp.GetRequiredService<McpServer>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = sp.GetRequiredService<IOptions<SseServerOptions>>().Value;
            var connectionManager = sp.GetRequiredService<IConnectionManager>();

            // Get auth handler from service provider
            var authHandler = sp.GetService<IAuthHandler>();

            return new SseTransportHost(
                server,
                loggerFactory,
                connectionManager,
                authHandler,
                options.AllowedOrigins,
                options.CanonicalOrigin
            );
        });

        // Add transport factory
        services.AddSingleton<ISseTransportFactory, SseTransportFactory>();

        // Register server configuration based on SSE options
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SseServerOptions>>().Value;
            return McpServerConfiguration.FromSseServerOptions(options);
        });

        // Add hosted service
        services.AddHostedService<McpServerHostedService>();

        return services;
    }

    /// <summary>
    /// Adds SSE transport services to the service collection using options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">The SSE server options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpSseTransport(
        this IServiceCollection services,
        SseServerOptions options
    )
    {
        return services.AddMcpSseTransport(opt =>
        {
            opt.Name = options.Name;
            opt.Title = options.Title;
            opt.Version = options.Version;
            opt.Instructions = options.Instructions;
            opt.Logging = CloneLoggingOptions(options.Logging);
            opt.Authentication = CloneAuthOptions(options.Authentication);
            opt.ToolRegistration = CloneToolRegistrationOptions(options.ToolRegistration);
            opt.Hostname = options.Hostname;
            opt.Port = options.Port;
            opt.Scheme = options.Scheme;
            opt.SsePath = options.SsePath;
            opt.HealthCheckPath = options.HealthCheckPath;
            opt.EnableCors = options.EnableCors;
            opt.AllowedOrigins = options.AllowedOrigins?.ToArray();
            opt.CanonicalOrigin = options.CanonicalOrigin;
            opt.ConnectionTimeout = options.ConnectionTimeout;
            opt.ConnectionTimeoutMinutes = options.ConnectionTimeoutMinutes;
            opt.Args = options.Args.ToArray();
            opt.CustomSettings = new Dictionary<string, string>(options.CustomSettings);
            opt.LogLevel = options.LogLevel;
            opt.LogFilePath = options.LogFilePath;
            opt.UseConsoleLogging = options.UseConsoleLogging;
            opt.Capabilities = options.Capabilities;
            opt.ToolAssemblyPaths = new List<string>(options.ToolAssemblyPaths);
            opt.NoAuthExplicitlyConfigured = options.NoAuthExplicitlyConfigured;
        });
    }

    /// <summary>
    /// Adds SSE transport services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="sseBuilder">The SSE transport builder</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMcpSseTransport(
        this IServiceCollection services,
        SseServerBuilder sseBuilder
    )
    {
        // If builder has options, use them directly
        if (sseBuilder.Options != null)
        {
            return services.AddMcpSseTransport(sseBuilder.Options);
        }

        // Otherwise, create options from the builder's properties
        var options = new SseServerOptions
        {
            Hostname = sseBuilder.Hostname,
            Port = sseBuilder.HostPort,
        };

        return services.AddMcpSseTransport(options);
    }

    private static Options.LoggingOptions CloneLoggingOptions(Options.LoggingOptions source)
    {
        return new Options.LoggingOptions
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

    private static Options.ToolRegistrationOptions CloneToolRegistrationOptions(
        Options.ToolRegistrationOptions source
    )
    {
        return new Options.ToolRegistrationOptions
        {
            IncludeEntryAssembly = source.IncludeEntryAssembly,
            Assemblies = new List<System.Reflection.Assembly>(source.Assemblies),
            ValidateToolMethods = source.ValidateToolMethods,
            EnableDetailedLogging = source.EnableDetailedLogging,
        };
    }
}
