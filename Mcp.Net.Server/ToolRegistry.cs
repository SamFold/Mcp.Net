using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server;

/// <summary>
/// Registry for MCP tools that handles discovery, validation, and registration.
/// </summary>
public class ToolRegistry
{
    private readonly ILogger<ToolRegistry> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<Assembly> _assemblies = new();
    private readonly Dictionary<string, ToolInfo> _registeredTools = new();

    /// <summary>
    /// Information about a registered tool.
    /// </summary>
    private class ToolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Type DeclaringType { get; set; } = typeof(object);
        public MethodInfo Method { get; set; } = null!;
        public JsonElement InputSchema { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="logger">The logger to use.</param>
    public ToolRegistry(IServiceProvider serviceProvider, ILogger<ToolRegistry> logger)
    {
        _serviceProvider =
            serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the number of registered tools.
    /// </summary>
    public int ToolCount => _registeredTools.Count;

    /// <summary>
    /// Gets the assemblies that have been added to the registry.
    /// </summary>
    public IReadOnlyCollection<Assembly> Assemblies => _assemblies.AsReadOnly();

    /// <summary>
    /// Gets the names of the registered tools.
    /// </summary>
    public IReadOnlyCollection<string> ToolNames => _registeredTools.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Adds an assembly to scan for tools.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The tool registry for chaining.</returns>
    public ToolRegistry AddAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
            _logger.LogInformation(
                "Added assembly to scan for tools: {AssemblyName}",
                assembly.GetName().Name
            );
        }

        return this;
    }

