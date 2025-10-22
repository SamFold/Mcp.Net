using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Capabilities;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Messages;
using Mcp.Net.Core.Models.Tools;
using Mcp.Net.Core.Transport;
using Mcp.Net.Server.Interfaces;
using Mcp.Net.Server.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Mcp.Net.Core.JsonRpc.JsonRpcMessageExtensions;

public class McpServer : IMcpServer
{
    // Dictionary to store method handlers that take a JSON string parameter
    private readonly Dictionary<string, Func<string, Task<object>>> _methodHandlers = new();
    private readonly Dictionary<string, Tool> _tools = new();
    private readonly Dictionary<string, Func<JsonElement?, Task<ToolCallResult>>> _toolHandlers =
        new();
    private IServerTransport? _transport;

    private readonly ServerInfo _serverInfo;
    private readonly ServerCapabilities _capabilities;
    private readonly string? _instructions;
    private readonly ILogger<McpServer> _logger;

    public McpServer(ServerInfo serverInfo, ServerOptions? options = null)
        : this(serverInfo, options, new LoggerFactory()) { }

    public McpServer(ServerInfo serverInfo, ServerOptions? options, ILoggerFactory loggerFactory)
    {
        _serverInfo = serverInfo;
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

    public async Task ConnectAsync(IServerTransport transport)
    {
        _transport = transport;

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

        return Task.FromResult<object>(
            new
            {
                protocolVersion = "2024-11-05", // Using latest from spec
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
        return Task.FromResult<object>(new { resources = Array.Empty<object>() });
    }

    private Task<object> HandleResourcesRead(ResourcesReadRequest request)
    {
        _logger.LogDebug("Handling resources/read request");

        if (string.IsNullOrEmpty(request.Uri))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid URI");
        }

        _logger.LogInformation("Resource read requested for URI: {Uri}", request.Uri);
        throw new McpException(ErrorCode.ResourceNotFound, $"Resource not found: {request.Uri}");
    }

    private Task<object> HandlePromptsList(PromptsListRequest _)
    {
        _logger.LogDebug("Handling prompts/list request");
        return Task.FromResult<object>(new { prompts = Array.Empty<object>() });
    }

    private Task<object> HandlePromptsGet(PromptsGetRequest request)
    {
        _logger.LogDebug("Handling prompts/get request");

        if (string.IsNullOrEmpty(request.Name))
        {
            throw new McpException(ErrorCode.InvalidParams, "Invalid prompt name");
        }

        _logger.LogInformation("Prompt requested: {Name}", request.Name);
        throw new McpException(ErrorCode.PromptNotFound, $"Prompt not found: {request.Name}");
    }

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
