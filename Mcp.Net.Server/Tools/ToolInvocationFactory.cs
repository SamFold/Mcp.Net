using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.Models.Content;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.Models.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Tools;

/// <summary>
/// Creates tool handler delegates that execute registered tool methods.
/// </summary>
internal sealed class ToolInvocationFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolInvocationFactory> _logger;

    public ToolInvocationFactory(
        IServiceProvider serviceProvider,
        ILogger<ToolInvocationFactory> logger
    )
    {
        _serviceProvider =
            serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Func<JsonElement?, Task<ToolCallResult>> CreateHandler(ToolDescriptor descriptor)
    {
        return async arguments =>
        {
            _logger.LogInformation("Tool {ToolName} invoked", descriptor.Name);

            try
            {
                var instance = ActivatorUtilities.CreateInstance(
                    _serviceProvider,
                    descriptor.DeclaringType
                );
                var methodParams = descriptor.Method.GetParameters();
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

                object? result = await InvokeToolAsync(descriptor.Method, instance, invokeParams);
                return NormalizeResult(descriptor.Name, result);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                var innerException = ex.InnerException;
                if (innerException is McpException mcpEx)
                {
                    throw mcpEx;
                }

                _logger.LogError(
                    innerException,
                    "Error invoking tool '{ToolName}': {ErrorMessage}",
                    descriptor.Name,
                    innerException.Message
                );

                return BuildErrorResult(innerException);
            }
            catch (Exception ex)
            {
                if (ex is McpException)
                {
                    throw;
                }

                _logger.LogError(
                    ex,
                    "Error executing tool '{ToolName}': {ErrorMessage}",
                    descriptor.Name,
                    ex.Message
                );

                return BuildErrorResult(ex);
            }
        };
    }

    private static string ResolveParameterBindingKey(ParameterInfo parameter) =>
        parameter.Name ?? string.Empty;

    private static async Task<object?> InvokeToolAsync(
        MethodInfo method,
        object instance,
        object?[] invokeParams
    )
    {
        if (method.ReturnType.IsAssignableTo(typeof(Task)))
        {
            if (method.Invoke(instance, invokeParams) is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return null;
        }

        return method.Invoke(instance, invokeParams);
    }

    private static ToolCallResult NormalizeResult(string toolName, object? result)
    {
        if (result is ToolCallResult toolCallResult)
        {
            return toolCallResult;
        }

        string resultText;
        if (result != null)
        {
            var resultType = result.GetType();
            if (
                !resultType.IsPrimitive
                && resultType != typeof(string)
                && resultType != typeof(decimal)
                && !resultType.IsEnum
            )
            {
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

    private static ToolCallResult BuildErrorResult(Exception exception)
    {
        return new ToolCallResult
        {
            IsError = true,
            Content = new ContentBase[]
            {
                new TextContent { Text = $"Error in tool execution: {exception.Message}" },
                new TextContent { Text = $"Stack trace:\n{exception.StackTrace}" },
            },
        };
    }
}
