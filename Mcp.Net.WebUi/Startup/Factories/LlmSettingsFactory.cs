using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.Chat.Factories;

namespace Mcp.Net.WebUi.Startup.Factories;

public static class LlmSettingsFactory
{
    public static DefaultLlmSettings CreateDefaultSettings(
        IConfiguration configuration,
        ILogger logger
    )
    {
        var providerName =
            configuration["LlmProvider"]
            ?? Environment.GetEnvironmentVariable("LLM_PROVIDER")
            ?? "anthropic";

        var provider =
            providerName.ToLower() == "openai" ? LlmProvider.OpenAI : LlmProvider.Anthropic;

        var modelName =
            configuration["LlmModel"]
            ?? Environment.GetEnvironmentVariable("LLM_MODEL")
            ?? (provider == LlmProvider.OpenAI ? "gpt-4o" : "claude-3-7-sonnet-latest");

        logger.LogInformation(
            "Default LLM settings - provider: {Provider}, model: {Model}",
            provider,
            modelName
        );

        return new DefaultLlmSettings
        {
            Provider = provider,
            ModelName = modelName,
            DefaultSystemPrompt =
                "You are a helpful assistant with access to various tools including calculators and Warhammer 40k themed functions. Use these tools when appropriate.",
        };
    }
}
