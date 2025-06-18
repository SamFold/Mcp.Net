using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Server;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.ServerBuilder;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Examples.SimpleServer;

class Program
{
    static async Task Main(string[] args)
    {
        // Check if --stdio flag is present before doing ANY console output
        bool useStdio = Array.Exists(args, arg => arg == "--stdio");

        if (useStdio)
        {
            // When using stdio mode, we need to use a special server implementation
            // that prevents ANY console output which would interfere with JSON-RPC
            await StrictStdioServer.RunAsync(args);
            return;
        }

        // Non-stdio mode continues with normal execution
        var options = CommandLineOptions.Parse(args);

        // Set log level based on command line option, or default to Information
        LogLevel logLevel = string.IsNullOrEmpty(options.LogLevel)
            ? LogLevel.Information
            : Enum.Parse<LogLevel>(options.LogLevel);

        // Display all registered tools at startup for easier debugging
        Environment.SetEnvironmentVariable("MCP_DEBUG_TOOLS", "true");

        await RunWithSseTransport(options, logLevel);
    }

    /// <summary>
    /// Simple method to run the server with SSE transport
    /// </summary>
    static async Task RunWithSseTransport(CommandLineOptions options, LogLevel logLevel)
    {
        int port = options.Port ?? 5000;
        Console.WriteLine($"Starting MCP server on port {port}...");

        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(logLevel);

        string hostname =
            options.Hostname
            ?? (Environment.GetEnvironmentVariable("PORT") != null ? "0.0.0.0" : "localhost");

        builder.Services.AddHealthChecks();

        // ========================================================================================
        // AUTHENTICATION CONFIGURATION - OPTION 1 (Recommended)
        // Using the fluent API directly on the McpServerBuilder
        // ========================================================================================

        // Add and configure MCP Server with the updated API
        builder.Services.AddMcpServer(mcpBuilder =>
        {
            // Configure the provided builder - DON'T create a new one!
            mcpBuilder
                .WithName(options.ServerName ?? "Simple MCP Server")
                .WithVersion("1.0.0")
                .WithInstructions("Example server with calculator and Warhammer 40k tools")
                .WithLogLevel(logLevel)
                .WithPort(port)
                .WithHostname(hostname);

            // Add the entry assembly to scan for tools
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                Console.WriteLine($"Scanning entry assembly for tools: {entryAssembly.FullName}");
            }

            // Add external tools assembly
            var externalToolsAssembly =
                typeof(Mcp.Net.Examples.ExternalTools.UtilityTools).Assembly;
            Console.WriteLine(
                $"Adding external tools assembly: {externalToolsAssembly.GetName().Name}"
            );
            mcpBuilder.WithAdditionalAssembly(externalToolsAssembly);

            // Add custom tool assemblies if specified
            if (options.ToolAssemblies != null)
            {
                foreach (var assemblyPath in options.ToolAssemblies)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(assemblyPath);
                        mcpBuilder.WithAdditionalAssembly(assembly);
                        Console.WriteLine($"Added tool assembly: {assembly.GetName().Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load assembly {assemblyPath}: {ex.Message}");
                    }
                }
            }

            // Configure common log levels using the specified log level
            mcpBuilder.ConfigureCommonLogLevels(
                toolsLevel: logLevel,
                transportLevel: logLevel,
                jsonRpcLevel: logLevel
            );

