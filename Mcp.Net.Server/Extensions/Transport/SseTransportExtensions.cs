using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Transport.Sse;
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

        // Add connection manager
        services.AddSingleton<SseConnectionManager>(sp =>
        {
            var server = sp.GetRequiredService<McpServer>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = sp.GetRequiredService<IOptions<SseServerOptions>>().Value;

            // Create a connection manager with timeout from options or default
            var connectionTimeout = options.ConnectionTimeout.GetValueOrDefault(
                TimeSpan.FromMinutes(30)
            );

            // Get auth handler from service provider
            var authHandler = sp.GetService<IAuthHandler>();

            return new SseConnectionManager(
                server,
                loggerFactory,
                connectionTimeout,
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
            opt.Version = options.Version;
            opt.Instructions = options.Instructions;
            opt.Hostname = options.Hostname;
            opt.Port = options.Port;
            opt.ConnectionTimeout = options.ConnectionTimeout;
            opt.LogLevel = options.LogLevel;
            opt.LogFilePath = options.LogFilePath;
            opt.UseConsoleLogging = options.UseConsoleLogging;
            opt.Capabilities = options.Capabilities;
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
}
