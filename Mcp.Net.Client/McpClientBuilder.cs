using Mcp.Net.Client.Interfaces;
using Mcp.Net.Client.Transport;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Client;

/// <summary>
/// Builder for creating and configuring MCP clients.
/// </summary>
public class McpClientBuilder
{
    private string _clientName = "McpClient";
    private string _clientVersion = "1.0.0";
    private ILogger? _logger;
    private string? _serverUrl;
    private string? _serverCommand;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private HttpClient? _httpClient;
    private TransportType _transportType = TransportType.SSE;

    /// <summary>
    /// Sets the client name.
    /// </summary>
    public McpClientBuilder WithName(string name)
    {
        _clientName = name;
        return this;
    }

    /// <summary>
    /// Sets the client version.
    /// </summary>
    public McpClientBuilder WithVersion(string version)
    {
        _clientVersion = version;
        return this;
    }

    /// <summary>
    /// Sets the logger for the client.
    /// </summary>
    public McpClientBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Configures the client to use Server-Sent Events transport with the specified URL.
    /// </summary>
    public McpClientBuilder UseSseTransport(string serverUrl)
    {
        _transportType = TransportType.SSE;
        _serverUrl = serverUrl;
        return this;
    }

    /// <summary>
    /// Configures the client to use Server-Sent Events transport with the specified HttpClient.
    /// </summary>
    public McpClientBuilder UseSseTransport(HttpClient httpClient)
    {
        _transportType = TransportType.SSE;
        _httpClient = httpClient;
        return this;
    }

    /// <summary>
    /// Configures the client to use Standard Input/Output transport with the system console.
    /// </summary>
    public McpClientBuilder UseStdioTransport()
    {
        _transportType = TransportType.StandardIO;
        return this;
    }

    /// <summary>
    /// Configures the client to use Standard Input/Output transport with the specified streams.
    /// </summary>
    public McpClientBuilder UseStdioTransport(Stream inputStream, Stream outputStream)
    {
        _transportType = TransportType.CustomIO;
        _inputStream = inputStream;
        _outputStream = outputStream;
        return this;
    }

    /// <summary>
    /// Configures the client to use Standard Input/Output transport with a server process.
    /// </summary>
    public McpClientBuilder UseStdioTransport(string serverCommand)
    {
        _transportType = TransportType.ServerCommand;
        _serverCommand = serverCommand;
        return this;
    }

    /// <summary>
    /// Builds the client with the configured options.
    /// </summary>
    public IMcpClient Build()
    {
        return _transportType switch
        {
            TransportType.SSE when _httpClient != null => new SseMcpClient(
                _httpClient,
                _clientName,
                _clientVersion,
                _logger
            ),

            TransportType.SSE when !string.IsNullOrEmpty(_serverUrl) => new SseMcpClient(
                _serverUrl!,
                _clientName,
                _clientVersion,
                _logger
            ),

            TransportType.ServerCommand when !string.IsNullOrEmpty(_serverCommand) =>
                new StdioMcpClient(_serverCommand!, _clientName, _clientVersion, _logger),

            TransportType.CustomIO when _inputStream != null && _outputStream != null =>
                new StdioMcpClient(
                    _inputStream,
                    _outputStream,
                    _clientName,
                    _clientVersion,
                    _logger
                ),

            TransportType.StandardIO => new StdioMcpClient(_clientName, _clientVersion, _logger),

            _ => throw new InvalidOperationException("Transport not properly configured"),
        };
    }

    /// <summary>
    /// Builds and initializes the client with the configured options.
    /// </summary>
    public async Task<IMcpClient> BuildAndInitializeAsync()
    {
        var client = Build();
        await client.Initialize();
        return client;
    }

    private enum TransportType
    {
        SSE,
        StandardIO,
        CustomIO,
        ServerCommand,
    }
}
