using System;
using System.Collections.Generic;
using System.Text.Json;
using Mcp.Net.Core.Interfaces;

namespace Mcp.Net.Core.JsonRpc;

/// <summary>
/// Parser for JSON-RPC messages
/// </summary>
public class JsonRpcMessageParser : IMessageParser
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcMessageParser"/> class
    /// </summary>
    public JsonRpcMessageParser()
    {
        _options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcMessageParser"/> class
    /// </summary>
    /// <param name="options">JSON serializer options</param>
    public JsonRpcMessageParser(JsonSerializerOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public bool TryParseMessage(ReadOnlySpan<char> input, out string message, out int consumed)
    {
        message = string.Empty;
        consumed = 0;

        // Look for newline as the message delimiter
        var newlinePos = input.IndexOf('\n');
        if (newlinePos < 0)
        {
            // No complete message available
            return false;
        }

        // Extract the message (without the newline)
        message = input.Slice(0, newlinePos).ToString();
        consumed = newlinePos + 1; // +1 to consume the newline too

        // Validate JSON
        try
        {
            using var doc = JsonDocument.Parse(message);
            return true;
        }
        catch (JsonException)
        {
            message = string.Empty;
            consumed = 0;
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsJsonRpcRequest(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Check if it's a JSON-RPC request (has id and method)
            return root.TryGetProperty("jsonrpc", out _)
                && root.TryGetProperty("id", out _)
                && root.TryGetProperty("method", out _)
                && !root.TryGetProperty("result", out _)
                && !root.TryGetProperty("error", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsJsonRpcNotification(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Check if it's a JSON-RPC notification (has method but no id)
            return root.TryGetProperty("jsonrpc", out _)
                && root.TryGetProperty("method", out _)
                && !root.TryGetProperty("id", out _)
                && !root.TryGetProperty("result", out _)
                && !root.TryGetProperty("error", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if a message is a JSON-RPC response
    /// </summary>
    /// <param name="message">The message to check</param>
    /// <returns>True if the message is a JSON-RPC response, false otherwise</returns>
    public bool IsJsonRpcResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Check if it's a JSON-RPC response (has id and either result or error)
            return root.TryGetProperty("jsonrpc", out _)
                && root.TryGetProperty("id", out _)
                && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public TMessage Deserialize<TMessage>(string json)
    {
        return JsonSerializer.Deserialize<TMessage>(json, _options)
            ?? throw new JsonException($"Failed to deserialize {typeof(TMessage).Name}");
    }

    /// <inheritdoc />
    public JsonRpcRequestMessage DeserializeRequest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract required properties
            var jsonRpc = root.GetProperty("jsonrpc").GetString() ?? "2.0";

            // Handle different ID types (string, number, or null)
            string id;
            var idElement = root.GetProperty("id");
            if (idElement.ValueKind == JsonValueKind.String)
            {
                id = idElement.GetString() ?? "0";
            }
            else if (idElement.ValueKind == JsonValueKind.Number)
            {
                if (idElement.TryGetInt64(out long longValue))
                    id = longValue.ToString();
                else
                    id = idElement.GetDouble().ToString();
            }
            else
            {
                id = "0";
            }

            var method = root.GetProperty("method").GetString() ?? "";

            // Extract params if present
            object? parameters = null;
            if (root.TryGetProperty("params", out var paramsElement))
            {
                // Convert JsonElement to appropriate .NET object
                parameters = JsonSerializer.Deserialize<object>(
                    paramsElement.GetRawText(),
                    _options
                );
            }

            var meta = ParseMeta(root);

            return new JsonRpcRequestMessage(jsonRpc, id, method, parameters, meta);
        }
        catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException)
        {
            throw new JsonException($"Failed to deserialize JSON-RPC request: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public JsonRpcResponseMessage DeserializeResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract required properties
            var jsonRpc = root.GetProperty("jsonrpc").GetString() ?? "2.0";

            // Handle different ID types (string, number, or null)
            string id;
            var idElement = root.GetProperty("id");
            if (idElement.ValueKind == JsonValueKind.String)
            {
                id = idElement.GetString() ?? "0";
            }
            else if (idElement.ValueKind == JsonValueKind.Number)
            {
                if (idElement.TryGetInt64(out long longValue))
                    id = longValue.ToString();
                else
                    id = idElement.GetDouble().ToString();
            }
            else
            {
                id = "0";
            }

            // Extract result or error
            object? result = null;
            JsonRpcError? error = null;

            if (root.TryGetProperty("result", out var resultElement))
            {
                // Convert JsonElement to appropriate .NET object
                result = JsonSerializer.Deserialize<object>(resultElement.GetRawText(), _options);
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                error = JsonSerializer.Deserialize<JsonRpcError>(
                    errorElement.GetRawText(),
                    _options
                );
            }

            var meta = ParseMeta(root);

            return new JsonRpcResponseMessage(jsonRpc, id, result, error, meta);
        }
        catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException)
        {
            throw new JsonException($"Failed to deserialize JSON-RPC response: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public JsonRpcNotificationMessage DeserializeNotification(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract required properties
            var jsonRpc = root.GetProperty("jsonrpc").GetString() ?? "2.0";
            var method = root.GetProperty("method").GetString() ?? "";

            // Extract params if present
            object? parameters = null;
            if (root.TryGetProperty("params", out var paramsElement))
            {
                // Convert JsonElement to appropriate .NET object
                parameters = JsonSerializer.Deserialize<object>(
                    paramsElement.GetRawText(),
                    _options
                );
            }

            var meta = ParseMeta(root);

            return new JsonRpcNotificationMessage(jsonRpc, method, parameters, meta);
        }
        catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException)
        {
            throw new JsonException(
                $"Failed to deserialize JSON-RPC notification: {ex.Message}",
                ex
            );
        }
    }

    private IDictionary<string, object?>? ParseMeta(JsonElement root)
    {
        if (!root.TryGetProperty("_meta", out var metaElement))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            metaElement.GetRawText(),
            _options
        );
    }
}
