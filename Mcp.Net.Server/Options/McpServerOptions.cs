using System;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Server.Authentication;

namespace Mcp.Net.Server.Options;

/// <summary>
/// Represents the base options for configuring an MCP server.
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// Gets or sets the name of the server.
    /// </summary>
    public string Name { get; set; } = "MCP Server";

    /// <summary>
    /// Gets or sets the version of the server.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the instructions for using the server.
    /// </summary>
    public string? Instructions { get; set; }


    /// <summary>
    /// Gets or sets the logging configuration.
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// Gets or sets the authentication configuration.
    /// </summary>
    public AuthOptions Authentication { get; set; } = new();

    /// <summary>
    /// Gets or sets the tool registration configuration.
    /// </summary>
    public ToolRegistrationOptions ToolRegistration { get; set; } = new();

    /// <summary>
    /// Gets or sets additional server capabilities.
    /// </summary>
    public ServerCapabilities? Capabilities { get; set; }

    // Backward compatibility properties
    /// <summary>
    /// Gets or sets whether authentication is explicitly disabled.
    /// This property is obsolete. Use Authentication.NoAuthExplicitlyConfigured instead.
    /// </summary>
    [Obsolete("Use Authentication.NoAuthExplicitlyConfigured instead")]
    public bool NoAuthExplicitlyConfigured
    {
        get => Authentication.NoAuthExplicitlyConfigured;
        set => Authentication.NoAuthExplicitlyConfigured = value;
    }

    /// <summary>
    /// Gets or sets the list of assembly paths to scan for tools.
    /// This property is obsolete. Use ToolRegistration.Assemblies instead.
    /// </summary>
    [Obsolete("Use ToolRegistration.Assemblies instead")]
    public List<string> ToolAssemblyPaths { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum log level for the server.
    /// This property is obsolete. Use Logging.MinimumLogLevel instead.
    /// </summary>
    [Obsolete("Use Logging.MinimumLogLevel instead")]
    public Microsoft.Extensions.Logging.LogLevel LogLevel
    {
        get => Logging.MinimumLogLevel;
        set => Logging.MinimumLogLevel = value;
    }

    /// <summary>
    /// Gets or sets whether console logging is enabled.
    /// This property is obsolete. Use Logging.UseConsoleLogging instead.
    /// </summary>
    [Obsolete("Use Logging.UseConsoleLogging instead")]
    public bool UseConsoleLogging
    {
        get => Logging.UseConsoleLogging;
        set => Logging.UseConsoleLogging = value;
    }

    /// <summary>
    /// Gets or sets the path to the log file.
    /// This property is obsolete. Use Logging.LogFilePath instead.
    /// </summary>
    [Obsolete("Use Logging.LogFilePath instead")]
    public string? LogFilePath
    {
        get => Logging.LogFilePath;
        set => Logging.LogFilePath = value;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="ServerOptions"/> class with default capabilities.
    /// </summary>
    /// <returns>A configured ServerOptions instance</returns>
    public ServerOptions ToServerOptions()
    {
        return new ServerOptions
        {
            Instructions = Instructions,
            Capabilities =
                Capabilities
                ?? new ServerCapabilities
                {
                    Tools = new { },
                    Resources = new { },
                    Prompts = new { },
                },
        };
    }

    /// <summary>
    /// Creates a new instance of the <see cref="ServerInfo"/> class with the configured name and version.
    /// </summary>
    /// <returns>A configured ServerInfo instance</returns>
    public ServerInfo ToServerInfo()
    {
        return new ServerInfo { Name = Name, Version = Version };
    }
}
