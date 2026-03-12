using Mcp.Net.Core.Models.Tools;
using Mcp.Net.WebUi.LLM.Clients;
using Mcp.Net.LLM.Anthropic;
using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.OpenAI;

namespace Mcp.Net.WebUi.LLM.Factories;

/// <summary>
/// Factory for creating LLM clients
/// </summary>
public class LlmClientFactory : ILlmClientProvider
{
    private readonly ILogger<AnthropicChatClient> _anthropicLogger;
    private readonly ILogger<OpenAiChatClient> _openAiLogger;
    private readonly ILogger<LlmClientFactory> _logger;

    public LlmClientFactory(
        ILogger<AnthropicChatClient> anthropicLogger,
        ILogger<OpenAiChatClient> openAiLogger,
        ILogger<LlmClientFactory> logger
    )
    {
        _anthropicLogger = anthropicLogger;
        _openAiLogger = openAiLogger;
        _logger = logger;
    }

    /// <summary>
    /// Create a new LLM client for the given provider and model.
    /// </summary>
    public IChatClient Create(LlmProvider provider, string model) =>
        Create(provider, new ChatClientOptions { Model = model });

    /// <summary>
    /// Create a new LLM client with the specified provider and options
    /// </summary>
    public IChatClient Create(LlmProvider provider, ChatClientOptions options)
    {
        // If API key is missing, try to get from environment variables
        if (string.IsNullOrEmpty(options.ApiKey))
        {
            options.ApiKey = provider switch
            {
                LlmProvider.Anthropic => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? "",
                _ => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
            };
        }

        // Use Anthropic by default
        if (provider == LlmProvider.Anthropic && !string.IsNullOrEmpty(options.ApiKey))
        {
            _logger.LogInformation("Creating Anthropic chat client with model: {Model}", options.Model);
            return new AnthropicChatClient(options, _anthropicLogger);
        }
        // Use OpenAI if specified and API key is available
        else if (provider == LlmProvider.OpenAI && !string.IsNullOrEmpty(options.ApiKey))
        {
            _logger.LogInformation("Creating OpenAI chat client with model: {Model}", options.Model);
            return new OpenAiChatClient(options, _openAiLogger);
        }
        // Fall back to stub implementation if no API key is available
        else
        {
            var keyVarName = provider == LlmProvider.OpenAI ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
            _logger.LogWarning(
                "Missing API key for {Provider}. Using stub implementation instead. Set the {KeyVarName} environment variable to use the actual LLM.",
                provider,
                keyVarName
            );
            return new StubChatClient(provider, options);
        }
    }
}
