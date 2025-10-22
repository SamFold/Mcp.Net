using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Net.Core.Models.Content;

namespace Mcp.Net.Core.Models.Content;

public class ContentBaseConverter : JsonConverter<ContentBase>
{
    public override ContentBase? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDoc.RootElement;

        if (!rootElement.TryGetProperty("type", out var typeProperty))
        {
            throw new JsonException("Missing 'type' property");
        }

        var contentType = typeProperty.GetString();
        return contentType switch
        {
            "text" => JsonSerializer.Deserialize<TextContent>(rootElement.GetRawText(), options),
            "image" => JsonSerializer.Deserialize<ImageContent>(rootElement.GetRawText(), options),
            "resource" => JsonSerializer.Deserialize<EmbeddedResource>(
                rootElement.GetRawText(),
                options
            ),
            "resource_link" => JsonSerializer.Deserialize<ResourceLinkContent>(
                rootElement.GetRawText(),
                options
            ),
            _ => throw new JsonException($"Unknown content type: {contentType}"),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ContentBase value,
        JsonSerializerOptions options
    )
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
