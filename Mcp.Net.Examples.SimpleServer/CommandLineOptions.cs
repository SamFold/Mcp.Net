using System;
using System.Collections.Generic;

namespace Mcp.Net.Examples.SimpleServer;

/// <summary>
/// Options for configuring the MCP server from command-line arguments
/// </summary>
public class CommandLineOptions
{
    /// <summary>
    /// Gets a value indicating whether to use stdio transport
    /// </summary>
    public bool UseStdio { get; private set; }

    /// <summary>
    /// Gets the port number to use
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Gets the hostname to use
    /// </summary>
    public string? Hostname { get; private set; }

    /// <summary>
    /// Gets the name of the server
    /// </summary>
    public string? ServerName { get; private set; }

    /// <summary>
    /// Gets the paths to assemblies containing tools to load
    /// </summary>
    public string[]? ToolAssemblies { get; private set; }

    /// <summary>
    /// Gets the log level to use
    /// </summary>
    public string? LogLevel { get; private set; }

    /// <summary>
    /// Gets a value indicating whether to disable authentication
    /// </summary>
    public bool NoAuth { get; private set; }

    /// <summary>
    /// Parses command-line arguments into a CommandLineOptions object
    /// </summary>
    /// <param name="args">The command-line arguments</param>
    /// <returns>A new CommandLineOptions object</returns>
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        var toolAssemblies = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? nextArg = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg)
            {
                case "--stdio":
                    options.UseStdio = true;
                    break;

                case "--port":
                    if (nextArg != null && int.TryParse(nextArg, out int port))
                    {
                        options.Port = port;
                        i++;
                    }
                    break;

                case "--hostname":
                    if (nextArg != null)
                    {
                        options.Hostname = nextArg;
                        i++;
                    }
                    break;

                case "--server-name":
                case "--name":
                    if (nextArg != null)
                    {
                        options.ServerName = nextArg;
                        i++;
                    }
                    break;

                case "--tool-assembly":
                case "--assembly":
                    if (nextArg != null)
                    {
                        toolAssemblies.Add(nextArg);
                        i++;
                    }
                    break;

                case "--log-level":
                    if (nextArg != null)
                    {
                        options.LogLevel = nextArg;
                        i++;
                    }
                    break;

                case "--no-auth":
                    options.NoAuth = true;
                    break;
            }
        }

        // Set tool assemblies if any were specified
        if (toolAssemblies.Count > 0)
        {
            options.ToolAssemblies = toolAssemblies.ToArray();
        }

        // Override with environment variables if they exist
        string? envPort = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int parsedPort))
        {
            options.Port = parsedPort;
        }

        string? envHost = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrEmpty(envHost))
        {
            options.Hostname = envHost;
        }

        string? envServerName = Environment.GetEnvironmentVariable("SERVER_NAME");
        if (!string.IsNullOrEmpty(envServerName))
        {
            options.ServerName = envServerName;
        }

        string? envLogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
        if (!string.IsNullOrEmpty(envLogLevel))
        {
            options.LogLevel = envLogLevel;
        }

        // Add MCP_ prefixed environment variables as well
        envPort = Environment.GetEnvironmentVariable("MCP_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out parsedPort))
        {
            options.Port = parsedPort;
        }

        envHost = Environment.GetEnvironmentVariable("MCP_HOSTNAME");
        if (!string.IsNullOrEmpty(envHost))
        {
            options.Hostname = envHost;
        }

        envServerName = Environment.GetEnvironmentVariable("MCP_SERVER_NAME");
        if (!string.IsNullOrEmpty(envServerName))
        {
            options.ServerName = envServerName;
        }

        envLogLevel = Environment.GetEnvironmentVariable("MCP_LOG_LEVEL");
        if (!string.IsNullOrEmpty(envLogLevel))
        {
            options.LogLevel = envLogLevel;
        }

        // Only print options if we're not using stdio
        if (!options.UseStdio)
        {
            Console.WriteLine($"Command-line options:");
            Console.WriteLine($"  Transport: {(options.UseStdio ? "stdio" : "SSE")}");
            Console.WriteLine($"  Port: {options.Port ?? 5000}");
            Console.WriteLine($"  Hostname: {options.Hostname ?? "localhost"}");
            Console.WriteLine($"  Server Name: {options.ServerName ?? "Simple MCP Server"}");
            Console.WriteLine($"  Log Level: {options.LogLevel ?? "Debug"}");
            Console.WriteLine($"  Authentication: {(options.NoAuth ? "Disabled" : "Enabled")}");

            if (options.ToolAssemblies != null && options.ToolAssemblies.Length > 0)
            {
                Console.WriteLine($"  Tool Assemblies:");
                foreach (var assembly in options.ToolAssemblies)
                {
                    Console.WriteLine($"    - {assembly}");
                }
            }
        }

        return options;
    }

    /// <summary>
    /// A specialized parser that never writes to console, for use in stdio mode
    /// </summary>
    public class StrictParser
    {
        /// <summary>
        /// Parses command-line arguments without any console output
        /// </summary>
        public CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            var toolAssemblies = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string? nextArg = i + 1 < args.Length ? args[i + 1] : null;

                switch (arg)
                {
                    case "--stdio":
                        options.UseStdio = true;
                        break;

                    case "--port":
                        if (nextArg != null && int.TryParse(nextArg, out int port))
                        {
                            options.Port = port;
                            i++;
                        }
                        break;

                    case "--hostname":
                        if (nextArg != null)
                        {
                            options.Hostname = nextArg;
                            i++;
                        }
                        break;

                    case "--server-name":
                    case "--name":
                        if (nextArg != null)
                        {
                            options.ServerName = nextArg;
                            i++;
                        }
                        break;

                    case "--tool-assembly":
                    case "--assembly":
                        if (nextArg != null)
                        {
                            toolAssemblies.Add(nextArg);
                            i++;
                        }
                        break;

                    case "--log-level":
                        if (nextArg != null)
                        {
                            options.LogLevel = nextArg;
                            i++;
                        }
                        break;

                    case "--no-auth":
                        options.NoAuth = true;
                        break;
                }
            }

            // Set tool assemblies if any were specified
            if (toolAssemblies.Count > 0)
            {
                options.ToolAssemblies = toolAssemblies.ToArray();
            }

            // Override with environment variables if they exist
            string? envPort = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int parsedPort))
            {
                options.Port = parsedPort;
            }

            string? envHost = Environment.GetEnvironmentVariable("HOSTNAME");
            if (!string.IsNullOrEmpty(envHost))
            {
                options.Hostname = envHost;
            }

            string? envServerName = Environment.GetEnvironmentVariable("SERVER_NAME");
            if (!string.IsNullOrEmpty(envServerName))
            {
                options.ServerName = envServerName;
            }

            string? envLogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
            if (!string.IsNullOrEmpty(envLogLevel))
            {
                options.LogLevel = envLogLevel;
            }

            // Add MCP_ prefixed environment variables as well
            envPort = Environment.GetEnvironmentVariable("MCP_PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out parsedPort))
            {
                options.Port = parsedPort;
            }

            envHost = Environment.GetEnvironmentVariable("MCP_HOSTNAME");
            if (!string.IsNullOrEmpty(envHost))
            {
                options.Hostname = envHost;
            }

            envServerName = Environment.GetEnvironmentVariable("MCP_SERVER_NAME");
            if (!string.IsNullOrEmpty(envServerName))
            {
                options.ServerName = envServerName;
            }

            envLogLevel = Environment.GetEnvironmentVariable("MCP_LOG_LEVEL");
            if (!string.IsNullOrEmpty(envLogLevel))
            {
                options.LogLevel = envLogLevel;
            }

            // IMPORTANT: In strict mode, we NEVER write to console
            options.UseStdio = true; // Force stdio mode to be safe

            return options;
        }
    }
}
