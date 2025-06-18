using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Net.Server;
using Mcp.Net.Server.Extensions;
using Mcp.Net.Server.ServerBuilder;
using Mcp.Net.Server.Options;
using Mcp.Net.Server.Transport.Stdio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// A special server class for running in strict stdio mode without ANY console output
/// </summary>
internal static class StrictStdioServer
{
    // Hold original streams for restoration if needed
    private static readonly TextWriter OriginalOutWriter = Console.Out;
    private static readonly TextWriter OriginalErrorWriter = Console.Error;
    
    /// <summary>
    /// Completely disables all console output by redirecting stdout and stderr to null streams
    /// </summary>
    private static void DisableAllConsoleOutput()
    {
        try
        {
            // Redirect both standard output and error to null streams
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
            
            // Disable trace output too
            System.Diagnostics.Trace.Listeners.Clear();
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.DefaultTraceListener { LogFileName = null });
        }
        catch
        {
            // Silently ignore any errors - never let this break the protocol
        }
    }
    
    /// <summary>
    /// Runs a server in strict stdio mode where absolutely nothing is written to stdout/stderr
    /// except JSON-RPC protocol messages
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        // CRITICAL: Disable all console output by redirecting standard streams
        // We must do this as early as possible to prevent any internal components
        // from writing to stdout/stderr
        DisableAllConsoleOutput();
        
        // Capture original standard in/out for protocol use
        Stream originalStdin = Console.OpenStandardInput();
        Stream originalStdout = Console.OpenStandardOutput();
        
        // We DO NOT print ANYTHING to stdout/stderr!
        // Create a logger that logs only to file (if possible)
        string? logFilePath = null;
        StreamWriter? logWriter = null;
        
        try
        {
            // Try to set up file logging in temp directory
            string tempDir = Path.GetTempPath();
            string logsDir = Path.Combine(tempDir, "mcp_logs");
            
            try
            {
                Directory.CreateDirectory(logsDir);
                logFilePath = Path.Combine(logsDir, $"mcp_stdio_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                logWriter = new StreamWriter(logFilePath, true);
            }
            catch (Exception)
            {
                // Silently fail - we'll operate without logging
            }
        }
        catch
        {
            // Silently fail - we'll operate without logging
        }
        
        // Helper method for silent logging
        void LogToFile(string message)
        {
            try
            {
                if (logWriter != null)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    logWriter.WriteLine($"[{timestamp}] {message}");
                    logWriter.Flush();
                }
            }
            catch
            {
                // Silently fail - never let logging errors break the protocol
            }
        }

        // Parse args silently
        var options = new CommandLineOptions.StrictParser().Parse(args);
        
        // Use default log level or parsed one
        LogLevel logLevel = string.IsNullOrEmpty(options.LogLevel)
            ? LogLevel.Warning  // Use higher level by default to reduce noise
            : Enum.Parse<LogLevel>(options.LogLevel); 

        LogToFile("Starting MCP server in strict stdio mode");
        
        // Set up a cancellation token source for shutdown
        var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            LogToFile("Received cancellation signal");
            e.Cancel = true;
            cancellationSource.Cancel();
        };

        // Create the server builder
        var serverBuilder = McpServerBuilder
            .ForStdio()
            .WithName(options.ServerName ?? "MCP Server")
            .WithVersion("1.0.0")
            .WithInstructions("Stdio-mode MCP Server with calculator and utility tools")
            .WithLogLevel(logLevel);

        // Track tools for logging only
        var toolAssemblies = new List<Assembly>();
            
        // Add the entry assembly to scan for tools
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            serverBuilder.WithAdditionalAssembly(entryAssembly);
            toolAssemblies.Add(entryAssembly);
        }

        // Add external tools assembly
        try
        {
            var externalToolsAssembly = typeof(Mcp.Net.Examples.ExternalTools.UtilityTools).Assembly;
            serverBuilder.WithAdditionalAssembly(externalToolsAssembly);
            toolAssemblies.Add(externalToolsAssembly);
        }
        catch (Exception ex)
        {
            LogToFile($"Failed to load external tools: {ex.Message}");
        }

        // Add custom tool assemblies if specified
        if (options.ToolAssemblies != null)
        {
            foreach (var assemblyPath in options.ToolAssemblies)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    serverBuilder.WithAdditionalAssembly(assembly);
                    toolAssemblies.Add(assembly);
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to load assembly {assemblyPath}: {ex.Message}");
                }
            }
        }

        // Configure logging - CRITICAL for stdio mode
        serverBuilder.ConfigureCommonLogLevels(
            // Set very high levels for anything that might touch stdout/stderr
            toolsLevel: LogLevel.Error,
            transportLevel: LogLevel.Error, 
            jsonRpcLevel: LogLevel.Error
        );

        // Configure auth
        if (options.NoAuth)
        {
            serverBuilder.WithAuthentication(auth => auth.WithNoAuth());
            LogToFile("Authentication disabled");
        }
        else
        {
            // Set up API key auth
            serverBuilder.WithAuthentication(auth =>
            {
                auth.WithApiKeyOptions(options =>
                {
                    options.HeaderName = "X-API-Key";
                    options.QueryParamName = "api_key";
                    options.DevelopmentApiKey = "dev-only-api-key"; 
                });

                auth.WithApiKey(
                    "api-f85d077e-4f8a-48c8-b9ff-ec1bb9e1772c",
                    "user1",
                    new Dictionary<string, string> { ["role"] = "admin" }
                );
                
                auth.WithApiKey(
                    "api-2e37dc50-b7a9-4c3d-8a88-99953c99e64b",
                    "user2",
                    new Dictionary<string, string> { ["role"] = "user" }
                );

                auth.WithSecuredPaths("/sse", "/messages");
            });
            
            LogToFile("Authentication configured with API keys");
        }

        // Configure file logging if available
        if (logFilePath != null)
        {
            serverBuilder.WithLogFile(logFilePath);
            LogToFile($"Configured file logging to: {logFilePath}");
        }

        // Build the server
        var server = serverBuilder.Build();
        LogToFile("Server built successfully");

        // Set up DI
        var services = new ServiceCollection();
        
        // Configure logging to avoid ANY console output
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Error); // Set very high threshold
        });

        // Set up logging options
        var loggingOptions = new LoggingOptions
        {
            MinimumLogLevel = LogLevel.Critical, // Only log the most severe issues
            UseConsoleLogging = false,        // Critical: NO console logging
            LogFilePath = logFilePath,        // May be null
            UseStdio = true,                  // We're in stdio mode
            PrettyConsoleOutput = false,      // No pretty output (we don't use console)
        };
        
        // VITAL: Set ALL component log levels to Critical to ensure absolutely nothing gets logged
        // This ensures that internal framework components don't try to log to the console
        loggingOptions.ComponentLogLevels = new Dictionary<string, LogLevel>
        {
            ["Microsoft"] = LogLevel.Critical,
            ["System"] = LogLevel.Critical,
            ["Mcp"] = LogLevel.Critical, 
            ["Mcp.Net"] = LogLevel.Critical,
            ["Default"] = LogLevel.Critical
        };
        
        var loggingProvider = new LoggingProvider(loggingOptions);
        var loggerFactory = loggingProvider.CreateLoggerFactory();
        
        // Extra safety: Make sure Serilog's global logger doesn't output anything to console
        try 
        {
            // Ensure the static Serilog.Log class has no console sinks
            Serilog.Log.CloseAndFlush();
        }
        catch
        {
            // Ignore any errors - we're just being extra cautious
        }
        services.AddSingleton<ILoggerFactory>(loggerFactory);

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Create and configure tool registry
        var logger = loggerFactory.CreateLogger<ToolRegistry>();
        var toolRegistry = new ToolRegistry(serviceProvider, logger);

        // Add assemblies to tool registry
        foreach (var assembly in toolAssemblies)
        {
            toolRegistry.AddAssembly(assembly);
            LogToFile($"Added assembly to tool registry: {assembly.GetName().Name}");
        }

        // Register tools with server
        toolRegistry.RegisterToolsWithServer(server);
        LogToFile("Tools registered with server");

        // Create our own custom StdioTransport to ensure we use the original stdin/stdout
        LogToFile("Creating custom StdioTransport with explicit streams");
        var transport = new Mcp.Net.Server.Transport.Stdio.StdioTransport(
            originalStdin,  // Use the captured stdin 
            originalStdout, // Use the captured stdout
            loggerFactory.CreateLogger<Mcp.Net.Server.Transport.Stdio.StdioTransport>()
        );
        
        try
        {
            // Connect directly to the transport instead of using StdioServerBuilder
            LogToFile("Connecting server directly to transport");
            await server.ConnectAsync(transport);
            LogToFile("Server started successfully");
            
            // Create a task that completes when the transport closes
            var transportTask = new TaskCompletionSource<bool>();
            transport.OnClose += () => transportTask.TrySetResult(true);
            
            // Wait for shutdown
            await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationSource.Token),
                transportTask.Task
            );
        }
        catch (OperationCanceledException)
        {
            LogToFile("Server shutdown due to cancellation");
        }
        catch (Exception ex)
        {
            LogToFile($"Server error: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (logWriter != null)
            {
                try
                {
                    LogToFile("Closing log file");
                    await logWriter.FlushAsync();
                    await logWriter.DisposeAsync();
                }
                catch
                {
                    // Silently fail - never let cleanup errors break the protocol
                }
            }
        }
    }
}