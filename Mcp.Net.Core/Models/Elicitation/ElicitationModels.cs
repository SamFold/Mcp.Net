using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.Models.Elicitation;

/// <summary>
/// Parameters for the <c>elicitation/create</c> JSON-RPC call.
/// </summary>
public sealed class ElicitationCreateParams
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("requestedSchema")]
    public ElicitationSchema RequestedSchema { get; set; } = new();
}

/// <summary>
/// Represents the flattened JSON schema supported by MCP elicitation.
/// </summary>
public sealed class ElicitationSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ElicitationSchemaProperty> Properties { get; } = new(StringComparer.Ordinal);

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; private set; }
        = null;

    /// <summary>
    /// Adds or replaces a property definition.
    /// </summary>
    public ElicitationSchema AddProperty(
        string name,
        ElicitationSchemaProperty property,
        bool required = false
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Property name must be provided.", nameof(name));
        }

        Properties[name] = property ?? throw new ArgumentNullException(nameof(property));

        if (required)
        {
            Required ??= new List<string>();
            if (!Required.Contains(name))
            {
                Required.Add(name);
            }
        }

        return this;
    }

    /// <summary>
    /// Marks the specified properties as required.
    /// </summary>
    public ElicitationSchema Require(params string[] properties)
    {
        if (properties == null || properties.Length == 0)
        {
            return this;
        }

        Required ??= new List<string>();
        foreach (var property in properties)
        {
            if (!string.IsNullOrWhiteSpace(property) && !Required.Contains(property))
            {
                Required.Add(property);
            }
        }

        return this;
    }
}

/// <summary>
/// Describes a single elicitation field.
/// </summary>
public sealed class ElicitationSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
        = null;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
        = null;

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }
        = null;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Enum { get; set; }
        = null;

    [JsonPropertyName("enumNames")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? EnumNames { get; set; }
        = null;

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Default { get; set; }
        = null;

    [JsonPropertyName("minimum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Minimum { get; set; }
        = null;

    [JsonPropertyName("maximum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Maximum { get; set; }
        = null;

    [JsonPropertyName("minLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; set; }
        = null;

    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }
        = null;

    public static ElicitationSchemaProperty ForString(
        string? title = null,
        string? description = null,
        string? format = null
    ) => new()
    {
        Type = "string",
        Title = title,
        Description = description,
        Format = format,
    };

    public static ElicitationSchemaProperty ForNumber(
        string? title = null,
        string? description = null,
        double? minimum = null,
        double? maximum = null
    ) => new()
    {
        Type = "number",
        Title = title,
        Description = description,
        Minimum = minimum,
        Maximum = maximum,
    };

    public static ElicitationSchemaProperty ForInteger(
        string? title = null,
        string? description = null,
        double? minimum = null,
        double? maximum = null
    ) => new()
    {
        Type = "integer",
        Title = title,
        Description = description,
        Minimum = minimum,
        Maximum = maximum,
    };

    public static ElicitationSchemaProperty ForBoolean(
        string? title = null,
        string? description = null
    ) => new()
    {
        Type = "boolean",
        Title = title,
        Description = description,
    };

    public static ElicitationSchemaProperty ForEnum(
        IEnumerable<string> values,
        IEnumerable<string>? displayNames = null,
        string? title = null,
        string? description = null
    )
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return new ElicitationSchemaProperty
        {
            Type = "string",
            Title = title,
            Description = description,
            Enum = values.ToArray(),
            EnumNames = displayNames?.ToArray(),
        };
    }
}

/// <summary>
/// Raw JSON result returned by the client for an elicitation request.
/// </summary>
public sealed class ElicitationCreateResult
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Content { get; set; }
        = null;
}

/// <summary>
/// High-level action the user took when responding to an elicitation prompt.
/// </summary>
public enum ElicitationAction
{
    Accept,
    Decline,
    Cancel,
}

/// <summary>
/// Represents a prompt the server sends to the client for elicitation.
/// </summary>
public sealed class ElicitationPrompt
{
    public string Message { get; }
    public ElicitationSchema RequestedSchema { get; }

    public ElicitationPrompt(string message, ElicitationSchema requestedSchema)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        RequestedSchema = requestedSchema
            ?? throw new ArgumentNullException(nameof(requestedSchema));
    }
}

/// <summary>
/// Represents the normalized result of an elicitation exchange.
/// </summary>
public sealed class ElicitationResult
{
    public ElicitationAction Action { get; }
    public JsonElement? Content { get; }

    public ElicitationResult(ElicitationAction action, JsonElement? content)
    {
        Action = action;
        Content = content;
    }
}
