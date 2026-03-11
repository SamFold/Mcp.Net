using System.Globalization;
using System.Text.Json;

namespace Mcp.Net.Agent.Models;

public sealed record AgentExecutionDefaults
{
    public float? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

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

        return new AgentExecutionDefaults
        {
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
        };
    }

    public void ApplyToLegacyParameters(IDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        ApplyOptionalValue(parameters, "temperature", Temperature);
        ApplyOptionalValue(parameters, "max_tokens", MaxOutputTokens);
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
