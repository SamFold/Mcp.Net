using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Mcp.Net.Core.Attributes;
using Mcp.Net.Core.JsonRpc;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Tools;

/// <summary>
/// Scans assemblies to locate MCP tools and produce descriptors.
/// </summary>
internal sealed class ToolDiscoveryService
{
    private readonly ILogger<ToolDiscoveryService> _logger;

    public ToolDiscoveryService(ILogger<ToolDiscoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scans the provided assemblies and returns descriptors for each discovered MCP tool.
    /// </summary>
    /// <param name="assemblies">Assemblies that should be inspected for <see cref="McpToolAttribute"/> decorations.</param>
    /// <returns>Immutable collection of tool descriptors.</returns>
    public IReadOnlyCollection<ToolDescriptor> DiscoverTools(IEnumerable<Assembly> assemblies)
    {
        if (assemblies == null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        var discovered = new Dictionary<string, ToolDescriptor>();

        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var descriptor in DiscoverFromAssembly(assembly))
                {
                    if (!discovered.TryAdd(descriptor.Name, descriptor))
                    {
                        _logger.LogWarning(
                            "Tool with name '{ToolName}' already registered, skipping duplicate from {TypeName}.{MethodName}",
                            descriptor.Name,
                            descriptor.DeclaringType.FullName,
                            descriptor.Method.Name
                        );
                    }
                }
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

        return discovered.Values.ToList();
    }

    private IEnumerable<ToolDescriptor> DiscoverFromAssembly(Assembly assembly)
    {
        _logger.LogInformation(
            "Scanning assembly for tools: {AssemblyName}",
            assembly.GetName().Name
        );

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

        foreach (var toolType in toolTypes)
        {
            foreach (var descriptor in DiscoverFromType(toolType))
            {
                yield return descriptor;
            }
        }
    }

    private IEnumerable<ToolDescriptor> DiscoverFromType(Type toolType)
    {
        var descriptors = new List<ToolDescriptor>();

        try
        {
            _logger.LogDebug("Scanning type for tools: {TypeName}", toolType.FullName);

            var methods = toolType
                .GetMethods()
                .Where(m => m.GetCustomAttribute<McpToolAttribute>() != null)
                .ToList();

            _logger.LogDebug(
                "Found {Count} tool methods in type {TypeName}",
                methods.Count,
                toolType.FullName
            );

            foreach (var method in methods)
            {
                ToolDescriptor? descriptor = CreateDescriptor(toolType, method);
                if (descriptor != null)
                {
                    descriptors.Add(descriptor);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover tools from type {TypeName}", toolType.FullName);
        }

        return descriptors;
    }

    private ToolDescriptor? CreateDescriptor(Type declaringType, MethodInfo method)
    {
        try
        {
            var toolAttribute = method.GetCustomAttribute<McpToolAttribute>();
            if (toolAttribute == null)
            {
                return null;
            }

            var toolName = string.IsNullOrWhiteSpace(toolAttribute.Name)
                ? method.Name
                : toolAttribute.Name;

            ValidateMethodSignature(declaringType, method);
            var inputSchema = GenerateInputSchema(method);
            var annotations = BuildAnnotations(toolAttribute);

            _logger.LogInformation(
                "Discovered tool '{ToolName}' from method {TypeName}.{MethodName}",
                toolName,
                declaringType.FullName,
                method.Name
            );

            return new ToolDescriptor(
                toolName,
                toolAttribute.Description ?? string.Empty,
                declaringType,
                method,
                inputSchema,
                annotations
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
            return null;
        }
    }

    private static IDictionary<string, object?>? BuildAnnotations(
        McpToolAttribute toolAttribute
    )
    {
        if (toolAttribute == null)
        {
            return null;
        }

        var entries = new List<object?>();

        if (!string.IsNullOrWhiteSpace(toolAttribute.Category))
        {
            var primaryOrder = double.IsNaN(toolAttribute.CategoryOrder)
                ? (double?)null
                : toolAttribute.CategoryOrder;
            entries.Add(
                BuildCategoryEntry(
                    toolAttribute.Category,
                    toolAttribute.CategoryDisplayName,
                    primaryOrder
                )
            );
        }

        if (toolAttribute.Categories is { Length: > 0 })
        {
            foreach (var category in toolAttribute.Categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                entries.Add(category.Trim());
            }
        }

        var distinctEntries = entries
            .Where(entry => entry is not null)
            .Distinct(new CategoryEntryEqualityComparer())
            .ToList();

        if (distinctEntries.Count == 0)
        {
            return null;
        }

        var annotations = new Dictionary<string, object?>(
            StringComparer.OrdinalIgnoreCase
        );

        if (distinctEntries.Count == 1)
        {
            annotations["category"] = distinctEntries[0];
        }
        else
        {
            annotations["categories"] = distinctEntries;
        }

        return annotations;
    }

    private static object BuildCategoryEntry(
        string categoryId,
        string? displayName,
        double? order
    )
    {
        if (string.IsNullOrWhiteSpace(displayName) && !order.HasValue)
        {
            return categoryId.Trim();
        }

        var result = new Dictionary<string, object?>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["id"] = categoryId.Trim(),
            ["name"] = categoryId.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            result["displayName"] = displayName!.Trim();
        }

        if (order.HasValue)
        {
            result["order"] = order.Value;
        }

        return result;
    }

    private sealed class CategoryEntryEqualityComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (x is string xs && y is string ys)
            {
                return string.Equals(xs, ys, StringComparison.OrdinalIgnoreCase);
            }

            if (x is IDictionary<string, object?> xDict && y is IDictionary<string, object?> yDict)
            {
                if (
                    xDict.TryGetValue("id", out var xId)
                    && yDict.TryGetValue("id", out var yId)
                    && xId is string xIdStr
                    && yId is string yIdStr
                )
                {
                    return string.Equals(xIdStr, yIdStr, StringComparison.OrdinalIgnoreCase);
                }
            }

            return x.Equals(y);
        }

        public int GetHashCode(object? obj)
        {
            if (obj is null)
            {
                return 0;
            }

            if (obj is string str)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(str);
            }

            if (obj is IDictionary<string, object?> dict && dict.TryGetValue("id", out var id))
            {
                if (id is string idStr)
                {
                    return StringComparer.OrdinalIgnoreCase.GetHashCode(idStr);
                }
            }

            return obj.GetHashCode();
        }
    }

