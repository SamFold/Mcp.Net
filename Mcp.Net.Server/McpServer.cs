using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Logging;
using Mcp.Net.Server.Services;
using Mcp.Net.Server.Models;
using Mcp.Net.Server.Completions;
using Mcp.Net.Server.Transport.Sse;
using Mcp.Net.Server.Transport.Stdio;
using static Mcp.Net.Core.JsonRpc.JsonRpcMessageExtensions;

public class McpServer : IMcpServer
{
    public const string LatestProtocolVersion = "2025-06-18";

    private static readonly IReadOnlyList<string> s_supportedProtocolVersions = new[]
    {
        LatestProtocolVersion,
        "2024-11-05",
    };

    private readonly Dictionary<string, Func<string, string?, Task<object>>> _methodHandlers = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponseMessage>> _pendingClientRequests = new();
    private readonly IConnectionManager _connectionManager;
    private readonly IToolService _toolService;
    private readonly IResourceService _resourceService;
    private readonly IPromptService _promptService;
    private readonly ICompletionService _completionService;
    private readonly IToolInvocationContextAccessor _toolInvocationContextAccessor;
    private TimeSpan _clientRequestTimeout = TimeSpan.FromSeconds(60);

    private readonly ServerInfo _serverInfo;
    private readonly ServerCapabilities _capabilities;
    private readonly string? _instructions;
    private readonly ILogger<McpServer> _logger;
    private string? _negotiatedProtocolVersion;

    public McpServer(
        ServerInfo serverInfo,
        IConnectionManager connectionManager,
        ServerOptions? options = null,
        IToolService? toolService = null,
        IResourceService? resourceService = null,
        IPromptService? promptService = null,
        ICompletionService? completionService = null,
        IToolInvocationContextAccessor? toolInvocationContextAccessor = null
    )
        : this(
            serverInfo,
            connectionManager,
            options,
            new LoggerFactory(),
            toolService,
            resourceService,
            promptService,
            completionService
        ) { }

    public McpServer(
        ServerInfo serverInfo,
        IConnectionManager connectionManager,
        ServerOptions? options,
        ILoggerFactory loggerFactory,
        IToolService? toolService = null,
        IResourceService? resourceService = null,
        IPromptService? promptService = null,
        ICompletionService? completionService = null,
        IToolInvocationContextAccessor? toolInvocationContextAccessor = null
    )
    {
        _serverInfo = serverInfo;
        if (string.IsNullOrWhiteSpace(_serverInfo.Title))
            _serverInfo.Title = _serverInfo.Name;
        _capabilities = options?.Capabilities ?? new ServerCapabilities();
        _logger = loggerFactory.CreateLogger<McpServer>();
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _toolInvocationContextAccessor =
            toolInvocationContextAccessor ?? new ToolInvocationContextAccessor();

        _toolService =
            toolService
            ?? new ToolService(
                _capabilities,
                loggerFactory.CreateLogger<ToolService>(),
                _toolInvocationContextAccessor
            );
        _resourceService =
            resourceService
            ?? new ResourceService(loggerFactory.CreateLogger<ResourceService>());
        _promptService =
            promptService
            ?? new PromptService(_capabilities, loggerFactory.CreateLogger<PromptService>());
        _completionService =
            completionService
            ?? new CompletionService(
                _capabilities,
                loggerFactory.CreateLogger<CompletionService>()
            );

        // Ensure all capabilities are initialized
        if (_capabilities.Tools == null)
            _capabilities.Tools = new { };

        if (_capabilities.Resources == null)
            _capabilities.Resources = new { };

        if (_capabilities.Prompts == null)
            _capabilities.Prompts = new { };

        _instructions = options?.Instructions;
        InitializeDefaultMethods();

        _logger.LogDebug(
            "McpServer created with server info: {Name} {Version}",
            serverInfo.Name,
            serverInfo.Version
        );
    }

