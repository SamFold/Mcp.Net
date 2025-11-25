using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mcp.Net.Server.Models;

namespace Mcp.Net.Server.Transport.Sse;

internal sealed class SseJsonRpcProcessor
{
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly McpServer _server;

    public SseJsonRpcProcessor(McpServer server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public async Task ProcessAsync(
        HttpContext context,
        string sessionId,
        SseTransport transport,
        ILogger logger
    )
    {
        var body = await ReadRequestBodyAsync(context, sessionId, logger);
        if (body == null)
        {
            return;
        }

        JsonRpcPayload payload;
        try
        {
            if (!TryParseJsonRpcPayload(body, out payload, out var parseError))
            {
                logger.LogError("Invalid JSON-RPC payload: {Message}", parseError);
                await WriteInvalidRequestAsync(
                    context,
                    sessionId,
                    parseError ?? "Invalid payload"
                );
                return;
            }
        }
        catch (JsonException ex)
        {
            await HandleJsonParsingError(context, ex, logger);
            return;
        }

        if (!await ValidateProtocolVersionAsync(context, sessionId, payload.Method, logger))
        {
            return;
        }

        await DispatchPayloadAsync(context, sessionId, transport, payload, logger);
    }

    private async Task<bool> ValidateProtocolVersionAsync(
        HttpContext context,
        string sessionId,
        string? method,
        ILogger logger
    )
    {
        if (
            string.IsNullOrEmpty(_server.NegotiatedProtocolVersion)
            || string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        var protocolHeader = context.Request.Headers["MCP-Protocol-Version"].ToString();
        if (string.IsNullOrWhiteSpace(protocolHeader))
        {
            logger.LogWarning(
                "Missing MCP-Protocol-Version header for session {SessionId}",
                sessionId
            );
            await WriteProtocolVersionErrorAsync(
                context,
                sessionId,
                $"Missing MCP-Protocol-Version header. Expected {_server.NegotiatedProtocolVersion}"
            );
            return false;
        }

        if (
            !string.Equals(
                protocolHeader,
                _server.NegotiatedProtocolVersion,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            logger.LogWarning(
                "Unsupported MCP-Protocol-Version {Protocol} for session {SessionId}",
                protocolHeader,
                sessionId
            );
            await WriteProtocolVersionErrorAsync(
                context,
                sessionId,
                $"Unsupported MCP-Protocol-Version \"{protocolHeader}\". Expected {_server.NegotiatedProtocolVersion}"
            );
            return false;
        }

        return true;
    }

    private async Task DispatchPayloadAsync(
        HttpContext context,
        string sessionId,
        SseTransport transport,
        JsonRpcPayload payload,
        ILogger logger
    )
    {
        switch (payload.Kind)
        {
            case JsonRpcPayloadKind.Response:
                logger.LogDebug(
                    "JSON-RPC Response: Id={Id}, HasError={HasError}",
                    payload.Response?.Id,
                    payload.Response?.Error != null
                );
                if (payload.Response != null)
                {
                    await _server.HandleClientResponseAsync(sessionId, payload.Response);
                }
                await WriteAcceptedAsync(
                    context,
                    sessionId,
                    _server.NegotiatedProtocolVersion
                );
                return;

            case JsonRpcPayloadKind.Notification:
                logger.LogDebug(
                    "JSON-RPC Notification: Method={Method}",
                    payload.Notification!.Method
                );
                transport.HandleNotification(payload.Notification!);
                await WriteAcceptedAsync(
                    context,
                    sessionId,
                    _server.NegotiatedProtocolVersion
                );
                return;

            case JsonRpcPayloadKind.Request:
                logger.LogDebug(
                    "JSON-RPC Request: Method={Method}, Id={Id}",
                    payload.Request!.Method,
                    payload.Request!.Id
                );

                if (payload.Request!.Params != null)
                {
                    logger.LogTrace(
                        "Request params: {Params}",
                        JsonSerializer.Serialize(payload.Request!.Params)
                    );
                }

                var requestContext = new ServerRequestContext(
                    sessionId,
                    transport.SessionId,
                    payload.Request!,
                    context.RequestAborted,
                    transport.Metadata
                );
                var response = await _server.HandleRequestAsync(requestContext);

                await transport.SendAsync(response);
                await WriteAcceptedAsync(
                    context,
                    sessionId,
                    _server.NegotiatedProtocolVersion
                );
                return;
        }
    }

    private static async Task<string?> ReadRequestBodyAsync(
        HttpContext context,
        string sessionId,
        ILogger logger
    )
    {
        context.Request.EnableBuffering();
        string body;
        using (
            var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true
            )
        )
        {
            body = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        logger.LogError("Empty JSON-RPC payload received");
        await WriteInvalidRequestAsync(context, sessionId, "Request body must not be empty");
        return null;
    }

    private static bool TryParseJsonRpcPayload(
        string body,
        out JsonRpcPayload payload,
        out string? error
    )
    {
        payload = default!;
        error = null;

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (
            !root.TryGetPropertyIgnoreCase("jsonrpc", out var jsonRpcVersion)
            || jsonRpcVersion.GetString() != "2.0"
        )
        {
            error = "Invalid or missing jsonrpc version";
            return false;
        }

        var hasMethod = root.TryGetPropertyIgnoreCase("method", out var methodElement);
        var hasId =
            root.TryGetPropertyIgnoreCase("id", out var idElement)
            && idElement.ValueKind != JsonValueKind.Null;
        var isResponsePayload =
            !hasMethod
            && (
                root.TryGetPropertyIgnoreCase("result", out _)
                || root.TryGetPropertyIgnoreCase("error", out _)
            );

        var method = hasMethod ? methodElement.GetString() : null;

        if (isResponsePayload)
        {
            var response = JsonSerializer.Deserialize<JsonRpcResponseMessage>(body, s_serializerOptions);
            if (response == null)
            {
                error = "Invalid response payload";
                return false;
            }

            payload = JsonRpcPayload.CreateResponse(response);
            return true;
        }

        if (!hasMethod)
        {
            error = "Missing method";
            return false;
        }

        if (!hasId)
        {
            var notification = JsonSerializer.Deserialize<JsonRpcNotificationMessage>(
                body,
                s_serializerOptions
            );
            if (notification == null)
            {
                error = "Invalid notification payload";
                return false;
            }

            payload = JsonRpcPayload.CreateNotification(method, notification);
            return true;
        }

        var request = JsonSerializer.Deserialize<JsonRpcRequestMessage>(body, s_serializerOptions);
        if (request == null)
        {
            error = "Invalid request payload";
            return false;
        }

        payload = JsonRpcPayload.CreateRequest(method, request);
        return true;
    }

    private static async Task HandleJsonParsingError(
        HttpContext context,
        JsonException ex,
        ILogger logger
    )
    {
        logger.LogError(ex, "JSON parsing error");

        try
        {
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            string rawContent = await reader.ReadToEndAsync();

            string truncatedContent =
                rawContent.Length > 300 ? rawContent.Substring(0, 297) + "..." : rawContent;

            logger.LogDebug("Raw JSON content that failed parsing: {Content}", truncatedContent);

            try
            {
                var doc = JsonDocument.Parse(rawContent);
                if (doc.RootElement.TryGetPropertyIgnoreCase("id", out var idElement))
                {
                    string idValue =
                        idElement.ValueKind == JsonValueKind.Number
                            ? idElement.GetRawText()
                            : idElement.ToString();
                    logger.LogInformation("Request had ID: {Id}", idValue);
                }
            }
            catch (JsonException)
            {
                logger.LogError("Content is not valid JSON");
            }

            context.Request.Body.Position = 0;
        }
        catch (Exception logEx)
        {
            logger.LogError(logEx, "Failed to log request content");
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            new JsonRpcError { Code = (int)ErrorCode.ParseError, Message = "Parse error" }
        );
    }

    private static async Task WriteInvalidRequestAsync(
        HttpContext context,
        string sessionId,
        string message
    )
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers["Mcp-Session-Id"] = sessionId;
        await context.Response.WriteAsJsonAsync(
            new JsonRpcError { Code = (int)ErrorCode.InvalidRequest, Message = message }
        );
    }

    private static async Task WriteProtocolVersionErrorAsync(
        HttpContext context,
        string sessionId,
        string message
    )
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers["Mcp-Session-Id"] = sessionId;
        await context.Response.WriteAsJsonAsync(
            new JsonRpcError { Code = (int)ErrorCode.InvalidRequest, Message = message }
        );
    }

    private static Task WriteAcceptedAsync(
        HttpContext context,
        string sessionId,
        string? negotiatedProtocolVersion
    )
    {
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.Headers["Mcp-Session-Id"] = sessionId;
        if (!string.IsNullOrWhiteSpace(negotiatedProtocolVersion))
        {
            context.Response.Headers["MCP-Protocol-Version"] = negotiatedProtocolVersion;
        }

        return Task.CompletedTask;
    }

    private sealed record JsonRpcPayload(
        JsonRpcPayloadKind Kind,
        string? Method,
        JsonRpcRequestMessage? Request,
        JsonRpcNotificationMessage? Notification,
        JsonRpcResponseMessage? Response
    )
    {
        public static JsonRpcPayload CreateResponse(JsonRpcResponseMessage response) =>
            new(JsonRpcPayloadKind.Response, null, null, null, response);

        public static JsonRpcPayload CreateNotification(
            string? method,
            JsonRpcNotificationMessage notification
        ) => new(JsonRpcPayloadKind.Notification, method, null, notification, null);

        public static JsonRpcPayload CreateRequest(string? method, JsonRpcRequestMessage request) =>
            new(JsonRpcPayloadKind.Request, method, request, null, null);
    }

    private enum JsonRpcPayloadKind
    {
        Request,
        Notification,
        Response,
    }
}
