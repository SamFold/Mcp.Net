using Mcp.Net.Client;
using Mcp.Net.Client.Interfaces;
using Mcp.Net.LLM.Core;
using Mcp.Net.LLM.Catalog;
using Mcp.Net.LLM.Completions;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Mcp.Net.LLM.Elicitation;
using System.Collections.Concurrent;
using Mcp.Net.Core.JsonRpc;
using Mcp.Net.WebUi.Adapters.Interfaces;
using Mcp.Net.WebUi.Adapters.SignalR;
using Mcp.Net.WebUi.Chat.Interfaces;
using Mcp.Net.WebUi.Hubs;
using Mcp.Net.WebUi.LLM.Factories;
using Microsoft.AspNetCore.SignalR;
using Mcp.Net.WebUi.Authentication;

namespace Mcp.Net.WebUi.Chat.Factories;

/// <summary>
/// Default LLM settings class to store application-wide defaults
/// </summary>
public class DefaultLlmSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.Anthropic;
    public string ModelName { get; set; } = "claude-sonnet-4-5-20250929";
    public string DefaultSystemPrompt { get; set; } = "You are a helpful assistant.";
}

/// <summary>
/// Factory for creating chat session components
/// </summary>
public class ChatFactory : IChatFactory
{
    private readonly ILogger<ChatFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ToolRegistry _toolRegistry;
    private readonly LlmClientFactory _clientFactory;
    private readonly DefaultLlmSettings _defaultSettings;
    private readonly IConfiguration _configuration;
    private readonly IMcpClientBuilderConfigurator _authConfigurator;
    private readonly ConcurrentDictionary<string, ElicitationCoordinator> _elicitationCoordinators = new();
    private readonly ConcurrentDictionary<string, IMcpClient> _sessionClients = new();
    private readonly ConcurrentDictionary<string, Action<JsonRpcNotificationMessage>> _toolNotificationHandlers = new();
    private readonly ConcurrentDictionary<string, IPromptResourceCatalog> _promptCatalogs = new();
    private readonly ConcurrentDictionary<string, ICompletionService> _completionServices = new();

