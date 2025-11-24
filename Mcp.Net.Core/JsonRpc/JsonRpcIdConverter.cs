using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.JsonRpc;

/// <summary>
/// Converts JSON-RPC ID values, which can be string, number, or null according to the spec.
/// </summary>
public class JsonRpcIdConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long longValue))
                return longValue.ToString();

            return reader.GetDouble().ToString();
        }

        throw new JsonException($"Unsupported token type for ID: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else if (IsInteger(value, out long longValue))
            writer.WriteNumberValue(longValue);
        else
            writer.WriteStringValue(value);
        // We can't use Logger directly here as it may not be available
        // Log ID conversion in the calling code instead
    }

    private static bool IsInteger(string value, out long number)
    {
        return long.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number
        );
    }
}