    /// <summary>
    /// Registers a resource that the server can advertise and serve to clients.
    /// </summary>
    /// <param name="resource">Metadata describing the resource.</param>
    /// <param name="reader">Delegate responsible for producing the resource contents when requested.</param>
    /// <param name="overwrite">When true, replaces an existing resource with the same URI.</param>
    public void RegisterResource(
        Resource resource,
        Func<CancellationToken, Task<ResourceContent[]>> reader,
        bool overwrite = false
    ) => _resourceService.RegisterResource(resource, reader, overwrite);

    public void RegisterResource(Resource resource, ResourceContent[] contents, bool overwrite = false) =>
        _resourceService.RegisterResource(resource, contents, overwrite);

    public bool UnregisterResource(string uri) => _resourceService.UnregisterResource(uri);

    /// <summary>
    /// Registers a prompt that the server can advertise to clients.
    /// </summary>
    /// <param name="prompt">Prompt metadata.</param>
    /// <param name="messageFactory">Delegate that builds the prompt messages when requested.</param>
    /// <param name="overwrite">When true, replaces an existing prompt with the same name.</param>
    public void RegisterPrompt(
        Prompt prompt,
        Func<CancellationToken, Task<object[]>> messageFactory,
        bool overwrite = false
    ) => _promptService.RegisterPrompt(prompt, messageFactory, overwrite);

    public void RegisterPrompt(Prompt prompt, object[] messages, bool overwrite = false) =>
        _promptService.RegisterPrompt(prompt, messages, overwrite);

    /// <summary>
    /// Registers a completion handler scoped to a specific prompt.
    /// </summary>
    /// <param name="promptName">The prompt name for which suggestions will be provided.</param>
    /// <param name="handler">The delegate responsible for producing completion suggestions.</param>
    /// <param name="overwrite">Set to <c>true</c> to replace an existing handler.</param>
    public void RegisterPromptCompletion(
        string promptName,
        CompletionHandler handler,
        bool overwrite = false
    ) => _completionService.RegisterPromptCompletion(promptName, handler, overwrite);

    public void RegisterResourceCompletion(
        string resourceUri,
        CompletionHandler handler,
        bool overwrite = false
    ) => _completionService.RegisterResourceCompletion(resourceUri, handler, overwrite);

    /// <summary>
    /// Removes a previously registered prompt.
    /// </summary>
    public bool UnregisterPrompt(string name) => _promptService.UnregisterPrompt(name);

    public static IReadOnlyList<string> SupportedProtocolVersions => s_supportedProtocolVersions;