    public ChatFactory(
        ILogger<ChatFactory> logger,
        ILoggerFactory loggerFactory,
        IHubContext<ChatHub> hubContext,
        ToolRegistry toolRegistry,
        LlmClientFactory clientFactory,
        DefaultLlmSettings defaultSettings,
        IConfiguration configuration,
        IMcpClientBuilderConfigurator authConfigurator
    )
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _hubContext = hubContext;
        _toolRegistry = toolRegistry;
        _clientFactory = clientFactory;
        _defaultSettings = defaultSettings;
        _configuration = configuration;
        _authConfigurator = authConfigurator;
    }

    /// <summary>
    /// Create a new SignalR chat adapter with its own dedicated LLM client
    /// </summary>
    public async Task<ISignalRChatAdapter> CreateSignalRAdapterAsync(
        string sessionId,
        string? model = null,
        string? provider = null,
        string? systemPrompt = null
    )
    {
        // Create chat session logger for this session
        var chatSessionLogger = _loggerFactory.CreateLogger<ChatSession>();

        // Create a new dedicated LLM client for this chat session
        IChatClient sessionClient = CreateClientForSession(sessionId, model, provider);

        // Set system prompt if provided and different from default
        string effectiveSystemPrompt = systemPrompt ?? _defaultSettings.DefaultSystemPrompt;

        if (effectiveSystemPrompt != sessionClient.GetSystemPrompt())
        {
            _logger.LogInformation("Setting system prompt for session {SessionId}", sessionId);
            sessionClient.SetSystemPrompt(effectiveSystemPrompt);
        }

        // Create dedicated MCP client for this session
        var sessionMcpClient = await CreateMcpClientForSessionAsync(sessionId);
        AttachToolListNotification(sessionId, sessionMcpClient);

        var (catalog, completionService) = await CreateSessionServicesAsync(sessionId, sessionMcpClient);

        // Create core chat session (no longer needs inputProvider)
        var chatSession = new ChatSession(
            sessionClient,
            sessionMcpClient,
            _toolRegistry,
            chatSessionLogger
        );

        // Create adapter logger
        var adapterLogger = _loggerFactory.CreateLogger<SignalRChatAdapter>();

        // Create SignalR adapter
        _elicitationCoordinators.TryGetValue(sessionId, out var coordinator);
        var adapter = new SignalRChatAdapter(
            chatSession,
            _hubContext,
            adapterLogger,
            sessionId,
            _toolRegistry,
            catalog,
            completionService,
            coordinator
        );

        _logger.LogInformation("Created SignalRChatAdapter for session {SessionId}", sessionId);

        return adapter;
    }

    /// <summary>
    /// Create a new SignalR chat adapter from an agent definition
    /// </summary>
    public async Task<ISignalRChatAdapter> CreateSignalRAdapterFromAgentAsync(
        string sessionId,
        AgentDefinition agent
    )
    {
        // Create chat session logger for this session
        var chatSessionLogger = _loggerFactory.CreateLogger<ChatSession>();

        // Create a new dedicated LLM client for this chat session based on the agent definition
        IChatClient sessionClient = CreateClientFromAgent(sessionId, agent);

        // Create dedicated MCP client for this session
        var sessionMcpClient = await CreateMcpClientForSessionAsync(sessionId);
        AttachToolListNotification(sessionId, sessionMcpClient);

        var (catalog, completionService) = await CreateSessionServicesAsync(sessionId, sessionMcpClient);

        // Create core chat session
        var chatSession = new ChatSession(
            sessionClient,
            sessionMcpClient,
            _toolRegistry,
            chatSessionLogger
        );

        // Create adapter logger
        var adapterLogger = _loggerFactory.CreateLogger<SignalRChatAdapter>();

        // Create SignalR adapter
        _elicitationCoordinators.TryGetValue(sessionId, out var coordinator);
        var adapter = new SignalRChatAdapter(
            chatSession,
            _hubContext,
            adapterLogger,
            sessionId,
            _toolRegistry,
            catalog,
            completionService,
            coordinator
        );

        _logger.LogInformation(
            "Created SignalRChatAdapter for session {SessionId} using agent {AgentName}",
            sessionId,
            agent.Name
        );

        return adapter;
    }

    /// <summary>
    /// Creates a new LLM client instance dedicated to a specific chat session
    /// </summary>
    private IChatClient CreateClientForSession(string sessionId, string? model, string? provider)
    {
        // Determine provider to use (from parameter or default)
        var providerEnum = LlmProvider.Anthropic;
        if (!string.IsNullOrEmpty(provider))
        {
            if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                providerEnum = LlmProvider.OpenAI;
            }
        }
        else
        {
            providerEnum = _defaultSettings.Provider;
        }

        // Determine model to use (from parameter or default)
        var modelName = model ?? _defaultSettings.ModelName;

        _logger.LogDebug(
            "Model selection for session {SessionId}: Requested '{RequestedModel}', using '{FinalModel}'",
            sessionId,
            model,
            modelName
        );

        // Create client options
        var options = new ChatClientOptions { Model = modelName };

        // Create LLM client through factory
        var client = _clientFactory.Create(providerEnum, options);

        // Register available tools with the client
        client.RegisterTools(_toolRegistry.EnabledTools);

        _logger.LogInformation(
            "Created new {Provider} client with model {Model} for session {SessionId}",
            providerEnum,
            modelName,
            sessionId
        );

        return client;
    }

    /// <summary>
    /// Creates a new LLM client instance from an agent definition
    /// </summary>
    private IChatClient CreateClientFromAgent(string sessionId, AgentDefinition agent)
    {
        _logger.LogDebug(
            "Creating client for session {SessionId} using agent {AgentName} ({AgentId})",
            sessionId,
            agent.Name,
            agent.Id
        );

        // Create client options with parameters from the agent definition
        var options = new ChatClientOptions { Model = agent.ModelName };

        // Apply additional parameters from the agent definition
        if (agent.Parameters != null)
        {
            // Temperature
            if (
                agent.Parameters.TryGetValue("temperature", out var temperatureValue)
                && temperatureValue is float or double or int
            )
            {
                options.Temperature = Convert.ToSingle(temperatureValue);
                _logger.LogDebug(
                    "Set temperature to {Temperature} from agent definition",
                    options.Temperature
                );
            }
        }

        // Create LLM client through factory
        var client = _clientFactory.Create(agent.Provider, options);

        // Set system prompt from agent
        client.SetSystemPrompt(agent.SystemPrompt);

        // Register specific tools from agent (if any)
        if (agent.ToolIds != null && agent.ToolIds.Any())
        {
            var tools = _toolRegistry
                .EnabledTools.Where(t => agent.ToolIds.Contains(t.Name))
                .ToList();

            if (tools.Any())
            {
                _logger.LogDebug("Registering {Count} tools from agent definition", tools.Count);
                client.RegisterTools(tools);
            }
            else
            {
                // Fall back to all tools if none of the specified tools are found
                _logger.LogWarning(
                    "None of the {0} specified tools in agent {1} were found, using all enabled tools",
                    agent.ToolIds.Count,
                    agent.Id
                );
                client.RegisterTools(_toolRegistry.EnabledTools);
            }
        }
        else
        {
            // Register all available tools as fallback
            client.RegisterTools(_toolRegistry.EnabledTools);
        }

        _logger.LogInformation(
            "Created new {Provider} client with model {Model} for session {SessionId} using agent {AgentName}",
            agent.Provider,
            agent.ModelName,
            sessionId,
            agent.Name
        );

        return client;
    }

    /// <summary>
    /// Creates a dedicated MCP client instance for a specific chat session
    /// </summary>
    private async Task<IMcpClient> CreateMcpClientForSessionAsync(string sessionId)
    {
        var mcpServerUrl = _configuration["McpServer:Url"] ?? "http://localhost:5000/";

        try
        {
            bool createdCoordinator = false;
            var coordinator = _elicitationCoordinators.GetOrAdd(
                sessionId,
                sid =>
                {
                    createdCoordinator = true;
                    _logger.LogDebug(
                        "Creating new elicitation coordinator for session {SessionId}.",
                        sid
                    );
                    return new ElicitationCoordinator(
                        _loggerFactory.CreateLogger<ElicitationCoordinator>()
                    );
                }
            );

            if (!createdCoordinator)
            {
                _logger.LogDebug(
                    "Reusing existing elicitation coordinator for session {SessionId}.",
                    sessionId
                );
            }

            _logger.LogDebug(
                "Creating MCP client for session {SessionId} targeting {Url}",
                sessionId,
                mcpServerUrl
            );

            var clientBuilder = new McpClientBuilder()
                .UseSseTransport(mcpServerUrl)
                .WithElicitationHandler(coordinator.HandleAsync);

            await _authConfigurator.ConfigureAsync(clientBuilder).ConfigureAwait(false);

            var mcpClient = await clientBuilder.BuildAndInitializeAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "Created dedicated MCP client for session {SessionId}",
                sessionId
            );

            return mcpClient;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client for session {SessionId}", sessionId);
            throw;
        }
    }

    private async Task<(IPromptResourceCatalog Catalog, ICompletionService CompletionService)> CreateSessionServicesAsync(
        string sessionId,
        IMcpClient mcpClient
    )
    {
        if (_promptCatalogs.TryRemove(sessionId, out var existingCatalog))
        {
            existingCatalog.Dispose();
        }
        if (_completionServices.TryRemove(sessionId, out var existingCompletion))
        {
            existingCompletion.Clear();
        }

        var catalogLogger = _loggerFactory.CreateLogger<PromptResourceCatalog>();
        var catalog = new PromptResourceCatalog(mcpClient, catalogLogger);
        await catalog.InitializeAsync().ConfigureAwait(false);
        _promptCatalogs[sessionId] = catalog;

        var completionLogger = _loggerFactory.CreateLogger<CompletionService>();
        var completionService = new CompletionService(mcpClient, completionLogger);
        _completionServices[sessionId] = completionService;

        return (catalog, completionService);
    }

    private void AttachToolListNotification(string sessionId, IMcpClient client)
    {
        Action<JsonRpcNotificationMessage> handler = notification =>
        {
            if (notification.Method == "tools/list_changed")
            {
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await _toolRegistry.RefreshAsync(client);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to refresh tools after tools/list_changed for session {SessionId}",
                                sessionId
                            );
                        }
                    }
                );
            }
        };

        client.OnNotification += handler;
        _toolNotificationHandlers[sessionId] = handler;
        _sessionClients[sessionId] = client;
    }

    /// <summary>
    /// Create session metadata for a new chat
    /// </summary>
    public ChatSessionMetadata CreateSessionMetadata(
        string sessionId,
        string? model = null,
        string? provider = null,
        string? systemPrompt = null
    )
    {
        // Parse provider string if provided
        LlmProvider providerEnum = LlmProvider.Anthropic;
        if (!string.IsNullOrEmpty(provider))
        {
            if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                providerEnum = LlmProvider.OpenAI;
            }
        }

        // Use the specified model or default
        var modelName = model ?? _defaultSettings.ModelName;

        // Create session metadata
        var metadata = new ChatSessionMetadata
        {
            Id = sessionId,
            Title = $"New Chat {DateTime.UtcNow:g}",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Model = modelName,
            Provider = providerEnum,
            SystemPrompt = systemPrompt ?? _defaultSettings.DefaultSystemPrompt,
        };

        _logger.LogInformation(
            "Created chat session metadata for session {SessionId} with model {Model}",
            sessionId,
            modelName
        );

        return metadata;
    }

    /// <summary>
    /// Create session metadata from an agent definition
    /// </summary>
    public ChatSessionMetadata CreateSessionMetadataFromAgent(
        string sessionId,
        AgentDefinition agent
    )
    {
        // Create session metadata from agent
        var metadata = new ChatSessionMetadata
        {
            Id = sessionId,
            Title = $"Chat with {agent.Name}",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Model = agent.ModelName,
            Provider = agent.Provider,
            SystemPrompt = agent.SystemPrompt,
            AgentId = agent.Id,
            AgentName = agent.Name,
        };

        _logger.LogInformation(
            "Created chat session metadata for session {SessionId} with agent {AgentName}",
            sessionId,
            agent.Name
        );

        return metadata;
    }

    /// <summary>
    /// Releases per-session state retained by the factory (such as elicitation coordinators).
    /// </summary>
    public void ReleaseSessionResources(string sessionId)
    {
        if (_elicitationCoordinators.TryRemove(sessionId, out var coordinator))
        {
            coordinator.SetProvider(null);
            _logger.LogDebug(
                "Released session resources for {SessionId} (elicitation coordinator disposed).",
                sessionId
            );
        }

        if (_promptCatalogs.TryRemove(sessionId, out var catalog))
        {
            catalog.Dispose();
            _logger.LogDebug("Disposed prompt catalog for {SessionId}", sessionId);
        }

        if (_completionServices.TryRemove(sessionId, out var completionService))
        {
            completionService.Clear();
            _logger.LogDebug("Cleared completion cache for {SessionId}", sessionId);
        }

        if (
            _toolNotificationHandlers.TryRemove(sessionId, out var notificationHandler)
            && _sessionClients.TryRemove(sessionId, out var client)
        )
        {
            client.OnNotification -= notificationHandler;
        }
    }
}
