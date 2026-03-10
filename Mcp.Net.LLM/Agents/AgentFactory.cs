using Mcp.Net.LLM.Factories;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Models.Exceptions;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Mcp.Net.LLM.Agents;

/// <summary>
/// Factory implementation for creating agent definitions and chat clients
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly IUserApiKeyProvider? _userApiKeyProvider;
    private readonly ILogger<AgentFactory> _logger;

    // Default system prompt to use when none is provided
    private const string DefaultSystemPrompt =
        "You are a helpful AI assistant. Provide accurate, helpful responses "
        + "to questions and make good use of any available tools when appropriate.";

    // Default parameters
    private static readonly Dictionary<string, object> DefaultParameters = new()
    {
        { "temperature", 0.7f },
        { "max_tokens", 2048 },
    };

    /// <summary>
    /// Initializes a new instance of the AgentFactory class
    /// </summary>
    public AgentFactory(
        IAgentRegistry agentRegistry,
        IChatClientFactory chatClientFactory,
        IToolRegistry toolRegistry,
        IApiKeyProvider apiKeyProvider,
        ILogger<AgentFactory> logger,
        IUserApiKeyProvider? userApiKeyProvider = null
    )
    {
        _agentRegistry = agentRegistry;
        _chatClientFactory = chatClientFactory;
        _toolRegistry = toolRegistry;
        _apiKeyProvider = apiKeyProvider;
        _userApiKeyProvider = userApiKeyProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IChatClient> CreateClientFromAgentAsync(string agentId)
    {
        var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
        if (agent == null)
        {
            _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
            throw new KeyNotFoundException($"Agent with ID {agentId} not found");
        }

        return await CreateClientFromAgentDefinitionAsync(agent);
    }

    /// <inheritdoc/>
    public async Task<IChatClient> CreateClientFromAgentDefinitionAsync(AgentDefinition agent)
    {
        return await CreateClientFromAgentDefinitionAsync(agent, null);
    }

    /// <summary>
    /// Creates a chat client for a specific user from an agent definition
    /// </summary>
    /// <param name="agent">The agent definition</param>
    /// <param name="userId">The user ID, or null for the default API key</param>
    /// <returns>A configured chat client instance</returns>
    /// <exception cref="ToolNotFoundException">Thrown when one or more tools specified in the agent definition cannot be found in the tool registry</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the API key for the specified provider cannot be found</exception>
    public async Task<IChatClient> CreateClientFromAgentDefinitionAsync(
        AgentDefinition agent,
        string? userId
    )
    {
        _logger.LogInformation(
            "Creating chat client for agent {AgentName} using {Provider} {Model}{UserContext}",
            agent.Name,
            agent.Provider,
            agent.ModelName,
            !string.IsNullOrEmpty(userId) ? " for user " + userId : string.Empty
        );

        // Create client options from agent definition
        var clientOptions = new ChatClientOptions
        {
            Model = agent.ModelName,
            SystemPrompt = agent.SystemPrompt,
        };

        // Apply any additional parameters
        if (
            TryGetFloatParameter(agent.Parameters, "temperature", out var temperature)
        )
        {
            clientOptions.Temperature = temperature;
        }

        if (TryGetIntParameter(agent.Parameters, "max_tokens", out var maxOutputTokens))
        {
            clientOptions.MaxOutputTokens = maxOutputTokens;
        }

        // Get the appropriate API key
        clientOptions.ApiKey = await GetApiKeyAsync(agent.Provider, userId);

        // Create the chat client
        var chatClient = _chatClientFactory.Create(agent.Provider, clientOptions);

        // Register tools if specified
        if (agent.ToolIds.Count > 0)
        {
            var availableToolNames = _toolRegistry.AllTools.Select(t => t.Name).ToHashSet();
            var missingToolIds = agent
                .ToolIds.Where(id => !availableToolNames.Contains(id))
                .ToList();

            // Check if any requested tools are missing
            if (missingToolIds.Count > 0)
            {
                var errorMessage =
                    $"Could not find the following tools: {string.Join(", ", missingToolIds)}";
                _logger.LogError(errorMessage);
                throw new ToolNotFoundException(missingToolIds, errorMessage);
            }

            var tools = _toolRegistry.AllTools.Where(t => agent.ToolIds.Contains(t.Name)).ToList();
            _logger.LogDebug("Registering {ToolCount} tools with the chat client", tools.Count);
            chatClient.RegisterTools(tools);
        }

        return chatClient;
    }

    private static bool TryGetFloatParameter(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out float value
    )
    {
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            value = default;
            return false;
        }

        switch (rawValue)
        {
            case float single:
                value = single;
                return true;
            case double doubleValue:
                value = Convert.ToSingle(doubleValue);
                return true;
            case decimal decimalValue:
                value = Convert.ToSingle(decimalValue);
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case string stringValue
                when float.TryParse(
                    stringValue,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out value
                ):
                return true;
            case JsonElement element:
                return TryGetFloatFromJsonElement(element, out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetIntParameter(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out int value
    )
    {
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            value = default;
            return false;
        }

        switch (rawValue)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case double doubleValue
                when doubleValue >= int.MinValue
                    && doubleValue <= int.MaxValue
                    && Math.Abs(doubleValue % 1) < double.Epsilon:
                value = Convert.ToInt32(doubleValue);
                return true;
            case decimal decimalValue
                when decimalValue >= int.MinValue
                    && decimalValue <= int.MaxValue
                    && decimal.Truncate(decimalValue) == decimalValue:
                value = Convert.ToInt32(decimalValue);
                return true;
            case string stringValue
                when int.TryParse(
                    stringValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                ):
                return true;
            case JsonElement element:
                return TryGetIntFromJsonElement(element, out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetFloatFromJsonElement(JsonElement element, out float value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetSingle(out value):
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var doubleValue):
                value = Convert.ToSingle(doubleValue);
                return true;
            case JsonValueKind.String:
                return float.TryParse(
                    element.GetString(),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out value
                );
            default:
                value = default;
                return false;
        }
    }

    private static bool TryGetIntFromJsonElement(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt32(out value):
                return true;
            case JsonValueKind.String:
                return int.TryParse(
                    element.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value
                );
            default:
                value = default;
                return false;
        }
    }

    /// <inheritdoc/>
    public Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string createdByUserId
    )
    {
        return CreateAgentAsync(provider, modelName, DefaultSystemPrompt, createdByUserId);
    }

    /// <inheritdoc/>
    public Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        string createdByUserId
    )
    {
        return CreateAgentAsync(
            provider,
            modelName,
            systemPrompt,
            Array.Empty<string>(),
            createdByUserId
        );
    }

    /// <inheritdoc/>
    public Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolIds,
        string createdByUserId
    )
    {
        return CreateAgentAsync(
            provider,
            modelName,
            systemPrompt,
            toolIds,
            AgentCategory.Uncategorized,
            createdByUserId
        );
    }

    /// <inheritdoc/>
    public Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolIds,
        AgentCategory category,
        string createdByUserId
    )
    {
        if (string.IsNullOrEmpty(createdByUserId))
        {
            throw new ArgumentNullException(
                nameof(createdByUserId),
                "User ID is required when creating an agent"
            );
        }

        // Create a default name based on the provider and model
        var name = $"{provider} {modelName} Agent";

        // Create a default description
        var description =
            $"Agent using {provider} {modelName} with {(toolIds.Any() ? "specific tools" : "no tools")}";

        var agent = new AgentDefinition
        {
            Name = name,
            Description = description,
            Provider = provider,
            ModelName = modelName,
            SystemPrompt = systemPrompt,
            ToolIds = toolIds.ToList(),
            Parameters = new Dictionary<string, object>(DefaultParameters),
            Category = category,
            CreatedBy = createdByUserId,
            ModifiedBy = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _logger.LogInformation(
            "Created new agent definition: {AgentName} ({AgentId}) by user {UserId}",
            agent.Name,
            agent.Id,
            createdByUserId
        );

        return Task.FromResult(agent);
    }

    /// <inheritdoc/>
    public async Task<AgentDefinition> CreateAgentWithToolCategoriesAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolCategories,
        string createdByUserId
    )
    {
        return await CreateAgentWithToolCategoriesAsync(
            provider,
            modelName,
            systemPrompt,
            toolCategories,
            AgentCategory.Uncategorized,
            createdByUserId
        );
    }

    /// <inheritdoc/>
    public async Task<AgentDefinition> CreateAgentWithToolCategoriesAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolCategories,
        AgentCategory agentCategory,
        string createdByUserId
    )
    {
        if (string.IsNullOrEmpty(createdByUserId))
        {
            throw new ArgumentNullException(
                nameof(createdByUserId),
                "User ID is required when creating an agent"
            );
        }

        // Collect all tools from the specified categories using the tool registry
        var toolIds = new List<string>();

        foreach (var category in toolCategories)
        {
            var categoryTools = await _toolRegistry.GetToolsByCategoryAsync(category);
            toolIds.AddRange(categoryTools);
        }

        // Remove any duplicates
        toolIds = toolIds.Distinct().ToList();

        return await CreateAgentAsync(
            provider,
            modelName,
            systemPrompt,
            toolIds,
            agentCategory,
            createdByUserId
        );
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetToolCategoriesAsync()
    {
        return _toolRegistry.GetToolCategoriesAsync();
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetToolsByCategoryAsync(string category)
    {
        return _toolRegistry.GetToolsByCategoryAsync(category);
    }

    /// <summary>
    /// Gets the API key for the specified provider and optional user
    /// </summary>
    /// <param name="provider">The LLM provider</param>
    /// <param name="userId">Optional user ID for user-specific API keys</param>
    /// <returns>The API key to use</returns>
    private async Task<string> GetApiKeyAsync(LlmProvider provider, string? userId)
    {
        try
        {
            // If we have a user ID and a user API key provider, try to get a user-specific key
            if (!string.IsNullOrEmpty(userId) && _userApiKeyProvider != null)
            {
                return await _userApiKeyProvider.GetApiKeyForUserAsync(provider, userId);
            }

            // Otherwise, fall back to the default provider
            return await _apiKeyProvider.GetApiKeyAsync(provider);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "Failed to get API key for provider: {Provider}", provider);
            throw;
        }
    }
}
