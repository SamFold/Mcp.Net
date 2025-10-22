using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mcp.Net.Core.Interfaces;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.Server.Transport.Sse;

/// <summary>
/// Manages SSE connections for server-client communication
/// </summary>
public class SseConnectionManager
{
    /// <summary>
    /// Shared serializer options that allow case-insensitive property matching.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentDictionary<string, SseTransport> _connections = new();
    private readonly ILogger<SseConnectionManager> _logger;
    private readonly TimeSpan _connectionTimeout;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpServer _server;
    private readonly IAuthHandler? _authHandler;
    private readonly HashSet<string> _allowedOrigins;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseConnectionManager"/> class.
    /// </summary>
    /// <param name="server">The MCP server instance that will execute JSON-RPC requests.</param>
    /// <param name="loggerFactory">Factory used to create scoped loggers for transports.</param>
    /// <param name="connectionTimeout">Optional idle timeout window for transports.</param>
    /// <param name="authHandler">Optional authentication handler applied to HTTP requests.</param>
    /// <param name="allowedOrigins">Optional set of allowed origins that may access the HTTP endpoint.</param>
    /// <param name="canonicalOrigin">Canonical origin derived from the server's MCP endpoint.</param>
    public SseConnectionManager(
        McpServer server,
        ILoggerFactory loggerFactory,
        TimeSpan? connectionTimeout = null,
        IAuthHandler? authHandler = null,
        IEnumerable<string>? allowedOrigins = null,
        string? canonicalOrigin = null
    )
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<SseConnectionManager>();
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromMinutes(30);
        _authHandler = authHandler;
        _allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (allowedOrigins != null)
        {
            foreach (var origin in allowedOrigins)
            {
                var normalized = NormalizeOrigin(origin);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    _allowedOrigins.Add(normalized);
                }
            }
        }

        var normalizedCanonical = NormalizeOrigin(canonicalOrigin);
        if (_allowedOrigins.Count == 0 && !string.IsNullOrEmpty(normalizedCanonical))
        {
            _allowedOrigins.Add(normalizedCanonical);
        }
    }

    /// <summary>
    /// Retrieves a transport by its session identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier associated with the SSE connection.</param>
    /// <returns>The registered transport, or <c>null</c> when no transport exists for the session.</returns>
    public SseTransport? GetTransport(string sessionId)
    {
        if (_connections.TryGetValue(sessionId, out var transport))
        {
            return transport;
        }

        _logger.LogWarning("Transport not found for session ID: {SessionId}", sessionId);
        return null;
    }

    /// <summary>
    /// Returns a snapshot of the currently connected transports.
    /// </summary>
    public IReadOnlyCollection<SseTransport> GetAllTransports()
    {
        return _connections.Values.ToArray();
    }

    /// <summary>
    /// Gets the count of active transports currently registered with the manager.
    /// </summary>
    public int GetConnectionCount()
    {
        return _connections.Count;
    }

    /// <summary>
    /// Registers an SSE transport and wires up lifecycle callbacks so it can be tracked and removed automatically.
    /// </summary>
    /// <param name="transport">The transport to register.</param>
    public void RegisterTransport(SseTransport transport)
    {
        _connections[transport.SessionId] = transport;
        _logger.LogInformation(
            "Registered transport with session ID: {SessionId}",
            transport.SessionId
        );

        // Remove the transport when it closes
        transport.OnClose += () =>
        {
            _logger.LogInformation(
                "Transport closed, removing from connection manager: {SessionId}",
                transport.SessionId
            );
            RemoveTransport(transport.SessionId);
        };
    }

    /// <summary>
    /// Removes a transport from the manager.
    /// </summary>
    /// <param name="sessionId">The session identifier of the transport to remove.</param>
    /// <returns><c>true</c> when the transport was removed; otherwise <c>false</c>.</returns>
    public bool RemoveTransport(string sessionId)
    {
        return _connections.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Closes all registered transports synchronously by awaiting the asynchronous variant.
    /// </summary>
    public void CloseAllConnections()
    {
        CloseAllConnectionsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously closes all registered transports and waits for completion (with a short timeout per transport).
    /// </summary>
    public async Task CloseAllConnectionsAsync()
    {
        _logger.LogInformation("Closing all SSE connections...");

        // Create a copy of the connections to avoid enumeration issues
        var transportsCopy = _connections.Values.ToArray();

        // Close each transport
        var closeTasks = transportsCopy
            .Select(async transport =>
            {
                try
                {
                    await transport.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error closing transport: {SessionId}",
                        transport.SessionId
                    );
                }
            })
            .ToArray();

        // Wait for all connections to close with a timeout
        await Task.WhenAll(closeTasks).WaitAsync(TimeSpan.FromSeconds(10));

        // Clear the connections dictionary
        _connections.Clear();
    }

    /// <summary>
    /// Handles a newly established SSE GET request and keeps the connection alive until the client disconnects.
    /// </summary>
    /// <param name="context">The HTTP context representing the GET handshake.</param>
    /// <returns>A task that completes once the connection terminates.</returns>
    public async Task HandleSseConnectionAsync(HttpContext context)
    {
        // Create and set up logger with connection details
        var logger = _loggerFactory.CreateLogger("SSE");
        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        logger.LogInformation("New SSE connection from {ClientIp}", clientIp);

        if (!await EnsureValidOriginAsync(context, logger))
        {
            return;
        }

        // Authenticate the connection if authentication is configured
        if (_authHandler != null)
        {
            var authResult = await _authHandler.AuthenticateAsync(context);
            if (!authResult.Succeeded)
            {
                logger.LogWarning(
                    "Authentication failed from {ClientIp}: {Reason}",
                    clientIp,
                    authResult.FailureReason
                );

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Unauthorized", message = authResult.FailureReason }
                );
                return;
            }

            logger.LogInformation(
                "Authenticated connection from {ClientIp} for user {UserId}",
                clientIp,
                authResult.UserId
            );

            // Store authentication result in context for later use
            context.Items["AuthResult"] = authResult;
        }

        var transport = CreateTransport(context);

        // If authenticated, store authentication info in transport metadata
        if (_authHandler != null && context.Items.ContainsKey("AuthResult"))
        {
            var authResult = (AuthResult)context.Items["AuthResult"]!;
            transport.Metadata["UserId"] = authResult.UserId!;

            foreach (var claim in authResult.Claims)
            {
                transport.Metadata[$"Claim_{claim.Key}"] = claim.Value;
            }
        }

        RegisterTransport(transport);

        var sessionId = transport.SessionId;
        logger.LogInformation("Registered SSE transport with session ID {SessionId}", sessionId);

        using (
            logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["SessionId"] = sessionId,
                    ["ClientIp"] = clientIp,
                }
            )
        )
        {
            try
            {
                await _server.ConnectAsync(transport);
                logger.LogInformation("Server connected to transport");

                // Keep the connection alive until client disconnects
                await Task.Delay(-1, context.RequestAborted);
            }
            catch (TaskCanceledException)
            {
                logger.LogInformation("SSE connection closed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in SSE connection");
            }
            finally
            {
                await transport.CloseAsync();
                logger.LogInformation("SSE transport closed");
            }
        }
    }

    /// <summary>
    /// Processes a single HTTP POST containing a JSON-RPC message, delivering it to the underlying transport.
    /// </summary>
    /// <param name="context">The HTTP POST context.</param>
    /// <returns>A task that completes when the message has been validated and dispatched.</returns>
    public async Task HandleMessageAsync(HttpContext context)
    {
        var logger = _loggerFactory.CreateLogger("MessageEndpoint");

        if (!await EnsureValidOriginAsync(context, logger))
        {
            return;
        }

        var sessionId = await ResolveSessionIdAsync(context, logger);
        if (sessionId == null)
        {
            return;
        }

        if (!await AuthenticateRequestAsync(context, logger))
        {
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId }))
        {
            var transport = await ResolveTransportAsync(context, sessionId, logger);
            if (transport == null)
            {
                return;
            }

            try
            {
                var body = await ReadRequestBodyAsync(context, sessionId, logger);
                if (body == null)
                {
                    return;
                }

                JsonRpcPayload payload;
                try
                {
                    if (
                        !TryParseJsonRpcPayload(
                            body,
                            s_jsonSerializerOptions,
                            out payload,
                            out var parseError
                        )
                    )
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
            catch (JsonException ex)
            {
                await HandleJsonParsingError(context, ex, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(
                    new JsonRpcError
                    {
                        Code = (int)ErrorCode.InternalError,
                        Message = "Internal server error",
                    }
                );
            }
        }
    }

    /// <summary>
    /// Validates the Origin header for the incoming request, enforcing the allowed origin list.
    /// </summary>
    private async Task<bool> EnsureValidOriginAsync(HttpContext context, ILogger logger)
    {
        var originHeader = context.Request.Headers["Origin"].ToString();
        var normalizedOrigin = NormalizeOrigin(originHeader);

        if (!string.IsNullOrEmpty(normalizedOrigin) && IsOriginAllowed(normalizedOrigin))
        {
            return true;
        }

        var hostHeader = context.Request.Headers.Host.ToString();
        if (!string.IsNullOrWhiteSpace(hostHeader))
        {
            var scheme = !string.IsNullOrWhiteSpace(context.Request.Scheme)
                ? context.Request.Scheme
                : "http";
            var hostCandidate = NormalizeOrigin($"{scheme}://{hostHeader}");
            if (!string.IsNullOrEmpty(hostCandidate) && IsOriginAllowed(hostCandidate))
            {
                if (string.IsNullOrEmpty(originHeader))
                {
                    logger.LogDebug(
                        "Origin header missing; accepted request because host {Host} is permitted.",
                        hostHeader
                    );
                }

                return true;
            }
        }

        if (_allowedOrigins.Count == 0)
        {
            logger.LogWarning(
                "No allowed origins configured; allowing request from {Origin} to preserve compatibility.",
                string.IsNullOrWhiteSpace(originHeader) ? "<missing>" : originHeader
            );
            return true;
        }

        var message = string.IsNullOrWhiteSpace(originHeader)
            ? "Origin header is required for MCP HTTP requests."
            : $"Origin '{originHeader}' is not permitted.";

        logger.LogWarning(
            "Rejecting request due to invalid origin {Origin}. Allowed origins: {AllowedOrigins}",
            string.IsNullOrWhiteSpace(originHeader) ? "<missing>" : originHeader,
            string.Join(", ", _allowedOrigins)
        );

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "invalid_origin", message },
                cancellationToken: context.RequestAborted
            );
        }

        return false;
    }

    /// <summary>
    /// Determines whether an origin value exists in the allowed origin list.
    /// </summary>
    private bool IsOriginAllowed(string origin) => _allowedOrigins.Contains(origin);

    /// <summary>
    /// Normalizes an origin into a lower-case scheme/host/port string.
    /// </summary>
    private static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            var leftPart = uri.GetLeftPart(UriPartial.Authority);
            return leftPart.TrimEnd('/').ToLowerInvariant();
        }

        return origin.Trim().TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>
    /// Resolves the session identifier from headers or query parameters, replying with an error when absent.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="logger">Logger for emitting diagnostic messages.</param>
    /// <returns>The session identifier when available; otherwise <c>null</c>.</returns>
    private async Task<string?> ResolveSessionIdAsync(HttpContext context, ILogger logger)
    {
        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();

        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = context.Request.Query["sessionId"].ToString();
        }

        if (!string.IsNullOrEmpty(sessionId))
        {
            return sessionId;
        }

        logger.LogWarning("Message received without session ID");
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Missing session ID" });
        return null;
    }

    /// <summary>
    /// Validates the request with the configured authentication handler (if one exists).
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <returns><c>true</c> when the request is authenticated or no handler is configured; otherwise <c>false</c>.</returns>
    private async Task<bool> AuthenticateRequestAsync(HttpContext context, ILogger logger)
    {
        if (_authHandler == null)
        {
            return true;
        }

        var authResult = await _authHandler.AuthenticateAsync(context);
        if (authResult.Succeeded)
        {
            logger.LogDebug("Message endpoint authenticated for user {UserId}", authResult.UserId);
            return true;
        }

        logger.LogWarning(
            "Authentication failed for message endpoint: {Reason}",
            authResult.FailureReason
        );
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { error = "Unauthorized", message = authResult.FailureReason }
        );
        return false;
    }

    /// <summary>
    /// Retrieves the transport associated with the supplied session identifier, replying with 404 when missing.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="sessionId">The resolved session identifier.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <returns>The transport if one is registered; otherwise <c>null</c>.</returns>
    private async Task<SseTransport?> ResolveTransportAsync(
        HttpContext context,
        string sessionId,
        ILogger logger
    )
    {
        var transport = GetTransport(sessionId);
        if (transport != null)
        {
            return transport;
        }

        logger.LogWarning("Session not found for ID {SessionId}", sessionId);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "Session not found" });
        return null;
    }

    /// <summary>
    /// Buffers the HTTP request body and ensures it contains content.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="sessionId">The session identifier used for error responses.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <returns>The request body text when present; otherwise <c>null</c>.</returns>
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

    /// <summary>
    /// Categorizes a JSON-RPC payload, returning a strongly-typed representation for downstream processing.
    /// </summary>
    /// <param name="body">Raw JSON payload.</param>
    /// <param name="serializerOptions">Serializer options used for deserialization.</param>
    /// <param name="payload">The parsed payload when successful.</param>
    /// <param name="error">Output error message when parsing fails.</param>
    /// <returns><c>true</c> when parsing succeeds; otherwise <c>false</c>.</returns>
    private static bool TryParseJsonRpcPayload(
        string body,
        JsonSerializerOptions serializerOptions,
        out JsonRpcPayload payload,
        out string? error
    )
    {
        payload = default!;
        error = null;

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (
            !root.TryGetProperty("jsonrpc", out var jsonRpcVersion)
            || jsonRpcVersion.GetString() != "2.0"
        )
        {
            error = "Invalid or missing jsonrpc version";
            return false;
        }

        var hasMethod = root.TryGetProperty("method", out var methodElement);
        var hasId =
            root.TryGetProperty("id", out var idElement)
            && idElement.ValueKind != JsonValueKind.Null;
        var isResponsePayload =
            !hasMethod
            && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));

        var method = hasMethod ? methodElement.GetString() : null;

        if (isResponsePayload)
        {
            payload = JsonRpcPayload.CreateResponse();
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
                serializerOptions
            );
            if (notification == null)
            {
                error = "Invalid notification payload";
                return false;
            }

            payload = JsonRpcPayload.CreateNotification(method, notification);
            return true;
        }

        var request = JsonSerializer.Deserialize<JsonRpcRequestMessage>(body, serializerOptions);
        if (request == null)
        {
            error = "Invalid request payload";
            return false;
        }

        payload = JsonRpcPayload.CreateRequest(method, request);
        return true;
    }

    /// <summary>
    /// Validates the MCP protocol version header for non-initialize messages.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="sessionId">Session identifier used for error reporting.</param>
    /// <param name="method">The JSON-RPC method associated with the payload.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <returns><c>true</c> when the header is valid or not required; otherwise <c>false</c>.</returns>
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
                "Unsupported MCP-Protocol-Version \"{Version}\" for session {SessionId}",
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

    /// <summary>
    /// Dispatches the parsed payload to the SSE transport as a request, notification, or response acknowledgement.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="sessionId">Resolved session identifier.</param>
    /// <param name="transport">The SSE transport handling the connection.</param>
    /// <param name="payload">Typed JSON-RPC payload.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
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
                logger.LogDebug("Received JSON-RPC response payload");
                await WriteAcceptedAsync(context, sessionId, _server.NegotiatedProtocolVersion);
                return;

            case JsonRpcPayloadKind.Notification:
                logger.LogDebug(
                    "JSON-RPC Notification: Method={Method}",
                    payload.Notification!.Method
                );
                transport.HandleNotification(payload.Notification!);
                await WriteAcceptedAsync(context, sessionId, _server.NegotiatedProtocolVersion);
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

                transport.HandleRequest(payload.Request!);
                await WriteAcceptedAsync(context, sessionId, _server.NegotiatedProtocolVersion);
                return;
        }
    }

    /// <summary>
    /// Creates a new SSE transport from an HTTP context
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <returns>The created transport</returns>
    /// <summary>
    /// Creates a transport instance and associated response writer backed by the current HTTP response.
    /// </summary>
    private SseTransport CreateTransport(HttpContext context)
    {
        var responseWriter = new HttpResponseWriter(
            context.Response,
            _loggerFactory.CreateLogger<HttpResponseWriter>()
        );

        return new SseTransport(responseWriter, _loggerFactory.CreateLogger<SseTransport>());
    }

    /// <summary>
    /// Handles JSON parsing errors with detailed logging
    /// </summary>
    /// <summary>
    /// Writes a detailed parse-error response while logging the problematic payload for diagnostics.
    /// </summary>
    private static async Task HandleJsonParsingError(
        HttpContext context,
        JsonException ex,
        ILogger logger
    )
    {
        logger.LogError(ex, "JSON parsing error");

        try
        {
            // Rewind the request body stream to try to read it as raw data for debugging
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            string rawContent = await reader.ReadToEndAsync();

            string truncatedContent =
                rawContent.Length > 300 ? rawContent.Substring(0, 297) + "..." : rawContent;

            logger.LogDebug("Raw JSON content that failed parsing: {Content}", truncatedContent);

            try
            {
                var doc = JsonDocument.Parse(rawContent);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
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

        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(
            new JsonRpcError { Code = (int)ErrorCode.ParseError, Message = "Parse error" }
        );
    }

    /// <param name="context">HTTP context used to write the response.</param>
    /// <param name="sessionId">Session identifier to echo back in headers.</param>
    /// <param name="negotiatedProtocolVersion">Negotiated protocol version, when available.</param>
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

    /// <param name="context">HTTP context used to write the response.</param>
    /// <param name="sessionId">Session identifier to echo back in headers.</param>
    /// <param name="message">Error message describing the validation failure.</param>
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

    /// <param name="context">HTTP context used to write the response.</param>
    /// <param name="sessionId">Session identifier to echo back in headers.</param>
    /// <param name="message">Error message describing the protocol mismatch.</param>
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

    /// <param name="Kind">Type of payload being represented.</param>
    /// <param name="Method">Method name (when applicable).</param>
    /// <param name="Request">Typed JSON-RPC request when <paramref name="Kind"/> is <see cref="JsonRpcPayloadKind.Request"/>.</param>
    /// <param name="Notification">Typed JSON-RPC notification when <paramref name="Kind"/> is <see cref="JsonRpcPayloadKind.Notification"/>.</param>
    private sealed record JsonRpcPayload(
        JsonRpcPayloadKind Kind,
        string? Method,
        JsonRpcRequestMessage? Request,
        JsonRpcNotificationMessage? Notification
    )
    {
        public static JsonRpcPayload CreateResponse() =>
            new(JsonRpcPayloadKind.Response, null, null, null);

        public static JsonRpcPayload CreateNotification(
            string? method,
            JsonRpcNotificationMessage notification
        ) => new(JsonRpcPayloadKind.Notification, method, null, notification);

        public static JsonRpcPayload CreateRequest(string? method, JsonRpcRequestMessage request) =>
            new(JsonRpcPayloadKind.Request, method, request, null);
    }

    private enum JsonRpcPayloadKind
    {
        Request,
        Notification,
        Response,
    }
}
