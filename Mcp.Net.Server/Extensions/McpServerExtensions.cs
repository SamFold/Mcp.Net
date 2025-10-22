using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Net.Core;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Server.Transport.Sse;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Extensions;

/// <summary>
/// Extension methods for the MCP server.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Options for configuring the MCP server middleware.
    /// </summary>
    public class McpMiddlewareOptions
    {
        /// <summary>
        /// Gets or sets the path for MCP HTTP/SSE endpoint.
        /// </summary>
        public string SsePath { get; set; } = "/mcp";

        /// <summary>
        /// Gets or sets the path for message endpoints.
        /// </summary>
        [Obsolete("MCP now uses a single endpoint. Configure SsePath instead.")]
        public string MessagesPath
        {
            get => SsePath;
            set => SsePath = value;
        }

        /// <summary>
        /// Gets or sets the path for health checks.
        /// </summary>
        public string HealthCheckPath { get; set; } = "/health";

        /// <summary>
        /// Gets or sets whether to enable CORS for all origins.
        /// </summary>
        public bool EnableCors { get; set; } = true;

        /// <summary>
        /// Gets or sets the CORS origins to allow (if empty, all origins are allowed).
        /// </summary>
        public string[]? AllowedOrigins { get; set; }
    }

    /// <summary>
    /// Adds an MCP server to the application middleware pipeline with custom options.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="configure">Action to configure the middleware options</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseMcpServer(
        this IApplicationBuilder app,
        Action<McpMiddlewareOptions>? configure = null
    )
    {
        // Create and configure options
        var options = new McpMiddlewareOptions();
        configure?.Invoke(options);

        // Configure CORS if enabled
        if (options.EnableCors)
        {
            app.UseCors(builder =>
            {
                var corsBuilder = builder.AllowAnyMethod().AllowAnyHeader();

                if (options.AllowedOrigins != null && options.AllowedOrigins.Length > 0)
                {
                    corsBuilder.WithOrigins(options.AllowedOrigins);
                }
                else
                {
                    corsBuilder.AllowAnyOrigin();
                }
            });
        }

        // Add health checks if path is specified
        if (!string.IsNullOrEmpty(options.HealthCheckPath))
        {
            app.UseHealthChecks(options.HealthCheckPath);
        }

        // Add authentication middleware using options from the DI container
        app.UseMiddleware<Authentication.McpAuthenticationMiddleware>();

        // Add unified MCP endpoint
        app.Map(options.SsePath, sseApp => sseApp.UseMiddleware<SseConnectionMiddleware>());

        return app;
    }

    /// <summary>
    /// Registers all tools defined in an assembly with the MCP server
    /// </summary>
    /// <param name="server">The MCP server to register tools with</param>
    /// <param name="assembly">The assembly containing tool classes</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies</param>
    public static void RegisterToolsFromAssembly(
        this McpServer server,
        Assembly assembly,
        IServiceProvider serviceProvider
    )
    {
        // Get logger from service provider if available, otherwise create a temporary one
        ILogger<ToolRegistry> logger;
        if (serviceProvider.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<ToolRegistry>();
        }
        else
        {
            var tempLoggerFactory = new LoggerFactory();
            logger = tempLoggerFactory.CreateLogger<ToolRegistry>();
        }

        // Create a tool registry and register the tools
        var registry = new ToolRegistry(serviceProvider, logger);
        registry.AddAssembly(assembly);
        registry.RegisterToolsWithServer(server);
    }
}
