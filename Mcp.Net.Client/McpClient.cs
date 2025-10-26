using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Mcp.Net.Client.Elicitation;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Client;

/// <summary>
/// Base implementation of an MCP client.
/// </summary>
public abstract class McpClient : IMcpClient, IDisposable
{
    /// <summary>
    /// The latest MCP protocol revision supported by this client.
    /// </summary>
    public const string LatestProtocolVersion = "2025-06-18";

    protected readonly ClientInfo _clientInfo;
    private readonly ClientCapabilities _clientCapabilities;
    private static readonly string[] s_supportedProtocolVersions =
    {
        LatestProtocolVersion,
        "2024-11-05",
    };
    protected readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    protected ServerCapabilities? _serverCapabilities;
    private ServerInfo? _serverInfo;
    private string? _instructions;
    private string? _negotiatedProtocolVersion;
    protected readonly ILogger? _logger;
    private IElicitationRequestHandler? _elicitationHandler;
    private IClientTransport? _activeTransport;
    private CancellationTokenSource? _requestCancellationSource;

    // These are implemented as regular fields rather than auto-properties to allow derived classes to invoke them
    private Action<JsonRpcResponseMessage>? _onResponse;
    private Action<JsonRpcNotificationMessage>? _onNotification;
    private Action<Exception>? _onError;
    private Action? _onClose;

    // Expose as events for the interface
    public event Action<JsonRpcResponseMessage>? OnResponse
    {
        add => _onResponse += value;
        remove => _onResponse -= value;
    }

    public event Action<JsonRpcNotificationMessage>? OnNotification
    {
        add => _onNotification += value;
        remove => _onNotification -= value;
    }

    public event Action<Exception>? OnError
    {
        add => _onError += value;
        remove => _onError -= value;
    }

    public event Action? OnClose
    {
        add => _onClose += value;
        remove => _onClose -= value;
    }

