using Mcp.Net.Core.Models.Exceptions;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.Server.Authentication;
using Mcp.Net.Server.Interfaces;

namespace Mcp.Net.Server.Transport.Sse;

/// <summary>
/// Manages SSE connections for server-client communication
/// </summary>
public class SseTransportHost
{
    private readonly ILogger<SseTransportHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpServer _server;
    private readonly IConnectionManager _connectionManager;
    private readonly SseRequestSecurity _security;
    private readonly SseJsonRpcProcessor _messageProcessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseTransportHost"/> class.
    /// </summary>
    /// <param name="server">The MCP server instance that will execute JSON-RPC requests.</param>
    /// <param name="loggerFactory">Factory used to create scoped loggers for transports.</param>
    /// <param name="connectionManager">Shared connection registry used for session lookups.</param>
    /// <param name="authHandler">Optional authentication handler applied to HTTP requests.</param>
    /// <param name="allowedOrigins">Optional set of allowed origins that may access the HTTP endpoint.</param>
    /// <param name="canonicalOrigin">Canonical origin derived from the server's MCP endpoint.</param>
    public SseTransportHost(
        McpServer server,
        ILoggerFactory loggerFactory,
        IConnectionManager connectionManager,
        IAuthHandler? authHandler = null,
        IEnumerable<string>? allowedOrigins = null,
        string? canonicalOrigin = null
    )
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = loggerFactory.CreateLogger<SseTransportHost>();
        _security = new SseRequestSecurity(allowedOrigins, canonicalOrigin, authHandler);
        _messageProcessor = new SseJsonRpcProcessor(_server);
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
        await _connectionManager.CloseAllConnectionsAsync().ConfigureAwait(false);
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

        if (!await _security.ValidateOriginAsync(context, logger))
        {
            return;
        }

        var authOutcome = await _security.AuthenticateAsync(context, logger);
        if (!authOutcome.Success)
        {
            return;
        }

        var authResult = authOutcome.Result;

        var transport = CreateTransport(context);

        if (authResult != null)
        {
            logger.LogInformation(
                "Authenticated connection from {ClientIp} for user {UserId}",
                clientIp,
                authResult.UserId
            );

            transport.Metadata["UserId"] = authResult.UserId!;

            foreach (var claim in authResult.Claims)
            {
                transport.Metadata[$"Claim_{claim.Key}"] = claim.Value;
            }
        }

        var sessionId = transport.SessionId;
        logger.LogInformation("Created SSE transport with session ID {SessionId}", sessionId);

        await _connectionManager
            .RegisterTransportAsync(sessionId, transport)
            .ConfigureAwait(false);
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
                logger.LogInformation("Server connected to transport {SessionId}", sessionId);

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

        if (!await _security.ValidateOriginAsync(context, logger))
        {
            return;
        }

        var sessionId = await ResolveSessionIdAsync(context, logger);
        if (sessionId == null)
        {
            return;
        }

        var authOutcome = await _security.AuthenticateAsync(context, logger);
        if (!authOutcome.Success)
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
                await _messageProcessor.ProcessAsync(context, sessionId, transport, logger);
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
        var transport = await _connectionManager
            .GetTransportAsync(sessionId)
            .ConfigureAwait(false);

        if (transport is SseTransport sseTransport)
        {
            return sseTransport;
        }

        logger.LogWarning("Session not found for ID {SessionId}", sessionId);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "Session not found" });
        return null;
    }

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

}
