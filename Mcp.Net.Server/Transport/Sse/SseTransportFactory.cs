namespace Mcp.Net.Server.Transport.Sse;

/// <summary>
/// Interface for creating SSE transports
/// </summary>
public interface ISseTransportFactory
{
    /// <summary>
    /// Creates a new SSE transport for the given response
    /// </summary>
    /// <param name="response">The HTTP response to use</param>
    /// <returns>A new SSE transport</returns>
    SseTransport CreateTransport(HttpResponse response);
}

/// <summary>
/// Factory for creating and registering SSE transports
/// </summary>
public class SseTransportFactory : ISseTransportFactory
{
    private readonly SseTransportHost _connectionManager;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseTransportFactory"/> class
    /// </summary>
    /// <param name="connectionManager">Connection manager for SSE transports</param>
    /// <param name="loggerFactory">Logger factory</param>
    public SseTransportFactory(SseTransportHost connectionManager, ILoggerFactory loggerFactory)
    {
        _connectionManager = connectionManager;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public SseTransport CreateTransport(HttpResponse response)
    {
        // Create HTTP response writer
        var responseWriter = new HttpResponseWriter(
            response,
            _loggerFactory.CreateLogger<HttpResponseWriter>()
        );

        // Create SSE transport with the response writer
        var transport = new SseTransport(
            responseWriter,
            _loggerFactory.CreateLogger<SseTransport>()
        );

        // Register with connection manager
        _connectionManager.RegisterTransport(transport);

        return transport;
    }
}
