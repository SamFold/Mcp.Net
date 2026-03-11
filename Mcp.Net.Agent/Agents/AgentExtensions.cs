using System.Text.Json;
using Mcp.Net.Agent.Interfaces;
using Mcp.Net.Agent.Models;
using Mcp.Net.LLM.Models;
using Mcp.Net.Agent.Tools;

namespace Mcp.Net.Agent.Agents;

/// <summary>
/// Extension methods for working with agent definitions
/// </summary>
public static class AgentExtensions
{
    /// <summary>
    /// Adds tools from a category to an agent definition
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="category">The tool category to add tools from</param>
    /// <param name="toolRegistry">The tool registry to use for looking up tools</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static async Task<AgentDefinition> WithToolsFromCategoryAsync(
        this AgentDefinition agent,
        string category,
        IToolRegistry toolRegistry
    )
    {
        var tools = await toolRegistry.GetToolsByCategoryAsync(category);
        foreach (var tool in tools)
        {
            if (!agent.ToolIds.Contains(tool))
            {
                agent.ToolIds.Add(tool);
            }
        }

        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Adds tools from a category to an agent definition using the agent factory
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="category">The tool category to add tools from</param>
    /// <param name="factory">The agent factory to use for looking up tools</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static async Task<AgentDefinition> WithToolsFromCategoryAsync(
        this AgentDefinition agent,
        string category,
        IAgentFactory factory
    )
    {
        var tools = await factory.GetToolsByCategoryAsync(category);
        foreach (var tool in tools)
        {
            if (!agent.ToolIds.Contains(tool))
            {
                agent.ToolIds.Add(tool);
            }
        }

        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Sets the temperature parameter for the agent
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="temperature">The temperature value (0.0 to 1.0)</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static AgentDefinition WithTemperature(this AgentDefinition agent, float temperature)
    {
        // Ensure temperature is within valid range
        temperature = Math.Clamp(temperature, 0.0f, 1.0f);

        agent.ExecutionDefaults = agent.ExecutionDefaults with { Temperature = temperature };
        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Sets the max tokens parameter for the agent
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="maxTokens">The maximum number of tokens to generate</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static AgentDefinition WithMaxTokens(this AgentDefinition agent, int maxTokens)
    {
        // Ensure maxTokens is positive
        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be positive");
        }

        agent.ExecutionDefaults = agent.ExecutionDefaults with { MaxOutputTokens = maxTokens };
        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Sets default parameters for the agent
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static AgentDefinition WithDefaultParameters(this AgentDefinition agent)
    {
        // Apply sensible defaults based on the provider
        switch (agent.Provider)
        {
            case LlmProvider.OpenAI:
                agent.ExecutionDefaults = new AgentExecutionDefaults
                {
                    Temperature = 0.7f,
                    MaxOutputTokens = 2048,
                };
                agent.Parameters["top_p"] = 1.0f;
                break;

            case LlmProvider.Anthropic:
                agent.ExecutionDefaults = new AgentExecutionDefaults
                {
                    Temperature = 1.0f,
                    MaxOutputTokens = 1024,
                };
                break;

            default:
                agent.ExecutionDefaults = new AgentExecutionDefaults
                {
                    Temperature = 0.7f,
                    MaxOutputTokens = 2048,
                };
                break;
        }

        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Creates a deep copy of the agent definition
    /// </summary>
    /// <param name="agent">The agent definition to clone</param>
    /// <returns>A new agent definition with the same properties</returns>
    public static AgentDefinition Clone(this AgentDefinition agent)
    {
        // Create a deep copy using JSON serialization
        var json = JsonSerializer.Serialize(agent);
        var clone = JsonSerializer.Deserialize<AgentDefinition>(json);

        if (clone == null)
        {
            throw new InvalidOperationException("Failed to clone agent definition");
        }

        // Generate a new ID for the clone
        clone.Id = Guid.NewGuid().ToString();

        // Update timestamps
        clone.CreatedAt = DateTime.UtcNow;
        clone.UpdatedAt = DateTime.UtcNow;

        return clone;
    }

    /// <summary>
    /// Sets the category for the agent
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="category">The category to assign</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static AgentDefinition WithCategory(this AgentDefinition agent, AgentCategory category)
    {
        agent.Category = category;
        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }

    /// <summary>
    /// Sets a name and description for the agent based on its capabilities
    /// </summary>
    /// <param name="agent">The agent definition to modify</param>
    /// <param name="name">Custom name for the agent (optional)</param>
    /// <param name="description">Custom description for the agent (optional)</param>
    /// <returns>The modified agent definition for method chaining</returns>
    public static AgentDefinition WithNameAndDescription(
        this AgentDefinition agent,
        string? name = null,
        string? description = null
    )
    {
        // If name is provided, use it; otherwise generate one based on provider and category
        if (!string.IsNullOrEmpty(name))
        {
            agent.Name = name;
        }
        else
        {
            agent.Name = $"{agent.Provider} {agent.Category} Agent";
        }

        // If description is provided, use it; otherwise generate one
        if (!string.IsNullOrEmpty(description))
        {
            agent.Description = description;
        }
        else
        {
            var toolCount = agent.ToolIds.Count;
            var toolText = toolCount switch
            {
                0 => "no specialized tools",
                1 => "1 specialized tool",
                _ => $"{toolCount} specialized tools",
            };

            agent.Description =
                $"A {agent.Category.ToString().ToLower()} focused agent using "
                + $"{agent.Provider} {agent.ModelName} with {toolText}.";
        }

        agent.UpdatedAt = DateTime.UtcNow;
        return agent;
    }
}
