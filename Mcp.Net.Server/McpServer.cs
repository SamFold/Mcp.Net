using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.JsonRpc;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using static Mcp.Net.Core.JsonRpc.JsonRpcMessageExtensions;

public class McpServer : IMcpServer
{
    public const string LatestProtocolVersion = "2025-06-18";

    private static readonly IReadOnlyList<string> s_supportedProtocolVersions = new[]
    {
        LatestProtocolVersion,
        "2024-11-05",
    };

    // Dictionary to store method handlers that take a JSON string parameter
    private readonly Dictionary<string, Func<string, Task<object>>> _methodHandlers = new();
    private readonly Dictionary<string, Tool> _tools = new();
    private readonly Dictionary<string, Func<JsonElement?, Task<ToolCallResult>>> _toolHandlers =
        new();
    private readonly Dictionary<string, ResourceRegistration> _resources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resourceOrder = new();
    private readonly Dictionary<string, PromptRegistration> _prompts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _promptOrder = new();
    private readonly object _resourceLock = new();
    private readonly object _promptLock = new();
    private IServerTransport? _transport;

    private readonly ServerInfo _serverInfo;
    private readonly ServerCapabilities _capabilities;
    private readonly string? _instructions;
    private readonly ILogger<McpServer> _logger;
    private string? _negotiatedProtocolVersion;

    public McpServer(ServerInfo serverInfo, ServerOptions? options = null)
        : this(serverInfo, options, new LoggerFactory()) { }

    public McpServer(ServerInfo serverInfo, ServerOptions? options, ILoggerFactory loggerFactory)
    {
        _serverInfo = serverInfo;
        if (string.IsNullOrWhiteSpace(_serverInfo.Title))
            _serverInfo.Title = _serverInfo.Name;
        _capabilities = options?.Capabilities ?? new ServerCapabilities();
        _logger = loggerFactory.CreateLogger<McpServer>();

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
    )
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (string.IsNullOrWhiteSpace(resource.Uri))
        {
            throw new ArgumentException("Resource URI must be specified.", nameof(resource));
        }

        lock (_resourceLock)
        {
            var key = resource.Uri;
            if (_resources.ContainsKey(key))
            {
                if (!overwrite)
                {
                    throw new InvalidOperationException(
                        $"Resource '{resource.Uri}' is already registered."
                    );
                }
            }
            else
            {
                _resourceOrder.Add(key);
            }

            _resources[key] = new ResourceRegistration(CloneResource(resource), reader);
        }