    public string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <summary>
    /// Gets or sets the default timeout applied to server-initiated client requests.
    /// </summary>
    public TimeSpan ClientRequestTimeout
    {
        get => _clientRequestTimeout;
        set
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Client request timeout must be non-negative or Timeout.InfiniteTimeSpan."
                );
            }

            _clientRequestTimeout = value;
        }
    }

    public async Task ConnectAsync(IServerTransport transport)
    {
        _negotiatedProtocolVersion = null;

        var sessionId = transport.Id();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                "Transport must provide a non-empty identifier."
            );
        }

        _logger.LogInformation(
            "Rigging up event handlers for Transport Id = {TransportId}",
            sessionId
        );

        if (transport is SseTransport)
        {
            _logger.LogDebug(
                "Skipping inbound event wiring for SSE transport {TransportId}",
                sessionId
            );
        }
        else if (transport is StdioTransport stdioTransport)
        {
            stdioTransport.BindServer(this);
            _logger.LogDebug(
                "Skipping inbound event wiring for stdio transport {TransportId}",
                sessionId
            );
        }
        else
        {
            transport.OnRequest += request => HandleRequestWithTransport(transport, request);
            transport.OnNotification += HandleNotification;
            transport.OnResponse += HandleClientResponse;
        }

        transport.OnError += HandleTransportError;
        transport.OnClose += HandleTransportClose;

        _logger.LogInformation("MCP server connecting to transport");

        await _connectionManager
            .RegisterTransportAsync(sessionId, transport)
            .ConfigureAwait(false);

        await transport.StartAsync();
    }

    private async void HandleRequestWithTransport(
        IServerTransport transport,
        JsonRpcRequestMessage request
    )
    {
        using (_logger.BeginRequestScope(request.Id, request.Method))
        {
            var sessionId = transport.Id();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException(
                    "Transport must provide a non-empty identifier."
                );
            }

            _logger.LogDebug(
                "Received request: ID={RequestId}, Method={Method} on Transport={TransportId}",
                request.Id,
                request.Method,
                sessionId
            );
            try
            {
                await ProcessRequestAsync(transport, request, sessionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception processing request: ID={RequestId}, Method={Method}",
                    request.Id,
                    request.Method
                );
            }
        }
    }

    private void HandleNotification(JsonRpcNotificationMessage notification)
    {
        using (
            _logger.BeginScope(new Dictionary<string, object> { ["Method"] = notification.Method })
        )
        {
            _logger.LogDebug("Received notification for method {Method}", notification.Method);
            // Process notifications if needed
        }
    }

    private void HandleClientResponse(JsonRpcResponseMessage response)
    {
        if (response == null)
        {
            return;
        }

        using (_logger.BeginRequestScope(response.Id, "clientResponse"))
        {
            if (_pendingClientRequests.TryRemove(response.Id, out var pendingRequest))
            {
                if (response.Error != null)
                {
                    var message = string.IsNullOrWhiteSpace(response.Error.Message)
                        ? "Client returned an error response."
                        : response.Error.Message;

                    var errorCode = Enum.IsDefined(typeof(ErrorCode), response.Error.Code)
                        ? (ErrorCode)response.Error.Code
                        : ErrorCode.InternalError;

                    _logger.LogWarning(
                        "Client request {RequestId} failed: {Message}",
                        response.Id,
                        message
                    );

                    var exception = new McpException(errorCode, message, response.Error.Data);
                    pendingRequest.TrySetException(exception);
                }
                else
                {
                    pendingRequest.TrySetResult(response);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Received response for unknown or completed client request: {RequestId}",
                    response.Id
                );
            }
        }
    }

    private async Task ProcessRequestAsync(
        IServerTransport transport,
        JsonRpcRequestMessage request,
        string sessionId
    )
    {
        // Use timing scope to automatically log execution time
        using (_logger.BeginRequestScope(request.Id, request.Method))
        using (
            var timing = _logger.BeginTimingScope(
                $"Process{request.Method.Replace("/", "")}Request"
            )
        )
        {
            _logger.LogDebug("Processing request with ID {RequestId}", request.Id);
            var response = await ProcessJsonRpcRequest(request, sessionId);

            var hasError = response.Error != null;
            var logLevel = hasError ? LogLevel.Warning : LogLevel.Debug;

            _logger.Log(
                logLevel,
                "Sending response: ID={RequestId}, HasResult={HasResult}, HasError={HasError}",
                response.Id,
                response.Result != null,
                hasError
            );
            _logger.LogInformation($"Running ProcessRequest on Transport ID: {transport.Id()}");
            await transport.SendAsync(response);
        }
    }

    private async Task<IServerTransport> ResolveTransportAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        var resolved = await _connectionManager
            .GetTransportAsync(sessionId)
            .ConfigureAwait(false);

        if (resolved is IServerTransport serverTransport)
        {
            return serverTransport;
        }

        _logger.LogWarning(
            "Transport not found for session {SessionId}",
            sessionId
        );

        throw new InvalidOperationException(
            $"No active transport found for session '{sessionId}'."
        );
    }

    internal async Task<JsonRpcResponseMessage> SendClientRequestAsync(
        string sessionId,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("Method name must be provided.", nameof(method));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        var transport = await ResolveTransportAsync(sessionId).ConfigureAwait(false);

        var requestId = Guid.NewGuid().ToString("N");
        var requestMessage = new JsonRpcRequestMessage("2.0", requestId, method, parameters);

        var tcs = new TaskCompletionSource<JsonRpcResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        if (!_pendingClientRequests.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException(
                $"Failed to register pending client request with id '{requestId}'."
            );
        }

        _logger.LogDebug(
            "Sending client request {RequestId} for method {Method} via session {SessionId}",
            requestId,
            method,
            sessionId
        );

        try
        {
            await transport.SendRequestAsync(requestMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_pendingClientRequests.TryRemove(requestId, out var pending))
            {
                pending.TrySetException(ex);
            }

            throw;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_clientRequestTimeout != Timeout.InfiniteTimeSpan)
        {
            linkedCts.CancelAfter(_clientRequestTimeout);
        }

        using var registration = linkedCts.Token.Register(() =>
        {
            if (_pendingClientRequests.TryRemove(requestId, out var pending))
            {
                if (!cancellationToken.IsCancellationRequested && _clientRequestTimeout != Timeout.InfiniteTimeSpan)
                {
                    var timeoutException = new McpException(
                        ErrorCode.RequestTimeout,
                        $"Client request '{method}' timed out."
                    );
                    pending.TrySetException(timeoutException);
                }
                else
                {
                    pending.TrySetCanceled(
                        cancellationToken.IsCancellationRequested ? cancellationToken : linkedCts.Token
                    );
                }
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private void HandleTransportError(Exception ex)
    {
        _logger.LogError(ex, "Transport error");
        CancelPendingRequests(ex);
    }

    private void HandleTransportClose()
    {
        _logger.LogInformation("Transport connection closed");
        CancelPendingRequests(new OperationCanceledException("Transport connection closed."));
    }

    /// <summary>
    /// Cancels pending client requests for a specific session when its transport closes.
    /// </summary>
    /// <param name="sessionId">The session associated with the transport that closed.</param>
    public void HandleTransportClosed(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        CancelPendingRequests(new OperationCanceledException($"Transport {sessionId} closed."));
    }

    private void CancelPendingRequests(Exception? reason)
    {
        foreach (var pending in _pendingClientRequests.ToArray())
        {
            if (_pendingClientRequests.TryRemove(pending.Key, out var tcs))
            {
                if (reason != null)
                {
                    tcs.TrySetException(reason);
                }
                else
                {
                    tcs.TrySetCanceled();
                }
            }
        }
    }

    private void InitializeDefaultMethods()
    {
        // Register methods with their strongly-typed request handlers
        RegisterMethod<InitializeRequest>("initialize", HandleInitialize);
        RegisterMethod<ListToolsRequest>("tools/list", HandleToolsList);
        RegisterMethod<ToolCallRequest>("tools/call", HandleToolCall);
        RegisterMethod<ResourcesListRequest>("resources/list", HandleResourcesList);
        RegisterMethod<ResourcesReadRequest>("resources/read", HandleResourcesRead);
        RegisterMethod<PromptsListRequest>("prompts/list", HandlePromptsList);
        RegisterMethod<PromptsGetRequest>("prompts/get", HandlePromptsGet);
        RegisterMethod<CompletionCompleteParams>("completion/complete", HandleCompletionComplete);

        _logger.LogDebug("Default MCP methods registered");
    }

    private Task<object> HandleInitialize(InitializeRequest request)
    {
        _logger.LogInformation("Handling initialize request");

        if (string.IsNullOrWhiteSpace(request.ProtocolVersion))
        {
            _logger.LogWarning("Initialize request missing required protocolVersion");
            throw new McpException(ErrorCode.InvalidParams, "protocolVersion is required");
        }

        var requestedVersion = request.ProtocolVersion;
        string negotiatedVersion;

        if (s_supportedProtocolVersions.Contains(requestedVersion))
        {
            negotiatedVersion = requestedVersion;
        }
        else
        {
            negotiatedVersion = LatestProtocolVersion;
            _logger.LogInformation(
                "Client requested unsupported protocol version {RequestedVersion}; responding with {NegotiatedVersion}",
                requestedVersion,
                negotiatedVersion
            );
        }

        _negotiatedProtocolVersion = negotiatedVersion;

        _logger.LogInformation(
            "Negotiated MCP protocol version {NegotiatedVersion}",
            negotiatedVersion
        );

        return Task.FromResult<object>(
            new
            {
                protocolVersion = negotiatedVersion,
                capabilities = _capabilities,
                serverInfo = _serverInfo,
                instructions = _instructions,
            }
        );
    }

    private Task<object> HandleToolsList(ListToolsRequest _)
    {
        var tools = _toolService.GetTools();
        _logger.LogDebug("Handling tools/list request, returning {Count} tools", tools.Count);
        return Task.FromResult<object>(new { tools });
    }

    private async Task<object> HandleToolCall(
        ToolCallRequest request,
        string? sessionId
    )
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new McpException(
                ErrorCode.InternalError,
                "Tool call received without an active session."
            );
        }

        var result = await _toolService
            .ExecuteAsync(request.Name ?? string.Empty, request.GetArguments(), sessionId)
            .ConfigureAwait(false);
        return result;
    }

    private Task<object> HandleResourcesList(ResourcesListRequest _)
    {
        _logger.LogDebug("Handling resources/list request");
        var resources = _resourceService.ListResources();
        return Task.FromResult<object>(
            new ResourcesListResponse { Resources = resources.ToArray() }
        );
    }

    private async Task<object> HandleResourcesRead(ResourcesReadRequest request)
    {
        _logger.LogDebug("Handling resources/read request");
        var contents = await _resourceService
            .ReadResourceAsync(request.Uri ?? string.Empty)
            .ConfigureAwait(false);
        _logger.LogInformation("Resource read requested for URI: {Uri}", request.Uri);
        return new ResourceReadResponse { Contents = contents };
    }

    private Task<object> HandlePromptsList(PromptsListRequest _)
    {
        _logger.LogDebug("Handling prompts/list request");
        var prompts = _promptService.ListPrompts();
        return Task.FromResult<object>(new PromptsListResponse { Prompts = prompts.ToArray() });
    }

    private async Task<object> HandlePromptsGet(PromptsGetRequest request)
    {
        _logger.LogDebug("Handling prompts/get request");
        var messages = await _promptService
            .GetPromptMessagesAsync(request.Name ?? string.Empty)
            .ConfigureAwait(false);
        _logger.LogInformation("Prompt requested: {Name}", request.Name);
        return new PromptsGetResponse { Messages = messages };
    }

    private async Task<object> HandleCompletionComplete(CompletionCompleteParams request)
    {
        if (_capabilities.Completions == null)
        {
            _logger.LogWarning(
                "Received completion request but server does not advertise completion support."
            );
            throw new McpException(
                ErrorCode.MethodNotFound,
                "Server does not support completion/complete."
            );
        }

        var suggestions = await _completionService
            .CompleteAsync(request, CancellationToken.None)
            .ConfigureAwait(false);

        return new CompletionCompleteResult
        {
            Completion = suggestions,
        };
    }

    /// <summary>
    /// Register a method handler for a specific request type
    /// </summary>
    private void RegisterMethod<TRequest>(string methodName, Func<TRequest, Task<object>> handler)
        where TRequest : IMcpRequest
    {
        RegisterMethod<TRequest>(
            methodName,
            (request, _) => handler(request)
        );
    }

    private void RegisterMethod<TRequest>(
        string methodName,
        Func<TRequest, string?, Task<object>> handler
    )
        where TRequest : IMcpRequest
    {
        // Store a function that takes a JSON string, deserializes it and calls the handler
        _methodHandlers[methodName] = async (jsonParams, sessionId) =>
        {
            try
            {
                // Deserialize the JSON string to our request type
                TRequest? request;
                if (string.IsNullOrEmpty(jsonParams))
                {
                    // Create empty instance for parameter-less requests
                    request = Activator.CreateInstance<TRequest>();
                }
                else
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    request = JsonSerializer.Deserialize<TRequest>(jsonParams, options);
                }

                if (request == null)
                {
                    throw new McpException(
                        ErrorCode.InvalidParams,
                        $"Failed to deserialize parameters for {methodName}"
                    );
                }

                // Call the handler with the typed request
                return await handler(request, sessionId);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error for {MethodName}", methodName);
                throw new McpException(
                    ErrorCode.InvalidParams,
                    $"Invalid parameters: {ex.Message}"
                );
            }
        };

        _logger.LogDebug("Registered method: {MethodName}", methodName);
    }

    public void RegisterTool(
        string name,
        string? description,
        JsonElement inputSchema,
        Func<JsonElement?, Task<ToolCallResult>> handler,
        IDictionary<string, object?>? annotations = null
    ) => _toolService.RegisterTool(name, description, inputSchema, handler, annotations);

    public async Task<JsonRpcResponseMessage> ProcessJsonRpcRequest(
        JsonRpcRequestMessage request,
        string? sessionId = null
    )
    {
        if (!_methodHandlers.TryGetValue(request.Method, out var handler))
        {
            _logger.LogWarning("Method not found: {Method}", request.Method);
            return CreateErrorResponse(request.Id, ErrorCode.MethodNotFound, "Method not found");
        }

        try
        {
            // Convert params to a string for consistent handling
            string paramsJson = "";
            if (request.Params != null)
            {
                // Serialize params object to JSON string for handler
                paramsJson = JsonSerializer.Serialize(request.Params);
            }

            // Call the handler with the JSON string
            var result = await handler(paramsJson, sessionId);

            _logger.LogDebug(
                "Request {Id} ({Method}) handled successfully",
                request.Id,
                request.Method
            );
            // We can pass the result object directly now
            return new JsonRpcResponseMessage("2.0", request.Id, result, null);
        }
        catch (McpException ex)
        {
            _logger.LogWarning(
                "MCP exception handling request {Id} ({Method}): {Message}",
                request.Id,
                request.Method,
                ex.Message
            );
            return CreateErrorResponse(request.Id, ex.Code, ex.Message, ex.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling request {Id} ({Method})",
                request.Id,
                request.Method
            );
            return CreateErrorResponse(request.Id, ErrorCode.InternalError, ex.Message);
        }
    }

    /// <summary>
    /// Entry point for hosts to process a JSON-RPC request with explicit context.
    /// </summary>
    /// <param name="context">Request context containing session, transport, and payload.</param>
    /// <returns>The JSON-RPC response message.</returns>
    public Task<JsonRpcResponseMessage> HandleRequestAsync(ServerRequestContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return ProcessJsonRpcRequest(context.Request, context.SessionId);
    }

    /// <summary>
    /// Entry point for hosts to process a JSON-RPC notification.
    /// </summary>
    /// <param name="context">Notification context containing session, transport, and payload.</param>
    public Task HandleNotificationAsync(ServerRequestContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        HandleNotification(
            new JsonRpcNotificationMessage(context.Request.JsonRpc, context.Request.Method, context.Request.Params)
        );
        return Task.CompletedTask;
    }

    /// <summary>
    /// Entry point for hosts to surface client responses to server-initiated requests.
    /// </summary>
    /// <param name="sessionId">The session that originated the request.</param>
    /// <param name="response">The client response.</param>
    public Task HandleClientResponseAsync(string sessionId, JsonRpcResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier must be provided.", nameof(sessionId));
        }

        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        HandleClientResponse(response);
        return Task.CompletedTask;
    }
}
