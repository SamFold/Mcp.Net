using System.Globalization;
using System.Text.Json;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Agent.Models;

public sealed record AgentExecutionDefaults
{
    public float? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public ChatToolChoice? ToolChoice { get; init; }

    public static AgentExecutionDefaults FromLegacyParameters(
        IReadOnlyDictionary<string, object>? parameters
    )
    {
        if (parameters == null || parameters.Count == 0)
        {
            return new AgentExecutionDefaults();
        }

        float? temperature = TryGetFloatParameter(parameters, "temperature", out var parsedTemperature)
            ? parsedTemperature
            : null;
        int? maxOutputTokens = TryGetIntParameter(parameters, "max_tokens", out var parsedMaxOutputTokens)
            ? parsedMaxOutputTokens
            : null;
        ChatToolChoice? toolChoice = TryGetToolChoiceParameter(parameters, out var parsedToolChoice)
            ? parsedToolChoice
            : null;

        return new AgentExecutionDefaults
        {
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            ToolChoice = toolChoice,
        };
    }

    public void ApplyToLegacyParameters(IDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        ApplyOptionalValue(parameters, "temperature", Temperature);
        ApplyOptionalValue(parameters, "max_tokens", MaxOutputTokens);
        ApplyToolChoice(parameters, ToolChoice);
    }

    private static void ApplyOptionalValue<T>(
        IDictionary<string, object> parameters,
        string key,
        T? value
    )
        where T : struct
    {
        if (value.HasValue)
        {
            parameters[key] = value.Value;
            return;
        }

        parameters.Remove(key);
    }

    private static bool TryGetFloatParameter(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out float value
    )
    {
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            value = default;
            return false;
        }

        switch (rawValue)
        {
            case float single:
                value = single;
                return true;
            case double doubleValue:
                value = Convert.ToSingle(doubleValue);
                return true;
            case decimal decimalValue:
                value = Convert.ToSingle(decimalValue);
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case string stringValue
                when float.TryParse(
                    stringValue,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out value
                ):
                return true;
            case JsonElement element:
                return TryGetFloatFromJsonElement(element, out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetIntParameter(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out int value
    )
    {
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            value = default;
            return false;
        }

        switch (rawValue)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case double doubleValue
                when doubleValue >= int.MinValue
                    && doubleValue <= int.MaxValue
                    && Math.Abs(doubleValue % 1) < double.Epsilon:
                value = Convert.ToInt32(doubleValue);
                return true;
            case decimal decimalValue
                when decimalValue >= int.MinValue
                    && decimalValue <= int.MaxValue
                    && decimal.Truncate(decimalValue) == decimalValue:
                value = Convert.ToInt32(decimalValue);
                return true;
            case string stringValue
                when int.TryParse(
                    stringValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                ):
                return true;
            case JsonElement element:
                return TryGetIntFromJsonElement(element, out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetToolChoiceParameter(
        IReadOnlyDictionary<string, object> parameters,
        out ChatToolChoice? toolChoice
    )
    {
        toolChoice = null;

        if (!TryGetStringParameter(parameters, "tool_choice", out var rawToolChoice))
        {
            return false;
        }

        switch (rawToolChoice.Trim().ToLowerInvariant())
        {
            case "auto":
                toolChoice = ChatToolChoice.Auto;
                return true;
            case "none":
                toolChoice = ChatToolChoice.None;
                return true;
            case "required":
            case "any":
                toolChoice = ChatToolChoice.Required;
                return true;
            case "specific":
            case "tool":
                if (!TryGetStringParameter(parameters, "tool_name", out var toolName))
                {
                    return false;
                }

                toolChoice = ChatToolChoice.ForTool(toolName);
                return true;
            default:
                return false;
        }
    }

    private static void ApplyToolChoice(
        IDictionary<string, object> parameters,
        ChatToolChoice? toolChoice
    )
    {
        if (toolChoice == null)
        {
            parameters.Remove("tool_choice");
            parameters.Remove("tool_name");
            return;
        }

        switch (toolChoice.Kind)
        {
            case ChatToolChoiceKind.Auto:
                parameters["tool_choice"] = "auto";
                parameters.Remove("tool_name");
                break;
            case ChatToolChoiceKind.None:
                parameters["tool_choice"] = "none";
                parameters.Remove("tool_name");
                break;
            case ChatToolChoiceKind.Required:
                parameters["tool_choice"] = "required";
                parameters.Remove("tool_name");
                break;
            case ChatToolChoiceKind.Specific:
                parameters["tool_choice"] = "specific";
                parameters["tool_name"] = toolChoice.ToolName!;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(toolChoice));
        }
    }

    private static bool TryGetStringParameter(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out string value
    )
    {
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            value = string.Empty;
            return false;
        }

        switch (rawValue)
        {
            case string stringValue when !string.IsNullOrWhiteSpace(stringValue):
                value = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryGetFloatFromJsonElement(JsonElement element, out float value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetSingle(out value):
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var doubleValue):
                value = Convert.ToSingle(doubleValue);
                return true;
            case JsonValueKind.String:
                return float.TryParse(
                    element.GetString(),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out value
                );
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetIntFromJsonElement(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt32(out value):
                return true;
            case JsonValueKind.String:
                return int.TryParse(
                    element.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                );
            default:
                value = default;
                return false;
        }
    }
}