        _logger.LogInformation("Registered resource: {Uri}", resource.Uri);
    }

    /// <summary>
    /// Registers a resource with static content.
    /// </summary>
    public void RegisterResource(Resource resource, ResourceContent[] contents, bool overwrite = false)
    {
        if (contents == null)
        {
            throw new ArgumentNullException(nameof(contents));
        }

        RegisterResource(resource, _ => Task.FromResult(contents), overwrite);
    }

    /// <summary>
    /// Removes a previously registered resource.
    /// </summary>
    public bool UnregisterResource(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Resource URI must be specified.", nameof(uri));
        }

        lock (_resourceLock)
        {
            var removed = _resources.Remove(uri);
            if (removed)
            {
                _resourceOrder.Remove(uri);
            }

            return removed;
        }
    }

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
    )
    {
        if (prompt == null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        if (messageFactory == null)
        {
            throw new ArgumentNullException(nameof(messageFactory));
        }

        if (string.IsNullOrWhiteSpace(prompt.Name))
        {
            throw new ArgumentException("Prompt name must be specified.", nameof(prompt));
        }

        lock (_promptLock)
        {
            var key = prompt.Name;
            if (_prompts.ContainsKey(key))
            {
                if (!overwrite)
                {
                    throw new InvalidOperationException(
                        $"Prompt '{prompt.Name}' is already registered."
                    );
                }
            }
            else
            {
                _promptOrder.Add(key);
            }

            _prompts[key] = new PromptRegistration(ClonePrompt(prompt), messageFactory);
        }

        _logger.LogInformation("Registered prompt: {PromptName}", prompt.Name);
    }

    /// <summary>
    /// Registers a prompt with static messages.
    /// </summary>
    public void RegisterPrompt(Prompt prompt, object[] messages, bool overwrite = false)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        RegisterPrompt(prompt, _ => Task.FromResult(messages), overwrite);
    }

    /// <summary>
    /// Removes a previously registered prompt.
    /// </summary>
    public bool UnregisterPrompt(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prompt name must be specified.", nameof(name));
        }

        lock (_promptLock)
        {
            var removed = _prompts.Remove(name);
            if (removed)
            {
                _promptOrder.Remove(name);
            }

            return removed;
        }
    }

    public static IReadOnlyList<string> SupportedProtocolVersions => s_supportedProtocolVersions;

    public string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    public async Task ConnectAsync(IServerTransport transport)
    {
        _transport = transport;
        _negotiatedProtocolVersion = null;

        // Set up event handlers
        transport.OnRequest += HandleRequest;
        transport.OnNotification += HandleNotification;
        transport.OnError += HandleTransportError;
        transport.OnClose += HandleTransportClose;

        _logger.LogInformation("MCP server connecting to transport");

        // Start the transport
        await transport.StartAsync();
    }

    private async void HandleRequest(JsonRpcRequestMessage request)
    {
        using (_logger.BeginRequestScope(request.Id, request.Method))
        {
            _logger.LogDebug(
                "Received request: ID={RequestId}, Method={Method}",
                request.Id,
                request.Method
            );
            try
            {
                await ProcessRequestAsync(request).ConfigureAwait(false);
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

    private async Task ProcessRequestAsync(JsonRpcRequestMessage request)
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
            var response = await ProcessJsonRpcRequest(request);

            // Send the response via the transport
            if (_transport != null)
            {
                var hasError = response.Error != null;
                var logLevel = hasError ? LogLevel.Warning : LogLevel.Debug;

                _logger.Log(
                    logLevel,
                    "Sending response: ID={RequestId}, HasResult={HasResult}, HasError={HasError}",
                    response.Id,
                    response.Result != null,
                    hasError
                );

                // Pass the response directly to the transport
                await _transport.SendAsync(response);
            }
        }
    }

    private void HandleTransportError(Exception ex)
    {
        _logger.LogError(ex, "Transport error");
    }

    private void HandleTransportClose()
    {
        _logger.LogInformation("Transport connection closed");
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
        _logger.LogDebug("Handling tools/list request, returning {Count} tools", _tools.Count);
        return Task.FromResult<object>(new { tools = _tools.Values });
    }

    private async Task<object> HandleToolCall(ToolCallRequest request)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            _logger.LogWarning("Tool call received with empty tool name");
            throw new McpException(ErrorCode.InvalidParams, "Tool name cannot be empty");
        }

        // Create a tool-specific logging scope
        using (_logger.BeginToolScope<string>(request.Name))
        {
            try
            {
                if (!_toolHandlers.TryGetValue(request.Name, out var handler))
                {
                    _logger.LogWarning(
                        "Tool call received for unknown tool: {ToolName}",
                        request.Name
                    );
                    throw new McpException(
                        ErrorCode.InvalidParams,
                        $"Tool not found: {request.Name}"
                    );
                }

                // Extract arguments from the request if they exist
                JsonElement? argumentsElement = request.GetArguments();

                // Log the beginning of tool execution with parameters if present
                if (argumentsElement.HasValue)
                {
                    // Truncate large parameter values to avoid huge log entries
                    string argsJson = argumentsElement.Value.ToString();
                    string logArgs =
                        argsJson.Length > 500
                            ? argsJson.Substring(0, 500) + "... [truncated]"
                            : argsJson;

                    _logger.LogInformation(
                        "Executing tool {ToolName} with parameters: {Parameters}",
                        request.Name,
                        logArgs
                    );
                }
                else
                {
                    _logger.LogInformation(
                        "Executing tool {ToolName} with no parameters",
                        request.Name
                    );
                }

                // Use timing scope to measure and log execution time
                using (_logger.BeginTimingScope($"Execute{request.Name}Tool", LogLevel.Information))
                {
                    var response = await handler(argumentsElement);

                    // Log tool execution result
                    if (response.IsError)
                    {
                        string errorMessage = response.Content?.FirstOrDefault()
                            is TextContent textContent
                            ? textContent.Text
                            : "Unknown error";

                        _logger.LogWarning(
                            "Tool {ToolName} execution failed: {ErrorMessage}",
                            request.Name,
                            errorMessage
                        );
                    }
                    else
                    {
                        int contentCount = response.Content?.Count() ?? 0;
                        _logger.LogInformation(
                            "Tool {ToolName} executed successfully, returned {ContentCount} content items",
                            request.Name,
                            contentCount
                        );
                    }

                    return response;
                }
            }
            catch (JsonException ex)
            {
                // Handle JSON parsing errors
                _logger.LogToolException(ex, request.Name, "JSON parsing error");
                return new ToolCallResult
                {
                    IsError = true,
                    Content = new[]
                    {
                        new TextContent { Text = $"Invalid tool call parameters: {ex.Message}" },
                    },
                };
            }
            catch (McpException ex)
            {
                // Propagate MCP exceptions with their error codes
                _logger.LogWarning(
                    "MCP exception in tool {ToolName}: {Message}",
                    request.Name,
                    ex.Message
                );
                throw;
            }
            catch (Exception ex)
            {
                // Convert other exceptions to a tool response with error
                _logger.LogToolException(ex, request.Name);
                return new ToolCallResult
                {
                    IsError = true,
                    Content = new[]
                    {
                        new TextContent { Text = $"Error executing tool: {ex.Message}" },
                    },
                };
            }
        }
    }

    private Task<object> HandleResourcesList(ResourcesListRequest _)
    {
        _logger.LogDebug("Handling resources/list request");

        List<Resource> resources;
        lock (_resourceLock)
        {
            resources = new List<Resource>(_resourceOrder.Count);
            foreach (var uri in _resourceOrder)
            {
                if (_resources.TryGetValue(uri, out var registration))
                {
                    resources.Add(CloneResource(registration.Resource));
                }
            }
        }

        return Task.FromResult<object>(
            new ResourcesListResponse { Resources = resources.ToArray() }
        );
    }

    private async Task<object> HandleResourcesRead(ResourcesReadRequest request)
    {
        _logger.LogDebug("Handling resources/read request");

        if (string.IsNullOrEmpty(request.Uri))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid URI");
        }

        ResourceRegistration? registration;
        lock (_resourceLock)
        {
            if (!_resources.TryGetValue(request.Uri, out registration))
            {
                _logger.LogWarning("Resource not found: {Uri}", request.Uri);
                throw new McpException(
                    ErrorCode.ResourceNotFound,
                    $"Resource not found: {request.Uri}"
                );
            }
        }

        ResourceContent[] contents;
        try
        {
            contents = registration.Reader != null
                ? await registration.Reader(CancellationToken.None).ConfigureAwait(false)
                : Array.Empty<ResourceContent>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading resource {Uri}", request.Uri);
            throw new McpException(
                ErrorCode.InternalError,
                $"Failed to read resource: {request.Uri}"
            );
        }

        _logger.LogInformation("Resource read requested for URI: {Uri}", request.Uri);
        return new ResourceReadResponse { Contents = contents ?? Array.Empty<ResourceContent>() };
    }

    private Task<object> HandlePromptsList(PromptsListRequest _)
    {
        _logger.LogDebug("Handling prompts/list request");

        List<Prompt> prompts;
        lock (_promptLock)
        {
            prompts = new List<Prompt>(_promptOrder.Count);
            foreach (var name in _promptOrder)
            {
                if (_prompts.TryGetValue(name, out var registration))
                {
                    prompts.Add(ClonePrompt(registration.Prompt));
                }
            }
        }

        return Task.FromResult<object>(new PromptsListResponse { Prompts = prompts.ToArray() });
    }

    private async Task<object> HandlePromptsGet(PromptsGetRequest request)
    {
        _logger.LogDebug("Handling prompts/get request");

        if (string.IsNullOrEmpty(request.Name))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid prompt name");
        }

        PromptRegistration? registration;
        lock (_promptLock)
        {
            if (!_prompts.TryGetValue(request.Name, out registration))
            {
                _logger.LogWarning("Prompt not found: {Name}", request.Name);
                throw new McpException(
                    ErrorCode.PromptNotFound,
                    $"Prompt not found: {request.Name}"
                );
            }
        }

        object[] messages;
        try
        {
            messages = registration.MessagesFactory != null
                ? await registration.MessagesFactory(CancellationToken.None).ConfigureAwait(false)
                : Array.Empty<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prompt {Name}", request.Name);
            throw new McpException(
                ErrorCode.InternalError,
                $"Failed to generate prompt: {request.Name}"
            );
        }

        _logger.LogInformation("Prompt requested: {Name}", request.Name);
        return new PromptsGetResponse { Messages = messages ?? Array.Empty<object>() };
    }

    private static Resource CloneResource(Resource source)
    {
        return new Resource
        {
            Uri = source.Uri,
            Name = source.Name,
            Description = source.Description,
            MimeType = source.MimeType,
            Annotations = CloneDictionary(source.Annotations),
            Meta = CloneDictionary(source.Meta),
        };
    }

    private static Prompt ClonePrompt(Prompt source)
    {
        return new Prompt
        {
            Name = source.Name,
            Title = source.Title,
            Description = source.Description,
            Arguments = source.Arguments?.Select(ClonePromptArgument).ToArray(),
            Annotations = CloneDictionary(source.Annotations),
            Meta = CloneDictionary(source.Meta),
        };
    }

    private static PromptArgument ClonePromptArgument(PromptArgument source)
    {
        return new PromptArgument
        {
            Name = source.Name,
            Description = source.Description,
            Required = source.Required,
            Default = source.Default,
            Annotations = CloneDictionary(source.Annotations),
            Meta = CloneDictionary(source.Meta),
        };
    }

    private static IDictionary<string, object?>? CloneDictionary(
        IDictionary<string, object?>? source
    )
    {
        if (source == null)
        {
            return null;
        }

        return new Dictionary<string, object?>(source);
    }

    private sealed record ResourceRegistration(
        Resource Resource,
        Func<CancellationToken, Task<ResourceContent[]>> Reader
    );

    private sealed record PromptRegistration(
        Prompt Prompt,
        Func<CancellationToken, Task<object[]>> MessagesFactory
    );

    /// <summary>
    /// Register a method handler for a specific request type
    /// </summary>
    private void RegisterMethod<TRequest>(string methodName, Func<TRequest, Task<object>> handler)
        where TRequest : IMcpRequest
    {
        // Store a function that takes a JSON string, deserializes it and calls the handler
        _methodHandlers[methodName] = async (jsonParams) =>
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
                return await handler(request);
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

    public void RegisterToolsFromAssembly(Assembly assembly, IServiceProvider serviceProvider)
    {
        // This is a bridge to the extension method that does the actual work
        // This instance method ensures consistency across different calling patterns
        Mcp.Net.Server.Extensions.McpServerExtensions.RegisterToolsFromAssembly(
            this,
            assembly,
            serviceProvider
        );
    }

    public void RegisterTool(
        string name,
        string? description,
        JsonElement inputSchema,
        Func<JsonElement?, Task<ToolCallResult>> handler
    )
    {
        var tool = new Tool
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema,
        };

        _tools[name] = tool;
        _toolHandlers[name] = async (args) =>
        {
            try
            {
                _logger.LogInformation("Tool {ToolName} invoked", name);
                return await handler(args);
            }
            catch (Exception ex)
            {
                // Convert any exceptions in the tool handler to a proper CallToolResult with IsError=true
                _logger.LogError(ex, "Error in tool handler: {ToolName}", name);
                return new ToolCallResult
                {
                    IsError = true,
                    Content = new[]
                    {
                        new TextContent { Text = ex.Message },
                        new TextContent { Text = $"Stack trace:\n{ex.StackTrace}" },
                    },
                };
            }
        };

        // Ensure tools capability is registered
        if (_capabilities.Tools == null)
        {
            _capabilities.Tools = new { };
        }

        _logger.LogInformation(
            "Registered tool: {ToolName} - {Description}",
            name,
            description ?? "No description"
        );
    }

    public async Task<JsonRpcResponseMessage> ProcessJsonRpcRequest(JsonRpcRequestMessage request)
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
            var result = await handler(paramsJson);

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
}