    protected McpClient(
        string clientName,
        string clientVersion,
        ILogger? logger = null,
        string? clientTitle = null
    )
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(clientTitle) ? clientName : clientTitle;
        _clientInfo = new ClientInfo
        {
            Name = clientName,
            Version = clientVersion,
            Title = resolvedTitle,
        };
        _clientCapabilities = new ClientCapabilities
        {
            Roots = new RootsCapabilities { ListChanged = true },
            Sampling = new { },
            Elicitation = null,
        };
        _logger = logger;
    }

    /// <summary>
    /// The protocol version negotiated with the server during initialization.
    /// </summary>
    public string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <summary>
    /// Instructions returned by the server during initialization.
    /// </summary>
    public string? Instructions => _instructions;

    /// <summary>
    /// Capabilities advertised by the connected server.
    /// </summary>
    public ServerCapabilities? ServerCapabilities => _serverCapabilities;

    /// <summary>
    /// Information about the connected server.
    /// </summary>
    public ServerInfo? ServerInfo => _serverInfo;

    /// <summary>
    /// The set of protocol revisions recognised by this client.
    /// </summary>
    public IReadOnlyList<string> SupportedProtocolVersions => s_supportedProtocolVersions;

    /// <summary>
    /// Initialize the MCP protocol connection.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task Initialize();

    /// <summary>
    /// Initialize the MCP protocol connection using the specified transport.
    /// </summary>
    /// <param name="transport">The transport to use for the connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task InitializeProtocolAsync(IClientTransport transport)
    {
        _activeTransport = transport ?? throw new ArgumentNullException(nameof(transport));
        ResetRequestCancellationSource();

        // Hook up event handlers
        transport.OnError += RaiseOnError;
        transport.OnClose += HandleTransportClose;
        transport.OnResponse += response => RaiseOnResponse(response);
        transport.OnNotification += notification => RaiseOnNotification(notification);
        transport.OnRequest += HandleTransportRequest;

        // Send initialize request with client info
        var initializeParams = new InitializeRequest
        {
            ProtocolVersion = LatestProtocolVersion,
            Capabilities = _clientCapabilities,
            ClientInfo = _clientInfo,
        };

        _logger?.LogInformation("Sending initialize request to MCP server.");
        var response = await SendRequest("initialize", initializeParams);

        try
        {
            var initializeResponse = DeserializeResponse<InitializeResponse>(response);
            if (initializeResponse != null)
            {
                _logger?.LogDebug(
                    "Initialize response received with protocol {ProtocolVersion}.",
                    initializeResponse.ProtocolVersion
                );
                if (
                    string.IsNullOrWhiteSpace(initializeResponse.ProtocolVersion)
                    || !s_supportedProtocolVersions.Contains(initializeResponse.ProtocolVersion)
                )
                {
                    throw new InvalidOperationException(
                        $"Server negotiated unsupported MCP protocol version \"{initializeResponse?.ProtocolVersion}\"."
                    );
                }

                _serverCapabilities = initializeResponse.Capabilities ?? new ServerCapabilities();
                _serverInfo = initializeResponse.ServerInfo ?? new ServerInfo();
                _instructions = initializeResponse.Instructions;
                _negotiatedProtocolVersion = initializeResponse.ProtocolVersion;
                if (!string.IsNullOrWhiteSpace(_instructions))
                {
                    _logger?.LogDebug("Server instructions: {Instructions}", _instructions);
                }
                _logger?.LogInformation(
                    "Connected to server: {ServerName} {ServerVersion}",
                    initializeResponse.ServerInfo?.Name,
                    initializeResponse.ServerInfo?.Version
                );

                // Send initialized notification
                await SendNotification("notifications/initialized");
            }
            else
            {
                throw new Exception("Invalid initialize response from server");
            }
        }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException)
            {
                throw;
            }

            throw new Exception($"Failed to parse initialization response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Registers the handler responsible for satisfying server-initiated elicitation prompts.
    /// </summary>
    /// <param name="handler">The handler to invoke when an elicitation request arrives.</param>
    public void SetElicitationHandler(IElicitationRequestHandler? handler)
    {
        _elicitationHandler = handler;
        _clientCapabilities.Elicitation = handler != null ? new { } : null;
        if (_negotiatedProtocolVersion != null)
        {
            _logger?.LogWarning(
                "Elicitation handler updated after initialization; the capability advertisement will not change until the next session."
            );
        }
    }


    public async Task<Tool[]> ListTools()
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("tools/list");
        var toolsResponse = DeserializeResponse<ListToolsResponse>(response);
        return toolsResponse?.Tools ?? Array.Empty<Tool>();
    }

    public async Task<ToolCallResult> CallTool(string name, object? arguments = null)
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("tools/call", new { name, arguments });

        var result = DeserializeResponse<ToolCallResult>(response);
        if (result == null)
        {
            throw new Exception("Failed to parse tool response");
        }

        return result;
    }

    public async Task<Resource[]> ListResources()
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("resources/list");
        var resourcesResponse = DeserializeResponse<ResourcesListResponse>(response);
        return resourcesResponse?.Resources ?? Array.Empty<Resource>();
    }

    public async Task<ResourceContent[]> ReadResource(string uri)
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("resources/read", new { uri });
        var resourceResponse = DeserializeResponse<ResourceReadResponse>(response);
        return resourceResponse?.Contents
            ?? throw new Exception("Failed to parse resource response");
    }

    public async Task<Prompt[]> ListPrompts()
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("prompts/list");
        var promptsResponse = DeserializeResponse<PromptsListResponse>(response);
        return promptsResponse?.Prompts ?? Array.Empty<Prompt>();
    }

    public async Task<object[]> GetPrompt(string name)
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        var response = await SendRequest("prompts/get", new { name });
        var promptResponse = DeserializeResponse<PromptsGetResponse>(response);
        return promptResponse?.Messages ?? throw new Exception("Failed to parse prompt response");
    }

    public async Task<CompletionValues> CompleteAsync(
        CompletionReference reference,
        CompletionArgument argument,
        CompletionContext? context = null
    )
    {
        if (_serverCapabilities == null)
        {
            throw new InvalidOperationException("Client not initialized. Call Initialize() first.");
        }

        if (_serverCapabilities.Completions == null)
        {
            throw new InvalidOperationException(
                "Server does not advertise completion support."
            );
        }

        if (reference == null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (argument == null)
        {
            throw new ArgumentNullException(nameof(argument));
        }

        var payload = new CompletionCompleteParams
        {
            Reference = reference,
            Argument = argument,
            Context = context,
        };

        var response = await SendRequest("completion/complete", payload);
        var completionResponse = DeserializeResponse<CompletionCompleteResult>(response)
            ?? throw new Exception("Failed to parse completion response");

        return completionResponse.Completion ?? new CompletionValues();
    }

    /// <summary>
    /// Sends a request to the server and waits for a response.
    /// </summary>
    protected abstract Task<object> SendRequest(string method, object? parameters = null);

    /// <summary>
    /// Sends a notification to the server.
    /// </summary>
    protected abstract Task SendNotification(string method, object? parameters = null);

    /// <summary>
    /// Helper method to deserialize a response object safely
    /// </summary>
    protected T? DeserializeResponse<T>(object response)
    {
        // If it's already the right type, just return it
        if (response is T typedResponse)
            return typedResponse;

        // Otherwise, serialize to JSON and deserialize to the target type
        var json = JsonSerializer.Serialize(response);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    /// <summary>
    /// Invokes the OnError event
    /// </summary>
    protected void RaiseOnError(Exception ex)
    {
        _logger?.LogError(ex, "Client error");
        _onError?.Invoke(ex);
    }

    /// <summary>
    /// Invokes the OnClose event
    /// </summary>
    protected void RaiseOnClose()
    {
        _logger?.LogInformation("Client closed");
        _onClose?.Invoke();
    }

    /// <summary>
    /// Invokes the OnResponse event
    /// </summary>
    protected void RaiseOnResponse(JsonRpcResponseMessage response)
    {
        _logger?.LogDebug("Received response with ID: {Id}", response.Id);
        _onResponse?.Invoke(response);
    }

    /// <summary>
    /// Invokes the OnNotification event.
    /// </summary>
    /// <param name="notification">The notification received from the server.</param>
    protected void RaiseOnNotification(JsonRpcNotificationMessage notification)
    {
        _logger?.LogDebug("Received notification: {Method}", notification.Method);
        _onNotification?.Invoke(notification);
    }

    private void HandleTransportClose()
    {
        _logger?.LogInformation("Transport closed");
        _activeTransport = null;
        if (_requestCancellationSource != null)
        {
            try
            {
                _requestCancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; ignore.
            }
            finally
            {
                _requestCancellationSource.Dispose();
                _requestCancellationSource = null;
            }
        }

        RaiseOnClose();
    }

    private void HandleTransportRequest(JsonRpcRequestMessage request)
    {
        if (request == null)
        {
            return;
        }

        _ = Task.Run(() => ProcessServerRequestAsync(request));
    }

    private async Task ProcessServerRequestAsync(JsonRpcRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            _logger?.LogWarning(
                "Ignoring server request '{Method}' without an identifier.",
                request.Method
            );
            return;
        }

        var transport = _activeTransport;
        if (transport == null)
        {
            _logger?.LogWarning(
                "Received server request '{Method}' but no active transport is available.",
                request.Method
            );
            return;
        }

        JsonRpcResponseMessage response;
        try
        {
            response = await HandleServerRequestAsync(request).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            response = CreateErrorResponse(request, -32800, "Client cancelled the request.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Unhandled exception while processing server request '{Method}'.",
                request.Method
            );
            response = CreateErrorResponse(
                request,
                -32603,
                "Client failed to process the request."
            );
        }

        try
        {
            await transport.SendResponseAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to transmit response for server request '{Method}' ({RequestId}).",
                request.Method,
                request.Id
            );
        }
    }

    private Task<JsonRpcResponseMessage> HandleServerRequestAsync(JsonRpcRequestMessage request)
    {
        if (string.Equals(
                request.Method,
                "elicitation/create",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return HandleElicitationCreateRequestAsync(request);
        }

        _logger?.LogWarning(
            "Server invoked unsupported client method '{Method}'.",
            request.Method
        );
        return Task.FromResult(
            CreateErrorResponse(
                request,
                -32601,
                $"Client does not support method '{request.Method}'."
            )
        );
    }

    private async Task<JsonRpcResponseMessage> HandleElicitationCreateRequestAsync(
        JsonRpcRequestMessage request
    )
    {
        if (_elicitationHandler == null)
        {
            _logger?.LogWarning(
                "Received elicitation request '{RequestId}' but no handler is configured.",
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32601,
                "Client does not support elicitation."
            );
        }

        ElicitationRequestContext context;
        try
        {
            context = new ElicitationRequestContext(request);
        }
        catch (Exception ex) when (
            ex is ArgumentException or JsonException or InvalidOperationException
        )
        {
            _logger?.LogWarning(
                ex,
                "Failed to deserialize elicitation request payload for request {RequestId}.",
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32602,
                "Elicitation request parameters were invalid."
            );
        }

        _logger?.LogInformation(
            "Handling elicitation request '{RequestId}' with message '{Message}'.",
            request.Id,
            context.Message
        );

        var cancellationToken = _requestCancellationSource?.Token ?? CancellationToken.None;
        ElicitationClientResponse? handlerResponse;
        try
        {
            handlerResponse = await _elicitationHandler
                .HandleAsync(context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning(
                "Elicitation handler cancelled request {RequestId}.",
                request.Id
            );
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Elicitation handler threw an exception for request {RequestId}.",
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32603,
                "Elicitation handler failed."
            );
        }

        if (handlerResponse == null)
        {
            _logger?.LogWarning(
                "Elicitation handler returned null for request {RequestId}.",
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32603,
                "Elicitation handler returned no response."
            );
        }

        var normalizedAction = handlerResponse.Action?.Trim().ToLowerInvariant();
        if (normalizedAction != "accept" && normalizedAction != "decline" && normalizedAction != "cancel")
        {
            _logger?.LogWarning(
                "Handler produced unsupported elicitation action '{Action}' for request {RequestId}.",
                handlerResponse.Action,
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32602,
                $"Unsupported elicitation action '{handlerResponse.Action}'."
            );
        }

        if (normalizedAction == "accept" && handlerResponse.Content is null)
        {
            _logger?.LogWarning(
                "Handler accepted elicitation {RequestId} without providing content.",
                request.Id
            );
            return CreateErrorResponse(
                request,
                -32602,
                "Elicitation accept responses must include content."
            );
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = normalizedAction,
        };

        if (handlerResponse.Content is JsonElement contentElement)
        {
            result["content"] = contentElement;
        }

        _logger?.LogInformation(
            "Elicitation request {RequestId} completed with action {Action}.",
            request.Id,
            normalizedAction
        );

        return new JsonRpcResponseMessage("2.0", request.Id, result, null);
    }

    private static JsonRpcResponseMessage CreateErrorResponse(
        JsonRpcRequestMessage request,
        int code,
        string message,
        object? data = null
    )
    {
        return new JsonRpcResponseMessage(
            "2.0",
            request.Id,
            null,
            new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data,
            }
        );
    }

    private void ResetRequestCancellationSource()
    {
        if (_requestCancellationSource != null)
        {
            _requestCancellationSource.Cancel();
            _requestCancellationSource.Dispose();
        }

        _requestCancellationSource = new CancellationTokenSource();
    }

    public abstract void Dispose();
}
