namespace Mcp.Net.LLM.Models;

public static class ProviderModelDefaults
{
    public const string OpenAiChat = "gpt-5.4";
    public const string OpenAiImageGeneration = "gpt-image-1.5";
    public const string AnthropicChat = "claude-sonnet-4-6";

    public static string GetDefaultChatModel(LlmProvider provider) =>
        provider switch
        {
            LlmProvider.OpenAI => OpenAiChat,
            LlmProvider.Anthropic => AnthropicChat,
            _ => OpenAiChat,
        };
}
