using System;
using System.Threading;
using Mcp.Net.Client.Authentication;
using Mcp.Net.Client.Elicitation;
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
    private string? _apiKey;
    private string? _clientTitle;
    private IOAuthTokenProvider? _tokenProvider;
    private Func<ElicitationRequestContext, CancellationToken, Task<ElicitationClientResponse>>? _elicitationHandler;

    public McpClientBuilder() { }

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
    /// Sets the client title displayed during initialization.
    /// </summary>
    public McpClientBuilder WithTitle(string title)
    {
        _clientTitle = title;
        return this;
    }

    /// <summary>
    /// Configures the client to use a custom OAuth token provider.
    /// </summary>
    public McpClientBuilder WithOAuthTokenProvider(IOAuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
        return this;
    }

    /// <summary>
    /// Configures the client to obtain bearer tokens using the OAuth client credentials grant.
    /// </summary>
    public McpClientBuilder WithClientCredentialsAuth(
        OAuthClientOptions options,
        HttpClient? httpClient = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        _tokenProvider = new ClientCredentialsOAuthTokenProvider(
            options,
            httpClient ?? new HttpClient()
        );
        return this;
    }

    /// <summary>
    /// Configures the client to obtain bearer tokens using the OAuth device authorization flow.
    /// </summary>
    public McpClientBuilder WithDeviceCodeAuth(
        OAuthClientOptions options,
        Func<DeviceCodeInfo, CancellationToken, Task>? onInteraction = null,
        HttpClient? httpClient = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        _tokenProvider = new DeviceCodeOAuthTokenProvider(
            options,
            httpClient ?? new HttpClient(),
            onInteraction
        );
        return this;
    }

    /// <summary>
    /// Configures the client to obtain bearer tokens using the OAuth authorization code flow with PKCE.
    /// </summary>
    public McpClientBuilder WithAuthorizationCodeAuth(
        OAuthClientOptions options,
        Func<
            AuthorizationCodeRequest,
            CancellationToken,
            Task<AuthorizationCodeResult>
        > interactionHandler,
        HttpClient? httpClient = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(interactionHandler);
        _tokenProvider = new AuthorizationCodePkceOAuthTokenProvider(
            options,
            httpClient ?? new HttpClient(),
            interactionHandler
        );
        return this;
    }

    /// <summary>
    /// Sets the API key for authentication.
    /// </summary>
    public McpClientBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    /// <summary>
    /// Registers an elicitation handler that will be installed on the constructed client.
    /// </summary>
    /// <param name="handler">
    /// Delegate invoked whenever the server issues an <c>elicitation/create</c> request. Supply <c>null</c>
    /// to disable elicitation handling.
    /// </param>
    public McpClientBuilder WithElicitationHandler(
        Func<ElicitationRequestContext, CancellationToken, Task<ElicitationClientResponse>>? handler
    )
    {
        _elicitationHandler = handler;
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
        IMcpClient client = _transportType switch
        {
            TransportType.SSE when _httpClient != null => new SseMcpClient(
                _httpClient,
                _clientName,
                _clientVersion,
                _apiKey,
                _logger,
                clientTitle: _clientTitle,
                tokenProvider: _tokenProvider
            ),

            TransportType.SSE when !string.IsNullOrEmpty(_serverUrl) => new SseMcpClient(
                _serverUrl!,
                _clientName,
                _clientVersion,
                _apiKey,
                _logger,
                clientTitle: _clientTitle,
                tokenProvider: _tokenProvider
            ),

            TransportType.ServerCommand when !string.IsNullOrEmpty(_serverCommand) =>
                new StdioMcpClient(
                    _serverCommand!,
                    _clientName,
                    _clientVersion,
                    _logger,
                    _clientTitle
                ),

            TransportType.CustomIO when _inputStream != null && _outputStream != null =>
                new StdioMcpClient(
                    _inputStream,
                    _outputStream,
                    _clientName,
                    _clientVersion,
                    _logger,
                    _clientTitle
                ),

            TransportType.StandardIO => new StdioMcpClient(
                _clientName,
                _clientVersion,
                _logger,
                _clientTitle
            ),

            _ => throw new InvalidOperationException("Transport not properly configured"),
        };

        if (_elicitationHandler != null)
        {
            client.SetElicitationHandler(_elicitationHandler);
        }

        return client;
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
