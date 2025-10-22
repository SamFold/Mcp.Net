using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, SseTransport> _connections = new();
    private readonly ILogger<SseConnectionManager> _logger;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _connectionTimeout;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpServer _server;
    private readonly IAuthHandler? _authHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseConnectionManager"/> class
    /// </summary>
    /// <param name="server">The MCP server instance</param>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    /// <param name="connectionTimeout">Optional timeout for stale connections</param>
    /// <param name="authHandler">Optional authentication handler</param>
    public SseConnectionManager(
        McpServer server,
        ILoggerFactory loggerFactory,
        TimeSpan? connectionTimeout = null,
        IAuthHandler? authHandler = null
    )
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<SseConnectionManager>();
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromMinutes(30);
        _authHandler = authHandler;

        _cleanupTimer = new Timer(
            CleanupStaleConnections,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );
    }

    /// <summary>
    /// Gets a transport by session ID
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The transport, or null if not found</returns>
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
    /// Gets all connected transports
    /// </summary>
    /// <returns>Collection of all active transports</returns>
    public IReadOnlyCollection<SseTransport> GetAllTransports()
    {
        return _connections.Values.ToArray();
    }

    /// <summary>
    /// Gets the number of active connections
    /// </summary>
    /// <returns>Number of active connections</returns>
    public int GetConnectionCount()
    {
        return _connections.Count;
    }

    /// <summary>
    /// Registers a transport with the connection manager
    /// </summary>
    /// <param name="transport">The transport to register</param>
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
    /// Removes a transport from the connection manager
    /// </summary>
    /// <param name="sessionId">The session ID to remove</param>
    /// <returns>True if the transport was found and removed, false otherwise</returns>
    public bool RemoveTransport(string sessionId)
    {
        return _connections.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Closes all connections and disposes resources (synchronous version)
    /// </summary>
    public void CloseAllConnections()
    {
        CloseAllConnectionsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Closes all connections and disposes resources
    /// </summary>
    public async Task CloseAllConnectionsAsync()
    {
        _logger.LogInformation("Closing all SSE connections...");
        _cleanupTimer.Dispose();

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
    /// Creates, initializes, and manages an SSE connection from an HTTP context
    /// </summary>
    /// <param name="context">The HTTP context for the SSE connection</param>
    /// <returns>A task that completes when the connection is closed</returns>
    public async Task HandleSseConnectionAsync(HttpContext context)
    {
        // Create and set up logger with connection details
        var logger = _loggerFactory.CreateLogger("SSE");
        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        logger.LogInformation("New SSE connection from {ClientIp}", clientIp);

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
    /// Process JSON-RPC messages from client
    /// </summary>
    /// <param name="context">The HTTP context containing the message</param>
    /// <returns>A task that completes when the message is processed</returns>
    public async Task HandleMessageAsync(HttpContext context)
    {
        var logger = _loggerFactory.CreateLogger("MessageEndpoint");
        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();

        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = context.Request.Query["sessionId"].ToString();
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            logger.LogWarning("Message received without session ID");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Missing session ID" });
            return;
        }

        // Authenticate the message if authentication is configured
        if (_authHandler != null)
        {
            var authResult = await _authHandler.AuthenticateAsync(context);
            if (!authResult.Succeeded)
            {
                logger.LogWarning(
                    "Authentication failed for message endpoint: {Reason}",
                    authResult.FailureReason
                );
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Unauthorized", message = authResult.FailureReason }
                );
                return;
            }

            logger.LogDebug("Message endpoint authenticated for user {UserId}", authResult.UserId);
        }

        using (logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId }))
        {
            var transport = GetTransport(sessionId);
            if (transport == null)
            {
                logger.LogWarning("Session not found for ID {SessionId}", sessionId);
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Session not found" });
                return;
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<JsonRpcRequestMessage>(
                    context.Request.Body
                );

                if (request == null)
                {
                    logger.LogError("Invalid JSON-RPC request format");
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(
                        new JsonRpcError
                        {
                            Code = (int)ErrorCode.InvalidRequest,
                            Message = "Invalid request",
                        }
                    );
                    return;
                }

                logger.LogDebug(
                    "JSON-RPC Request: Method={Method}, Id={Id}",
                    request.Method,
                    request.Id ?? "null"
                );

                if (request.Params != null)
                {
                    logger.LogTrace(
                        "Request params: {Params}",
                        JsonSerializer.Serialize(request.Params)
                    );
                }

                // Process the request through the transport
                transport.HandleRequest(request);

                // Return 202 Accepted immediately
                context.Response.StatusCode = 202;
                await context.Response.WriteAsJsonAsync(new { status = "accepted" });
            }
            catch (JsonException ex)
            {
                await HandleJsonParsingError(context, ex, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message");
                context.Response.StatusCode = 500;
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
    /// Creates a new SSE transport from an HTTP context
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <returns>The created transport</returns>
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

    /// <summary>
    /// Periodically checks for and removes stale connections
    /// </summary>
    private void CleanupStaleConnections(object? state)
    {
        try
        {
            _logger.LogDebug("Checking for stale connections...");

            //TODO: Implement this properly
            // In a real implementation, we would track last activity time for each connection
            // and remove those that have been inactive for longer than the timeout.
            // For now, since we don't have a way to track activity, we'll just log the count.

            _logger.LogDebug("Active connections: {ConnectionCount}", _connections.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up stale connections");
        }
    }
}
