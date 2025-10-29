using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.LLM.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mcp.Net.LLM.ApiKeys;

/// <summary>
/// Default implementation of IApiKeyProvider that retrieves API keys from environment variables or configuration
/// </summary>
public class DefaultApiKeyProvider : IApiKeyProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultApiKeyProvider> _logger;
    private readonly IEnvironmentVariableProvider _environment;

    // Configuration key prefixes for different providers
    private const string OpenAiKeyPrefix = "OpenAI:ApiKey";
    private const string AnthropicKeyPrefix = "Anthropic:ApiKey";

    // Environment variable names for different providers
    private const string OpenAiEnvVar = "OPENAI_API_KEY";
    private const string AnthropicEnvVar = "ANTHROPIC_API_KEY";

    /// <summary>
    /// Initializes a new instance of DefaultApiKeyProvider
    /// </summary>
    /// <param name="configuration">The configuration to read API keys from</param>
    /// <param name="logger">The logger</param>
    public DefaultApiKeyProvider(
        IConfiguration configuration,
        ILogger<DefaultApiKeyProvider> logger,
        IEnvironmentVariableProvider environment
    )
    {
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    /// <inheritdoc/>
    public Task<string> GetApiKeyAsync(LlmProvider provider)
    {
        var key = GetApiKeyFromConfiguration(provider);

        if (string.IsNullOrEmpty(key))
        {
            key = GetApiKeyFromEnvironment(provider);
        }

        if (string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("No API key found for provider: {Provider}", provider);
            throw new KeyNotFoundException($"No API key found for provider: {provider}");
        }

        return Task.FromResult(key);
    }

    /// <summary>
    /// Gets the API key from configuration
    /// </summary>
    /// <param name="provider">The LLM provider</param>
    /// <returns>The API key if found, otherwise null or empty string</returns>
    private string GetApiKeyFromConfiguration(LlmProvider provider)
    {
        var configKey = provider switch
        {
            LlmProvider.OpenAI => OpenAiKeyPrefix,
            LlmProvider.Anthropic => AnthropicKeyPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        var key = _configuration[configKey];

        if (!string.IsNullOrEmpty(key))
        {
            _logger.LogDebug("Found API key in configuration for provider: {Provider}", provider);
            return key;
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the API key from environment variables
    /// </summary>
    /// <param name="provider">The LLM provider</param>
    /// <returns>The API key if found, otherwise null or empty string</returns>
    private string GetApiKeyFromEnvironment(LlmProvider provider)
    {
        var envVar = provider switch
        {
            LlmProvider.OpenAI => OpenAiEnvVar,
            LlmProvider.Anthropic => AnthropicEnvVar,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        var key = _environment.GetEnvironmentVariable(envVar);

        if (!string.IsNullOrEmpty(key))
        {
            _logger.LogDebug("Found API key in environment for provider: {Provider}", provider);
            return key;
        }

        return string.Empty;
    }
}
