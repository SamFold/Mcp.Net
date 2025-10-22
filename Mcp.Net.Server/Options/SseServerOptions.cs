
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
    /// Gets or sets the path for SSE connections.
    /// </summary>
    public string SsePath { get; set; } = "/sse";

    /// <summary>
    /// Gets or sets the path for message endpoints.
    /// </summary>
    public string MessagesPath { get; set; } = "/messages";

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

    // Backward compatibility properties
    /// <summary>
    /// Gets or sets the authentication handler.
    /// This property is obsolete. Use Authentication.AuthHandler instead.
    /// </summary>
    [Obsolete("Use Authentication.AuthHandler instead")]
    public IAuthHandler? AuthHandler
    {
        get => Authentication.AuthHandler;
        set => Authentication.AuthHandler = value;
    }

    /// <summary>
    /// Gets or sets the API key validator.
    /// This property is obsolete. Use Authentication.ApiKeyValidator instead.
    /// </summary>
    [Obsolete("Use Authentication.ApiKeyValidator instead")]
    public IApiKeyValidator? ApiKeyValidator
    {
        get => Authentication.ApiKeyValidator;
        set => Authentication.ApiKeyValidator = value;
    }

    /// <summary>
    /// Gets or sets the API key options when using API key authentication.
    /// This property is obsolete. Use Authentication as ApiKeyAuthOptions instead.
    /// </summary>
    [Obsolete("Use Authentication as ApiKeyAuthOptions instead")]
    public ApiKeyAuthOptions? ApiKeyOptions
    {
        get => Authentication as ApiKeyAuthOptions;
        set
        {
            if (value != null)
            {
                Authentication = value;
            }
        }
    }

    /// <summary>
    /// Configures the server with the specified API key.
    /// This method is obsolete. Use Authentication.WithApiKey instead.
    /// </summary>
    /// <param name="apiKey">The API key</param>
    /// <returns>The options instance for chaining</returns>
    [Obsolete("Use Authentication.WithApiKey instead")]
    public SseServerOptions WithApiKey(string apiKey)
    {
        if (Authentication is ApiKeyAuthOptions apiKeyAuth)
        {
            apiKeyAuth.WithApiKey(apiKey);
        }
        else
        {
            Authentication = new ApiKeyAuthOptions().WithApiKey(apiKey);
        }
        return this;
    }
}