    /// <summary>
    /// Scans all registered assemblies for tools and registers them with the provided server.
    /// </summary>
    /// <param name="server">The server to register tools with.</param>
    public void RegisterToolsWithServer(McpServer server)
    {
        if (server == null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        // First, discover all tools from assemblies
        DiscoverTools();

        // Then register them with the server
        foreach (var (toolName, toolInfo) in _registeredTools)
        {
            try
            {
                RegisterToolWithServer(server, toolInfo);
                _logger.LogInformation("Registered tool '{ToolName}' with server", toolName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register tool '{ToolName}' with server", toolName);
            }
        }
    }

    /// <summary>
    /// Discovers tools from all registered assemblies.
    /// </summary>
    private void DiscoverTools()
    {
        foreach (var assembly in _assemblies)
        {
            try
            {
                DiscoverToolsFromAssembly(assembly);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to discover tools from assembly {AssemblyName}",
                    assembly.GetName().Name
                );
            }
        }
    }

    /// <summary>
    /// Discovers tools from a specific assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    private void DiscoverToolsFromAssembly(Assembly assembly)
    {
        _logger.LogInformation(
            "Scanning assembly for tools: {AssemblyName}",
            assembly.GetName().Name
        );

        // Find all types that either have the McpToolAttribute or have methods with McpToolAttribute
        var toolTypes = assembly
            .GetTypes()
            .Where(t =>
                t.GetCustomAttributes<McpToolAttribute>().Any()
                || t.GetMethods().Any(m => m.GetCustomAttribute<McpToolAttribute>() != null)
            )
            .ToList();

        _logger.LogInformation(
            "Found {Count} tool classes in assembly {AssemblyName}",
            toolTypes.Count,
            assembly.GetName().Name
        );

        // Process each tool type
        foreach (var toolType in toolTypes)
        {
            DiscoverToolsFromType(toolType);
        }
    }

    /// <summary>
    /// Discovers tools from a specific type.
    /// </summary>
    /// <param name="toolType">The type to scan for tools.</param>
    private void DiscoverToolsFromType(Type toolType)
    {
        try
        {
            _logger.LogDebug("Scanning type for tools: {TypeName}", toolType.FullName);

            // Find all methods with McpToolAttribute
            var methods = toolType
                .GetMethods()
                .Where(m => m.GetCustomAttribute<McpToolAttribute>() != null)
                .ToList();

            _logger.LogDebug(
                "Found {Count} tool methods in type {TypeName}",
                methods.Count,
                toolType.FullName
            );

            // Process each method
            foreach (var method in methods)
            {
                DiscoverToolFromMethod(toolType, method);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to discover tools from type {TypeName}",
                toolType.FullName
            );
        }
    }

    /// <summary>
    /// Discovers a tool from a specific method.
    /// </summary>
    /// <param name="declaringType">The type declaring the method.</param>
    /// <param name="method">The method to scan for tools.</param>
    private void DiscoverToolFromMethod(Type declaringType, MethodInfo method)
    {
        try
        {
            var toolAttribute = method.GetCustomAttribute<McpToolAttribute>();
            if (toolAttribute == null)
            {
                return; // Skip if attribute is null (shouldn't happen due to the Where filter)
            }

            var toolName = toolAttribute.Name;
            if (string.IsNullOrEmpty(toolName))
            {
                _logger.LogWarning(
                    "Method {TypeName}.{MethodName} has McpToolAttribute with empty name, using method name instead",
                    declaringType.FullName,
                    method.Name
                );
                toolName = method.Name;
            }

            // Ensure tool name is unique
            if (_registeredTools.ContainsKey(toolName))
            {
                _logger.LogWarning(
                    "Tool with name '{ToolName}' is already registered, skipping duplicate from {TypeName}.{MethodName}",
                    toolName,
                    declaringType.FullName,
                    method.Name
                );
                return;
            }

            // Generate input schema
            var inputSchema = GenerateInputSchema(method);

            // Validate method signature
            ValidateMethodSignature(declaringType, method);

            // Add to registered tools
            _registeredTools[toolName] = new ToolInfo
            {
                Name = toolName,
                Description = toolAttribute.Description ?? string.Empty,
                DeclaringType = declaringType,
                Method = method,
                InputSchema = inputSchema,
            };

            _logger.LogInformation(
                "Discovered tool '{ToolName}' from method {TypeName}.{MethodName}",
                toolName,
                declaringType.FullName,
                method.Name
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to discover tool from method {TypeName}.{MethodName}",
                declaringType.FullName,
                method.Name
            );
        }
    }

    /// <summary>
    /// Validates a method signature for use as a tool method.
    /// </summary>
    /// <param name="declaringType">The type declaring the method.</param>
    /// <param name="method">The method to validate.</param>
    private void ValidateMethodSignature(Type declaringType, MethodInfo method)
    {
        // Check parameters for proper attributes
        foreach (var param in method.GetParameters())
        {
            var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();

            // Log warnings for parameters without McpParameterAttribute
            if (paramAttr == null)
            {
                _logger.LogWarning(
                    "Parameter '{ParameterName}' in method {TypeName}.{MethodName} does not have McpParameterAttribute. "
                        + "Consider adding it for better documentation.",
                    param.Name,
                    declaringType.FullName,
                    method.Name
                );
            }
        }

        // Check return type
        if (method.ReturnType == typeof(void))
        {
            _logger.LogWarning(
                "Method {TypeName}.{MethodName} has void return type. "
                    + "Tools should return a value or Task for better usability.",
                declaringType.FullName,
                method.Name
            );
        }
        else if (
            method.ReturnType.IsAssignableTo(typeof(Task))
            && method.ReturnType.IsGenericType == false
        )
        {
            _logger.LogWarning(
                "Method {TypeName}.{MethodName} returns Task without a result type. "
                    + "Consider using Task<T> instead for better usability.",
                declaringType.FullName,
                method.Name
            );
        }
    }

    private static string ResolveParameterBindingKey(ParameterInfo parameter) =>
        parameter.Name ?? string.Empty;

    /// <summary>
    /// Registers a discovered tool with the server.
    /// </summary>
    /// <param name="server">The server to register with.</param>
    /// <param name="toolInfo">Information about the tool to register.</param>
    private void RegisterToolWithServer(McpServer server, ToolInfo toolInfo)
    {
        server.RegisterTool(
            name: toolInfo.Name,
            description: toolInfo.Description,
            inputSchema: toolInfo.InputSchema,
            handler: async (arguments) =>
            {
                try
                {
                    // Create instance of the tool class
                    var instance = ActivatorUtilities.CreateInstance(
                        _serviceProvider,
                        toolInfo.DeclaringType
                    );

                    // Prepare parameters for method invocation
                    var methodParams = toolInfo.Method.GetParameters();
                    var invokeParams = new object?[methodParams.Length];

                    for (int i = 0; i < methodParams.Length; i++)
                    {
                        var param = methodParams[i];
                        var bindingKey = ResolveParameterBindingKey(param);
                        var displayName = param.Name ?? bindingKey;

                        if (
                            arguments.HasValue
                            && !string.IsNullOrEmpty(bindingKey)
                            && arguments.Value.TryGetProperty(bindingKey, out var paramValue)
                        )
                        {
                            try
                            {
                                invokeParams[i] = JsonSerializer.Deserialize(
                                    paramValue.GetRawText(),
                                    param.ParameterType
                                );
                            }
                            catch (JsonException ex)
                            {
                                throw new McpException(
                                    ErrorCode.InvalidParams,
                                    $"Invalid value for parameter '{displayName}': {ex.Message}"
                                );
                            }
                        }
                        else if (param.HasDefaultValue)
                        {
                            invokeParams[i] = param.DefaultValue;
                        }
                        else if (
                            param.GetCustomAttribute<McpParameterAttribute>()?.Required == true
                        )
                        {
                            throw new McpException(
                                ErrorCode.InvalidParams,
                                $"Required parameter '{displayName}' was not provided"
                            );
                        }
                        else
                        {
                            invokeParams[i] = param.ParameterType.IsValueType
                                ? Activator.CreateInstance(param.ParameterType)
                                : null;
                        }
                    }

                    // Invoke method
                    object? result;
                    if (toolInfo.Method.ReturnType.IsAssignableTo(typeof(Task)))
                    {
                        var task = toolInfo.Method.Invoke(instance, invokeParams) as Task;
                        if (task != null)
                        {
                            await task;
                            // Get the result if it's a Task<T>
                            var resultProperty = task.GetType().GetProperty("Result");
                            result = resultProperty?.GetValue(task);
                        }
                        else
                        {
                            result = null;
                        }
                    }
                    else
                    {
                        result = toolInfo.Method.Invoke(instance, invokeParams);
                    }

                    // If the method already returns a CallToolResult, return it directly
                    if (result is ToolCallResult callToolResult)
                    {
                        return callToolResult;
                    }

                    // Otherwise, wrap the result in a CallToolResult
                    string resultText;
                    if (result != null)
                    {
                        Type resultType = result.GetType();
                        // Serialize to JSON for non-primitive types (complex objects)
                        if (
                            !resultType.IsPrimitive
                            && resultType != typeof(string)
                            && resultType != typeof(decimal)
                            && !resultType.IsEnum
                        )
                        {
                            // Properly serialize complex objects to JSON
                            resultText = JsonSerializer.Serialize(
                                result,
                                new JsonSerializerOptions
                                {
                                    WriteIndented = true,
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                }
                            );
                        }
                        else
                        {
                            // Use ToString for primitive types
                            resultText = result.ToString() ?? string.Empty;
                        }
                    }
                    else
                    {
                        resultText = string.Empty;
                    }

                    return new ToolCallResult
                    {
                        Content = new[] { new TextContent { Text = resultText } },
                        IsError = false,
                    };
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    // Unwrap the TargetInvocationException to get the actual exception
                    var innerException = ex.InnerException;
                    if (innerException is McpException mcpEx)
                    {
                        throw mcpEx; // Let the McpServer handle this specially
                    }

                    // Log the exception
                    _logger.LogError(
                        innerException,
                        "Error invoking tool '{ToolName}': {ErrorMessage}",
                        toolInfo.Name,
                        innerException.Message
                    );

                    // Return a formatted error response
                    return new ToolCallResult
                    {
                        IsError = true,
                        Content = new[]
                        {
                            new TextContent
                            {
                                Text = $"Error in tool execution: {innerException.Message}",
                            },
                            new TextContent { Text = $"Stack trace:\n{innerException.StackTrace}" },
                        },
                    };
                }
                catch (Exception ex)
                {
                    // Handle all other exceptions
                    if (ex is McpException)
                    {
                        throw; // Let the McpServer handle this specially
                    }

                    // Log the exception
                    _logger.LogError(
                        ex,
                        "Error executing tool '{ToolName}': {ErrorMessage}",
                        toolInfo.Name,
                        ex.Message
                    );

                    return new ToolCallResult
                    {
                        IsError = true,
                        Content = new[]
                        {
                            new TextContent { Text = $"Error in tool execution: {ex.Message}" },
                            new TextContent { Text = $"Stack trace:\n{ex.StackTrace}" },
                        },
                    };
                }
            }
        );
    }

    /// <summary>
    /// Generates a JSON Schema for a method's parameters.
    /// </summary>
    /// <param name="method">The method to generate a schema for.</param>
    /// <returns>A JSON Schema describing the method's parameters.</returns>
    private static JsonElement GenerateInputSchema(MethodInfo method)
    {
        var properties = new Dictionary<string, JsonElement>();
        var requiredProperties = new List<string>();

        foreach (var param in method.GetParameters())
        {
            var paramName = param.Name?.ToLowerInvariant() ?? $"param{param.Position}";
            var paramSchema = JsonSchemaGenerator.GenerateParameterSchema(param);
            properties[paramName] = paramSchema;

            var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
            if (paramAttr?.Required == true)
            {
                requiredProperties.Add(paramName);
            }
        }

        // Convert JsonElement dictionary to object dictionary to match the SchemaObject property type
        var objectProperties = properties.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

        var schema = new JsonSchemaGenerator.SchemaObject
        {
            Properties = objectProperties,
            Required = requiredProperties,
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
