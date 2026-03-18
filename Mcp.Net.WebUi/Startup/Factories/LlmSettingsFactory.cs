using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.LLM;

namespace Mcp.Net.WebUi.Startup.Factories;

public static class LlmSettingsFactory
{
    public static DefaultLlmSettings CreateDefaultSettings(
        IConfiguration configuration,
        ILogger logger)
    {
        var providerName =
            configuration["LlmProvider"]
            ?? Environment.GetEnvironmentVariable("LLM_PROVIDER")
            ?? "anthropic";

        var provider = providerName.Equals("openai", StringComparison.OrdinalIgnoreCase)
            ? LlmProvider.OpenAI
            : LlmProvider.Anthropic;

        var modelName =
            configuration["LlmModel"]
            ?? Environment.GetEnvironmentVariable("LLM_MODEL")
            ?? ProviderModelDefaults.GetDefaultChatModel(provider);

        logger.LogInformation(
            "Default LLM settings — provider: {Provider}, model: {Model}",
            provider,
            modelName
        );

        return new DefaultLlmSettings
        {
            Provider = provider,
            ModelName = modelName,
        };
    }
}
