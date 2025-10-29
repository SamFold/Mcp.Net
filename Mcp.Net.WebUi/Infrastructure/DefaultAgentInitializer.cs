using Mcp.Net.LLM.Agents;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.WebUi.Infrastructure;

/// <summary>
/// Initializes default agents on application startup
/// </summary>
public static class DefaultAgentInitializer
{
    /// <summary>
    /// Initializes system default agents
    /// </summary>
    public static async Task InitializeDefaultAgentsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var defaultAgentManager = scope.ServiceProvider.GetRequiredService<DefaultAgentManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Checking if default agents should be initialized...");

            // Check if any agents already exist
            var agentRegistry = scope.ServiceProvider.GetRequiredService<IAgentRegistry>();
            var existingAgents = await agentRegistry.GetAllAgentsAsync();

            if (existingAgents.Any())
            {
                logger.LogInformation(
                    "Found {Count} existing agents, skipping default agent initialization",
                    existingAgents.Count()
                );
                return;
            }

            logger.LogInformation("No agents found, initializing default agents (optional)...");

            try
            {
                // 1. Try to create a global default agent
                var globalDefault = await defaultAgentManager.EnsureGlobalDefaultAgentAsync();

                // 2. Try to create provider defaults
                await defaultAgentManager.EnsureProviderDefaultAgentAsync(LlmProvider.OpenAI);
                await defaultAgentManager.EnsureProviderDefaultAgentAsync(LlmProvider.Anthropic);

                // 3. Try to create model-specific defaults for common models

                // OpenAI models
                await defaultAgentManager.EnsureModelDefaultAgentAsync(
                    "gpt-5",
                    LlmProvider.OpenAI
                );

                // Anthropic models
                await defaultAgentManager.EnsureModelDefaultAgentAsync(
                    "claude-sonnet-4-5-20250929",
                    LlmProvider.Anthropic
                );

                logger.LogInformation("Default agents initialized successfully");
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to initialize default agents, continuing without them"
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing system default agents");
            // Allow application to continue even if default agent creation fails
            // Users will need to create agents manually in this case
        }
    }
}
