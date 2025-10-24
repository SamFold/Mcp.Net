using System.Diagnostics;
using Mcp.Net.Client.Transport;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Client;

/// <summary>
/// An MCP client that communicates with a server using standard input/output streams.
/// </summary>
public class StdioMcpClient : McpClient
{
    private readonly StdioClientTransport _transport;

    /// <summary>
    /// Initializes a new instance of the StdioMcpClient class using system console streams.
    /// </summary>
    /// <param name="clientName">The name of the client.</param>
    /// <param name="clientVersion">The version of the client.</param>
    /// <param name="logger">Optional logger for client events.</param>
    /// <param name="clientTitle">Optional display title reported during initialize.</param>
    public StdioMcpClient(
        string clientName = "StdioClient",
        string clientVersion = "1.0.0",
        ILogger? logger = null,
        string? clientTitle = null
    )
        : base(clientName, clientVersion, logger, clientTitle)
    {
        _transport = new StdioClientTransport(logger);
        InitializeTransport();
    }

    /// <summary>
    /// Initializes a new instance of the StdioMcpClient class using custom IO streams.
    /// </summary>
    /// <param name="inputStream">The input stream to read from.</param>
    /// <param name="outputStream">The output stream to write to.</param>
    /// <param name="clientName">The name of the client.</param>
    /// <param name="clientVersion">The version of the client.</param>
    /// <param name="logger">Optional logger for client events.</param>
    /// <param name="clientTitle">Optional display title reported during initialize.</param>
    public StdioMcpClient(
        Stream inputStream,
        Stream outputStream,
        string clientName = "StdioClient",
        string clientVersion = "1.0.0",
        ILogger? logger = null,
        string? clientTitle = null
    )
        : base(clientName, clientVersion, logger, clientTitle)
    {
        _transport = new StdioClientTransport(inputStream, outputStream, logger);
        InitializeTransport();
    }

    /// <summary>
    /// Initializes a new instance of the StdioMcpClient class using a server process.
    /// </summary>
    /// <param name="serverCommand">The command to start the server process.</param>
    /// <param name="clientName">The name of the client.</param>
    /// <param name="clientVersion">The version of the client.</param>
    /// <param name="logger">Optional logger for client events.</param>
    /// <param name="clientTitle">Optional display title reported during initialize.</param>
    public StdioMcpClient(
        string serverCommand,
        string clientName = "StdioClient",
        string clientVersion = "1.0.0",
        ILogger? logger = null,
        string? clientTitle = null
    )
        : base(clientName, clientVersion, logger, clientTitle)
    {
        _transport = new StdioClientTransport(serverCommand, logger);
        InitializeTransport();
    }

    private void InitializeTransport()
    {
        // Start the transport
        _transport.StartAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Initializes the client by establishing a connection to the server.
    /// </summary>
    public override async Task Initialize()
    {
        _logger?.LogInformation("Initializing StdioMcpClient...");
        await InitializeProtocolAsync(_transport);
        _transport.UpdateNegotiatedProtocolVersion(NegotiatedProtocolVersion);
    }

    /// <summary>
    /// Sends a request to the server and waits for a response.
    /// </summary>
    protected override async Task<object> SendRequest(string method, object? parameters = null)
    {
        return await _transport.SendRequestAsync(method, parameters);
    }

    /// <summary>
    /// Sends a notification to the server.
    /// </summary>
    protected override async Task SendNotification(string method, object? parameters = null)
    {
        await _transport.SendNotificationAsync(method, parameters);
    }

    /// <summary>
    /// Disposes of resources used by the client.
    /// </summary>
    public override void Dispose()
    {
        _logger?.LogInformation("Disposing StdioMcpClient...");
        _transport.CloseAsync().GetAwaiter().GetResult();
    }
}
