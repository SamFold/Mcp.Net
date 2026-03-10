using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Models.Exceptions;

namespace Mcp.Net.Agent.Interfaces;

/// <summary>
/// Factory interface for creating agent definitions and chat clients from those definitions
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Creates a chat client from an agent definition identified by ID
    /// </summary>
    /// <param name="agentId">The unique ID of the agent definition</param>
    /// <returns>A configured chat client instance</returns>
    Task<IChatClient> CreateClientFromAgentAsync(string agentId);

    /// <summary>
    /// Creates a chat client directly from an agent definition
    /// </summary>
    /// <param name="agent">The agent definition to use</param>
    /// <returns>A configured chat client instance</returns>
    /// <exception cref="ToolNotFoundException">Thrown when one or more tools specified in the agent definition cannot be found in the tool registry</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the API key for the specified provider cannot be found</exception>
    Task<IChatClient> CreateClientFromAgentDefinitionAsync(AgentDefinition agent);

    /// <summary>
    /// Creates a chat client for a specific user from an agent definition
    /// </summary>
    /// <param name="agent">The agent definition to use</param>
    /// <param name="userId">The user ID for user-specific API keys</param>
    /// <returns>A configured chat client instance with user-specific API key</returns>
    /// <exception cref="ToolNotFoundException">Thrown when one or more tools specified in the agent definition cannot be found in the tool registry</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the API key for the specified provider cannot be found</exception>
    Task<IChatClient> CreateClientFromAgentDefinitionAsync(AgentDefinition agent, string userId);

    /// <summary>
    /// Creates a new agent definition with minimal configuration (provider and model)
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with sensible defaults</returns>
    Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string createdByUserId
    );

    /// <summary>
    /// Creates a new agent definition with provider, model and system prompt
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="systemPrompt">The system prompt for the agent</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with sensible defaults</returns>
    Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        string createdByUserId
    );

    /// <summary>
    /// Creates a new agent definition with provider, model, system prompt and specific tools
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="systemPrompt">The system prompt for the agent</param>
    /// <param name="toolIds">The specific tool IDs to include</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with the specified tools</returns>
    Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolIds,
        string createdByUserId
    );

    /// <summary>
    /// Creates a new agent definition with provider, model, system prompt, specific tools, and specified category
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="systemPrompt">The system prompt for the agent</param>
    /// <param name="toolIds">The specific tool IDs to include</param>
    /// <param name="category">The specific category to assign to the agent</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with the specified tools and category</returns>
    Task<AgentDefinition> CreateAgentAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolIds,
        AgentCategory category,
        string createdByUserId
    );

    /// <summary>
    /// Creates a new agent definition with provider, model, system prompt and tools from categories
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="systemPrompt">The system prompt for the agent</param>
    /// <param name="toolCategories">The tool categories to include tools from</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with tools from the specified categories</returns>
    Task<AgentDefinition> CreateAgentWithToolCategoriesAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolCategories,
        string createdByUserId
    );

    /// <summary>
    /// Creates a new agent definition with provider, model, system prompt, tools from categories, and a specified agent category
    /// </summary>
    /// <param name="provider">The LLM provider to use</param>
    /// <param name="modelName">The model name</param>
    /// <param name="systemPrompt">The system prompt for the agent</param>
    /// <param name="toolCategories">The tool categories to include tools from</param>
    /// <param name="agentCategory">The specific category to assign to the agent</param>
    /// <param name="createdByUserId">ID of the user creating the agent</param>
    /// <returns>A new agent definition with tools from the specified categories and the specified agent category</returns>
    Task<AgentDefinition> CreateAgentWithToolCategoriesAsync(
        LlmProvider provider,
        string modelName,
        string systemPrompt,
        IEnumerable<string> toolCategories,
        AgentCategory agentCategory,
        string createdByUserId
    );

    /// <summary>
    /// Gets all available tool categories from the underlying tool registry
    /// </summary>
    /// <returns>A list of tool category names</returns>
    Task<IEnumerable<string>> GetToolCategoriesAsync();

    /// <summary>
    /// Gets all tool IDs within a specific category from the underlying tool registry
    /// </summary>
    /// <param name="category">The tool category to get tools from</param>
    /// <returns>A list of tool IDs in the specified category</returns>
    Task<IEnumerable<string>> GetToolsByCategoryAsync(string category);
}
