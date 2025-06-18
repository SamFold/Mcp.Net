using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Tools;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.Agents;

/// <summary>
/// Service for managing system default agents and agent selection
/// </summary>
public class DefaultAgentManager
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IAgentFactory _agentFactory;
    private readonly IToolRegistry _toolsRegistry;
    private readonly ILogger<DefaultAgentManager> _logger;

    public DefaultAgentManager(
        IAgentRegistry agentRegistry,
        IAgentFactory agentFactory,
        IToolRegistry toolsRegistry,
        ILogger<DefaultAgentManager> logger
    )
    {
        _agentRegistry = agentRegistry;
        _agentFactory = agentFactory;
        _toolsRegistry = toolsRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Get a default agent for a specific model and optional provider
    /// </summary>
    public async Task<AgentDefinition?> GetDefaultAgentForModelAsync(
        string modelName,
        LlmProvider provider = LlmProvider.OpenAI
    )
    {
        var agents = await _agentRegistry.GetAllAgentsAsync();

        // Try to find a default agent specifically for this model
        var modelDefault = agents.FirstOrDefault(a =>
            a.IsSystemDefault && a.DefaultForModel == modelName && a.Provider == provider
        );

        if (modelDefault != null)
        {
            return modelDefault;
        }

        // If no model-specific default, fall back to provider default
        var providerDefault = await GetDefaultAgentForProviderAsync(provider);
        if (providerDefault != null)
        {
            return providerDefault;
        }

        // Return null if no default agent exists - don't auto-create
        return null;
    }

    /// <summary>
    /// Get a default agent for a specific provider
    /// </summary>
    public async Task<AgentDefinition?> GetDefaultAgentForProviderAsync(LlmProvider provider)
    {
        var agents = await _agentRegistry.GetAllAgentsAsync();

        // Find a default agent for this provider
        var providerDefault = agents.FirstOrDefault(a =>
            a.IsSystemDefault && a.DefaultForProvider == provider
        );

        if (providerDefault != null)
        {
            return providerDefault;
        }

        // If no provider-specific default, fall back to global default
        // IMPORTANT: Don't call GetSystemDefaultAgentAsync here to avoid recursion
        var globalDefault = agents.FirstOrDefault(a => a.IsSystemDefault && a.IsGlobalDefault);
        if (globalDefault != null)
        {
            return globalDefault;
        }

        // Return null if no default agent exists - don't auto-create
        return null;
    }

    /// <summary>
    /// Get the global system default agent
    /// </summary>
    public async Task<AgentDefinition?> GetSystemDefaultAgentAsync()
    {
        var agents = await _agentRegistry.GetAllAgentsAsync();

        // Find the global default agent
        var globalDefault = agents.FirstOrDefault(a => a.IsSystemDefault && a.IsGlobalDefault);

        // Return null if no global default exists - don't auto-create
        return globalDefault;
    }

    /// <summary>
    /// Check if an agent is a system default
    /// </summary>
    public bool IsDefaultAgent(AgentDefinition agent)
    {
        return agent.IsSystemDefault;
    }

    /// <summary>
    /// Create a model-specific default agent if it doesn't exist
    /// </summary>
    public async Task<AgentDefinition> EnsureModelDefaultAgentAsync(
        string modelName,
        LlmProvider provider,
        string? systemPrompt = null,
        IEnumerable<string>? toolIds = null
    )
    {
        // IMPORTANT: Don't call GetDefaultAgentForModelAsync here to avoid recursion
        var agents = await _agentRegistry.GetAllAgentsAsync();
        var existingAgent = agents.FirstOrDefault(a =>
            a.IsSystemDefault && a.DefaultForModel == modelName && a.Provider == provider
        );
        if (existingAgent != null)
        {
            return existingAgent;
        }

        // Create a new default agent for this model
        var agent = await _agentFactory.CreateAgentAsync(
            provider,
            modelName,
            "System" // createdByUserId
        );

        // Set default properties
        agent.Name = $"Default {provider} {modelName} Agent";
        agent.Description = $"System default agent for {provider} {modelName}";
        agent.Category = AgentCategory.General;
        agent.IsSystemDefault = true;
        agent.DefaultForModel = modelName;
        agent.DefaultForProvider = provider;
        agent.ModifiedBy = "System";

        // Set system prompt if provided
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            agent.SystemPrompt = systemPrompt;
        }
        else
        {
            // Use a sensible default system prompt
            agent.SystemPrompt = GetDefaultSystemPrompt(provider, modelName);
        }

        // Add tools if provided
        if (toolIds != null)
        {
            agent.ToolIds.AddRange(toolIds);
        }
        else
        {
            // Add some basic default tools
            await AddDefaultToolsAsync(agent);
        }

        // Register the agent
        await _agentRegistry.RegisterAgentAsync(agent, "System");
        _logger.LogInformation(
            "Created default agent for {Provider} {Model}: {AgentId}",
            provider,
            modelName,
            agent.Id
        );

        return agent;
    }

    /// <summary>
    /// Create a provider-specific default agent if it doesn't exist
    /// </summary>
    public async Task<AgentDefinition> EnsureProviderDefaultAgentAsync(
        LlmProvider provider,
        string? systemPrompt = null,
        IEnumerable<string>? toolIds = null
    )
    {
        // IMPORTANT: Don't call GetDefaultAgentForProviderAsync here to avoid recursion
        var agents = await _agentRegistry.GetAllAgentsAsync();
        var existingAgent = agents.FirstOrDefault(a =>
            a.IsSystemDefault && a.DefaultForProvider == provider
        );
        if (existingAgent != null)
        {
            return existingAgent;
        }

        // Choose a recommended model for this provider
        string modelName = GetRecommendedModelForProvider(provider);

        // Create a new default agent for this provider
        var agent = await _agentFactory.CreateAgentAsync(
            provider,
            modelName,
            "System" // createdByUserId
        );

        // Set default properties
        agent.Name = $"Default {provider} Agent";
        agent.Description = $"System default agent for {provider}";
        agent.Category = AgentCategory.General;
        agent.IsSystemDefault = true;
        agent.DefaultForProvider = provider;
        agent.ModifiedBy = "System";

        // Set system prompt if provided
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            agent.SystemPrompt = systemPrompt;
        }
        else
        {
            // Use a sensible default system prompt
            agent.SystemPrompt = GetDefaultSystemPrompt(provider, modelName);
        }

        // Add tools if provided
        if (toolIds != null)
        {
            agent.ToolIds.AddRange(toolIds);
        }
        else
        {
            // Add some basic default tools
            await AddDefaultToolsAsync(agent);
        }

        // Register the agent
        await _agentRegistry.RegisterAgentAsync(agent, "System");
        _logger.LogInformation(
            "Created default agent for {Provider}: {AgentId}",
            provider,
            agent.Id
        );

        return agent;
    }

    /// <summary>
    /// Create a global default agent if it doesn't exist
    /// </summary>
    public async Task<AgentDefinition> EnsureGlobalDefaultAgentAsync()
    {
        // IMPORTANT: Don't call GetSystemDefaultAgentAsync here to avoid recursion
        var agents = await _agentRegistry.GetAllAgentsAsync();
        var existingAgent = agents.FirstOrDefault(a => a.IsSystemDefault && a.IsGlobalDefault);
        if (existingAgent != null)
        {
            return existingAgent;
        }

        // Use OpenAI GPT-4 as the default global model
        var agent = await _agentFactory.CreateAgentAsync(
            LlmProvider.OpenAI,
            "gpt-4o",
            "System" // createdByUserId
        );

        // Set default properties
        agent.Name = "Default System Agent";
        agent.Description = "Global system default agent";
        agent.Category = AgentCategory.General;
        agent.IsSystemDefault = true;
        agent.IsGlobalDefault = true;
        agent.ModifiedBy = "System";

        // Set a generic default system prompt
        agent.SystemPrompt =
            "You are a helpful, harmless, and honest assistant. "
            + "Answer questions accurately and concisely.";

        // Add basic default tools
        await AddDefaultToolsAsync(agent);

        // Register the agent
        await _agentRegistry.RegisterAgentAsync(agent, "System");
        _logger.LogInformation("Created global default agent: {AgentId}", agent.Id);

        return agent;
    }

    /// <summary>
    /// Gets a recommended model name for the given provider
    /// </summary>
    private string GetRecommendedModelForProvider(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.OpenAI => "gpt-4o",
            LlmProvider.Anthropic => "claude-3-sonnet-20240229",
            _ => "gpt-4o", // Default to GPT-4 for unknown providers
        };
    }

    /// <summary>
    /// Gets a default system prompt appropriate for the provider and model
    /// </summary>
    private string GetDefaultSystemPrompt(LlmProvider provider, string modelName)
    {
        // Could customize based on provider/model, but keeping simple for now
        return "You are a helpful, harmless, and honest assistant. "
            + "Answer questions accurately and concisely.";
    }

    /// <summary>
    /// Adds default tools to an agent definition
    /// </summary>
    private async Task AddDefaultToolsAsync(AgentDefinition agent)
    {
        // Get available tool categories
        var categories = await _agentFactory.GetToolCategoriesAsync();

        // Add utility tools if available
        if (categories.Contains("utility"))
        {
            var utilityTools = await _agentFactory.GetToolsByCategoryAsync("utility");
            agent.ToolIds.AddRange(utilityTools.Take(3));
        }

        // Add search tools if available
        if (categories.Contains("search"))
        {
            var searchTools = await _agentFactory.GetToolsByCategoryAsync("search");
            agent.ToolIds.AddRange(searchTools.Take(1));
        }
    }
}