            // Configure authentication based on command line options
            if (options.NoAuth)
            {
                // Disable authentication completely
                mcpBuilder.WithAuthentication(auth => auth.WithNoAuth());
                Console.WriteLine("Authentication disabled via --no-auth flag");
            }
            else
            {
                // Configure standard authentication with API keys
                mcpBuilder.WithAuthentication(auth =>
                {
                    // Configure API key options
                    auth.WithApiKeyOptions(options =>
                    {
                        options.HeaderName = "X-API-Key";
                        options.QueryParamName = "api_key";
                        options.DevelopmentApiKey = "dev-only-api-key"; // Only for dev/testing
                    });

                    // Add API keys with user IDs and claims (use realistic GUIDs for real keys)
                    auth.WithApiKey(
                        "api-f85d077e-4f8a-48c8-b9ff-ec1bb9e1772c", // Real API key example
                        "user1",
                        new Dictionary<string, string> { ["role"] = "admin" }
                    );
                    auth.WithApiKey(
                        "api-2e37dc50-b7a9-4c3d-8a88-99953c99e64b", // Real API key example
                        "user2",
                        new Dictionary<string, string> { ["role"] = "user" }
                    );

                    // Configure secured paths if needed
                    auth.WithSecuredPaths("/sse", "/messages");
                });
            }
        });

        // ========================================================================================
        // AUTHENTICATION CONFIGURATION - OPTION 2 (Alternative)
        // Using the ASP.NET Core DI extensions directly
        // This shows how to use the new ASP.NET Core authentication extensions if you
        // need more control or want to configure authentication separately from the server
        // ========================================================================================

        /* Uncomment this section to use the ASP.NET Core DI extensions approach instead
        
        // Register MCP server without authentication
        builder.Services.AddMcpServer(mcpBuilder =>
        {
            mcpBuilder
                .WithName(options.ServerName ?? "Simple MCP Server")
                .WithVersion("1.0.0")
                .WithInstructions("Example server with calculator and Warhammer 40k tools")
                .WithLogLevel(logLevel)
                .WithPort(port)
                .WithHostname(hostname);
                
            // Add tool assemblies
            mcpBuilder.WithAdditionalAssembly(
                typeof(Mcp.Net.Examples.ExternalTools.UtilityTools).Assembly
            );
        });
        
        // Add API key authentication using the new ASP.NET Core extensions
        builder.Services.AddApiKeyAuthentication(auth =>
        {
            // Configure API key options
            auth.ConfigureOptions(options =>
            {
                options.HeaderName = "X-API-Key";
                options.QueryParamName = "api_key";
                options.DevelopmentApiKey = "dev-only-api-key"; // Only for dev/testing
            });
            
            // Add API keys (use realistic GUIDs for real keys)
            auth.AddApiKey("api-f85d077e-4f8a-48c8-b9ff-ec1bb9e1772c", "user1", new Dictionary<string, string> { ["role"] = "admin" });
            auth.AddApiKey("api-2e37dc50-b7a9-4c3d-8a88-99953c99e64b", "user2", new Dictionary<string, string> { ["role"] = "user" });
            
            // Add secured paths
            auth.AddSecuredPaths("/sse", "/messages");
        });
        
        */

        var app = builder.Build();

        if (options.NoAuth)
        {
            Console.WriteLine("Authentication is DISABLED - no API key required");
        }
        else
        {
            Console.WriteLine("Using API keys for authentication:");
            Console.WriteLine("  - api-f85d077e-4f8a-48c8-b9ff-ec1bb9e1772c (user1, admin)");
            Console.WriteLine("  - api-2e37dc50-b7a9-4c3d-8a88-99953c99e64b (user2, user)");
            Console.WriteLine("  - dev-only-api-key (dev-user) - DEVELOPMENT USE ONLY");
        }

        // Create a cancellation token source for graceful shutdown
        var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("Shutdown signal received, beginning graceful shutdown");
            e.Cancel = true; // Prevent immediate termination
            cancellationSource.Cancel();
        };

        // Use the enhanced configurable middleware from McpServerExtensions
        Mcp.Net.Server.Extensions.McpServerExtensions.UseMcpServer(
            app,
            options =>
            {
                options.SsePath = "/sse";
                options.MessagesPath = "/messages";
                options.HealthCheckPath = "/health";
                options.EnableCors = true;
                // Allow all origins by default, or specify allowed origins
                // options.AllowedOrigins = new[] { "http://localhost:3000", "https://example.com" };
            }
        );

        Console.WriteLine("Health check endpoint enabled at /health");
        Console.WriteLine("SSE endpoint enabled at /sse");
        Console.WriteLine("Messages endpoint enabled at /messages");

        // Display the server URL
        // Updated to use the new type after refactoring
        var config = app.Services.GetRequiredService<Mcp.Net.Server.McpServerConfiguration>();
        Console.WriteLine($"Server started at http://{config.Hostname}:{config.Port}");
        Console.WriteLine("Press Ctrl+C to stop the server.");

        try
        {
            // Use the WebApplication.RunAsync method without the cancellation token
            var serverTask = app.RunAsync($"http://{hostname}:{port}");

            // Wait for cancellation
            await Task.Delay(Timeout.Infinite, cancellationSource.Token);

            // Stop the web application
            await app.StopAsync();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Server shutdown complete");
        }
    }

    /// <summary>
    /// Run the server with stdio transport
    /// </summary>
    static async Task RunWithStdioTransport(CommandLineOptions options, LogLevel logLevel)
    {
        // Set up silent logging (no console output to avoid interfering with stdio transport)
        StreamWriter? logWriter = null;
        string? logFilePath = null;

        try
        {
            // Try to use temp directory for logs (should work in most environments)
            string tempDir = Path.GetTempPath();
            string logsDir = Path.Combine(tempDir, "mcp_logs");

            try
            {
                Directory.CreateDirectory(logsDir);
                logFilePath = Path.Combine(
                    logsDir,
                    $"server_stdio_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );
                logWriter = new StreamWriter(logFilePath, true);
            }
            catch (IOException)
            {
                // If we can't create the directory or file, we'll operate with null logging
                // This ensures the server still works even in restricted environments
            }
        }
        catch
        {
            // Fallback if temp directory access fails
            logWriter = null;
        }

        // Helper method to log silently (to file if available, otherwise no-op)
        void LogToFile(string message)
        {
            if (logWriter != null)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                logWriter.WriteLine($"[{timestamp}] {message}");
                logWriter.Flush();
            }
            // If logWriter is null, this becomes a no-op (silent logging)
        }

        LogToFile("Starting MCP server with stdio transport...");

        // Create a cancellation token source for graceful shutdown
        var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            LogToFile("Shutdown signal received, beginning graceful shutdown");
            e.Cancel = true; // Prevent immediate termination
            cancellationSource.Cancel();
        };

        // Use the new explicit transport selection with builder pattern
        var serverBuilder = McpServerBuilder
            .ForStdio()
            .WithName(options.ServerName ?? "Simple MCP Server")
            .WithVersion("1.0.0")
            .WithInstructions("Example server with calculator and Warhammer 40k tools")
            .WithLogLevel(logLevel);

        // Add the entry assembly to scan for tools
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            LogToFile($"Scanning entry assembly for tools: {entryAssembly.FullName}");
        }

        // Add external tools assembly
        var externalToolsAssembly = typeof(Mcp.Net.Examples.ExternalTools.UtilityTools).Assembly;
        LogToFile($"Adding external tools assembly: {externalToolsAssembly.GetName().Name}");
        serverBuilder.WithAdditionalAssembly(externalToolsAssembly);

        // Add custom tool assemblies if specified
        if (options.ToolAssemblies != null)
        {
            foreach (var assemblyPath in options.ToolAssemblies)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    serverBuilder.WithAdditionalAssembly(assembly);
                    LogToFile($"Added tool assembly: {assembly.GetName().Name}");
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to load assembly {assemblyPath}: {ex.Message}");
                }
            }
        }

        // Configure common log levels using the specified log level
        serverBuilder.ConfigureCommonLogLevels(
            toolsLevel: logLevel,
            transportLevel: logLevel,
            jsonRpcLevel: logLevel
        );

        // Configure authentication based on command line options
        if (options.NoAuth)
        {
            // Disable authentication completely
            serverBuilder.WithAuthentication(auth => auth.WithNoAuth());
            LogToFile("Authentication disabled via --no-auth flag");
        }
        else
        {
            // Configure standard authentication with API keys
            serverBuilder.WithAuthentication(auth =>
            {
                // Configure API key options
                auth.WithApiKeyOptions(options =>
                {
                    options.HeaderName = "X-API-Key";
                    options.QueryParamName = "api_key";
                    options.DevelopmentApiKey = "dev-only-api-key"; // Only for dev/testing
                });

                // Add API keys with user IDs and claims (use realistic GUIDs for real keys)
                auth.WithApiKey(
                    "api-f85d077e-4f8a-48c8-b9ff-ec1bb9e1772c", // Real API key example
                    "user1",
                    new Dictionary<string, string> { ["role"] = "admin" }
                );
                auth.WithApiKey(
                    "api-2e37dc50-b7a9-4c3d-8a88-99953c99e64b", // Real API key example
                    "user2",
                    new Dictionary<string, string> { ["role"] = "user" }
                );

                // Configure secured paths if needed
                auth.WithSecuredPaths("/sse", "/messages");
            });
        }

        if (logFilePath != null)
        {
            LogToFile($"Logs will be written to: {logFilePath}");
            serverBuilder.WithLogFile(logFilePath);
        }
        else
        {
            // When running in a restricted environment, don't configure file logging
            LogToFile("File logging disabled - unable to create log directory");
        }

        // Build the server with all configuration in place
        var server = serverBuilder.Build();

        // Create a ServiceCollection for dependency injection that matches what ASP.NET Core would do
        var services = new ServiceCollection();

        // Configure the logging
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(logLevel);
        });

        // Register a logger factory
        var loggingOptions = new LoggingOptions
        {
            MinimumLogLevel = logLevel,
            UseConsoleLogging = false, // Don't use console output since we're using stdio
            LogFilePath = logFilePath, // This may be null, which is handled gracefully
            UseStdio = true,
        };
        var loggingProvider = new LoggingProvider(loggingOptions);
        var loggerFactory = loggingProvider.CreateLoggerFactory();
        services.AddSingleton<ILoggerFactory>(loggerFactory);

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Create a tool registry to discover and register tools
        var logger = loggerFactory.CreateLogger<ToolRegistry>();
        var toolRegistry = new ToolRegistry(serviceProvider, logger);

        // Add assemblies to the tool registry
        if (entryAssembly != null)
        {
            toolRegistry.AddAssembly(entryAssembly);
            LogToFile($"Adding entry assembly to tool registry: {entryAssembly.GetName().Name}");
        }

        toolRegistry.AddAssembly(externalToolsAssembly);
        LogToFile(
            $"Adding external tools assembly to tool registry: {externalToolsAssembly.GetName().Name}"
        );

        // Add any custom tool assemblies from command line options
        if (options.ToolAssemblies != null)
        {
            foreach (var assemblyPath in options.ToolAssemblies)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    toolRegistry.AddAssembly(assembly);
                    LogToFile(
                        $"Adding custom tool assembly to tool registry: {assembly.GetName().Name}"
                    );
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to load assembly {assemblyPath}: {ex.Message}");
                }
            }
        }

        // Register tools with the server before connecting to the transport
        toolRegistry.RegisterToolsWithServer(server);

        // Get a builder specific to the Stdio transport - we need to use the correct type
        var stdioBuilder = new StdioServerBuilder(loggerFactory);

        LogToFile("Starting server using StdioServerBuilder...");

        // Start the server using the Stdio builder with our pre-configured server
        await stdioBuilder.StartAsync(server);

        LogToFile("Server started with stdio transport");
        LogToFile("Waiting for cancellation token...");

        // Wait for cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            LogToFile("Server shutdown complete");
        }
        finally
        {
            // Properly dispose the log writer if it exists
            if (logWriter != null)
            {
                await logWriter.FlushAsync();
                await logWriter.DisposeAsync();
            }
        }
    }
}
