using System.Reflection;
using Mcp.Net.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Net.Server;

/// <summary>
/// Central registry responsible for discovering tool implementations and wiring them into an <see cref="McpServer"/>.
/// </summary>
public class ToolRegistry
{
    private readonly ILogger<ToolRegistry> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ToolDiscoveryService _discoveryService;
    private readonly ToolInvocationFactory _invocationFactory;

    private readonly List<Assembly> _assemblies = new();
    private readonly Dictionary<string, ToolDescriptor> _registeredTools = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    public ToolRegistry(IServiceProvider serviceProvider, ILogger<ToolRegistry> logger)
    {
        _serviceProvider =
            serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var loggerFactory =
            serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        _discoveryService = new ToolDiscoveryService(
            loggerFactory.CreateLogger<ToolDiscoveryService>()
        );
        _invocationFactory = new ToolInvocationFactory(
            _serviceProvider,
            loggerFactory.CreateLogger<ToolInvocationFactory>()
        );
    }

    /// <summary>
    /// Gets the number of registered tools.
    /// </summary>
    public int ToolCount => _registeredTools.Count;

    /// <summary>
    /// Gets the assemblies that have been added to the registry.
    /// </summary>
    public IReadOnlyCollection<Assembly> Assemblies => _assemblies.AsReadOnly();

    /// <summary>
    /// Gets the names of the registered tools.
    /// </summary>
    public IReadOnlyCollection<string> ToolNames => _registeredTools.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Adds an assembly to scan for tools.
    /// </summary>
    public ToolRegistry AddAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
            _logger.LogInformation(
                "Added assembly to scan for tools: {AssemblyName}",
                assembly.GetName().Name
            );
        }

        return this;
    }

    /// <summary>
    /// Discovers tools from the configured assemblies and registers them with the supplied server instance.
    /// </summary>
    /// <param name="server">The target server that should expose the discovered tools.</param>
    public void RegisterToolsWithServer(McpServer server)
    {
        if (server == null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        DiscoverTools();

        foreach (var descriptor in _registeredTools.Values)
        {
            try
            {
                RegisterToolWithServer(server, descriptor);
                _logger.LogInformation("Registered tool '{ToolName}' with server", descriptor.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to register tool '{ToolName}' with server",
                    descriptor.Name
                );
            }
        }
    }

    /// <summary>
    /// Discovers tools from the assemblies tracked by this registry.
    /// </summary>
    private void DiscoverTools()
    {
        if (_assemblies.Count == 0)
        {
            _logger.LogWarning("No assemblies registered for tool discovery");
            return;
        }

        var descriptors = _discoveryService.DiscoverTools(_assemblies);

        _registeredTools.Clear();
        foreach (var descriptor in descriptors)
        {
            _registeredTools[descriptor.Name] = descriptor;
        }

        _logger.LogInformation(
            "Discovered {ToolCount} tools across {AssemblyCount} assemblies",
            _registeredTools.Count,
            _assemblies.Count
        );
    }

    private void RegisterToolWithServer(McpServer server, ToolDescriptor descriptor)
    {
        var handler = _invocationFactory.CreateHandler(descriptor);

        server.RegisterTool(
            name: descriptor.Name,
            description: descriptor.Description,
            inputSchema: descriptor.InputSchema,
            handler: handler
        );
    }
}
