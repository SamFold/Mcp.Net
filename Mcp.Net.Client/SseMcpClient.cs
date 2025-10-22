using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Transport;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Client;

/// <summary>
/// An MCP client that communicates with a server using Server-Sent Events (SSE).
/// </summary>
public class SseMcpClient : McpClient
{
    private readonly SseClientTransport _transport;
    private readonly string? _apiKey;

    /// <summary>
    /// Initializes a new instance of the SseMcpClient class with a specified URL.
    /// </summary>
    /// <param name="serverUrl">The URL of the server.</param>
    /// <param name="clientName">The name of the client.</param>
    /// <param name="clientVersion">The version of the client.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="logger">Optional logger for client events.</param>
    /// <param name="clientTitle">Optional display title reported during initialize.</param>
    /// <param name="tokenProvider">Optional OAuth token provider used to satisfy bearer challenges.</param>
    public SseMcpClient(
        string serverUrl,
        string clientName = "SseClient",
        string clientVersion = "1.0.0",
        string? apiKey = null,
        ILogger? logger = null,
        string? clientTitle = null,
        IOAuthTokenProvider? tokenProvider = null
    )
        : base(clientName, clientVersion, logger, clientTitle)
    {
        _apiKey = apiKey;
        _transport = new SseClientTransport(serverUrl, logger, apiKey, tokenProvider);
        InitializeTransport();
    }

    /// <summary>
    /// Initializes a new instance of the SseMcpClient class with a specified HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="clientName">The name of the client.</param>
    /// <param name="clientVersion">The version of the client.</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="logger">Optional logger for client events.</param>
    /// <param name="clientTitle">Optional display title reported during initialize.</param>
    /// <param name="tokenProvider">Optional OAuth token provider used to satisfy bearer challenges.</param>
    public SseMcpClient(
        HttpClient httpClient,
        string clientName = "SseClient",
        string clientVersion = "1.0.0",
        string? apiKey = null,
        ILogger? logger = null,
        string? clientTitle = null,
        IOAuthTokenProvider? tokenProvider = null
    )
        : base(clientName, clientVersion, logger, clientTitle)
    {
        _apiKey = apiKey;
        _transport = new SseClientTransport(httpClient, logger, apiKey, tokenProvider);
        InitializeTransport();
    }

    private void InitializeTransport()
    {
        // Wire up events from the transport
        _transport.OnError += (ex) => RaiseOnError(ex);
        _transport.OnClose += () => RaiseOnClose();
    }

    /// <summary>
    /// Initializes the client by establishing a connection to the server.
    /// </summary>
    public override async Task Initialize()
    {
        _logger?.LogInformation("Initializing SseMcpClient...");

        // Start the transport
        await _transport.StartAsync();

        // Initialize the protocol
        await InitializeProtocolAsync(_transport);
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
        _logger?.LogInformation("Disposing SseMcpClient...");
        _transport.CloseAsync().GetAwaiter().GetResult();
    }
}