    private void ValidateMethodSignature(Type declaringType, MethodInfo method)
    {
        foreach (var param in method.GetParameters())
        {
            var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
            if (paramAttr == null)
            {
                _logger.LogWarning(
                    "Parameter '{ParameterName}' in method {TypeName}.{MethodName} does not have McpParameterAttribute. Consider adding it for documentation.",
                    param.Name,
                    declaringType.FullName,
                    method.Name
                );
            }
        }

        if (method.ReturnType == typeof(void))
        {
            _logger.LogWarning(
                "Method {TypeName}.{MethodName} has void return type. Tools should return a value or Task for better usability.",
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
                "Method {TypeName}.{MethodName} returns Task without a result type. Consider using Task<T> instead for better usability.",
                declaringType.FullName,
                method.Name
            );
        }
    }

    private static JsonElement GenerateInputSchema(MethodInfo method)
    {
        var properties = new Dictionary<string, JsonElement>();
        var requiredProperties = new List<string>();

        foreach (var param in method.GetParameters())
        {
            var paramName = param.Name ?? $"param{param.Position}";
            var paramSchema = JsonSchemaGenerator.GenerateParameterSchema(param);
            properties[paramName] = paramSchema;

            var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
            if (paramAttr?.Required == true)
            {
                requiredProperties.Add(paramName);
            }
        }

        var schema = new JsonSchemaGenerator.SchemaObject
        {
            Properties = properties.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            Required = requiredProperties,
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
