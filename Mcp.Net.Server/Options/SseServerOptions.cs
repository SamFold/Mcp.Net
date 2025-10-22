
using System;
using Mcp.Net.Server.Authentication;

namespace Mcp.Net.Server.Options;

/// <summary>
/// Represents options for configuring an SSE-based MCP server.
/// </summary>
public class SseServerOptions : McpServerOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SseServerOptions"/> class.
    /// </summary>
    public SseServerOptions()
    {
        // Initialize the connection timeout from the minutes value
        ConnectionTimeout = TimeSpan.FromMinutes(ConnectionTimeoutMinutes);
    }

    /// <summary>
    /// Gets or sets the hostname to listen on.
    /// </summary>
    public string Hostname { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the port to listen on.
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the URL scheme (http/https).
    /// </summary>
    public string Scheme { get; set; } = "http";


    /// <summary>
    /// Gets the base URL of the server.
    /// </summary>
    public string BaseUrl => $"{Scheme}://{Hostname}:{Port}";

    /// <summary>
    /// Gets or sets the path for MCP HTTP/SSE transport.
    /// </summary>
    public string SsePath { get; set; } = "/mcp";

    /// <summary>
    /// Gets or sets the path for message endpoints.
    /// </summary>
    [Obsolete("MCP now uses a single endpoint. Use SsePath instead.")]
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

    /// <summary>
    /// Gets the canonical origin (scheme + host + port) for the MCP endpoint.
    /// </summary>
    public string? CanonicalOrigin { get; internal set; }

    /// <summary>
    /// Gets or sets the command-line arguments.
    /// </summary>
    public string[] Args { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets custom settings for the server.
    /// </summary>
    public Dictionary<string, string> CustomSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets the connection timeout in minutes.
    /// </summary>
    public int ConnectionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets the connection timeout as a TimeSpan.
    /// </summary>
    public TimeSpan? ConnectionTimeout { get; set; }


    /// <summary>
    /// Validates the options are correctly configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the options are invalid.</exception>
    public void Validate()
    {
        if (Port <= 0)
        {
            throw new InvalidOperationException("Port must be greater than zero");
        }

        if (string.IsNullOrEmpty(Hostname))
        {
            throw new InvalidOperationException("Hostname must not be empty");
        }

        if (Scheme != "http" && Scheme != "https")
        {
            throw new InvalidOperationException("Scheme must be 'http' or 'https'");
        }
    }

}
