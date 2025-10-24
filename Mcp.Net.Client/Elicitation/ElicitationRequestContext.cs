using System;
using System.Collections.Generic;
using System.Text.Json;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Elicitation;

namespace Mcp.Net.Client.Elicitation;

/// <summary>
/// Provides contextual information about an elicitation request originating from the server.
/// </summary>
public sealed class ElicitationRequestContext
{
    private static readonly JsonSerializerOptions s_deserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ElicitationRequestContext"/> class.
    /// </summary>
    /// <param name="request">The JSON-RPC request that initiated the elicitation flow.</param>
    /// <exception cref="ArgumentNullException">Thrown when the request is null.</exception>
    public ElicitationRequestContext(JsonRpcRequestMessage request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));

        if (request.Params is null)
        {
            throw new ArgumentException("Elicitation request parameters cannot be null.", nameof(request));
        }

        var payloadElement = NormalizeParams(request.Params);
        var createParams = payloadElement.Deserialize<ElicitationCreateParams>(s_deserializationOptions);
        if (createParams == null)
        {
            throw new InvalidOperationException("Unable to deserialize elicitation parameters.");
        }

        if (string.IsNullOrWhiteSpace(createParams.Message))
        {
            throw new InvalidOperationException("Elicitation request message must be provided.");
        }

        Message = createParams.Message;
        RawParams = payloadElement;
        RequestedSchema = createParams.RequestedSchema ?? new ElicitationSchema();
        EnsureSchemaHydrated(RawParams, RequestedSchema);
    }

    /// <summary>
    /// Gets the server supplied message to present to the user.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the schema describing the expected response payload.
    /// </summary>
    public ElicitationSchema RequestedSchema { get; }

    /// <summary>
    /// Gets the original JSON payload supplied by the server.
    /// </summary>
    public JsonElement RawParams { get; }

    /// <summary>
    /// Gets the underlying JSON-RPC request.
    /// </summary>
    public JsonRpcRequestMessage Request { get; }

    private static JsonElement NormalizeParams(object paramsObject)
    {
        if (paramsObject is JsonElement element)
        {
            return element;
        }

        return JsonSerializer.SerializeToElement(paramsObject, s_deserializationOptions);
    }

    private static void EnsureSchemaHydrated(JsonElement rawParams, ElicitationSchema schema)
    {
        if (schema == null)
        {
            return;
        }

        if (schema.Properties.Count > 0)
        {
            return;
        }

        if (!rawParams.TryGetProperty("requestedSchema", out var schemaElement))
        {
            return;
        }

        HashSet<string> requiredNames = new(StringComparer.Ordinal);
        if (
            schemaElement.TryGetProperty("required", out var requiredElement)
            && requiredElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = item.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        requiredNames.Add(name);
                    }
                }
            }
        }

        if (
            schemaElement.TryGetProperty("properties", out var propertiesElement)
            && propertiesElement.ValueKind == JsonValueKind.Object
        )
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                var propertyModel = JsonSerializer.Deserialize<ElicitationSchemaProperty>(
                    property.Value.GetRawText(),
                    s_deserializationOptions
                );

                if (propertyModel != null)
                {
                    var isRequired = requiredNames.Contains(property.Name);
                    schema.AddProperty(property.Name, propertyModel, isRequired);
                }
            }
        }
    }
}
