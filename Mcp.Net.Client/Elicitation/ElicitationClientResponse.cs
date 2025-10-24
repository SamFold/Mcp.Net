using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Client.Elicitation;

/// <summary>
/// Represents the client's decision for an elicitation prompt.
/// </summary>
public sealed class ElicitationClientResponse
{
    private static readonly JsonSerializerOptions s_serializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private ElicitationClientResponse(string action, JsonElement? content)
    {
        Action = action;
        Content = content;
    }

    /// <summary>
    /// Gets the action to return to the server.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets the serialized response content when <see cref="Action"/> is <c>accept</c>.
    /// </summary>
    public JsonElement? Content { get; }

    /// <summary>
    /// Creates an acceptance response with content generated from an arbitrary object.
    /// </summary>
    /// <param name="content">The payload to serialize.</param>
    /// <returns>An elicitation response that accepts the prompt.</returns>
    public static ElicitationClientResponse Accept(object content)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var element = JsonSerializer.SerializeToElement(content, s_serializationOptions);
        return Accept(element);
    }

    /// <summary>
    /// Creates an acceptance response with an explicit JSON document.
    /// </summary>
    /// <param name="content">The JSON payload to return.</param>
    /// <returns>An elicitation response that accepts the prompt.</returns>
    public static ElicitationClientResponse Accept(JsonElement content)
    {
        return new ElicitationClientResponse("accept", content);
    }

    /// <summary>
    /// Creates a decline response indicating the user explicitly declined.
    /// </summary>
    public static ElicitationClientResponse Decline() =>
        new("decline", null);

    /// <summary>
    /// Creates a cancel response indicating the user cancelled the interaction.
    /// </summary>
    public static ElicitationClientResponse Cancel() =>
        new("cancel", null);
}
