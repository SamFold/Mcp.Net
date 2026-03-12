using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Agent.Tools;

/// <summary>
/// Generates input schemas for typed local tools without relying on MCP-specific discovery attributes.
/// </summary>
public static class LocalToolSchemaGenerator
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static JsonElement GenerateJsonSchema(
        Type type,
        JsonSerializerOptions? serializerOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(type);

        var options = serializerOptions ?? DefaultSerializerOptions;
        var nullabilityContext = new NullabilityInfoContext();
        var schema = BuildObjectSchema(type, options, nullabilityContext);
        return JsonSerializer.SerializeToElement(schema);
    }

    private static SchemaObject BuildObjectSchema(
        Type type,
        JsonSerializerOptions serializerOptions,
        NullabilityInfoContext nullabilityContext
    )
    {
        var properties = new Dictionary<string, object?>();
        var requiredProperties = new List<string>();

        foreach (var property in GetSerializableProperties(type))
        {
            var propertyName = GetPropertyName(property, serializerOptions);
            properties[propertyName] = BuildPropertySchema(
                property.PropertyType,
                serializerOptions,
                nullabilityContext
            );

            if (IsRequired(property, nullabilityContext))
            {
                requiredProperties.Add(propertyName);
            }
        }

        return new SchemaObject
        {
            Schema = "https://json-schema.org/draft/2020-12/schema",
            Type = "object",
            Properties = properties,
            Required = requiredProperties,
        };
    }

    private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.GetIndexParameters().Length == 0
                && property.GetMethod?.IsPublic == true
                && property.GetCustomAttribute<JsonIgnoreAttribute>() == null
            );

    private static string GetPropertyName(PropertyInfo property, JsonSerializerOptions serializerOptions)
    {
        var jsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        if (!string.IsNullOrWhiteSpace(jsonPropertyName))
        {
            return jsonPropertyName;
        }

        return serializerOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    private static object BuildPropertySchema(
        Type type,
        JsonSerializerOptions serializerOptions,
        NullabilityInfoContext nullabilityContext
    )
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        if (TryGetPrimitiveSchemaType(effectiveType, out var jsonType))
        {
            return new Dictionary<string, object?> { ["type"] = jsonType };
        }

        if (effectiveType.IsEnum)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = Enum.GetNames(effectiveType),
            };
        }

        if (TryGetArrayItemType(effectiveType, out var itemType))
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = BuildPropertySchema(itemType, serializerOptions, nullabilityContext),
            };
        }

        return BuildObjectSchema(effectiveType, serializerOptions, nullabilityContext);
    }

    private static bool TryGetPrimitiveSchemaType(Type type, out string jsonType)
    {
        jsonType = Type.GetTypeCode(type) switch
        {
            TypeCode.Int16
            or TypeCode.Int32
            or TypeCode.Int64 => "integer",
            TypeCode.Decimal
            or TypeCode.Double
            or TypeCode.Single => "number",
            TypeCode.String => "string",
            TypeCode.Boolean => "boolean",
            _ => string.Empty,
        };

        return jsonType.Length > 0;
    }

    private static bool TryGetArrayItemType(Type type, out Type itemType)
    {
        itemType = null!;

        if (type == typeof(string) || IsDictionaryType(type))
        {
            return false;
        }

        if (type.IsArray)
        {
            itemType = type.GetElementType()!;
            return true;
        }

        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            itemType = type.GetGenericArguments()[0];
            return true;
        }

        return false;
    }

    private static bool IsDictionaryType(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces().Any(interfaceType =>
            interfaceType.IsGenericType
            && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>)
        );

    private static bool IsRequired(
        PropertyInfo property,
        NullabilityInfoContext nullabilityContext
    )
    {
        var propertyType = property.PropertyType;
        if (Nullable.GetUnderlyingType(propertyType) != null)
        {
            return false;
        }

        if (propertyType.IsValueType)
        {
            return true;
        }

        var nullability = nullabilityContext.Create(property);
        var state = nullability.WriteState != NullabilityState.Unknown
            ? nullability.WriteState
            : nullability.ReadState;

        return state != NullabilityState.Nullable;
    }

    private sealed class SchemaObject
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = "https://json-schema.org/draft/2020-12/schema";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("properties")]
        public Dictionary<string, object?> Properties { get; set; } = new();

        [JsonPropertyName("required")]
        public List<string> Required { get; set; } = new();
    }
}
