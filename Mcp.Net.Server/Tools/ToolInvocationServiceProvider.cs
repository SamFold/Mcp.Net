using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mcp.Net.Server.Elicitation;

namespace Mcp.Net.Server.Tools;

/// <summary>
/// Wraps a service provider to supply session-bound services for a single tool invocation.
/// </summary>
internal sealed class ToolInvocationServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _inner;
    private readonly McpServer _server;
    private readonly string _sessionId;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<IElicitationService> _elicitationService;

    public ToolInvocationServiceProvider(
        IServiceProvider inner,
        McpServer server,
        string sessionId,
        ILoggerFactory loggerFactory
    )
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : throw new ArgumentException("Session id must be provided.", nameof(sessionId));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _elicitationService = new Lazy<IElicitationService>(() =>
            new ElicitationService(
                _server,
                _sessionId,
                _loggerFactory.CreateLogger<ElicitationService>()
            )
        );
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IElicitationService))
        {
            if (_inner.GetService(typeof(IElicitationServiceFactory)) is IElicitationServiceFactory factory)
            {
                return factory.Create(_sessionId);
            }

            return _elicitationService.Value;
        }

        return _inner.GetService(serviceType);
    }
}
