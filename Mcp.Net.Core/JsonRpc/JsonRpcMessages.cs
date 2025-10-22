using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Net.Core.JsonRpc;

/// <summary>
/// Immutable record for a JSON-RPC request
/// </summary>
/// <param name="JsonRpc">The JSON-RPC version</param>
/// <param name="Id">The request ID</param>
/// <param name="Method">The method to invoke</param>
/// <param name="Params">The method parameters</param>
/// <param name="Meta">Optional metadata associated with the request.</param>
public record JsonRpcRequestMessage(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(JsonRpcIdConverter))]
        string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params,
    [property: JsonPropertyName("_meta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IDictionary<string, object?>? Meta = null
);

/// <summary>
/// Immutable record for a JSON-RPC response
/// </summary>
/// <param name="JsonRpc">The JSON-RPC version</param>
/// <param name="Id">The request ID that this responds to</param>
/// <param name="Result">The result of the method invocation</param>
/// <param name="Error">The error, if any</param>
/// <param name="Meta">Optional metadata associated with the response.</param>
public record JsonRpcResponseMessage(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(JsonRpcIdConverter))]
        string Id,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] JsonRpcError? Error,
    [property: JsonPropertyName("_meta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IDictionary<string, object?>? Meta = null
);

/// <summary>
/// Immutable record for a JSON-RPC notification
/// </summary>
/// <param name="JsonRpc">The JSON-RPC version</param>
/// <param name="Method">The method to invoke</param>
/// <param name="Params">The method parameters</param>
/// <param name="Meta">Optional metadata associated with the notification.</param>
public record JsonRpcNotificationMessage(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params,
    [property: JsonPropertyName("_meta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IDictionary<string, object?>? Meta = null
);
